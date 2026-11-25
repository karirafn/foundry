import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { signal } from '@angular/core';
import { NEVER } from 'rxjs';
import { page } from 'vitest/browser';
import { SetupWizardComponent } from './setup-wizard';
import { SystemSignalRService } from '../../../core/services/system-signalr.service';
import { SettingsService } from '../../../core/services/settings.service';
import { AccountService } from '../../settings/accounts/account.service';
import { API_KEY_RESPONSE, flushSettings } from '../../settings/testing/settings-page-harness';

const mockSystemSignalR = { reconnected: NEVER, reloadTrigger: NEVER, loginSessionUpdate: NEVER, notifications: signal([]).asReadonly() };

const ACCOUNT = {
  id: 'account-7',
  name: 'octocat',
  providerType: 'GitHub',
  baseUrl: 'https://github.com',
  hasToken: true,
  tokenStatus: 'present',
  deniedRepositories: [],
  tokenInvalidSince: null,
  nonConformingToken: null,
  namespaces: [],
  tokenExpiry: { status: 'notChecked', expiresAt: null },
};

const LONG_DETAIL =
  'A server at a-very-long-tunnel-hostname-that-keeps-going.example.com answered, but it isn\'t this Foundry. ' +
  'Point the tunnel or proxy at port 5099 — the webhook port — not Foundry\'s main port or another service.';

/** Renders the wizard resumed at Webhooks, with a server error under the URL field. */
async function renderResumedAtWebhooks(): Promise<HTMLElement> {
  TestBed.configureTestingModule({
    imports: [SetupWizardComponent],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter([]),
      { provide: SystemSignalRService, useValue: mockSystemSignalR },
    ],
  });
  const httpMock = TestBed.inject(HttpTestingController);
  TestBed.inject(AccountService).loadAccounts();
  httpMock.expectOne('/api/accounts').flush([ACCOUNT]);
  TestBed.inject(SettingsService).loadSettings();
  flushSettings(httpMock, { ...API_KEY_RESPONSE, webhookPublicBaseUrl: null });

  const fixture = TestBed.createComponent(SetupWizardComponent);
  await fixture.whenStable();
  const host = fixture.nativeElement as HTMLElement;
  const input = host.querySelector('#setup-webhook-url') as HTMLInputElement;
  input.value = 'https://a-very-long-tunnel-hostname-that-keeps-going.example.com';
  input.dispatchEvent(new Event('input'));
  (host.querySelector('.setup-webhook-step__continue-btn') as HTMLButtonElement).click();
  httpMock.expectOne('/api/settings/webhooks').flush(
    { type: 'tag:foundry,2026:problems/Settings.WebhookPublicBaseUrlUnreachable', detail: LONG_DETAIL },
    { status: 400, statusText: 'Bad Request' },
  );
  await fixture.whenStable();
  return host;
}

// Runs in a real browser (test-layout target); jsdom would measure every box as 0.
describe('setup wizard layout', () => {
  // Restores the runner's viewport, so later layout specs measure as before.
  const original = { width: window.innerWidth, height: window.innerHeight };

  afterEach(async () => {
    await page.viewport(original.width, original.height);
  });

  it('fits four steps and the webhook step into 320px, stacking the actions', async () => {
    // Arrange
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    await page.viewport(320, 900);

    // Act
    const host = await renderResumedAtWebhooks();
    const steps = host.querySelector('.setup-wizard__steps')!.getBoundingClientRect();
    const actions = host.querySelector('.setup-webhook-step__actions')!.getBoundingClientRect();
    const continueBtn = host.querySelector('.setup-webhook-step__continue-btn')!.getBoundingClientRect();
    const error = host.querySelector('#setup-webhook-url-error')!.getBoundingClientRect();

    // Assert
    expect(consoleError).toHaveBeenCalledWith(expect.objectContaining({ status: 400, url: '/api/settings/webhooks' }));
    for (const step of Array.from(host.querySelectorAll('.setup-wizard__step'))) {
      expect(step.getBoundingClientRect().right).toBeLessThanOrEqual(steps.right + 0.5);
    }
    expect(getComputedStyle(host.querySelector('.setup-wizard__step')!, '::after').display).toBe('none');
    expect(continueBtn.width).toBeCloseTo(actions.width, 0);
    expect(continueBtn.height).toBeGreaterThanOrEqual(44);
    expect(error.right).toBeLessThanOrEqual(actions.right + 0.5);
    expect(host.scrollWidth).toBeLessThanOrEqual(320);
  });

  it('keeps the steps on one row with connectors and the button at the end from 640px', async () => {
    // Arrange
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    await page.viewport(1280, 900);

    // Act
    const host = await renderResumedAtWebhooks();
    const steps = Array.from(host.querySelectorAll('.setup-wizard__step')).map(s => s.getBoundingClientRect());
    const actions = host.querySelector('.setup-webhook-step__actions')!.getBoundingClientRect();
    const continueBtn = host.querySelector('.setup-webhook-step__continue-btn')!.getBoundingClientRect();

    // Assert
    expect(consoleError).toHaveBeenCalledWith(expect.objectContaining({ status: 400, url: '/api/settings/webhooks' }));
    expect(new Set(steps.map(s => Math.round(s.top))).size).toBe(1);
    expect(getComputedStyle(host.querySelector('.setup-wizard__step')!, '::after').display).not.toBe('none');
    expect(continueBtn.right).toBeCloseTo(actions.right, 0);
    expect(continueBtn.width).toBeLessThan(actions.width / 2);
  });
});

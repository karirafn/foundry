import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { Component, signal } from '@angular/core';
import { vi } from 'vitest';
import { NEVER } from 'rxjs';
import { SetupWizardComponent } from './setup-wizard';
import { SystemSignalRService } from '../../../core/services/system-signalr.service';
import { SettingsService } from '../../../core/services/settings.service';
import { AccountService } from '../../settings/accounts/account.service';
import { AccountSummary } from '../../settings/accounts/account.model';
import { API_KEY_RESPONSE, flushSettings } from '../../settings/testing/settings-page-harness';

const mockSystemSignalR = {
  reconnected: NEVER,
  reloadTrigger: NEVER,
  loginSessionUpdate: NEVER,
  repositoryWebhookChanged: NEVER,
  repositoryWebhooksChanged: NEVER,
  notifications: signal([]).asReadonly(),
};

@Component({ template: '', standalone: true })
class StubIssuesComponent {}

const STORED_URL = 'https://hooks.example.com';

function account(id: string): AccountSummary {
  return {
    id,
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
}

interface WizardState {
  accounts?: AccountSummary[];
  /** The loaded settings, or null when they could not be loaded. */
  settings?: Record<string, unknown> | null;
}

/** Builds the wizard after the app-start loads of accounts and settings have settled into the given state. */
function setup(state: WizardState = {}) {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    imports: [SetupWizardComponent],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter([{ path: 'issues', component: StubIssuesComponent }]),
      { provide: SystemSignalRService, useValue: mockSystemSignalR },
    ],
  });
  const httpMock = TestBed.inject(HttpTestingController);

  if (state.accounts !== undefined) {
    TestBed.inject(AccountService).loadAccounts();
    httpMock.expectOne('/api/accounts').flush(state.accounts);
  }
  if (state.settings !== undefined) {
    TestBed.inject(SettingsService).loadSettings();
    if (state.settings === null) {
      httpMock.expectOne('/api/settings').flush('Server Error', { status: 500, statusText: 'Internal Server Error' });
      httpMock.match('/api/credentials');
    } else {
      flushSettings(httpMock, { ...API_KEY_RESPONSE, ...state.settings });
    }
  }

  const fixture = TestBed.createComponent(SetupWizardComponent);
  const el = fixture.nativeElement as HTMLElement;
  return {
    fixture,
    component: fixture.componentInstance,
    httpMock,
    el,
    activeLabel: () => el.querySelector('.setup-wizard__step--active .setup-wizard__step-label')?.textContent?.trim(),
    completedLabels: () =>
      Array.from(el.querySelectorAll('.setup-wizard__step--completed .setup-wizard__step-label')).map(s => s.textContent?.trim()),
    resumeNote: () => el.querySelector('.setup-wizard__resume-note')?.textContent?.trim() ?? null,
  };
}

const RESUME_AT_WEBHOOKS = {
  accounts: [account('account-7'), account('account-8')],
  settings: { webhookPublicBaseUrl: null },
};
const RESUME_AT_REPOS = {
  accounts: [account('account-7')],
  settings: { webhookPublicBaseUrl: STORED_URL },
};

describe('SetupWizardComponent', () => {
  afterEach(() => {
    const httpMock = TestBed.inject(HttpTestingController);
    // The account step loads the token requirements list on every provider change; it is guidance only.
    httpMock.match((request) => request.url.endsWith('/token-requirements'));
    httpMock.verify();
  });

  describe('progress', () => {
    it('should render four step labels: Auth, Account, Webhooks, Repositories', () => {
      // Arrange
      const { fixture, el } = setup();

      // Act
      fixture.detectChanges();

      // Assert
      const labels = Array.from(el.querySelectorAll('.setup-wizard__step-label')).map(s => s.textContent?.trim());
      expect(labels).toEqual(['Auth', 'Account', 'Webhooks', 'Repositories']);
    });

    it('should render the progress nav with an accessible label', () => {
      // Arrange
      const { fixture, el } = setup();

      // Act
      fixture.detectChanges();

      // Assert
      expect(el.querySelector('nav.setup-wizard__progress')?.getAttribute('aria-label')).toBe('Setup progress');
    });

    it('should set aria-current="step" only on the active step', () => {
      // Arrange
      const { fixture, el } = setup();

      // Act
      fixture.detectChanges();

      // Assert
      const current = el.querySelectorAll('[aria-current="step"]');
      expect(current).toHaveLength(1);
      expect(current[0].classList).toContain('setup-wizard__step--active');
    });

    it('should mark a completed step with a hidden check and a screen-reader suffix', () => {
      // Arrange
      const { fixture, el } = setup(RESUME_AT_WEBHOOKS);

      // Act
      fixture.detectChanges();

      // Assert
      const completed = el.querySelectorAll('.setup-wizard__step--completed');
      expect(completed).toHaveLength(2);
      const auth = completed[0];
      expect(auth.querySelector('.setup-wizard__step-check')?.getAttribute('aria-hidden')).toBe('true');
      expect(auth.querySelector('.setup-wizard__step-check')?.textContent).toBe('✓');
      expect(auth.querySelector('.sr-only')?.textContent).toBe(', completed');
      expect(el.querySelector('.setup-wizard__step--active .setup-wizard__step-check')).toBeNull();
    });
  });

  describe('first step', () => {
    it('should open at Auth with no resume note when no account exists', () => {
      // Arrange
      const { fixture, el, activeLabel, resumeNote } = setup({ accounts: [], settings: { webhookPublicBaseUrl: STORED_URL } });

      // Act
      fixture.detectChanges();

      // Assert
      expect(activeLabel()).toBe('Auth');
      expect(el.querySelector('fd-setup-auth-step')).toBeTruthy();
      expect(resumeNote()).toBeNull();
    });

    it('should open at Webhooks with Auth and Account completed when an account exists but no URL is stored', () => {
      // Arrange
      const { fixture, el, activeLabel, completedLabels, resumeNote } = setup(RESUME_AT_WEBHOOKS);

      // Act
      fixture.detectChanges();

      // Assert
      expect(activeLabel()).toBe('Webhooks');
      expect(completedLabels()).toEqual(['Auth', 'Account']);
      expect(el.querySelector('fd-setup-webhook-step')).toBeTruthy();
      expect(resumeNote()).toBe(
        'Foundry now needs a public webhook URL. Your Claude authentication and provider account are already set up.',
      );
    });

    it('should open at Webhooks when an account exists but the settings could not be loaded', () => {
      // Arrange
      const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
      const { fixture, activeLabel } = setup({ accounts: [account('account-7')], settings: null });

      // Act
      fixture.detectChanges();

      // Assert
      expect(activeLabel()).toBe('Webhooks');
      expect(consoleError).toHaveBeenCalledWith(
        expect.objectContaining({ status: 500, url: '/api/settings' })
      );
    });

    it('should open at Repositories with the first account when an account and URL exist', () => {
      // Arrange
      const { fixture, httpMock, activeLabel, completedLabels, resumeNote } = setup({
        accounts: [account('account-7'), account('account-8')],
        settings: { webhookPublicBaseUrl: STORED_URL },
      });

      // Act
      fixture.detectChanges();

      // Assert
      expect(activeLabel()).toBe('Repositories');
      expect(completedLabels()).toEqual(['Auth', 'Account', 'Webhooks']);
      expect(resumeNote()).toBe(
        'Claude authentication, your provider account and the webhook URL are already set up. Add repositories now, or skip.',
      );
      httpMock.expectOne('/api/accounts/account-7/repositories/available-repositories').flush([]);
    });

    it('should not move focus on first render', () => {
      // Arrange
      const { fixture } = setup(RESUME_AT_WEBHOOKS);
      const before = document.activeElement;

      // Act
      fixture.detectChanges();

      // Assert
      expect(document.activeElement).toBe(before);
    });
  });

  describe('Back visibility', () => {
    it('should hide Back on the Webhooks step when the wizard opened there', () => {
      // Arrange
      const { fixture, el } = setup(RESUME_AT_WEBHOOKS);

      // Act
      fixture.detectChanges();

      // Assert
      expect(el.querySelector('.setup-webhook-step__back-btn')).toBeNull();
    });

    it('should hide Back on the Repositories step when the wizard opened there', () => {
      // Arrange
      const { fixture, el, httpMock } = setup(RESUME_AT_REPOS);

      // Act
      fixture.detectChanges();
      httpMock.expectOne('/api/accounts/account-7/repositories/available-repositories').flush([]);
      fixture.detectChanges();

      // Assert
      expect(el.querySelector('.setup-repos-step__back-btn')).toBeNull();
    });

    it('should show Back on Repositories after resuming at Webhooks, and return to Webhooks but no further', () => {
      // Arrange
      const { fixture, component, el, httpMock, activeLabel } = setup(RESUME_AT_WEBHOOKS);
      fixture.detectChanges();
      component.onWebhookComplete();
      fixture.detectChanges();
      httpMock.expectOne('/api/accounts/account-7/repositories/available-repositories').flush([]);
      fixture.detectChanges();
      expect(el.querySelector('.setup-repos-step__back-btn')).toBeTruthy();

      // Act
      component.onBack();
      fixture.detectChanges();
      component.onBack();
      fixture.detectChanges();

      // Assert
      expect(activeLabel()).toBe('Webhooks');
      expect(el.querySelector('fd-setup-account-step')).toBeNull();
    });

    it('should show Back on Webhooks in a fresh run', () => {
      // Arrange
      const { fixture, component, el } = setup({ accounts: [], settings: { webhookPublicBaseUrl: null } });
      fixture.detectChanges();
      component.onAuthComplete();
      fixture.detectChanges();

      // Act
      component.onAccountComplete('account-42');
      fixture.detectChanges();

      // Assert
      expect(el.querySelector('.setup-webhook-step__back-btn')).toBeTruthy();
    });
  });

  describe('fresh run', () => {
    it('should advance Auth → Account → Webhooks → Repositories, passing the created account', () => {
      // Arrange
      const { fixture, component, el, httpMock, activeLabel } = setup({ accounts: [], settings: { webhookPublicBaseUrl: null } });
      fixture.detectChanges();

      // Act
      component.onAuthComplete();
      fixture.detectChanges();
      const afterAuth = activeLabel();
      component.onAccountComplete('account-42');
      fixture.detectChanges();
      const afterAccount = activeLabel();
      component.onWebhookComplete();
      fixture.detectChanges();

      // Assert
      expect(afterAuth).toBe('Account');
      expect(afterAccount).toBe('Webhooks');
      expect(activeLabel()).toBe('Repositories');
      expect(el.querySelector('fd-setup-repos-step')).toBeTruthy();
      expect(component.createdAccountId()).toBe('account-42');
      httpMock.expectOne('/api/accounts/account-42/repositories/available-repositories').flush([]);
    });

    it('should return to Account from Webhooks and to Auth from Account', () => {
      // Arrange
      const { fixture, component, activeLabel } = setup();
      fixture.detectChanges();
      component.onAuthComplete();
      component.onAccountComplete('account-42');
      fixture.detectChanges();

      // Act
      component.onBack();
      fixture.detectChanges();
      const afterFirstBack = activeLabel();
      component.onBack();
      fixture.detectChanges();

      // Assert
      expect(afterFirstBack).toBe('Account');
      expect(activeLabel()).toBe('Auth');
    });

    it('should focus the new step heading on a step change', async () => {
      // Arrange
      const { fixture, component, el } = setup({ accounts: [], settings: { webhookPublicBaseUrl: null } });
      document.body.appendChild(el);
      fixture.detectChanges();

      // Act
      component.onAuthComplete();
      component.onAccountComplete('account-42');
      fixture.detectChanges();
      await fixture.whenStable();

      // Assert
      expect(document.activeElement?.id).toBe('setup-webhook-title');
      el.remove();
    });
  });

  it('should navigate to /issues when onReposComplete is called', () => {
    // Arrange
    const { fixture, component } = setup();
    fixture.detectChanges();
    const navigateSpy = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    // Act
    component.onReposComplete();

    // Assert
    expect(navigateSpy).toHaveBeenCalledWith(['/issues']);
  });
});

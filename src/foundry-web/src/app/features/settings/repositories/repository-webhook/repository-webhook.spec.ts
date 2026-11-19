import { Subject } from 'rxjs';
import { vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { RepositoryWebhookComponent } from './repository-webhook';
import { RepositoryWebhookInfo } from '../repository.model';
import { RepositoryService, RepositoryWebhookUpdate } from '../repository.service';
import { SystemSignalRService } from '../../../../core/services/system-signalr.service';

const ACCOUNT_ID = '00000000-0000-0000-0000-000000000001';
const REPO_ID = '00000000-0000-0000-0000-000000000010';
const BASE = `/api/accounts/${ACCOUNT_ID}/repositories/${REPO_ID}/webhook`;

const NOT_REGISTERED: RepositoryWebhookInfo = {
  status: 'not-registered',
  payloadUrl: 'https://hooks.example.com/webhooks/github/abc',
  contentType: 'application/json',
  events: ['issues', 'pull_request', 'check_suite'],
  backstopIntervalMinutes: 15,
  provider: 'github',
};

const GITLAB_NOT_REGISTERED: RepositoryWebhookInfo = {
  status: 'not-registered',
  payloadUrl: 'https://hooks.example.com/webhooks/gitlab/abc',
  contentType: 'application/json',
  events: ['Issues events (Work item events)', 'Comments', 'Merge request events', 'Pipeline events'],
  backstopIntervalMinutes: 15,
  provider: 'gitlab',
};

function setup(info: RepositoryWebhookInfo | 'load-error') {
  const webhookUpdates = new Subject<RepositoryWebhookUpdate>();
  TestBed.configureTestingModule({
    imports: [RepositoryWebhookComponent],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter([]),
      {
        provide: SystemSignalRService,
        useValue: { reconnected: new Subject<void>(), repositoryWebhookChanged: new Subject(), repositoryWebhooksChanged: new Subject<void>() },
      },
      {
        provide: RepositoryService,
        useFactory: () => Object.assign(new RepositoryService(), { webhookUpdates: webhookUpdates.asObservable() }),
      },
    ],
  });

  const fixture = TestBed.createComponent(RepositoryWebhookComponent);
  fixture.componentRef.setInput('accountId', ACCOUNT_ID);
  fixture.componentRef.setInput('repositoryId', REPO_ID);
  const httpMock = TestBed.inject(HttpTestingController);

  fixture.detectChanges();
  if (info === 'load-error') {
    httpMock.expectOne(BASE).flush('boom', { status: 500, statusText: 'Server Error' });
  } else {
    httpMock.expectOne(BASE).flush(info);
  }
  fixture.detectChanges();

  const el = fixture.nativeElement as HTMLElement;
  return { fixture, httpMock, el, webhookUpdates };
}

describe('RepositoryWebhookComponent', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
  });

  it('shows the manual registration instructions without the secret when not registered', () => {
    // Arrange & Act
    const { el } = setup(NOT_REGISTERED);

    // Assert
    expect(el.querySelector('.repository-webhook__status-value')?.textContent?.trim()).toBe('Not registered');
    expect(el.textContent).toContain(NOT_REGISTERED.payloadUrl!);
    expect(el.textContent).toContain('application/json');
    const events = Array.from(el.querySelectorAll('.repository-webhook__events li')).map(li => li.textContent?.trim());
    expect(events).toEqual(NOT_REGISTERED.events);
    expect(el.querySelector('.repository-webhook__secret')).toBeNull();
  });

  it('reveals the secret only on demand', () => {
    // Arrange
    const { fixture, httpMock, el } = setup(NOT_REGISTERED);
    const reveal = Array.from(el.querySelectorAll('button')).find(b => b.textContent?.includes('Reveal secret'))!;

    // Act
    reveal.click();
    const request = httpMock.expectOne(`${BASE}/secret`);
    request.flush({ secret: 's3cr3t' });
    fixture.detectChanges();

    // Assert
    expect(request.request.method).toBe('POST');
    expect(el.querySelector('.repository-webhook__secret')?.textContent?.trim()).toBe('s3cr3t');
  });

  it('records the registration and shows the waiting hint', () => {
    // Arrange
    const { fixture, httpMock, el } = setup(NOT_REGISTERED);
    const added = Array.from(el.querySelectorAll('button')).find(b => b.textContent?.includes("I've added the webhook"))!;

    // Act
    added.click();
    httpMock.expectOne(`${BASE}/registered`).flush({ ...NOT_REGISTERED, status: 'pending' });
    fixture.detectChanges();

    // Assert
    expect(el.querySelector('.repository-webhook__status-value')?.textContent?.trim()).toBe('Waiting for first delivery');
    expect(el.textContent).toContain('redeliver the ping');
  });

  it('hides the instructions when the webhook is active', () => {
    // Arrange & Act
    const { el } = setup({ ...NOT_REGISTERED, status: 'active' });

    // Assert
    expect(el.querySelector('.repository-webhook__status-value')?.textContent?.trim()).toBe('Active');
    expect(el.querySelector('.repository-webhook__details')).toBeNull();
    expect(el.textContent).toContain('every 15 minutes');
  });

  it('says Foundry registers a not-registered webhook automatically, before the manual details', () => {
    // Arrange & Act
    const { el } = setup(NOT_REGISTERED);

    // Assert
    const hint = el.querySelector('.repository-webhook__hint');
    expect(hint?.textContent?.replace(/\s+/g, ' ').trim()).toBe(
      "Foundry registers this webhook automatically once the account's token can manage webhooks. To add it by hand instead, use the details below.",
    );
    const details = el.querySelector('.repository-webhook__details');
    expect(details).not.toBeNull();
    expect(hint!.compareDocumentPosition(details!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('points at Dispatch settings when Foundry has no webhook URL', () => {
    // Arrange & Act
    const { el } = setup({ ...NOT_REGISTERED, status: 'not-configured', payloadUrl: null });

    // Assert
    expect(el.querySelector('.repository-webhook__status-value')?.textContent?.trim()).toBe('No webhook URL');
    expect(el.querySelector('.repository-webhook__hint')?.textContent?.replace(/\s+/g, ' ').trim()).toBe(
      'Foundry has no webhook URL yet. Set it in Dispatch settings; until then this repository polls at its normal interval.',
    );
    expect(el.textContent).not.toContain('Webhooks:Port');
    expect(el.querySelector('.repository-webhook__details')).toBeNull();
  });

  it('shows GitLab instructions with the secret token, triggers and the Test action', () => {
    // Arrange & Act
    const { el } = setup(GITLAB_NOT_REGISTERED);

    // Assert
    const terms = Array.from(el.querySelectorAll('.repository-webhook__details dt')).map(dt => dt.textContent?.trim());
    expect(terms).toEqual(['Payload URL', 'Secret token', 'Trigger']);
    expect(el.textContent).toContain(GITLAB_NOT_REGISTERED.payloadUrl!);
    const events = Array.from(el.querySelectorAll('.repository-webhook__events li')).map(li => li.textContent?.trim());
    expect(events).toEqual(GITLAB_NOT_REGISTERED.events);
    expect(el.querySelector('.repository-webhook__test-hint')?.textContent).toContain('"Test" action');
  });

  it('points GitLab users at the Test action while waiting for the first delivery', () => {
    // Arrange & Act
    const { el } = setup({ ...GITLAB_NOT_REGISTERED, status: 'pending' });

    // Assert
    expect(el.textContent).toContain('GitLab sends no ping');
    expect(el.textContent).not.toContain('redeliver the ping');
  });

  function registerButton(el: HTMLElement): HTMLButtonElement {
    return Array.from(el.querySelectorAll('button')).find(b => b.textContent?.includes('Register webhook'))!;
  }

  it('registers the webhook and shows the pending status', () => {
    // Arrange
    const { fixture, httpMock, el } = setup(NOT_REGISTERED);

    // Act
    registerButton(el).click();
    const request = httpMock.expectOne(`${BASE}/register`);
    request.flush({ webhook: { ...NOT_REGISTERED, status: 'pending' }, outcome: { kind: 'created' } });
    fixture.detectChanges();

    // Assert
    expect(request.request.method).toBe('POST');
    expect(el.querySelector('.repository-webhook__status-value')?.textContent?.trim()).toBe('Waiting for first delivery');
    const outcome = el.querySelector('.repository-webhook__outcome');
    expect(outcome?.textContent).toContain('Webhook created on GitHub');
    expect(outcome?.classList).toContain('repository-webhook__outcome--success');
  });

  it('keeps the manual instructions and explains a missing permission', () => {
    // Arrange
    const { fixture, httpMock, el } = setup(NOT_REGISTERED);

    // Act
    registerButton(el).click();
    httpMock.expectOne(`${BASE}/register`).flush({
      webhook: NOT_REGISTERED,
      outcome: { kind: 'failed', reason: 'insufficient-permission', detail: '403' },
    });
    fixture.detectChanges();

    // Assert
    expect(el.querySelector('.repository-webhook__outcome')?.textContent)
      .toContain('Webhooks (read and write) permission');
    expect(el.querySelector('.repository-webhook__details')).not.toBeNull();
  });

  it('asks for the Maintainer role on the account token on GitLab', () => {
    // Arrange
    const { fixture, httpMock, el } = setup(GITLAB_NOT_REGISTERED);

    // Act
    registerButton(el).click();
    httpMock.expectOne(`${BASE}/register`).flush({
      webhook: GITLAB_NOT_REGISTERED,
      outcome: { kind: 'failed', reason: 'insufficient-permission', detail: '403' },
    });
    fixture.detectChanges();

    // Assert
    expect(el.querySelector('.repository-webhook__outcome')?.textContent?.trim()).toBe(
      'The account token needs the Maintainer role to manage webhooks. Give the token that permission, or add the webhook manually.',
    );
  });

  it('asks to re-enter an unreadable account token', () => {
    // Arrange
    const { fixture, httpMock, el } = setup(NOT_REGISTERED);

    // Act
    registerButton(el).click();
    httpMock.expectOne(`${BASE}/register`).flush({
      webhook: NOT_REGISTERED,
      outcome: { kind: 'skipped', reason: 'token-unreadable' },
    });
    fixture.detectChanges();

    // Assert
    expect(el.querySelector('.repository-webhook__outcome')?.textContent?.trim()).toBe(
      "The account token can't be read. Re-enter it on the account, or add the webhook manually.",
    );
  });

  it('points at Dispatch settings when registration is skipped for a missing webhook URL', () => {
    // Arrange
    const { fixture, httpMock, el } = setup(NOT_REGISTERED);

    // Act
    registerButton(el).click();
    httpMock.expectOne(`${BASE}/register`).flush({
      webhook: NOT_REGISTERED,
      outcome: { kind: 'skipped', reason: 'not-configured' },
    });
    fixture.detectChanges();

    // Assert
    expect(el.querySelector('.repository-webhook__outcome')?.textContent?.trim()).toBe(
      'Foundry has no webhook URL yet, so nothing was registered. Set it in Dispatch settings.',
    );
  });
  it('says Foundry registers the webhook automatically and the action registers it now', () => {
    // Arrange & Act
    const { el } = setup(NOT_REGISTERED);

    // Assert
    expect(el.querySelector('.repository-webhook__register .repository-webhook__hint')?.textContent?.replace(/\s+/g, ' ').trim())
      .toBe("Foundry registers this webhook on GitHub automatically. Use this to create or update it now with the account's token.");
  });

  it('shows an error when the registration request fails', () => {
    // Arrange
    const { fixture, httpMock, el } = setup(NOT_REGISTERED);

    // Act
    registerButton(el).click();
    httpMock.expectOne(`${BASE}/register`).flush('boom', { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    // Assert
    expect(el.querySelector('.repository-webhook__error')?.textContent).toContain("Couldn't register the webhook");
  });

  it('offers no register action when webhooks are not configured', () => {
    // Arrange & Act
    const { el } = setup({ ...NOT_REGISTERED, status: 'not-configured', payloadUrl: null });

    // Assert
    expect(registerButton(el)).toBeUndefined();
  });

  describe('live updates', () => {
    const PENDING: RepositoryWebhookInfo = { ...NOT_REGISTERED, status: 'pending' };
    const ACTIVE: RepositoryWebhookInfo = { ...NOT_REGISTERED, status: 'active' };

    function announcement(el: HTMLElement): string {
      return el.querySelector('[aria-live="polite"]')?.textContent?.trim() ?? '';
    }

    function statusText(el: HTMLElement): string | undefined {
      return el.querySelector('.repository-webhook__status-value')?.textContent?.trim();
    }

    it('renders a re-fetched status for this repository and announces the change', () => {
      const { fixture, el, webhookUpdates } = setup(PENDING);

      webhookUpdates.next({ kind: 'repository', repositoryId: REPO_ID, info: ACTIVE });
      fixture.detectChanges();

      expect(statusText(el)).toBe('Active');
      expect(announcement(el)).toBe('Webhook status changed to Active');
    });

    it('leaves the announcement empty on first load', () => {
      const { el } = setup(PENDING);

      expect(announcement(el)).toBe('');
    });

    it('does not announce an update that keeps the status', () => {
      const { fixture, el, webhookUpdates } = setup(PENDING);

      webhookUpdates.next({ kind: 'repository', repositoryId: REPO_ID, info: PENDING });
      fixture.detectChanges();

      expect(announcement(el)).toBe('');
    });

    it('ignores an update for another repository', () => {
      const { fixture, el, webhookUpdates } = setup(PENDING);

      webhookUpdates.next({ kind: 'repository', repositoryId: 'other', info: ACTIVE });
      fixture.detectChanges();

      expect(statusText(el)).toBe('Waiting for first delivery');
    });

    it('keeps the revealed secret when a live update arrives', () => {
      const { fixture, httpMock, el, webhookUpdates } = setup(NOT_REGISTERED);
      Array.from(el.querySelectorAll('button')).find(b => b.textContent?.includes('Reveal secret'))!.click();
      httpMock.expectOne(`${BASE}/secret`).flush({ secret: 's3cr3t' });
      fixture.detectChanges();

      webhookUpdates.next({ kind: 'repository', repositoryId: REPO_ID, info: { ...NOT_REGISTERED, status: 'silent' } });
      fixture.detectChanges();

      expect(el.querySelector('.repository-webhook__secret')?.textContent?.trim()).toBe('s3cr3t');
    });

    it('re-fetches its own webhook when every repository changed', () => {
      const { fixture, httpMock, el, webhookUpdates } = setup(PENDING);

      webhookUpdates.next({ kind: 'all' });
      httpMock.expectOne(BASE).flush(ACTIVE);
      fixture.detectChanges();

      expect(statusText(el)).toBe('Active');
    });

    it('keeps the last known details without a message when a re-fetch fails', () => {
      const { fixture, httpMock, el, webhookUpdates } = setup(PENDING);
      const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);

      webhookUpdates.next({ kind: 'all' });
      httpMock.expectOne(BASE).flush('boom', { status: 500, statusText: 'Server Error' });
      fixture.detectChanges();

      expect(statusText(el)).toBe('Waiting for first delivery');
      expect(el.querySelector('[role="alert"]')?.textContent?.trim()).toBe('');
      consoleError.mockRestore();
    });

    it('clears a load error when a live update brings the details', () => {
      const { fixture, el, webhookUpdates } = setup('load-error');

      webhookUpdates.next({ kind: 'repository', repositoryId: REPO_ID, info: ACTIVE });
      fixture.detectChanges();

      expect(el.textContent).not.toContain("Couldn't load the webhook details.");
      expect(statusText(el)).toBe('Active');
    });

    it('moves focus to the heading when the focused control disappears', async () => {
      const { fixture, el, webhookUpdates } = setup(NOT_REGISTERED);
      document.body.appendChild(el);
      const marked = Array.from(el.querySelectorAll('button')).find(b => b.textContent?.includes("I've added the webhook"))!;
      marked.focus();
      expect(document.activeElement).toBe(marked);

      webhookUpdates.next({ kind: 'repository', repositoryId: REPO_ID, info: ACTIVE });
      fixture.detectChanges();
      await fixture.whenStable();

      expect(document.activeElement).toBe(el.querySelector('#repository-webhook-heading'));
      el.remove();
    });

    it('leaves focus alone when it was outside the panel', async () => {
      const { fixture, el, webhookUpdates } = setup(NOT_REGISTERED);
      document.body.appendChild(el);
      const outside = document.createElement('button');
      document.body.appendChild(outside);
      outside.focus();

      webhookUpdates.next({ kind: 'repository', repositoryId: REPO_ID, info: ACTIVE });
      fixture.detectChanges();
      await fixture.whenStable();

      expect(document.activeElement).toBe(outside);
      outside.remove();
      el.remove();
    });
  });
});

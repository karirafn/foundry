import { TestBed } from '@angular/core/testing';
import { HttpTestingController } from '@angular/common/http/testing';
import { API_KEY_RESPONSE, flushSettings, setupSettingsPage } from '../../settings/testing/settings-page-harness';
import { SetupWebhookStepComponent } from './setup-webhook-step';

const STORED_URL = 'https://hooks.example.com';
const NEW_URL = 'https://new.example.com';

// Problem type copied from the settings Dispatch spec, which mirrors the server's ProblemDetails type URI.
const UNREACHABLE_PROBLEM = {
  type: 'tag:foundry,2026:problems/Settings.WebhookPublicBaseUrlUnreachable',
  detail: 'new.example.com didn\'t answer within 10 seconds.',
};
const BAD_REQUEST = { status: 400, statusText: 'Bad Request' };
const SERVER_ERROR = { status: 500, statusText: 'Internal Server Error' };

interface StepOptions {
  settings?: Record<string, unknown> | 'error';
  showBack?: boolean;
}

function setup(options: StepOptions = {}) {
  const { fixture, httpMock } = setupSettingsPage(SetupWebhookStepComponent);
  if (options.settings === 'error') {
    httpMock.expectOne('/api/settings').flush('Server Error', SERVER_ERROR);
    httpMock.match('/api/credentials');
  } else {
    flushSettings(httpMock, { ...API_KEY_RESPONSE, webhookPublicBaseUrl: null, ...options.settings });
  }
  if (options.showBack !== undefined) {
    fixture.componentRef.setInput('showBack', options.showBack);
  }
  const completed: number[] = [];
  const backs: number[] = [];
  fixture.componentInstance.complete.subscribe(() => completed.push(1));
  fixture.componentInstance.back.subscribe(() => backs.push(1));
  fixture.detectChanges();

  const el = fixture.nativeElement as HTMLElement;
  const query = <T extends Element>(selector: string) => el.querySelector(selector) as T;
  return {
    fixture,
    httpMock,
    el,
    completed,
    backs,
    input: () => query<HTMLInputElement>('#setup-webhook-url'),
    continueBtn: () => query<HTMLButtonElement>('.setup-webhook-step__continue-btn'),
    backBtn: () => query<HTMLButtonElement>('.setup-webhook-step__back-btn'),
    fieldError: () => query<HTMLElement>('#setup-webhook-url-error').textContent?.trim(),
    saveError: () => query<HTMLElement>('.setup-webhook-step__save-error').textContent?.trim(),
    status: () => query<HTMLElement>('#setup-webhook-status').textContent?.trim(),
  };
}

function type(fixture: { detectChanges: () => void }, input: HTMLInputElement, value: string): void {
  input.value = value;
  input.dispatchEvent(new Event('input'));
  fixture.detectChanges();
}

async function settle(fixture: { whenStable: () => Promise<unknown>; detectChanges: () => void }): Promise<void> {
  fixture.detectChanges();
  await fixture.whenStable();
}

describe('SetupWebhookStepComponent', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  describe('settings unavailable', () => {
    it('should replace the port, guide and field with an alert', () => {
      // Arrange & Act
      const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
      const { el, input } = setup({ settings: 'error' });

      // Assert
      expect(el.querySelector('[role="alert"].setup-webhook-step__load-error')?.textContent)
        .toContain('Couldn\'t load Foundry\'s settings, so the webhook port is unknown.');
      expect(el.querySelector('.setup-webhook-step__port')).toBeNull();
      expect(el.querySelector('fd-webhook-expose-guide')).toBeNull();
      expect(input()).toBeNull();
      expect(consoleError).toHaveBeenCalledWith(expect.objectContaining({ status: 500, url: '/api/settings' }));
    });

    it('should reload the settings when Retry is clicked and then show the field', () => {
      // Arrange
      const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
      const { fixture, el, httpMock, input } = setup({ settings: 'error' });
      const retry = el.querySelector('.setup-webhook-step__retry-btn') as HTMLButtonElement;

      // Act
      retry.click();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, webhookPublicBaseUrl: STORED_URL });
      fixture.detectChanges();

      // Assert
      expect(input().value).toBe(STORED_URL);
      expect(consoleError).toHaveBeenCalledWith(expect.objectContaining({ status: 500, url: '/api/settings' }));
    });

    it('should still show Back', () => {
      // Arrange & Act
      const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
      const { backBtn } = setup({ settings: 'error' });

      // Assert
      expect(backBtn()).toBeTruthy();
      expect(consoleError).toHaveBeenCalledWith(expect.objectContaining({ status: 500, url: '/api/settings' }));
    });
  });

  describe('idle', () => {
    it('should show the webhook port named by its label', () => {
      // Arrange & Act
      const { el } = setup({ settings: { webhookPort: 6001 } });

      // Assert
      const value = el.querySelector('.setup-webhook-step__port-value') as HTMLElement;
      expect(value.textContent?.trim()).toBe('6001');
      expect(value.getAttribute('aria-labelledby')).toBe('setup-webhook-port-label');
      expect(el.querySelector('#setup-webhook-port-label')?.textContent?.trim()).toBe('Webhook port');
    });

    it('should show the expose guide for the webhook port', () => {
      // Arrange & Act
      const { el } = setup({ settings: { webhookPort: 6001 } });

      // Assert
      expect(el.querySelector('fd-webhook-expose-guide')?.textContent).toContain('ngrok http 6001');
    });

    it('should start with an empty field and "Verify and continue" on a first visit', () => {
      // Arrange & Act
      const { input, continueBtn } = setup();

      // Assert
      expect(input().value).toBe('');
      expect(continueBtn().textContent?.trim()).toBe('Verify and continue');
    });

    it('should prefill the stored URL and read "Continue"', () => {
      // Arrange & Act
      const { input, continueBtn } = setup({ settings: { webhookPublicBaseUrl: STORED_URL } });

      // Assert
      expect(input().value).toBe(STORED_URL);
      expect(continueBtn().textContent?.trim()).toBe('Continue');
    });

    it('should complete without a request when the stored URL is unchanged', () => {
      // Arrange
      const { continueBtn, completed, httpMock } = setup({ settings: { webhookPublicBaseUrl: STORED_URL } });

      // Act
      continueBtn().click();

      // Assert
      httpMock.expectNone('/api/settings/webhooks');
      expect(completed).toHaveLength(1);
    });

    it('should read "Verify and continue" once the stored URL is edited', () => {
      // Arrange
      const { fixture, input, continueBtn } = setup({ settings: { webhookPublicBaseUrl: STORED_URL } });

      // Act
      type(fixture, input(), NEW_URL);

      // Assert
      expect(continueBtn().textContent?.trim()).toBe('Verify and continue');
    });
  });

  describe('client-invalid', () => {
    it('should not send a request, show the error, mark the field invalid and focus it', async () => {
      // Arrange
      const { fixture, input, continueBtn, fieldError, httpMock } = setup();
      type(fixture, input(), 'http://hooks.example.com');

      // Act
      continueBtn().click();
      await settle(fixture);

      // Assert
      httpMock.expectNone('/api/settings/webhooks');
      expect(fieldError()).toBe('Use an absolute https URL, for example https://hooks.example.com.');
      expect(input().getAttribute('aria-invalid')).toBe('true');
      expect(document.activeElement).toBe(input());
    });

    it('should not show an error while typing before the first submit', () => {
      // Arrange
      const { fixture, input, fieldError } = setup();

      // Act
      type(fixture, input(), 'http://');

      // Assert
      expect(fieldError()).toBe('');
      expect(input().getAttribute('aria-invalid')).toBeNull();
    });

    it('should re-validate on every input after a failed submit', () => {
      // Arrange
      const { fixture, input, continueBtn, fieldError } = setup();
      continueBtn().click();
      fixture.detectChanges();
      expect(fieldError()).toBe('Enter the public https URL your tunnel or proxy serves.');

      // Act
      type(fixture, input(), NEW_URL);

      // Assert
      expect(fieldError()).toBe('');
      expect(input().getAttribute('aria-invalid')).toBeNull();
    });

    it('should keep the error span in the DOM and describe the field with hint and error', () => {
      // Arrange & Act
      const { el, input } = setup();

      // Assert
      expect(el.querySelector('#setup-webhook-url-error')).toBeTruthy();
      expect(input().getAttribute('aria-describedby')).toBe('setup-webhook-url-hint setup-webhook-url-error');
    });
  });

  describe('pending', () => {
    it('should save the trimmed URL with the stored backstop interval', () => {
      // Arrange
      const { fixture, input, continueBtn, httpMock } = setup({ settings: { backstopIntervalMinutes: 25 } });
      type(fixture, input(), `  ${NEW_URL} `);

      // Act
      continueBtn().click();

      // Assert
      const req = httpMock.expectOne('/api/settings/webhooks');
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual({ webhookPublicBaseUrl: NEW_URL, backstopIntervalMinutes: 25 });
      req.flush({ ...API_KEY_RESPONSE, webhookPublicBaseUrl: NEW_URL });
    });

    it('should send the default backstop interval when none is stored', () => {
      // Arrange
      const { fixture, input, continueBtn, httpMock } = setup({ settings: { backstopIntervalMinutes: null } });
      type(fixture, input(), NEW_URL);

      // Act
      continueBtn().click();

      // Assert
      const req = httpMock.expectOne('/api/settings/webhooks');
      expect(req.request.body.backstopIntervalMinutes).toBe(15);
      req.flush({ ...API_KEY_RESPONSE, webhookPublicBaseUrl: NEW_URL });
    });

    it('should show the busy state and ignore further clicks while verifying', () => {
      // Arrange
      const { fixture, el, input, continueBtn, backBtn, status, httpMock } = setup();
      type(fixture, input(), NEW_URL);

      // Act
      continueBtn().click();
      fixture.detectChanges();
      continueBtn().click();

      // Assert
      expect(continueBtn().textContent?.trim()).toBe('Verifying…');
      expect(continueBtn().querySelector('fd-spinner')).toBeTruthy();
      expect(continueBtn().disabled).toBe(false);
      expect(continueBtn().getAttribute('aria-disabled')).toBe('true');
      expect(input().readOnly).toBe(true);
      expect(backBtn().disabled).toBe(true);
      expect(el.querySelector('.setup-webhook-step')?.getAttribute('aria-busy')).toBe('true');
      expect(status()).toBe('Checking that new.example.com reaches this Foundry. This can take up to 10 seconds.');
      httpMock.expectOne('/api/settings/webhooks').flush({ ...API_KEY_RESPONSE, webhookPublicBaseUrl: NEW_URL });
    });
  });

  describe('server errors', () => {
    it('should show an unreachable error under the field, mark it invalid, clear the status and focus the field', async () => {
      // Arrange
      const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
      const { fixture, input, continueBtn, fieldError, status, completed, httpMock } = setup();
      type(fixture, input(), NEW_URL);

      // Act
      continueBtn().click();
      httpMock.expectOne('/api/settings/webhooks').flush(UNREACHABLE_PROBLEM, BAD_REQUEST);
      await settle(fixture);

      // Assert
      expect(fieldError()).toBe(UNREACHABLE_PROBLEM.detail);
      expect(input().getAttribute('aria-invalid')).toBe('true');
      expect(status()).toBe('');
      expect(document.activeElement).toBe(input());
      expect(continueBtn().textContent?.trim()).toBe('Verify and continue');
      expect(completed).toHaveLength(0);
      expect(consoleError).toHaveBeenCalledWith(expect.objectContaining({ status: 400, url: '/api/settings/webhooks' }));
    });

    it('should show an invalid-URL error from the server under the field', () => {
      // Arrange
      const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
      const { fixture, input, continueBtn, fieldError, httpMock } = setup();
      type(fixture, input(), 'https://localhost');

      // Act
      continueBtn().click();
      httpMock.expectOne('/api/settings/webhooks').flush({
        type: 'tag:foundry,2026:problems/Settings.InvalidWebhookPublicBaseUrl',
        detail: 'GitHub and GitLab can\'t deliver to this address.',
      }, BAD_REQUEST);
      fixture.detectChanges();

      // Assert
      expect(fieldError()).toBe('GitHub and GitLab can\'t deliver to this address.');
      expect(consoleError).toHaveBeenCalledWith(expect.objectContaining({ status: 400, url: '/api/settings/webhooks' }));
    });

    it('should hide a server error once the field is edited and show it again when the submitted URL returns', () => {
      // Arrange
      const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
      const { fixture, input, continueBtn, fieldError, httpMock } = setup();
      type(fixture, input(), NEW_URL);
      continueBtn().click();
      httpMock.expectOne('/api/settings/webhooks').flush(UNREACHABLE_PROBLEM, BAD_REQUEST);
      fixture.detectChanges();

      // Act
      type(fixture, input(), 'https://new.example.co');
      const afterEdit = fieldError();
      type(fixture, input(), ` ${NEW_URL}`);

      // Assert
      expect(afterEdit).toBe('');
      expect(fieldError()).toBe(UNREACHABLE_PROBLEM.detail);
      expect(consoleError).toHaveBeenCalledWith(expect.objectContaining({ status: 400, url: '/api/settings/webhooks' }));
    });

    it('should show any other error as a general alert with its detail', () => {
      // Arrange
      const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
      const { fixture, input, continueBtn, fieldError, saveError, httpMock } = setup();
      type(fixture, input(), NEW_URL);

      // Act
      continueBtn().click();
      httpMock.expectOne('/api/settings/webhooks').flush({ type: 'tag:foundry,2026:problems/Other', detail: 'Something broke.' }, SERVER_ERROR);
      fixture.detectChanges();

      // Assert
      expect(saveError()).toBe('Something broke.');
      expect(fieldError()).toBe('');
      expect(consoleError).toHaveBeenCalledWith(expect.objectContaining({ status: 500, url: '/api/settings/webhooks' }));
    });

    it('should fall back to a generic general error when the response has no detail', () => {
      // Arrange
      const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
      const { fixture, input, continueBtn, saveError, httpMock } = setup();
      type(fixture, input(), NEW_URL);

      // Act
      continueBtn().click();
      httpMock.expectOne('/api/settings/webhooks').flush('Server Error', SERVER_ERROR);
      fixture.detectChanges();

      // Assert
      expect(saveError()).toBe('Couldn\'t save the webhook URL. Please try again.');
      expect(consoleError).toHaveBeenCalledWith(expect.objectContaining({ status: 500, url: '/api/settings/webhooks' }));
    });
  });

  describe('success', () => {
    it('should complete once the URL verifies', () => {
      // Arrange
      const { fixture, input, continueBtn, completed, httpMock } = setup();
      type(fixture, input(), NEW_URL);

      // Act
      continueBtn().click();
      httpMock.expectOne('/api/settings/webhooks').flush({ ...API_KEY_RESPONSE, webhookPublicBaseUrl: NEW_URL });
      fixture.detectChanges();

      // Assert
      expect(completed).toHaveLength(1);
    });
  });

  describe('back', () => {
    it('should emit back when Back is clicked', () => {
      // Arrange
      const { backBtn, backs } = setup();

      // Act
      backBtn().click();

      // Assert
      expect(backs).toHaveLength(1);
    });

    it('should hide Back when showBack is false', () => {
      // Arrange & Act
      const { backBtn } = setup({ showBack: false });

      // Assert
      expect(backBtn()).toBeNull();
    });
  });

  it('should label the region by its focusable heading', () => {
    // Arrange & Act
    const { el } = setup();

    // Assert
    const heading = el.querySelector('#setup-webhook-title') as HTMLElement;
    expect(heading.textContent?.trim()).toBe('Expose Webhooks');
    expect(heading.getAttribute('tabindex')).toBe('-1');
    expect(el.querySelector('.setup-webhook-step')?.getAttribute('aria-labelledby')).toBe('setup-webhook-title');
  });
});

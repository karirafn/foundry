import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { NEVER } from 'rxjs';
import { SetupAuthStepComponent } from './setup-auth-step';
import { SystemSignalRService } from '../../../core/services/system-signalr.service';

const mockSystemSignalR = { reconnected: NEVER, reloadTrigger: NEVER, loginSessionUpdate: NEVER, notifications: signal([]).asReadonly() };

const CREDENTIALS_API_KEY_RESPONSE = {
  accountId: '00000000-0000-0000-0000-000000000001',
  authMode: 'ApiKey',
  apiKeyStatus: 'Present',
  oAuthStatus: 'NotConfigured',
  subscriptionType: null,
  oAuthAccountEmail: null,
  oAuthAccountOrgName: null,
};

const CREDENTIALS_API_KEY_NOT_CONFIGURED = {
  accountId: '00000000-0000-0000-0000-000000000001',
  authMode: 'ApiKey',
  apiKeyStatus: 'NotConfigured',
  oAuthStatus: 'NotConfigured',
  subscriptionType: null,
  oAuthAccountEmail: null,
  oAuthAccountOrgName: null,
};

const CREDENTIALS_API_KEY_UNREADABLE = {
  accountId: '00000000-0000-0000-0000-000000000001',
  authMode: 'ApiKey',
  apiKeyStatus: 'Unreadable',
  oAuthStatus: 'NotConfigured',
  subscriptionType: null,
  oAuthAccountEmail: null,
  oAuthAccountOrgName: null,
};

function setup() {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    imports: [SetupAuthStepComponent],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: SystemSignalRService, useValue: mockSystemSignalR },
    ],
  });

  const fixture = TestBed.createComponent(SetupAuthStepComponent);
  const httpMock = TestBed.inject(HttpTestingController);
  return { fixture, component: fixture.componentInstance, httpMock };
}

describe('SetupAuthStepComponent', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  // Cycle 1: renders radio toggle with neither selected, Next disabled
  it('should render a radio toggle with neither option selected and Next button disabled', () => {
    // Arrange
    const { fixture } = setup();

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const radios = el.querySelectorAll<HTMLInputElement>('input[type="radio"]');
    const button = el.querySelector('button') as HTMLButtonElement;

    expect(radios.length).toBe(2);
    expect(radios[0].checked).toBe(false);
    expect(radios[1].checked).toBe(false);
    expect(button.disabled).toBe(true);
  });

  // Cycle 2: no API key input visible before selecting a mode
  it('should not show API key input before selecting a mode', () => {
    // Arrange
    const { fixture } = setup();

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const input = el.querySelector('input[type="password"]');
    expect(input).toBeNull();
  });

  // Cycle 3: selecting API Key shows API key input
  it('should show the API key input after selecting API Key mode', () => {
    // Arrange
    const { fixture } = setup();
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const radios = el.querySelectorAll<HTMLInputElement>('input[type="radio"]');
    const apiKeyRadio = radios[0];
    apiKeyRadio.click();
    fixture.detectChanges();

    // Assert
    const input = el.querySelector('input[type="password"]');
    expect(input).toBeTruthy();
  });

  // Cycle 4: Next button disabled when API Key mode selected but input empty
  it('should keep Next button disabled when API Key selected but input is empty', () => {
    // Arrange
    const { fixture } = setup();
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const radios = el.querySelectorAll<HTMLInputElement>('input[type="radio"]');
    radios[0].click();
    fixture.detectChanges();

    // Assert
    const button = el.querySelector('button[class*="next-btn"]') as HTMLButtonElement;
    expect(button.disabled).toBe(true);
  });

  // Cycle 5: Next button enabled when API Key mode selected and input has a value
  it('should enable Next button when API Key selected and input has a value', () => {
    // Arrange
    const { fixture } = setup();
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const radios = el.querySelectorAll<HTMLInputElement>('input[type="radio"]');
    radios[0].click();
    fixture.detectChanges();

    const input = el.querySelector('input[type="password"]') as HTMLInputElement;
    input.value = 'sk-ant-test123';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Assert
    const button = el.querySelector('button[class*="next-btn"]') as HTMLButtonElement;
    expect(button.disabled).toBe(false);
  });

  // Cycle 6: Next button disabled while saving (API Key mode)
  it('should disable Next button while saving in API Key mode', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const radios = el.querySelectorAll<HTMLInputElement>('input[type="radio"]');
    radios[0].click();
    fixture.detectChanges();

    const input = el.querySelector('input[type="password"]') as HTMLInputElement;
    input.value = 'sk-ant-test123';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Act
    const button = el.querySelector('button[class*="next-btn"]') as HTMLButtonElement;
    button.click();
    fixture.detectChanges();

    // Assert
    expect(button.disabled).toBe(true);

    // Cleanup
    httpMock.expectOne('/api/credentials/auth').flush(CREDENTIALS_API_KEY_RESPONSE);
  });

  // Cycle 7: emits complete after successful API Key save
  it('should emit complete event after a successful API Key save', () => {
    // Arrange
    const { fixture, component, httpMock } = setup();
    fixture.detectChanges();

    let emitted = false;
    component.complete.subscribe(() => (emitted = true));

    const el = fixture.nativeElement as HTMLElement;
    const radios = el.querySelectorAll<HTMLInputElement>('input[type="radio"]');
    radios[0].click();
    fixture.detectChanges();

    const input = el.querySelector('input[type="password"]') as HTMLInputElement;
    input.value = 'sk-ant-test123';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Act
    const button = el.querySelector('button[class*="next-btn"]') as HTMLButtonElement;
    button.click();
    fixture.detectChanges();

    httpMock.expectOne('/api/credentials/auth').flush(CREDENTIALS_API_KEY_RESPONSE);
    fixture.detectChanges();

    // Assert
    expect(emitted).toBe(true);
  });

  // Cycle 8: shows error on API Key save failure
  it('should display an error message when the API Key save fails', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const radios = el.querySelectorAll<HTMLInputElement>('input[type="radio"]');
    radios[0].click();
    fixture.detectChanges();

    const input = el.querySelector('input[type="password"]') as HTMLInputElement;
    input.value = 'sk-ant-test123';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Act
    const button = el.querySelector('button[class*="next-btn"]') as HTMLButtonElement;
    button.click();
    fixture.detectChanges();

    httpMock.expectOne('/api/credentials/auth').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    fixture.detectChanges();

    // Assert
    const errorEl = el.querySelector('[role="alert"]');
    expect(errorEl?.textContent?.trim()).toBeTruthy();
  });

  // Cycle 9: does not emit complete on API Key save failure
  it('should not emit complete event when the API Key save fails', () => {
    // Arrange
    const { fixture, component, httpMock } = setup();
    fixture.detectChanges();

    let emitted = false;
    component.complete.subscribe(() => (emitted = true));

    const el = fixture.nativeElement as HTMLElement;
    const radios = el.querySelectorAll<HTMLInputElement>('input[type="radio"]');
    radios[0].click();
    fixture.detectChanges();

    const input = el.querySelector('input[type="password"]') as HTMLInputElement;
    input.value = 'sk-ant-test123';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Act
    const button = el.querySelector('button[class*="next-btn"]') as HTMLButtonElement;
    button.click();
    fixture.detectChanges();

    httpMock.expectOne('/api/credentials/auth').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    fixture.detectChanges();

    // Assert
    expect(emitted).toBe(false);
  });

  // Cycle 10: does not emit complete if saveSuccess is already true from prior navigation
  it('should not emit complete if saveSuccess is already true when the component initializes', () => {
    // Arrange
    const { fixture, component, httpMock } = setup();
    component['_settingsService'].saveSuccess.set(true);

    let emitted = false;
    component.complete.subscribe(() => (emitted = true));

    // Act
    fixture.detectChanges();

    // Assert
    expect(emitted).toBe(false);

    // Cleanup
    httpMock.expectNone('/api/credentials/auth');
  });

  // Cycle 11: selecting OAuth shows fd-oauth-panel
  it('should show fd-oauth-panel when OAuth mode is selected', () => {
    // Arrange
    const { fixture } = setup();
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const radios = el.querySelectorAll<HTMLInputElement>('input[type="radio"]');
    radios[1].click();
    fixture.detectChanges();

    // Assert
    const panel = el.querySelector('fd-oauth-panel');
    expect(panel).toBeTruthy();
  });

  // Cycle 12: Next button enabled immediately after selecting OAuth mode
  it('should enable Next button when OAuth mode is selected', () => {
    // Arrange
    const { fixture } = setup();
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const radios = el.querySelectorAll<HTMLInputElement>('input[type="radio"]');
    radios[1].click();
    fixture.detectChanges();

    // Assert
    const nextButton = el.querySelector('button[class*="next-btn"]') as HTMLButtonElement;
    expect(nextButton.disabled).toBe(false);
  });

  // Cycle 13: shows non-blocking warning note when OAuth status is not Present
  it('should show the not-logged-in note when OAuth status is NotConfigured', () => {
    // Arrange
    const { fixture } = setup();
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const radios = el.querySelectorAll<HTMLInputElement>('input[type="radio"]');
    radios[1].click();
    fixture.detectChanges();

    // Assert — role="note" is not a valid ARIA role; query by class instead
    const note = el.querySelector('.setup-auth-step__oauth-note');
    expect(note?.textContent).toContain("You haven't logged in yet");
  });

  // Cycle 14: no warning note when OAuth status is Present
  it('should not show the not-logged-in note when OAuth status is Present', () => {
    // Arrange
    const { fixture, component } = setup();
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const radios = el.querySelectorAll<HTMLInputElement>('input[type="radio"]');
    radios[1].click();
    fixture.detectChanges();

    // Simulate settings loaded with Present status
    const service = component['_settingsService'];
    service.authSettings.set({
      mode: 'oauth',
      apiKeyStatus: 'NotConfigured',
      apiKeyConfigured: false,
      oauth: { status: 'Present', subscriptionType: null },
      accountEmail: null,
      accountOrgName: null,
    });
    fixture.detectChanges();

    // Assert
    const note = el.querySelector('.setup-auth-step__oauth-note');
    expect(note).toBeFalsy();
  });

  // N3: role="note" is not a valid ARIA role
  it('should not render any element with role="note" (invalid ARIA role)', () => {
    // Arrange
    const { fixture } = setup();
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const radios = el.querySelectorAll<HTMLInputElement>('input[type="radio"]');
    radios[1].click();
    fixture.detectChanges();

    // Assert — no element carries the invalid role="note" attribute
    expect(el.querySelector('[role="note"]')).toBeFalsy();
  });

  // Cycle 15: emits complete on Next click in OAuth mode (no gating on status)
  it('should emit complete after clicking Next in OAuth mode', () => {
    // Arrange
    const { fixture, component } = setup();
    fixture.detectChanges();

    let emitted = false;
    component.complete.subscribe(() => (emitted = true));

    const el = fixture.nativeElement as HTMLElement;
    const radios = el.querySelectorAll<HTMLInputElement>('input[type="radio"]');
    radios[1].click();
    fixture.detectChanges();

    // Act
    const nextButton = el.querySelector('button[class*="next-btn"]') as HTMLButtonElement;
    nextButton.click();
    fixture.detectChanges();

    // Assert
    expect(emitted).toBe(true);
  });

  // Finding 4: focus moves to auth heading on Succeeded phase
  it('should move focus to the auth heading when login phase becomes Succeeded', async () => {
    // Arrange
    const { fixture } = setup();
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const radios = el.querySelectorAll<HTMLInputElement>('input[type="radio"]');
    radios[1].click();
    fixture.detectChanges();

    const service = fixture.componentInstance['_settingsService'];

    // Act — simulate Succeeded phase
    (service as unknown as { _loginPhaseSignal: { set: (v: string) => void } })._loginPhaseSignal.set('Succeeded');
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    // Assert — focus moves to auth heading
    const authHeading = el.querySelector('.setup-auth-step__title[tabindex="-1"]') as HTMLElement;
    expect(authHeading).toBeTruthy();
    expect(document.activeElement).toBe(authHeading);

    document.body.removeChild(fixture.nativeElement);
  });

  // Finding 5: Cancel returns focus to login button or auth heading
  it('should return focus to the Log in button on Cancel when in OAuth mode', async () => {
    // Arrange
    const { fixture } = setup();
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const radios = el.querySelectorAll<HTMLInputElement>('input[type="radio"]');
    radios[1].click();
    fixture.detectChanges();

    // Act — cancel login
    fixture.componentInstance.cancelLogin();
    await fixture.whenStable();
    fixture.detectChanges();

    // Assert — focus returns to Log in button (visible when NotConfigured)
    const loginBtn = el.querySelector('.oauth-panel__login-btn') as HTMLButtonElement;
    expect(loginBtn).toBeTruthy();
    expect(document.activeElement).toBe(loginBtn);

    document.body.removeChild(fixture.nativeElement);
  });

  describe('API-key status notes', () => {
    it('should render a muted note when apiKeyStatus is NotConfigured and API key mode is selected', () => {
      // Arrange
      const { fixture, httpMock } = setup();
      fixture.detectChanges();

      const el = fixture.nativeElement as HTMLElement;
      const radios = el.querySelectorAll<HTMLInputElement>('input[type="radio"]');
      radios[0].click();
      fixture.detectChanges();

      // Simulate service loading NotConfigured credentials
      const service = fixture.componentInstance['_settingsService'];
      service.loadSettings();
      httpMock.expectOne('/api/settings').flush({
        maxConcurrent: 3, timeoutMinutes: 60, probeIntervalMinutes: 60, pollIntervalSeconds: 30,
        usageLimitResetsAt: null, isDispatchPaused: false, autoResumeOnUsageReset: true,
        installDotnet: false, installAngular: false, installGlab: false, installGh: false,
        installChromium: false, installDocker: false, imageBuildStatus: 'Idle',
        lastImageBuildError: null, hasUsableImage: false, nextRetryAt: null, attempt: 0,
        systemPromptTemplate: null, workerPromptTemplate: null,
      });
      httpMock.expectOne('/api/credentials').flush(CREDENTIALS_API_KEY_NOT_CONFIGURED);
      fixture.detectChanges();

      // Act
      const note = el.querySelector('.setup-auth-step__oauth-note:not(.setup-auth-step__note--warning)');

      // Assert
      expect(note).toBeTruthy();
      expect(note?.textContent).toContain('No API key is set yet');
    });

    it('should render a warning note with role="status" when apiKeyStatus is Unreadable and API key mode is selected', () => {
      // Arrange
      const { fixture, httpMock } = setup();
      fixture.detectChanges();

      const el = fixture.nativeElement as HTMLElement;
      const radios = el.querySelectorAll<HTMLInputElement>('input[type="radio"]');
      radios[0].click();
      fixture.detectChanges();

      // Simulate service loading Unreadable credentials
      const service = fixture.componentInstance['_settingsService'];
      service.loadSettings();
      httpMock.expectOne('/api/settings').flush({
        maxConcurrent: 3, timeoutMinutes: 60, probeIntervalMinutes: 60, pollIntervalSeconds: 30,
        usageLimitResetsAt: null, isDispatchPaused: false, autoResumeOnUsageReset: true,
        installDotnet: false, installAngular: false, installGlab: false, installGh: false,
        installChromium: false, installDocker: false, imageBuildStatus: 'Idle',
        lastImageBuildError: null, hasUsableImage: false, nextRetryAt: null, attempt: 0,
        systemPromptTemplate: null, workerPromptTemplate: null,
      });
      httpMock.expectOne('/api/credentials').flush(CREDENTIALS_API_KEY_UNREADABLE);
      fixture.detectChanges();

      // Act — the Unreadable note region is always present; check content is shown
      const warningRegion = el.querySelector('.setup-auth-step__note--warning');

      // Assert — role="status" (polite) because this is steady-state, not action-triggered;
      // role="alert" remains only on the #api-key-error region for action-triggered errors.
      expect(warningRegion).toBeTruthy();
      expect(warningRegion?.getAttribute('role')).toBe('status');
      expect(warningRegion?.textContent).toContain("can't be read");
    });

    it('should render the alert SVG icon inside the Unreadable warning note', () => {
      // Arrange
      const { fixture, httpMock } = setup();
      fixture.detectChanges();

      const el = fixture.nativeElement as HTMLElement;
      const radios = el.querySelectorAll<HTMLInputElement>('input[type="radio"]');
      radios[0].click();
      fixture.detectChanges();

      const service = fixture.componentInstance['_settingsService'];
      service.loadSettings();
      httpMock.expectOne('/api/settings').flush({
        maxConcurrent: 3, timeoutMinutes: 60, probeIntervalMinutes: 60, pollIntervalSeconds: 30,
        usageLimitResetsAt: null, isDispatchPaused: false, autoResumeOnUsageReset: true,
        installDotnet: false, installAngular: false, installGlab: false, installGh: false,
        installChromium: false, installDocker: false, imageBuildStatus: 'Idle',
        lastImageBuildError: null, hasUsableImage: false, nextRetryAt: null, attempt: 0,
        systemPromptTemplate: null, workerPromptTemplate: null,
      });
      httpMock.expectOne('/api/credentials').flush(CREDENTIALS_API_KEY_UNREADABLE);
      fixture.detectChanges();

      // Act
      const warningRegion = el.querySelector('.setup-auth-step__note--warning');
      const svg = warningRegion?.querySelector('svg[aria-hidden="true"]');

      // Assert — alert-triangle SVG is present and hidden from AT (decorative)
      expect(svg).toBeTruthy();
    });

    it('should keep the Unreadable live-region wrapper in the DOM even when apiKeyStatus is NotConfigured (persistent pattern)', () => {
      // Arrange
      const { fixture, httpMock } = setup();
      fixture.detectChanges();

      const el = fixture.nativeElement as HTMLElement;
      const radios = el.querySelectorAll<HTMLInputElement>('input[type="radio"]');
      radios[0].click();
      fixture.detectChanges();

      const service = fixture.componentInstance['_settingsService'];
      service.loadSettings();
      httpMock.expectOne('/api/settings').flush({
        maxConcurrent: 3, timeoutMinutes: 60, probeIntervalMinutes: 60, pollIntervalSeconds: 30,
        usageLimitResetsAt: null, isDispatchPaused: false, autoResumeOnUsageReset: true,
        installDotnet: false, installAngular: false, installGlab: false, installGh: false,
        installChromium: false, installDocker: false, imageBuildStatus: 'Idle',
        lastImageBuildError: null, hasUsableImage: false, nextRetryAt: null, attempt: 0,
        systemPromptTemplate: null, workerPromptTemplate: null,
      });
      httpMock.expectOne('/api/credentials').flush(CREDENTIALS_API_KEY_NOT_CONFIGURED);
      fixture.detectChanges();

      // Act
      const warningRegion = el.querySelector('.setup-auth-step__note--warning');

      // Assert — wrapper always in DOM; empty content when status is not Unreadable
      expect(warningRegion).toBeTruthy();
      expect(warningRegion?.textContent?.trim()).toBe('');
    });

    it('should not have more than one role="alert" near the API key input (no double assertive announcement)', () => {
      // Arrange
      const { fixture, httpMock } = setup();
      fixture.detectChanges();

      const el = fixture.nativeElement as HTMLElement;
      const radios = el.querySelectorAll<HTMLInputElement>('input[type="radio"]');
      radios[0].click();
      fixture.detectChanges();

      const service = fixture.componentInstance['_settingsService'];
      service.loadSettings();
      httpMock.expectOne('/api/settings').flush({
        maxConcurrent: 3, timeoutMinutes: 60, probeIntervalMinutes: 60, pollIntervalSeconds: 30,
        usageLimitResetsAt: null, isDispatchPaused: false, autoResumeOnUsageReset: true,
        installDotnet: false, installAngular: false, installGlab: false, installGh: false,
        installChromium: false, installDocker: false, imageBuildStatus: 'Idle',
        lastImageBuildError: null, hasUsableImage: false, nextRetryAt: null, attempt: 0,
        systemPromptTemplate: null, workerPromptTemplate: null,
      });
      httpMock.expectOne('/api/credentials').flush(CREDENTIALS_API_KEY_UNREADABLE);
      fixture.detectChanges();

      // Act — count role="alert" elements in the API key section
      const apiKeySection = el.querySelector('.setup-auth-step__form') as HTMLElement;
      const alertEls = Array.from(apiKeySection.querySelectorAll('[role="alert"]'));

      // Assert — exactly one assertive alert region (#api-key-error); the Unreadable note uses role="status"
      expect(alertEls.length).toBe(1);
      expect(alertEls[0].id).toBe('api-key-error');
    });

    it('should not render any note when apiKeyStatus is Present and API key mode is selected', () => {
      // Arrange
      const { fixture, httpMock } = setup();
      fixture.detectChanges();

      const el = fixture.nativeElement as HTMLElement;
      const radios = el.querySelectorAll<HTMLInputElement>('input[type="radio"]');
      radios[0].click();
      fixture.detectChanges();

      // Simulate service loading Present credentials
      const service = fixture.componentInstance['_settingsService'];
      service.loadSettings();
      httpMock.expectOne('/api/settings').flush({
        maxConcurrent: 3, timeoutMinutes: 60, probeIntervalMinutes: 60, pollIntervalSeconds: 30,
        usageLimitResetsAt: null, isDispatchPaused: false, autoResumeOnUsageReset: true,
        installDotnet: false, installAngular: false, installGlab: false, installGh: false,
        installChromium: false, installDocker: false, imageBuildStatus: 'Idle',
        lastImageBuildError: null, hasUsableImage: false, nextRetryAt: null, attempt: 0,
        systemPromptTemplate: null, workerPromptTemplate: null,
      });
      httpMock.expectOne('/api/credentials').flush(CREDENTIALS_API_KEY_RESPONSE);
      fixture.detectChanges();

      // Act — Present should show no warning content in the persistent wrapper
      const warningRegion = el.querySelector('.setup-auth-step__note--warning');
      // The wrapper is always in the DOM (persistent live-region) but should have no inner content
      const svg = warningRegion?.querySelector('svg');
      const span = warningRegion?.querySelector('span');

      // Assert — wrapper present but inner content absent (hidden state)
      expect(warningRegion).toBeTruthy();
      expect(svg).toBeFalsy();
      expect(span).toBeFalsy();
    });
  });

  // Fix 1: aria-describedby on API key input includes the Unreadable note id
  it('should include api-key-unreadable-note in the API key input aria-describedby', () => {
    // Arrange
    const { fixture } = setup();
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const radios = el.querySelectorAll<HTMLInputElement>('input[type="radio"]');
    radios[0].click();
    fixture.detectChanges();

    // Act
    const input = el.querySelector<HTMLInputElement>('input[type="password"]');
    const describedBy = input?.getAttribute('aria-describedby') ?? '';

    // Assert — the input references both the error region and the unreadable note
    expect(describedBy).toContain('api-key-unreadable-note');
    expect(describedBy).toContain('api-key-error');
    // The referenced element must exist in the DOM (persistent live-region pattern)
    expect(el.querySelector('#api-key-unreadable-note')).toBeTruthy();
  });

  // Finding 9: startLoginError rendered in OAuth section
  it('should show startLoginError when loginPhase is null and error is non-null in OAuth mode', () => {
    // Arrange
    const { fixture } = setup();
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const radios = el.querySelectorAll<HTMLInputElement>('input[type="radio"]');
    radios[1].click();
    fixture.detectChanges();

    const service = fixture.componentInstance['_settingsService'];

    // Act — simulate startLogin POST failure
    service.startLogin();
    TestBed.inject(HttpTestingController).expectOne('/api/credentials/login/start').flush(
      'Server Error',
      { status: 500, statusText: 'Internal Server Error' }
    );
    fixture.detectChanges();

    // Assert — error displayed in role="alert"
    const alerts = Array.from(el.querySelectorAll('[role="alert"]')) as HTMLElement[];
    const errorAlert = alerts.find(a => a.textContent?.includes('Failed to start login'));
    expect(errorAlert).toBeTruthy();
  });
});


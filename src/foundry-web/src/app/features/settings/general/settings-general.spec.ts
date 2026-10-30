import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { By } from '@angular/platform-browser';
import { NgModel } from '@angular/forms';
import { signal } from '@angular/core';
import { NEVER } from 'rxjs';
import { SettingsGeneralComponent } from './settings-general';
import { SettingsService } from '../../../core/services/settings.service';
import { SystemSignalRService } from '../../../core/services/system-signalr.service';
import { RateBudgetService } from '../../../core/services/rate-budget.service';

const mockSystemSignalR = { reconnected: NEVER, reloadTrigger: NEVER, loginSessionUpdate: NEVER, notifications: signal([]).asReadonly() };
const mockRateBudgetService = { snapshot: signal(null).asReadonly() };

const IMAGE_FLAGS_DEFAULTS = {
  installDotnet: false,
  installAngular: false,
  installChromium: false,
  installDocker: false,
  imageBuildStatus: 'Idle',
  lastImageBuildError: null,
  hasUsableImage: false,
  nextRetryAt: null,
  attempt: 0,
};

const BASE_RESPONSE = {
  usageLimitResetsAt: null,
  isDispatchPaused: false,
  autoResumeOnUsageReset: true,
  ...IMAGE_FLAGS_DEFAULTS,
  systemPromptTemplate: null,
  workerPromptTemplate: null,
};

const API_KEY_RESPONSE = {
  maxConcurrent: 3,
  timeoutMinutes: 60,
  probeIntervalMinutes: 60,
  pollIntervalSeconds: 30,
  ...BASE_RESPONSE,
};

const CREDENTIALS_API_KEY = {
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

const CREDENTIALS_OAUTH = {
  accountId: '00000000-0000-0000-0000-000000000001',
  authMode: 'OAuth',
  oAuthStatus: 'Present',
  subscriptionType: 'pro',
  oAuthAccountEmail: 'user@example.com',
  oAuthAccountOrgName: null,
};

const CREDENTIALS_OAUTH_NOT_CONFIGURED = {
  accountId: '00000000-0000-0000-0000-000000000001',
  authMode: 'OAuth',
  oAuthStatus: 'NotConfigured',
  subscriptionType: null,
  oAuthAccountEmail: null,
  oAuthAccountOrgName: null,
};

const CREDENTIALS_OAUTH_RELOGIN_NEEDED = {
  accountId: '00000000-0000-0000-0000-000000000001',
  authMode: 'OAuth',
  oAuthStatus: 'ReLoginNeeded',
  subscriptionType: null,
  oAuthAccountEmail: null,
  oAuthAccountOrgName: null,
};

const OAUTH_RESPONSE = { ...API_KEY_RESPONSE };
const OAUTH_NOT_CONFIGURED_RESPONSE = { ...API_KEY_RESPONSE };
const OAUTH_RELOGIN_NEEDED_RESPONSE = { ...API_KEY_RESPONSE };

function setup() {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    imports: [SettingsGeneralComponent],
    providers: [
      SettingsService,
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: SystemSignalRService, useValue: mockSystemSignalR },
      { provide: RateBudgetService, useValue: mockRateBudgetService },
    ],
  });

  const service = TestBed.inject(SettingsService);
  service.loadSettings();
  const httpMock = TestBed.inject(HttpTestingController);

  return { service, httpMock };
}

function flushSettings(httpMock: HttpTestingController, settingsResponse: object = API_KEY_RESPONSE, credentialsResponse: object = CREDENTIALS_API_KEY): void {
  httpMock.expectOne('/api/settings').flush(settingsResponse);
  httpMock.expectOne('/api/credentials').flush(credentialsResponse);
}

describe('SettingsGeneralComponent', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
  });

  it('should render the "Worker Authentication" section title', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const titles = Array.from(el.querySelectorAll('.general-settings__section-title'));
    const authTitle = titles.find(t => t.textContent?.includes('Worker Authentication'));

    // Assert
    expect(authTitle).toBeTruthy();
  });

  it('should set tabindex="-1" on the Worker Authentication heading for focus management', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const authHeading = Array.from(el.querySelectorAll('.general-settings__section-title'))
      .find(h => h.textContent?.includes('Worker Authentication'));

    // Assert
    expect(authHeading?.getAttribute('tabindex')).toBe('-1');
  });

  it('should render API Key and OAuth radio options', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const radios = el.querySelectorAll('input[type="radio"]');

    // Assert
    expect(radios.length).toBe(2);
  });

  it('should render the API key input when mode is api_key', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const input = el.querySelector('.general-settings__api-key-input');

    // Assert
    expect(input).toBeTruthy();
  });

  it('should not render the API key input when mode is oauth', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_OAUTH);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const input = el.querySelector('.general-settings__api-key-input');

    // Assert
    expect(input).toBeFalsy();
  });

  it('should render fd-oauth-panel when mode is oauth', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_OAUTH);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const panel = el.querySelector('fd-oauth-panel');

    // Assert
    expect(panel).toBeTruthy();
  });

  it('should wrap the radio group in a fieldset with screen-reader legend', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const fieldset = el.querySelector('fieldset.general-settings__mode-fieldset');
    const legend = fieldset?.querySelector('legend');

    // Assert
    expect(fieldset).toBeTruthy();
    expect(legend?.classList.contains('sr-only')).toBe(true);
  });

  it('should set aria-invalid on the API key input when saveError is present', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    const service = TestBed.inject(SettingsService);
    service.updateAuthMode('api_key', 'bad-key');
    httpMock.expectOne('/api/credentials/auth').flush('Bad Request', {
      status: 400,
      statusText: 'Bad Request',
    });
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const input = el.querySelector('.general-settings__api-key-input') as HTMLInputElement;
    expect(input.getAttribute('aria-invalid')).toBe('true');
    const errorEl = el.querySelector('#api-key-error');
    expect(errorEl).toBeTruthy();
  });

  it('should call updateAuthMode when saveApiKey is invoked', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    const component = fixture.componentInstance as unknown as { _apiKeyValue: string };
    component._apiKeyValue = 'test-key';
    fixture.componentInstance.saveApiKey();

    // Assert
    const req = httpMock.expectOne('/api/credentials/auth');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ mode: 'api_key', apiKey: 'test-key' });
    req.flush(CREDENTIALS_API_KEY);
  });

  it('should show "Switch account" button when OAuth mode is active and status is Present', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_OAUTH);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const switchBtn = el.querySelector('.oauth-panel__switch-btn');

    // Assert
    expect(switchBtn).toBeTruthy();
  });

  it('should not show "Switch account" button when OAuth status is NotConfigured', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_OAUTH_NOT_CONFIGURED);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const switchBtn = el.querySelector('.oauth-panel__switch-btn');

    // Assert
    expect(switchBtn).toBeFalsy();
  });

  it('should show confirm group when "Switch account" is clicked', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_OAUTH);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const switchBtn = el.querySelector('.oauth-panel__switch-btn') as HTMLButtonElement;
    switchBtn.click();
    fixture.detectChanges();

    // Assert — uses role="group", not role="dialog"
    const group = el.querySelector('[role="group"][aria-label="Confirm account switch"]');
    expect(group?.textContent).toContain('Signing in will pause dispatch');
  });

  it('should use role="group" (not role="dialog") for the inline confirm', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_OAUTH);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const switchBtn = el.querySelector('.oauth-panel__switch-btn') as HTMLButtonElement;
    switchBtn.click();
    fixture.detectChanges();

    // Assert — no dialog role
    expect(el.querySelector('[role="dialog"]')).toBeFalsy();
    expect(el.querySelector('[role="group"]')).toBeTruthy();
  });

  it('should return focus to the auth heading when Cancel is clicked', async () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();
    flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_OAUTH);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const switchBtn = el.querySelector('.oauth-panel__switch-btn') as HTMLButtonElement;
    switchBtn.click();
    fixture.detectChanges();

    // Act
    const cancelBtn = el.querySelector('.general-settings__switch-cancel-btn') as HTMLButtonElement;
    cancelBtn.click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    // Assert — focus returns to auth heading (switch button is inside child panel, not accessible from host ViewChild)
    const authHeading = Array.from(el.querySelectorAll('.general-settings__section-title'))
      .find(h => h.textContent?.includes('Worker Authentication')) as HTMLElement;
    expect(document.activeElement).toBe(authHeading);

    document.body.removeChild(fixture.nativeElement);
  });

  it('should call pauseDispatch and startLogin when confirm button is clicked', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_OAUTH);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const switchBtn = el.querySelector('.oauth-panel__switch-btn') as HTMLButtonElement;
    switchBtn.click();
    fixture.detectChanges();

    // Act
    const confirmBtn = el.querySelector('.general-settings__switch-confirm-btn') as HTMLButtonElement;
    confirmBtn.click();
    fixture.detectChanges();

    // Assert — pauseDispatch fires and startLogin fires
    const pauseReq = httpMock.expectOne('/api/settings/dispatch/pause');
    expect(pauseReq.request.method).toBe('POST');
    pauseReq.flush(OAUTH_RESPONSE);

    const loginReq = httpMock.expectOne('/api/credentials/login/start');
    expect(loginReq.request.method).toBe('POST');
    loginReq.flush({ sessionId: 'test-session' });
  });

  it('should move focus to the auth heading after confirming account switch', async () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();
    flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_OAUTH);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const switchBtn = el.querySelector('.oauth-panel__switch-btn') as HTMLButtonElement;
    switchBtn.click();
    fixture.detectChanges();

    // Act
    const confirmBtn = el.querySelector('.general-settings__switch-confirm-btn') as HTMLButtonElement;
    confirmBtn.click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    httpMock.expectOne('/api/settings/dispatch/pause').flush(OAUTH_RESPONSE);
    httpMock.expectOne('/api/credentials/login/start').flush({ sessionId: 'test-session' });
    fixture.detectChanges();

    // Assert — focus moves to the auth heading, not lost to body
    const authHeading = Array.from(el.querySelectorAll('.general-settings__section-title'))
      .find(h => h.textContent?.includes('Worker Authentication')) as HTMLElement;
    expect(document.activeElement).toBe(authHeading);

    document.body.removeChild(fixture.nativeElement);
  });

  it('should announce drain text via text binding in the persistent role="status" element (not via @if content insertion)', () => {
    // Arrange — enter draining state
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_OAUTH);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;

    // Assert — the role="status" drain-gate element is present even before draining (empty)
    const drainGate = el.querySelector('.general-settings__drain-gate[role="status"]') as HTMLElement;
    expect(drainGate).toBeTruthy();
    expect(drainGate.textContent?.trim()).toBe('');

    // Act — trigger drain by confirming account switch
    const switchBtn = el.querySelector('.oauth-panel__switch-btn') as HTMLButtonElement;
    switchBtn.click();
    fixture.detectChanges();

    const confirmBtn = el.querySelector('.general-settings__switch-confirm-btn') as HTMLButtonElement;
    confirmBtn.click();
    fixture.detectChanges();
    httpMock.expectOne('/api/settings/dispatch/pause').flush(OAUTH_RESPONSE);
    httpMock.expectOne('/api/credentials/login/start').flush({ sessionId: 'test-session' });
    fixture.detectChanges();

    // Assert — same persistent element now has content (text binding changed, element was not re-mounted)
    const drainGateAfter = el.querySelector('.general-settings__drain-gate[role="status"]') as HTMLElement;
    expect(drainGateAfter).toBe(drainGate); // same DOM node
    expect(drainGateAfter.textContent).toContain('Dispatch is paused');
  });

  it('should show drain gate message after confirming account switch', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_OAUTH);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const switchBtn = el.querySelector('.oauth-panel__switch-btn') as HTMLButtonElement;
    switchBtn.click();
    fixture.detectChanges();

    // Act
    const confirmBtn = el.querySelector('.general-settings__switch-confirm-btn') as HTMLButtonElement;
    confirmBtn.click();
    fixture.detectChanges();
    httpMock.expectOne('/api/settings/dispatch/pause').flush(OAUTH_RESPONSE);
    httpMock.expectOne('/api/credentials/login/start').flush({ sessionId: 'test-session' });
    fixture.detectChanges();

    // Assert
    const drainGate = el.querySelector('.general-settings__drain-gate');
    expect(drainGate?.textContent).toContain('Dispatch is paused');
  });

  it('should keep the drain-gate role="status" wrapper in the DOM when OAuth mode is active', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_OAUTH);
    fixture.detectChanges();

    // Assert — the live-region wrapper must always be present (empty when not draining)
    const el = fixture.nativeElement as HTMLElement;
    const drainStatus = el.querySelector('.general-settings__drain-gate[role="status"]');
    expect(drainStatus).toBeTruthy();
    expect(drainStatus?.textContent?.trim()).toBe('');
  });

  it('should render the switch-error role="alert" as a sibling of drain-gate, not nested inside it', () => {
    // Arrange — switch account and fail the pause request to trigger a pauseResumeError
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_OAUTH);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const switchBtn = el.querySelector('.oauth-panel__switch-btn') as HTMLButtonElement;
    switchBtn.click();
    fixture.detectChanges();

    const confirmBtn = el.querySelector('.general-settings__switch-confirm-btn') as HTMLButtonElement;
    confirmBtn.click();
    fixture.detectChanges();
    // Fail the pause so pauseResumeError signal is set
    httpMock.expectOne('/api/settings/dispatch/pause').flush('Server Error', { status: 500, statusText: 'Internal Server Error' });
    httpMock.expectOne('/api/credentials/login/start').flush({ sessionId: 'test-session' });
    fixture.detectChanges();

    // Assert — role="alert" must NOT be a descendant of role="status"
    const drainGate = el.querySelector('.general-settings__drain-gate[role="status"]');
    const alertInsideDrainGate = drainGate?.querySelector('[role="alert"]');
    expect(alertInsideDrainGate).toBeFalsy();

    // Assert — a sibling role="alert" for the error should exist outside the drain-gate
    const oauthSection = el.querySelector('.general-settings__oauth-section');
    const siblingAlert = oauthSection?.querySelector('.general-settings__switch-error[role="alert"]');
    expect(siblingAlert).toBeTruthy();
  });

  it('should show confirm dialog when "Log in again" is clicked (ReLoginNeeded status)', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_OAUTH_RELOGIN_NEEDED);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const loginAgainBtn = el.querySelector('.oauth-panel__login-btn') as HTMLButtonElement;
    loginAgainBtn.click();
    fixture.detectChanges();

    // Assert — confirm group appears (same drain flow as Switch account)
    const group = el.querySelector('[role="group"][aria-label="Confirm account switch"]');
    expect(group).toBeTruthy();
    expect(group?.textContent).toContain('Signing in will pause dispatch');
  });

  it('should reset drain-gate state when login phase becomes Succeeded', () => {
    // Arrange — enter draining state via switch account
    const { httpMock, service } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_OAUTH);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const switchBtn = el.querySelector('.oauth-panel__switch-btn') as HTMLButtonElement;
    switchBtn.click();
    fixture.detectChanges();

    const confirmBtn = el.querySelector('.general-settings__switch-confirm-btn') as HTMLButtonElement;
    confirmBtn.click();
    fixture.detectChanges();
    httpMock.expectOne('/api/settings/dispatch/pause').flush(OAUTH_RESPONSE);
    httpMock.expectOne('/api/credentials/login/start').flush({ sessionId: 'test-session' });
    fixture.detectChanges();

    // Verify draining
    let drainGate = el.querySelector('.general-settings__drain-gate');
    expect(drainGate?.textContent).toContain('Dispatch is paused');

    // Act — simulate the service clearing loginPhase to null after settings reload (Finding 1 fix)
    (service as unknown as { _loginPhaseSignal: { set: (v: string | null) => void } })._loginPhaseSignal.set(null);
    fixture.detectChanges();

    // Assert — drain gate resets
    drainGate = el.querySelector('.general-settings__drain-gate');
    expect(drainGate?.textContent?.trim()).toBe('');
  });

  it('should show Resume button when login fails while switch-account drain is active (Finding 2)', () => {
    // Arrange — enter draining state
    const { httpMock, service } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_OAUTH);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const switchBtn = el.querySelector('.oauth-panel__switch-btn') as HTMLButtonElement;
    switchBtn.click();
    fixture.detectChanges();

    const confirmBtn = el.querySelector('.general-settings__switch-confirm-btn') as HTMLButtonElement;
    confirmBtn.click();
    fixture.detectChanges();
    httpMock.expectOne('/api/settings/dispatch/pause').flush(OAUTH_RESPONSE);
    httpMock.expectOne('/api/credentials/login/start').flush({ sessionId: 'test-session' });
    fixture.detectChanges();

    // Act — simulate Failed phase while still draining
    (service as unknown as { _loginPhaseSignal: { set: (v: string) => void } })._loginPhaseSignal.set('Failed');
    fixture.detectChanges();

    // Assert — Resume button is visible and is NOT nested inside the drain-gate live region
    const resumeBtn = el.querySelector('.general-settings__resume-btn');
    expect(resumeBtn).toBeTruthy();

    const drainGate = el.querySelector('.general-settings__drain-gate[role="status"]');
    const resumeInsideDrainGate = drainGate?.querySelector('.general-settings__resume-btn');
    expect(resumeInsideDrainGate).toBeFalsy();
  });

  it('should reset drain-gate state when startLogin POST fails while draining (Finding 6)', () => {
    // Arrange — enter draining state
    const { httpMock, service } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_OAUTH);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const switchBtn = el.querySelector('.oauth-panel__switch-btn') as HTMLButtonElement;
    switchBtn.click();
    fixture.detectChanges();

    const confirmBtn = el.querySelector('.general-settings__switch-confirm-btn') as HTMLButtonElement;
    confirmBtn.click();
    fixture.detectChanges();
    httpMock.expectOne('/api/settings/dispatch/pause').flush(OAUTH_RESPONSE);

    // Fail the login start POST
    httpMock.expectOne('/api/credentials/login/start').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    fixture.detectChanges();

    // Assert — drain gate is cleared (not stranded)
    const drainGate = el.querySelector('.general-settings__drain-gate');
    expect(drainGate?.textContent?.trim()).toBe('');
  });

  it('should move focus to auth heading when login phase becomes Succeeded (Finding 4)', async () => {
    // Arrange
    const { httpMock, service } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();
    flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_OAUTH);
    fixture.detectChanges();

    // Act — simulate Succeeded phase
    (service as unknown as { _loginPhaseSignal: { set: (v: string) => void } })._loginPhaseSignal.set('Succeeded');
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    // Assert — focus moves to auth heading
    const el = fixture.nativeElement as HTMLElement;
    const authHeading = Array.from(el.querySelectorAll('.general-settings__section-title'))
      .find(h => h.textContent?.includes('Worker Authentication')) as HTMLElement;
    expect(document.activeElement).toBe(authHeading);

    document.body.removeChild(fixture.nativeElement);
  });

  it('should show startLoginError in a role="alert" when loginPhase is null and error is set', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_OAUTH_NOT_CONFIGURED);
    fixture.detectChanges();

    // Act — startLogin fails (POST 500)
    const service = TestBed.inject(SettingsService);
    service.startLogin();
    httpMock.expectOne('/api/credentials/login/start').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    fixture.detectChanges();

    // Assert — startLoginError displayed
    const el = fixture.nativeElement as HTMLElement;
    const alerts = Array.from(el.querySelectorAll('[role="alert"]')) as HTMLElement[];
    const startLoginAlert = alerts.find(a => a.textContent?.includes('Failed to start login'));
    expect(startLoginAlert).toBeTruthy();
  });

  it('should focus the entry login button on cancel when login flow is not in drain state', async () => {
    // Arrange — Not-Configured state, no drain gate, login button is visible
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();
    flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_OAUTH_NOT_CONFIGURED);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;

    // Act — cancel login from an active phase (simulate by calling onCancelLogin directly)
    fixture.componentInstance.onCancelLogin();
    await fixture.whenStable();
    fixture.detectChanges();

    // Assert — focus goes to the Log in entry button (visible when not draining)
    const loginBtn = el.querySelector('.oauth-panel__login-btn') as HTMLButtonElement;
    expect(loginBtn).toBeTruthy();
    expect(document.activeElement).toBe(loginBtn);

    document.body.removeChild(fixture.nativeElement);
  });

  it('should focus auth heading on cancel when no entry login button is reachable', async () => {
    // Arrange — OAuth not yet selected (api_key mode, so no oauth-panel__login-btn or switch-btn visible)
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;

    // Act — cancel login; no oauth panel rendered so no entry button exists
    fixture.componentInstance.onCancelLogin();
    await fixture.whenStable();
    fixture.detectChanges();

    // Assert — falls back to auth heading
    const authHeading = Array.from(el.querySelectorAll('.general-settings__section-title'))
      .find(h => h.textContent?.includes('Worker Authentication')) as HTMLElement;
    expect(document.activeElement).toBe(authHeading);

    document.body.removeChild(fixture.nativeElement);
  });

  it('should render the "Worker Prompts" section title', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const headings = Array.from(el.querySelectorAll('h2'));
    const promptsHeading = headings.find(h => h.textContent?.trim() === 'Worker Prompts');

    // Assert
    expect(promptsHeading).toBeTruthy();
  });

  it('should render system prompt and worker prompt textareas with correct ids', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const systemTextarea = el.querySelector('#systemPromptTemplate') as HTMLTextAreaElement;
    const workerTextarea = el.querySelector('#workerPromptTemplate') as HTMLTextAreaElement;

    // Assert
    expect(systemTextarea).toBeTruthy();
    expect(workerTextarea).toBeTruthy();
    expect(systemTextarea.tagName).toBe('TEXTAREA');
    expect(workerTextarea.tagName).toBe('TEXTAREA');
  });

  it('should have labels with correct "for" attributes matching textarea ids', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const systemLabel = el.querySelector('label[for="systemPromptTemplate"]');
    const workerLabel = el.querySelector('label[for="workerPromptTemplate"]');

    // Assert
    expect(systemLabel).toBeTruthy();
    expect(workerLabel).toBeTruthy();
  });

  it('should set rows="6" on system prompt textarea and rows="4" on worker prompt textarea', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const systemTextarea = el.querySelector('#systemPromptTemplate') as HTMLTextAreaElement;
    const workerTextarea = el.querySelector('#workerPromptTemplate') as HTMLTextAreaElement;

    // Assert
    expect(systemTextarea.rows).toBe(6);
    expect(workerTextarea.rows).toBe(4);
  });

  it('should set aria-describedby="prompts-error" on both textareas', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const systemTextarea = el.querySelector('#systemPromptTemplate') as HTMLTextAreaElement;
    const workerTextarea = el.querySelector('#workerPromptTemplate') as HTMLTextAreaElement;

    // Assert
    expect(systemTextarea.getAttribute('aria-describedby')).toBe('prompts-error');
    expect(workerTextarea.getAttribute('aria-describedby')).toBe('prompts-error');
  });

  it('should call updatePromptTemplates with current values when Save is clicked', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock, {
      ...API_KEY_RESPONSE,
      systemPromptTemplate: 'System prompt text',
      workerPromptTemplate: 'Worker prompt text',
    });
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const promptsForm = el.querySelector('.general-settings__prompts-form') as HTMLElement;
    const saveBtn = promptsForm.querySelector('.general-settings__save-btn') as HTMLButtonElement;
    saveBtn.click();

    // Assert
    const req = httpMock.expectOne('/api/settings/prompts');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({
      systemPromptTemplate: 'System prompt text',
      workerPromptTemplate: 'Worker prompt text',
    });
    req.flush({ ...API_KEY_RESPONSE, systemPromptTemplate: 'System prompt text', workerPromptTemplate: 'Worker prompt text' });
  });

  it('should show "Prompt templates saved successfully" after a successful prompts save', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    const service = TestBed.inject(SettingsService);
    service.updatePromptTemplates({ systemPromptTemplate: 'sys', workerPromptTemplate: 'worker' });
    httpMock.expectOne('/api/settings/prompts').flush({
      ...API_KEY_RESPONSE,
      systemPromptTemplate: 'sys',
      workerPromptTemplate: 'worker',
    });
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const successEls = Array.from(el.querySelectorAll('[role="status"]'));
    const promptsSuccess = successEls.find(e => e.textContent?.includes('Prompt templates saved successfully'));
    expect(promptsSuccess).toBeTruthy();
  });

  it('should show error message when prompts save fails', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    const service = TestBed.inject(SettingsService);
    service.updatePromptTemplates({ systemPromptTemplate: 'sys', workerPromptTemplate: 'worker' });
    httpMock.expectOne('/api/settings/prompts').flush('Bad Request', {
      status: 400,
      statusText: 'Bad Request',
    });
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const errorEl = el.querySelector('#prompts-error');
    expect(errorEl).toBeTruthy();
    expect(errorEl?.getAttribute('role')).toBe('alert');
    expect(errorEl?.textContent).toContain('Failed to save prompt templates');
  });

  it('should disable the Save button and show "Saving..." while prompts are saving', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    const service = TestBed.inject(SettingsService);
    service.updatePromptTemplates({ systemPromptTemplate: 'sys', workerPromptTemplate: 'worker' });
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const promptsForm = el.querySelector('.general-settings__prompts-form') as HTMLElement;
    const saveBtn = promptsForm.querySelector('.general-settings__save-btn') as HTMLButtonElement;
    expect(saveBtn.disabled).toBe(true);
    expect(saveBtn.textContent?.trim()).toBe('Saving...');

    httpMock.expectOne('/api/settings/prompts').flush({
      ...API_KEY_RESPONSE,
      systemPromptTemplate: 'sys',
      workerPromptTemplate: 'worker',
    });
  });

  it('should initialize textarea values from loaded settings', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock, {
      ...API_KEY_RESPONSE,
      systemPromptTemplate: 'System prompt text',
      workerPromptTemplate: 'Worker prompt text',
    });
    fixture.detectChanges();

    // Act
    const systemNgModel = fixture.debugElement.query(By.css('#systemPromptTemplate')).injector.get(NgModel);
    const workerNgModel = fixture.debugElement.query(By.css('#workerPromptTemplate')).injector.get(NgModel);

    // Assert
    expect(systemNgModel.model).toBe('System prompt text');
    expect(workerNgModel.model).toBe('Worker prompt text');
  });

  describe('Provider Hosts section', () => {
    it('should render the "Provider Hosts" section title', () => {
      // Arrange
      const { httpMock } = setup();
      const fixture = TestBed.createComponent(SettingsGeneralComponent);
      fixture.detectChanges();
      flushSettings(httpMock);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const headings = Array.from(el.querySelectorAll('h2'));
      const hostsHeading = headings.find(h => h.textContent?.trim() === 'Provider Hosts');

      // Assert
      expect(hostsHeading).toBeTruthy();
    });

    it('should render the allowedProviderHosts textarea with id="allowedProviderHosts"', () => {
      // Arrange
      const { httpMock } = setup();
      const fixture = TestBed.createComponent(SettingsGeneralComponent);
      fixture.detectChanges();
      flushSettings(httpMock);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const textarea = el.querySelector('#allowedProviderHosts') as HTMLTextAreaElement;

      // Assert
      expect(textarea).toBeTruthy();
      expect(textarea.tagName).toBe('TEXTAREA');
    });

    it('should seed the textarea with allowedProviderHosts joined by newlines', () => {
      // Arrange
      const { httpMock } = setup();
      const fixture = TestBed.createComponent(SettingsGeneralComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, allowedProviderHosts: ['git.example.com', 'gitlab.internal.corp'] });
      fixture.detectChanges();

      // Act
      const hostsNgModel = fixture.debugElement.query(By.css('#allowedProviderHosts')).injector.get(NgModel);

      // Assert
      expect(hostsNgModel.model).toBe('git.example.com\ngitlab.internal.corp');
    });

    it('should seed the textarea with empty string when allowedProviderHosts is empty', () => {
      // Arrange
      const { httpMock } = setup();
      const fixture = TestBed.createComponent(SettingsGeneralComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, allowedProviderHosts: [] });
      fixture.detectChanges();

      // Act
      const hostsNgModel = fixture.debugElement.query(By.css('#allowedProviderHosts')).injector.get(NgModel);

      // Assert
      expect(hostsNgModel.model).toBe('');
    });

    it('should call updateAllowedProviderHosts with parsed array when Save is clicked', () => {
      // Arrange
      const { httpMock } = setup();
      const fixture = TestBed.createComponent(SettingsGeneralComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, allowedProviderHosts: [] });
      fixture.detectChanges();

      // Act — set textarea value and click Save
      const component = fixture.componentInstance as unknown as { _allowedHostsValue: { set: (v: string) => void } };
      component._allowedHostsValue.set('git.example.com\ngitlab.internal.corp');
      fixture.detectChanges();

      const el = fixture.nativeElement as HTMLElement;
      const hostsForm = el.querySelector('.general-settings__hosts-form') as HTMLElement;
      const saveBtn = hostsForm.querySelector('.general-settings__save-btn') as HTMLButtonElement;
      saveBtn.click();

      // Assert
      const req = httpMock.expectOne('/api/settings/allowed-provider-hosts');
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual({ hosts: ['git.example.com', 'gitlab.internal.corp'] });
      req.flush({ ...API_KEY_RESPONSE, allowedProviderHosts: ['git.example.com', 'gitlab.internal.corp'] });
    });

    it('should split newline and comma-separated input, trim, and drop blanks', () => {
      // Arrange
      const { httpMock } = setup();
      const fixture = TestBed.createComponent(SettingsGeneralComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, allowedProviderHosts: [] });
      fixture.detectChanges();

      // Act
      const component = fixture.componentInstance as unknown as { _allowedHostsValue: { set: (v: string) => void } };
      component._allowedHostsValue.set('  git.example.com , \n  gitlab.internal.corp  \n\n  ');
      fixture.detectChanges();

      const el = fixture.nativeElement as HTMLElement;
      const hostsForm = el.querySelector('.general-settings__hosts-form') as HTMLElement;
      const saveBtn = hostsForm.querySelector('.general-settings__save-btn') as HTMLButtonElement;
      saveBtn.click();

      // Assert
      const req = httpMock.expectOne('/api/settings/allowed-provider-hosts');
      expect(req.request.body).toEqual({ hosts: ['git.example.com', 'gitlab.internal.corp'] });
      req.flush({ ...API_KEY_RESPONSE, allowedProviderHosts: ['git.example.com', 'gitlab.internal.corp'] });
    });

    it('should disable the Save button while savingHosts() is true', () => {
      // Arrange
      const { httpMock, service } = setup();
      const fixture = TestBed.createComponent(SettingsGeneralComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, allowedProviderHosts: [] });
      fixture.detectChanges();

      // Act — trigger a save to put service in saving state
      service.updateAllowedProviderHosts(['git.example.com']);
      fixture.detectChanges();

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      const hostsForm = el.querySelector('.general-settings__hosts-form') as HTMLElement;
      const saveBtn = hostsForm.querySelector('.general-settings__save-btn') as HTMLButtonElement;
      expect(saveBtn.disabled).toBe(true);
      expect(saveBtn.textContent?.trim()).toBe('Saving...');

      httpMock.expectOne('/api/settings/allowed-provider-hosts').flush({ ...API_KEY_RESPONSE, allowedProviderHosts: ['git.example.com'] });
    });

    it('should render saveHostsError() text in the error region', () => {
      // Arrange
      const { httpMock, service } = setup();
      const fixture = TestBed.createComponent(SettingsGeneralComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, allowedProviderHosts: [] });
      fixture.detectChanges();

      // Act — trigger a failed save
      service.updateAllowedProviderHosts(['bad host!']);
      httpMock.expectOne('/api/settings/allowed-provider-hosts').flush(
        { type: 'tag:foundry,2026:problems/Settings.InvalidHost', title: 'Bad Request', status: 400, detail: 'Host "bad host!" is invalid' },
        { status: 400, statusText: 'Bad Request' }
      );
      fixture.detectChanges();

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      const errorEl = el.querySelector('#allowed-provider-hosts-error') as HTMLElement;
      expect(errorEl).toBeTruthy();
      expect(errorEl.getAttribute('role')).toBe('alert');
      expect(errorEl.textContent).toContain('bad host!');
    });

    it('should render success message in aria-live="polite" region after a successful hosts save', () => {
      // Arrange
      const { httpMock, service } = setup();
      const fixture = TestBed.createComponent(SettingsGeneralComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, allowedProviderHosts: [] });
      fixture.detectChanges();

      // Act
      service.updateAllowedProviderHosts(['git.example.com']);
      httpMock.expectOne('/api/settings/allowed-provider-hosts').flush({ ...API_KEY_RESPONSE, allowedProviderHosts: ['git.example.com'] });
      fixture.detectChanges();

      // Assert — success region uses role="status" matching the established sibling pattern
      const el = fixture.nativeElement as HTMLElement;
      const hostsForm = el.querySelector('.general-settings__hosts-form') as HTMLElement;
      const successEl = hostsForm.querySelector('[role="status"].general-settings__save-success') as HTMLElement;
      expect(successEl).toBeTruthy();
      expect(successEl.textContent).toContain('Provider hosts saved successfully');
    });

    it('should set aria-invalid on the allowedProviderHosts textarea when saveHostsError is present', () => {
      // Arrange
      const { httpMock, service } = setup();
      const fixture = TestBed.createComponent(SettingsGeneralComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, allowedProviderHosts: [] });
      fixture.detectChanges();

      // Act — trigger a failed save
      service.updateAllowedProviderHosts(['bad host!']);
      httpMock.expectOne('/api/settings/allowed-provider-hosts').flush('Host "bad host!" is invalid', {
        status: 400,
        statusText: 'Bad Request',
      });
      fixture.detectChanges();

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      const textarea = el.querySelector('#allowedProviderHosts') as HTMLTextAreaElement;
      expect(textarea.getAttribute('aria-invalid')).toBe('true');
    });

    it('should not set aria-invalid on the allowedProviderHosts textarea when there is no error', () => {
      // Arrange
      const { httpMock } = setup();
      const fixture = TestBed.createComponent(SettingsGeneralComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, allowedProviderHosts: [] });
      fixture.detectChanges();

      // Assert — no error, attribute must be absent
      const el = fixture.nativeElement as HTMLElement;
      const textarea = el.querySelector('#allowedProviderHosts') as HTMLTextAreaElement;
      expect(textarea.getAttribute('aria-invalid')).toBeNull();
    });

    it('should display hint text mentioning comma-separated input and 50-hostname cap', () => {
      // Arrange
      const { httpMock } = setup();
      const fixture = TestBed.createComponent(SettingsGeneralComponent);
      fixture.detectChanges();
      flushSettings(httpMock);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const hint = el.querySelector('#allowed-provider-hosts-hint') as HTMLElement;

      // Assert
      expect(hint).toBeTruthy();
      expect(hint.textContent).toContain('comma-separated');
      expect(hint.textContent).toContain('Maximum 50 hostnames');
    });
  });

  describe('API-key status indicator', () => {
    it('should render the configured-indicator when apiKeyStatus is Present', () => {
      // Arrange
      const { httpMock } = setup();
      const fixture = TestBed.createComponent(SettingsGeneralComponent);
      fixture.detectChanges();
      flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_API_KEY);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const indicator = el.querySelector('#api-key-configured .general-settings__configured-indicator');

      // Assert
      expect(indicator).toBeTruthy();
      expect(indicator?.textContent).toContain('API key is configured');
    });

    it('should render the muted hint when apiKeyStatus is NotConfigured', () => {
      // Arrange
      const { httpMock } = setup();
      const fixture = TestBed.createComponent(SettingsGeneralComponent);
      fixture.detectChanges();
      flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_API_KEY_NOT_CONFIGURED);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const hint = el.querySelector('#api-key-configured .general-settings__field-hint--credential-status');

      // Assert
      expect(hint).toBeTruthy();
      expect(hint?.textContent).toContain('No API key is set');
    });

    it('should render the warning box with role="alert" when apiKeyStatus is Unreadable', () => {
      // Arrange
      const { httpMock } = setup();
      const fixture = TestBed.createComponent(SettingsGeneralComponent);
      fixture.detectChanges();
      flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_API_KEY_UNREADABLE);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const warningRegion = el.querySelector('#api-key-configured[role="alert"]');
      const warning = el.querySelector('.general-settings__api-key-warning');

      // Assert — the persistent wrapper carries role="alert" and contains the warning content
      expect(warningRegion).toBeTruthy();
      expect(warning).toBeTruthy();
      expect(warning?.textContent).toContain('can no longer be read');
      expect(warning?.textContent).toContain('Re-enter');
    });

    it('should keep id="api-key-configured" always in the DOM so the input aria-describedby never dangles', () => {
      // Arrange
      const { httpMock } = setup();
      const fixture = TestBed.createComponent(SettingsGeneralComponent);
      fixture.detectChanges();
      flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_API_KEY_NOT_CONFIGURED);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const statusEl = el.querySelector('#api-key-configured');
      const input = el.querySelector('.general-settings__api-key-input');

      // Assert — element with id="api-key-configured" is always present and input references it
      expect(statusEl).toBeTruthy();
      expect(input?.getAttribute('aria-describedby')).toContain('api-key-configured');
    });

    it('should keep id="api-key-configured" in the DOM even when apiKeyStatus is Present', () => {
      // Arrange
      const { httpMock } = setup();
      const fixture = TestBed.createComponent(SettingsGeneralComponent);
      fixture.detectChanges();
      flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_API_KEY);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const statusEl = el.querySelector('#api-key-configured');

      // Assert — persistent wrapper always in the DOM, not created/destroyed per status
      expect(statusEl).toBeTruthy();
    });

    it('should keep id="api-key-configured" in the DOM when apiKeyStatus is Unreadable', () => {
      // Arrange
      const { httpMock } = setup();
      const fixture = TestBed.createComponent(SettingsGeneralComponent);
      fixture.detectChanges();
      flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_API_KEY_UNREADABLE);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const statusEl = el.querySelector('#api-key-configured');

      // Assert
      expect(statusEl).toBeTruthy();
    });

    it('should set role="alert" on the persistent wrapper only when apiKeyStatus is Unreadable', () => {
      // Arrange — Present state: no role="alert" on the wrapper
      const { httpMock } = setup();
      const fixture = TestBed.createComponent(SettingsGeneralComponent);
      fixture.detectChanges();
      flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_API_KEY);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const wrapper = el.querySelector('#api-key-configured');

      // Assert — no alert role when not Unreadable
      expect(wrapper?.getAttribute('role')).not.toBe('alert');
    });

    it('should not render the configured-indicator when apiKeyStatus is NotConfigured', () => {
      // Arrange
      const { httpMock } = setup();
      const fixture = TestBed.createComponent(SettingsGeneralComponent);
      fixture.detectChanges();
      flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_API_KEY_NOT_CONFIGURED);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const indicator = el.querySelector('.general-settings__configured-indicator');

      // Assert
      expect(indicator).toBeFalsy();
    });

    it('should render the NotConfigured hint with the credential-status class (not the shared field-hint class alone)', () => {
      // Arrange
      const { httpMock } = setup();
      const fixture = TestBed.createComponent(SettingsGeneralComponent);
      fixture.detectChanges();
      flushSettings(httpMock, API_KEY_RESPONSE, CREDENTIALS_API_KEY_NOT_CONFIGURED);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const hint = el.querySelector('.general-settings__field-hint--credential-status');

      // Assert — the dedicated class exists so it can receive higher-contrast styling
      expect(hint).toBeTruthy();
      expect(hint?.textContent).toContain('No API key is set');
    });
  });

  describe('Provider Rate Budget section', () => {
    it('should render the "Provider Rate Budget" section title', () => {
      // Arrange
      const { httpMock } = setup();
      const fixture = TestBed.createComponent(SettingsGeneralComponent);
      fixture.detectChanges();
      flushSettings(httpMock);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const titles = Array.from(el.querySelectorAll('.general-settings__section-title'));
      const rateBudgetTitle = titles.find((t) => t.textContent?.includes('Provider Rate Budget'));

      // Assert
      expect(rateBudgetTitle).toBeTruthy();
    });

    it('should render the fd-rate-budget-panel component', () => {
      // Arrange
      const { httpMock } = setup();
      const fixture = TestBed.createComponent(SettingsGeneralComponent);
      fixture.detectChanges();
      flushSettings(httpMock);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const panel = el.querySelector('fd-rate-budget-panel');

      // Assert
      expect(panel).toBeTruthy();
    });
  });
});

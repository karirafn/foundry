import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { By } from '@angular/platform-browser';
import { NgModel } from '@angular/forms';
import { signal } from '@angular/core';
import { NEVER } from 'rxjs';
import { vi } from 'vitest';
import { SettingsGeneralComponent } from './settings-general';
import { SettingsService } from '../../../core/services/settings.service';
import { SystemSignalRService } from '../../../core/services/system-signalr.service';
import { RateBudgetService } from '../../../core/services/rate-budget.service';

const mockSystemSignalR = { reconnected: NEVER, reloadTrigger: NEVER, loginSessionUpdate: NEVER, notifications: signal([]).asReadonly() };
const mockRateBudgetService = { snapshot: signal(null).asReadonly() };

const IMAGE_FLAGS_DEFAULTS = {
  installDotnet: false,
  installAngular: false,
  installGlab: false,
  installGh: false,
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

  it('should render the "Worker Limits" section with values from the service', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock, { ...API_KEY_RESPONSE, maxConcurrent: 5, timeoutMinutes: 90 });
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const maxInput = el.querySelector('#maxConcurrent') as HTMLInputElement;
    const timeoutInput = el.querySelector('#timeoutMinutes') as HTMLInputElement;

    // Assert
    expect(maxInput).toBeTruthy();
    expect(timeoutInput).toBeTruthy();
    const maxNgModel = fixture.debugElement.query(By.css('#maxConcurrent')).injector.get(NgModel);
    const timeoutNgModel = fixture.debugElement.query(By.css('#timeoutMinutes')).injector.get(NgModel);
    expect(maxNgModel.model).toBe(5);
    expect(timeoutNgModel.model).toBe(90);
  });

  it('should call updateWorkerLimits with current input values when Save is clicked', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const saveBtn = Array.from(el.querySelectorAll('button')).find(
      btn => btn.textContent?.trim() === 'Save' && btn.closest('.general-settings__limits-form')
    ) as HTMLButtonElement;
    saveBtn.click();

    // Assert
    const req = httpMock.expectOne('/api/settings/limits');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ maxConcurrent: 3, timeoutMinutes: 60 });
    req.flush(API_KEY_RESPONSE);
  });

  it('should show "Worker limits saved successfully" after a successful save', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    const service = TestBed.inject(SettingsService);
    service.updateWorkerLimits(3, 60);
    httpMock.expectOne('/api/settings/limits').flush(API_KEY_RESPONSE);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const successEls = Array.from(el.querySelectorAll('[role="status"]'));
    const limitsSuccess = successEls.find(e => e.textContent?.includes('Worker limits saved successfully'));
    expect(limitsSuccess).toBeTruthy();
  });

  it('should show error message when save limits fails', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    const service = TestBed.inject(SettingsService);
    service.updateWorkerLimits(3, 60);
    httpMock.expectOne('/api/settings/limits').flush('Bad Request', {
      status: 400,
      statusText: 'Bad Request',
    });
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const errorEl = el.querySelector('#limits-error');
    expect(errorEl).toBeTruthy();
    expect(errorEl?.getAttribute('role')).toBe('alert');
    expect(errorEl?.textContent).toContain('Failed to save worker limits');
  });

  it('should disable the Save button while limits are saving', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    const service = TestBed.inject(SettingsService);
    service.updateWorkerLimits(3, 60);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const limitsForm = el.querySelector('.general-settings__limits-form') as HTMLElement;
    const saveBtn = limitsForm.querySelector('.general-settings__save-btn') as HTMLButtonElement;
    expect(saveBtn.disabled).toBe(true);
    expect(saveBtn.textContent?.trim()).toBe('Saving...');

    httpMock.expectOne('/api/settings/limits').flush(API_KEY_RESPONSE);
  });

  it('should render the "Worker Limits" heading', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const headings = Array.from(el.querySelectorAll('h2'));
    const limitsHeading = headings.find(h => h.textContent?.trim() === 'Worker Limits');

    // Assert
    expect(limitsHeading).toBeTruthy();
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

  it('should render the "Dispatch Settings" section title', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const headings = Array.from(el.querySelectorAll('h2'));
    const dispatchHeading = headings.find(h => h.textContent?.trim() === 'Dispatch Settings');

    // Assert
    expect(dispatchHeading).toBeTruthy();
  });

  it('should initialize checkbox from settings autoResumeOnUsageReset value', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock, { ...API_KEY_RESPONSE, autoResumeOnUsageReset: false });
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const checkbox = el.querySelector('#autoResume') as HTMLInputElement;

    // Assert
    expect(checkbox).toBeTruthy();
    const ngModel = fixture.debugElement.query(By.css('#autoResume')).injector.get(NgModel);
    expect(ngModel.model).toBe(false);
  });

  it('should call updateDispatchSettings with autoResumeOnUsageReset when Save is clicked', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock, { ...API_KEY_RESPONSE, autoResumeOnUsageReset: true });
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const dispatchForm = el.querySelector('.general-settings__dispatch-form') as HTMLElement;
    const saveBtn = dispatchForm.querySelector('.general-settings__save-btn') as HTMLButtonElement;
    saveBtn.click();

    // Assert
    const req = httpMock.expectOne('/api/settings/dispatch');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ autoResumeOnUsageReset: true, probeIntervalMinutes: 60, pollIntervalSeconds: 30 });
    req.flush(API_KEY_RESPONSE);
  });

  it('should render a "Credit check interval (minutes)" label and number input in the Dispatch Settings form', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const probeInput = el.querySelector('#probeIntervalMinutes') as HTMLInputElement;
    const probeLabel = el.querySelector('label[for="probeIntervalMinutes"]') as HTMLLabelElement;

    // Assert
    expect(probeInput).toBeTruthy();
    expect(probeInput.type).toBe('number');
    expect(probeLabel?.textContent?.trim()).toBe('Credit check interval (minutes)');
  });

  it('should describe the probe-interval field purpose in the hint text', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const hint = el.querySelector('#probe-interval-hint') as HTMLElement;

    // Assert — hint explains purpose and includes minimum
    expect(hint).toBeTruthy();
    expect(hint.textContent).toContain('Claude account');
    expect(hint.textContent).toContain('Minimum 5 minutes');
  });

  it('should conditionally bind aria-describedby on the autoResume checkbox based on dispatch error presence', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act — no error initially
    const el = fixture.nativeElement as HTMLElement;
    const checkbox = el.querySelector('#autoResume') as HTMLInputElement;

    // Assert — no aria-describedby when there is no save error
    expect(checkbox.getAttribute('aria-describedby')).toBeNull();

    // Act — trigger a dispatch save error
    const service = TestBed.inject(SettingsService);
    service.updateDispatchSettings(true, 60, 30);
    httpMock.expectOne('/api/settings/dispatch').flush('Bad Request', { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();

    // Assert — aria-describedby is bound to dispatch-error when error is present
    expect(checkbox.getAttribute('aria-describedby')).toBe('dispatch-error');
  });

  it('should initialize probeIntervalMinutes input from loaded settings', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock, { ...API_KEY_RESPONSE, probeIntervalMinutes: 30 });
    fixture.detectChanges();

    // Act
    const probeNgModel = fixture.debugElement.query(By.css('#probeIntervalMinutes')).injector.get(NgModel);

    // Assert
    expect(probeNgModel.model).toBe(30);
  });

  it('should show a validation error and set aria-invalid when probeIntervalMinutes is below the minimum (5)', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act — set value below minimum
    const component = fixture.componentInstance as unknown as { _probeIntervalValue: { set: (v: number) => void } };
    component._probeIntervalValue.set(3);
    fixture.detectChanges();

    // Assert — always-present error element has message text and input is aria-invalid
    const el = fixture.nativeElement as HTMLElement;
    const errorSpan = el.querySelector('#probe-interval-error[role="alert"]') as HTMLElement;
    expect(errorSpan).toBeTruthy();
    expect(errorSpan.textContent).toContain('at least 5 minutes');
    const probeInput = el.querySelector('#probeIntervalMinutes') as HTMLInputElement;
    expect(probeInput.getAttribute('aria-invalid')).toBe('true');
  });

  it('should render the probe-interval error element in the DOM at all times (always-present pattern)', () => {
    // Arrange — value is valid (at minimum)
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock, { ...API_KEY_RESPONSE, probeIntervalMinutes: 5 });
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const errorSpan = el.querySelector('#probe-interval-error[role="alert"]') as HTMLElement;

    // Assert — element is always in DOM; content is empty when valid; aria-invalid is absent
    expect(errorSpan).toBeTruthy();
    expect(errorSpan.textContent?.trim()).toBe('');
    const probeInput = el.querySelector('#probeIntervalMinutes') as HTMLInputElement;
    expect(probeInput.getAttribute('aria-invalid')).toBeNull();
  });

  it('should disable the Save button when probeIntervalMinutes is below the minimum', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act — set value below minimum
    const component = fixture.componentInstance as unknown as { _probeIntervalValue: { set: (v: number) => void } };
    component._probeIntervalValue.set(2);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const dispatchForm = el.querySelector('.general-settings__dispatch-form') as HTMLElement;
    const saveBtn = dispatchForm.querySelector('.general-settings__save-btn') as HTMLButtonElement;
    expect(saveBtn.disabled).toBe(true);
  });

  it('should include probeIntervalMinutes in the dispatch settings save payload', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock, { ...API_KEY_RESPONSE, probeIntervalMinutes: 45, pollIntervalSeconds: 90, autoResumeOnUsageReset: false });
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const dispatchForm = el.querySelector('.general-settings__dispatch-form') as HTMLElement;
    const saveBtn = dispatchForm.querySelector('.general-settings__save-btn') as HTMLButtonElement;
    saveBtn.click();

    // Assert
    const req = httpMock.expectOne('/api/settings/dispatch');
    expect(req.request.body).toEqual({ autoResumeOnUsageReset: false, probeIntervalMinutes: 45, pollIntervalSeconds: 90 });
    req.flush(API_KEY_RESPONSE);
  });

  it('should disable Save button while dispatch settings are saving', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    const service = TestBed.inject(SettingsService);
    service.updateDispatchSettings(true, 60, 30);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const dispatchForm = el.querySelector('.general-settings__dispatch-form') as HTMLElement;
    const saveBtn = dispatchForm.querySelector('.general-settings__save-btn') as HTMLButtonElement;
    expect(saveBtn.disabled).toBe(true);
    expect(saveBtn.textContent?.trim()).toBe('Saving...');

    httpMock.expectOne('/api/settings/dispatch').flush(API_KEY_RESPONSE);
  });

  it('should show "Dispatch settings saved successfully" after a successful save', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    const service = TestBed.inject(SettingsService);
    service.updateDispatchSettings(true, 60, 30);
    httpMock.expectOne('/api/settings/dispatch').flush(API_KEY_RESPONSE);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const successEls = Array.from(el.querySelectorAll('[role="status"]'));
    const dispatchSuccess = successEls.find(e => e.textContent?.includes('Dispatch settings saved successfully'));
    expect(dispatchSuccess).toBeTruthy();
  });

  it('should show error message when dispatch save fails', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    const service = TestBed.inject(SettingsService);
    service.updateDispatchSettings(true, 60, 30);
    httpMock.expectOne('/api/settings/dispatch').flush('Bad Request', {
      status: 400,
      statusText: 'Bad Request',
    });
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const errorEl = el.querySelector('#dispatch-error');
    expect(errorEl).toBeTruthy();
    expect(errorEl?.getAttribute('role')).toBe('alert');
    expect(errorEl?.textContent).toContain('Failed to save dispatch settings');
  });

  describe('Worker Image section', () => {
    it('should render the "Worker Image" section title', () => {
      // Arrange
      const { httpMock } = setup();
      const fixture = TestBed.createComponent(SettingsGeneralComponent);
      fixture.detectChanges();
      flushSettings(httpMock);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const headings = Array.from(el.querySelectorAll('h2'));
      const imageHeading = headings.find(h => h.textContent?.trim() === 'Worker Image');

      // Assert
      expect(imageHeading).toBeTruthy();
    });

    it('should render six checkboxes for the image flags', () => {
      // Arrange
      const { httpMock } = setup();
      const fixture = TestBed.createComponent(SettingsGeneralComponent);
      fixture.detectChanges();
      flushSettings(httpMock);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const imageForm = el.querySelector('.general-settings__image-form') as HTMLElement;
      const checkboxes = imageForm?.querySelectorAll('input[type="checkbox"]');

      // Assert
      expect(checkboxes?.length).toBe(6);
    });

    it('should initialize checkboxes from loaded settings', () => {
      // Arrange
      const { httpMock } = setup();
      const fixture = TestBed.createComponent(SettingsGeneralComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, installDotnet: true, installGlab: true });
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const dotnetCheckbox = el.querySelector('#installDotnet') as HTMLInputElement;
      const glabCheckbox = el.querySelector('#installGlab') as HTMLInputElement;
      const angularCheckbox = el.querySelector('#installAngular') as HTMLInputElement;

      // Assert
      const dotnetModel = fixture.debugElement.query(By.css('#installDotnet')).injector.get(NgModel);
      const glabModel = fixture.debugElement.query(By.css('#installGlab')).injector.get(NgModel);
      const angularModel = fixture.debugElement.query(By.css('#installAngular')).injector.get(NgModel);
      expect(dotnetModel.model).toBe(true);
      expect(glabModel.model).toBe(true);
      expect(angularModel.model).toBe(false);
      expect(dotnetCheckbox).toBeTruthy();
      expect(glabCheckbox).toBeTruthy();
    });

    it('should disable the Save button when flags are not dirty', () => {
      // Arrange
      const { httpMock } = setup();
      const fixture = TestBed.createComponent(SettingsGeneralComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, installDotnet: false });
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const imageForm = el.querySelector('.general-settings__image-form') as HTMLElement;
      const saveBtn = imageForm?.querySelector('.general-settings__save-btn') as HTMLButtonElement;

      // Assert
      expect(saveBtn.disabled).toBe(true);
    });

    it('should enable the Save button when a flag has changed', () => {
      // Arrange
      const { httpMock } = setup();
      const fixture = TestBed.createComponent(SettingsGeneralComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, installDotnet: false });
      fixture.detectChanges();

      // Act — toggle installDotnet to make it dirty
      const component = fixture.componentInstance as unknown as { _installDotnetValue: { set: (v: boolean) => void } };
      component._installDotnetValue.set(true);
      fixture.detectChanges();

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      const imageForm = el.querySelector('.general-settings__image-form') as HTMLElement;
      const saveBtn = imageForm?.querySelector('.general-settings__save-btn') as HTMLButtonElement;
      expect(saveBtn.disabled).toBe(false);
    });

    it('should call updateWorkerImageFlags when Save is clicked', () => {
      // Arrange
      const { httpMock } = setup();
      const fixture = TestBed.createComponent(SettingsGeneralComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, installDotnet: false, installAngular: false, installGlab: false, installGh: false });
      fixture.detectChanges();

      // Make dirty
      const component = fixture.componentInstance as unknown as { _installDotnetValue: { set: (v: boolean) => void } };
      component._installDotnetValue.set(true);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const imageForm = el.querySelector('.general-settings__image-form') as HTMLElement;
      const saveBtn = imageForm?.querySelector('.general-settings__save-btn') as HTMLButtonElement;
      saveBtn.click();

      // Assert
      const req = httpMock.expectOne('/api/settings/worker-image');
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual({ installDotnet: true, installAngular: false, installGlab: false, installGh: false, installChromium: false, installDocker: false });
      req.flush({ ...API_KEY_RESPONSE, installDotnet: true });
    });

    it('should disable checkboxes and Save while imageBuildStatus is Building', () => {
      // Arrange
      const { httpMock, service } = setup();
      const fixture = TestBed.createComponent(SettingsGeneralComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, imageBuildStatus: 'Building' });
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const imageForm = el.querySelector('.general-settings__image-form') as HTMLElement;
      const checkboxes = Array.from(imageForm?.querySelectorAll('input[type="checkbox"]') ?? []) as HTMLInputElement[];
      const saveBtn = imageForm?.querySelector('.general-settings__save-btn') as HTMLButtonElement;

      // Assert
      expect(service.imageBuildStatus()).toBe('Building');
      checkboxes.forEach(cb => expect(cb.disabled).toBe(true));
      expect(saveBtn.disabled).toBe(true);
    });

    it('should show persistent role="status" region with building text when Building', () => {
      // Arrange
      const { httpMock } = setup();
      const fixture = TestBed.createComponent(SettingsGeneralComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, imageBuildStatus: 'Building' });
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const statusRegions = Array.from(el.querySelectorAll('.general-settings__image-status[role="status"]')) as HTMLElement[];

      // Assert — at least one polite region contains the building text
      const buildingRegion = statusRegions.find(r => r.textContent?.includes('Building worker image'));
      expect(buildingRegion).toBeTruthy();
      expect(buildingRegion?.getAttribute('aria-live')).toBe('polite');
    });

    it('should have a persistent role="status" image-failed region always in the DOM (polite, not assertive)', () => {
      // Arrange
      const { httpMock } = setup();
      const fixture = TestBed.createComponent(SettingsGeneralComponent);
      fixture.detectChanges();
      flushSettings(httpMock);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      // The failed-text container must be role="status" (polite) — countdown must not be assertive
      const statusRegion = el.querySelector('.general-settings__image-status[role="status"][aria-live="polite"]') as HTMLElement;

      // Assert — the polite failed-text region exists (may be empty when Idle)
      expect(statusRegion).toBeTruthy();
    });

    it('should show failed text in role="status" (polite) region and build log pre element when Failed', () => {
      // Arrange
      const { httpMock } = setup();
      const fixture = TestBed.createComponent(SettingsGeneralComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, imageBuildStatus: 'Failed', lastImageBuildError: 'Step 2/5 FAILED', nextRetryAt: null, attempt: 0 });
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const statusRegions = Array.from(el.querySelectorAll('.general-settings__image-status[role="status"]')) as HTMLElement[];
      const logPre = el.querySelector('.general-settings__image-log') as HTMLElement;

      // Assert — one of the status regions contains the failed text
      const failedRegion = statusRegions.find(r => r.textContent?.includes('Worker image build failed'));
      expect(failedRegion).toBeTruthy();
      expect(logPre).toBeTruthy();
      expect(logPre.tagName).toBe('PRE');
      expect(logPre.getAttribute('tabindex')).toBe('0');
      expect(logPre.getAttribute('aria-label')).toBe('Build log output');
      expect(logPre.textContent).toContain('Step 2/5 FAILED');
    });

    it('should show Retry button when imageBuildStatus is Failed', () => {
      // Arrange
      const { httpMock } = setup();
      const fixture = TestBed.createComponent(SettingsGeneralComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, imageBuildStatus: 'Failed', lastImageBuildError: 'error' });
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const imageForm = el.querySelector('.general-settings__image-form') as HTMLElement;
      const retryBtn = imageForm?.querySelector('.general-settings__retry-btn') as HTMLButtonElement;

      // Assert
      expect(retryBtn).toBeTruthy();
      expect(retryBtn.textContent?.trim()).toBe('Retry');
    });

    it('should not show Retry button when imageBuildStatus is Idle', () => {
      // Arrange
      const { httpMock } = setup();
      const fixture = TestBed.createComponent(SettingsGeneralComponent);
      fixture.detectChanges();
      flushSettings(httpMock);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const retryBtn = el.querySelector('.general-settings__retry-btn');

      // Assert
      expect(retryBtn).toBeFalsy();
    });

    it('should call retryImageBuild when Retry button is clicked', () => {
      // Arrange
      const { httpMock } = setup();
      const fixture = TestBed.createComponent(SettingsGeneralComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, imageBuildStatus: 'Failed', lastImageBuildError: 'err' });
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const retryBtn = el.querySelector('.general-settings__retry-btn') as HTMLButtonElement;
      retryBtn.click();

      // Assert
      const req = httpMock.expectOne('/api/settings/worker-image/retry');
      expect(req.request.method).toBe('POST');
      req.flush({ ...API_KEY_RESPONSE, imageBuildStatus: 'Building' });
    });

    it('should have labels with correct "for" attributes for each checkbox', () => {
      // Arrange
      const { httpMock } = setup();
      const fixture = TestBed.createComponent(SettingsGeneralComponent);
      fixture.detectChanges();
      flushSettings(httpMock);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const dotnetLabel = el.querySelector('label[for="installDotnet"]');
      const angularLabel = el.querySelector('label[for="installAngular"]');
      const glabLabel = el.querySelector('label[for="installGlab"]');
      const ghLabel = el.querySelector('label[for="installGh"]');
      const chromiumLabel = el.querySelector('label[for="installChromium"]');
      const dockerLabel = el.querySelector('label[for="installDocker"]');

      // Assert
      expect(dotnetLabel).toBeTruthy();
      expect(angularLabel).toBeTruthy();
      expect(glabLabel).toBeTruthy();
      expect(ghLabel).toBeTruthy();
      expect(chromiumLabel).toBeTruthy();
      expect(dockerLabel).toBeTruthy();
    });

    it('should initialize installChromium and installDocker checkboxes from loaded settings', () => {
      // Arrange
      const { httpMock } = setup();
      const fixture = TestBed.createComponent(SettingsGeneralComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, installChromium: true, installDocker: true });
      fixture.detectChanges();

      // Act
      const chromiumModel = fixture.debugElement.query(By.css('#installChromium')).injector.get(NgModel);
      const dockerModel = fixture.debugElement.query(By.css('#installDocker')).injector.get(NgModel);

      // Assert
      expect(chromiumModel.model).toBe(true);
      expect(dockerModel.model).toBe(true);
    });

    it('should include installChromium and installDocker in the save payload', () => {
      // Arrange
      const { httpMock } = setup();
      const fixture = TestBed.createComponent(SettingsGeneralComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, installChromium: false, installDocker: false });
      fixture.detectChanges();

      // Make dirty by toggling installChromium
      const component = fixture.componentInstance as unknown as { _installChromiumValue: { set: (v: boolean) => void } };
      component._installChromiumValue.set(true);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const imageForm = el.querySelector('.general-settings__image-form') as HTMLElement;
      const saveBtn = imageForm?.querySelector('.general-settings__save-btn') as HTMLButtonElement;
      saveBtn.click();

      // Assert
      const req = httpMock.expectOne('/api/settings/worker-image');
      expect(req.request.body).toEqual({ installDotnet: false, installAngular: false, installGlab: false, installGh: false, installChromium: true, installDocker: false });
      req.flush({ ...API_KEY_RESPONSE, installChromium: true });
    });

    it('should show error message when save image flags fails', () => {
      // Arrange
      const { httpMock, service } = setup();
      const fixture = TestBed.createComponent(SettingsGeneralComponent);
      fixture.detectChanges();
      flushSettings(httpMock);
      fixture.detectChanges();

      // Make dirty and attempt save
      service.updateWorkerImageFlags({ installDotnet: true, installAngular: false, installGlab: false, installGh: false, installChromium: false, installDocker: false });
      httpMock.expectOne('/api/settings/worker-image').flush('Error', { status: 500, statusText: 'Server Error' });
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const errorEl = el.querySelector('#image-flags-error');

      // Assert
      expect(errorEl).toBeTruthy();
      expect(errorEl?.getAttribute('role')).toBe('alert');
      expect(errorEl?.textContent).toContain('Failed to save worker image settings');
    });

    it('should wrap the four checkboxes in a fieldset with a sr-only legend', () => {
      // Arrange
      const { httpMock } = setup();
      const fixture = TestBed.createComponent(SettingsGeneralComponent);
      fixture.detectChanges();
      flushSettings(httpMock);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const fieldset = el.querySelector('fieldset.general-settings__image-fieldset') as HTMLFieldSetElement;
      const legend = fieldset?.querySelector('legend');
      const checkboxesInsideFieldset = fieldset?.querySelectorAll('input[type="checkbox"]');

      // Assert
      expect(fieldset).toBeTruthy();
      expect(legend?.classList.contains('sr-only')).toBe(true);
      expect(legend?.textContent?.trim()).toBe('Preinstalled toolchains');
      expect(checkboxesInsideFieldset?.length).toBe(6);
    });

    it('should show success message in role="status" region after a successful image flags save', () => {
      // Arrange
      const { httpMock, service } = setup();
      const fixture = TestBed.createComponent(SettingsGeneralComponent);
      fixture.detectChanges();
      flushSettings(httpMock);
      fixture.detectChanges();

      // Act
      service.updateWorkerImageFlags({ installDotnet: true, installAngular: false, installGlab: false, installGh: false, installChromium: false, installDocker: false });
      httpMock.expectOne('/api/settings/worker-image').flush({ ...API_KEY_RESPONSE, installDotnet: true, imageBuildStatus: 'Building' });
      fixture.detectChanges();

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      const successEls = Array.from(el.querySelectorAll('[role="status"]'));
      const imageFlagsSuccess = successEls.find(e => e.textContent?.includes('Worker image settings saved'));
      expect(imageFlagsSuccess).toBeTruthy();
    });

    it('should route retry errors to the image-flags-error region', () => {
      // Arrange
      const { httpMock, service } = setup();
      const fixture = TestBed.createComponent(SettingsGeneralComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, imageBuildStatus: 'Failed', lastImageBuildError: 'err' });
      fixture.detectChanges();

      // Act
      service.retryImageBuild();
      httpMock.expectOne('/api/settings/worker-image/retry').flush('Server Error', { status: 500, statusText: 'Internal Server Error' });
      fixture.detectChanges();

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      const errorEl = el.querySelector('#image-flags-error');
      expect(errorEl?.textContent).toContain('Failed to save worker image settings');
    });

    it('should have aria-label="Retry image build now" on the settings-page Retry button', () => {
      // Arrange
      const { httpMock } = setup();
      const fixture = TestBed.createComponent(SettingsGeneralComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, imageBuildStatus: 'Failed', lastImageBuildError: 'err' });
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const retryBtn = el.querySelector('.general-settings__retry-btn') as HTMLButtonElement;

      // Assert
      expect(retryBtn?.getAttribute('aria-label')).toBe('Retry image build now');
    });

    it('should disable the settings-page Retry button while savingImageFlags is true', () => {
      // Arrange
      const { httpMock, service } = setup();
      const fixture = TestBed.createComponent(SettingsGeneralComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, imageBuildStatus: 'Failed', lastImageBuildError: 'err' });
      fixture.detectChanges();

      // Act — trigger a save so savingImageFlags becomes true
      service.retryImageBuild();
      fixture.detectChanges();

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      const retryBtn = el.querySelector('.general-settings__retry-btn') as HTMLButtonElement;
      expect(retryBtn.disabled).toBe(true);

      httpMock.expectOne('/api/settings/worker-image/retry').flush({ ...API_KEY_RESPONSE, imageBuildStatus: 'Building', nextRetryAt: null, attempt: 0 });
    });

    describe('_imageFailedText copy matrix', () => {
      it('should show plain "Worker image build failed." when nextRetryAt is null and attempt is 0', () => {
        // Arrange
        const { httpMock } = setup();
        const fixture = TestBed.createComponent(SettingsGeneralComponent);
        fixture.detectChanges();
        flushSettings(httpMock, { ...API_KEY_RESPONSE, imageBuildStatus: 'Failed', lastImageBuildError: 'err', nextRetryAt: null, attempt: 0 });
        fixture.detectChanges();

        // Act
        const el = fixture.nativeElement as HTMLElement;
        const statusRegions = Array.from(el.querySelectorAll('.general-settings__image-status[role="status"]')) as HTMLElement[];
        const failedRegion = statusRegions.find(r => r.textContent?.includes('Worker image build failed'));

        // Assert
        expect(failedRegion?.textContent?.trim()).toBe('Worker image build failed.');
      });

      it('should show "(attempt N)" when nextRetryAt is null and attempt >= 1', () => {
        // Arrange
        const { httpMock } = setup();
        const fixture = TestBed.createComponent(SettingsGeneralComponent);
        fixture.detectChanges();
        flushSettings(httpMock, { ...API_KEY_RESPONSE, imageBuildStatus: 'Failed', nextRetryAt: null, attempt: 2 });
        fixture.detectChanges();

        // Act
        const el = fixture.nativeElement as HTMLElement;
        const statusRegions = Array.from(el.querySelectorAll('.general-settings__image-status[role="status"]')) as HTMLElement[];
        const failedRegion = statusRegions.find(r => r.textContent?.includes('Worker image build failed'));

        // Assert
        expect(failedRegion?.textContent).toContain('Worker image build failed (attempt 2)');
        expect(failedRegion?.textContent).not.toContain('retrying');
      });

      it('should show "retrying at HH:MM" when nextRetryAt is set and attempt < 1', () => {
        // Arrange
        const { httpMock } = setup();
        const fixture = TestBed.createComponent(SettingsGeneralComponent);
        fixture.detectChanges();
        flushSettings(httpMock, { ...API_KEY_RESPONSE, imageBuildStatus: 'Failed', nextRetryAt: '2026-08-08T12:30:00Z', attempt: 0 });
        fixture.detectChanges();

        // Act
        const el = fixture.nativeElement as HTMLElement;
        const statusRegions = Array.from(el.querySelectorAll('.general-settings__image-status[role="status"]')) as HTMLElement[];
        const failedRegion = statusRegions.find(r => r.textContent?.includes('Worker image build failed'));

        // Assert
        expect(failedRegion?.textContent).toContain('Worker image build failed — retrying at');
        expect(failedRegion?.textContent).not.toContain('attempt');
      });

      it('should show "retrying at HH:MM (attempt N)" when both nextRetryAt is set and attempt >= 1', () => {
        // Arrange
        const { httpMock } = setup();
        const fixture = TestBed.createComponent(SettingsGeneralComponent);
        fixture.detectChanges();
        flushSettings(httpMock, { ...API_KEY_RESPONSE, imageBuildStatus: 'Failed', nextRetryAt: '2026-08-08T12:30:00Z', attempt: 3 });
        fixture.detectChanges();

        // Act
        const el = fixture.nativeElement as HTMLElement;
        const statusRegions = Array.from(el.querySelectorAll('.general-settings__image-status[role="status"]')) as HTMLElement[];
        const failedRegion = statusRegions.find(r => r.textContent?.includes('Worker image build failed'));

        // Assert
        expect(failedRegion?.textContent).toContain('Worker image build failed — retrying at');
        expect(failedRegion?.textContent).toContain('(attempt 3)');
      });
    });

    describe('initial-failure assertive alert', () => {
      it('should have a visually-hidden role="alert" region that emits text on the initial Idle→Failed transition', () => {
        // Arrange — start Idle
        const { httpMock } = setup();
        const fixture = TestBed.createComponent(SettingsGeneralComponent);
        fixture.detectChanges();
        flushSettings(httpMock);
        fixture.detectChanges();

        const service = TestBed.inject(SettingsService);
        const el = fixture.nativeElement as HTMLElement;

        // Act — transition to Failed via loadSettings
        service.loadSettings();
        flushSettings(httpMock, { ...API_KEY_RESPONSE, imageBuildStatus: 'Failed', lastImageBuildError: 'err' });
        fixture.detectChanges();

        // Assert — assertive alert fires with failure text
        const alertEl = el.querySelector('[role="alert"].general-settings__image-failure-alert') as HTMLElement;
        expect(alertEl).toBeTruthy();
        expect(alertEl.textContent?.trim()).toContain('Worker image build failed');
      });

      it('should NOT fire the assertive alert when status stays Failed (countdown tick)', () => {
        // Arrange — use fake timers to control the alert clear timeout
        vi.useFakeTimers();
        const { httpMock } = setup();
        const fixture = TestBed.createComponent(SettingsGeneralComponent);
        fixture.detectChanges();
        flushSettings(httpMock, { ...API_KEY_RESPONSE, imageBuildStatus: 'Failed', lastImageBuildError: 'err', nextRetryAt: null, attempt: 0 });
        fixture.detectChanges();

        const service = TestBed.inject(SettingsService);
        const el = fixture.nativeElement as HTMLElement;

        // Advance past the clear timeout
        vi.advanceTimersByTime(0);
        fixture.detectChanges();

        // Act — countdown tick (nextRetryAt changes but status stays Failed)
        service.loadSettings();
        flushSettings(httpMock, { ...API_KEY_RESPONSE, imageBuildStatus: 'Failed', lastImageBuildError: 'err', nextRetryAt: '2026-08-08T12:30:00Z', attempt: 1 });
        fixture.detectChanges();

        // Assert — alert is empty (no re-announcement)
        const alertEl = el.querySelector('[role="alert"].general-settings__image-failure-alert') as HTMLElement;
        expect(alertEl?.textContent?.trim()).toBe('');

        vi.useRealTimers();
      });

      it('should clear the assertive alert when status returns to Idle', () => {
        // Arrange — start Failed
        const { httpMock } = setup();
        const fixture = TestBed.createComponent(SettingsGeneralComponent);
        fixture.detectChanges();
        flushSettings(httpMock, { ...API_KEY_RESPONSE, imageBuildStatus: 'Failed', lastImageBuildError: 'err' });
        fixture.detectChanges();

        const service = TestBed.inject(SettingsService);
        const el = fixture.nativeElement as HTMLElement;

        // Act — return to Idle
        service.loadSettings();
        flushSettings(httpMock, { ...API_KEY_RESPONSE, imageBuildStatus: 'Idle', lastImageBuildError: null });
        fixture.detectChanges();

        // Assert — alert is empty
        const alertEl = el.querySelector('[role="alert"].general-settings__image-failure-alert') as HTMLElement | null;
        expect(!alertEl || alertEl.textContent?.trim() === '').toBe(true);
      });
    });
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
      httpMock.expectOne('/api/settings/allowed-provider-hosts').flush('Host "bad host!" is invalid', {
        status: 400,
        statusText: 'Bad Request',
      });
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

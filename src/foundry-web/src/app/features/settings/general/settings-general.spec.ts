import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { By } from '@angular/platform-browser';
import { NgModel } from '@angular/forms';
import { NEVER } from 'rxjs';
import { SettingsGeneralComponent } from './settings-general';
import { SettingsService } from '../settings.service';
import { SystemSignalRService } from '../../../core/services/system-signalr.service';

const mockSystemSignalR = { reconnected: NEVER, dispatchStateChanged: NEVER, loginSessionUpdate: NEVER, notifications: [] };

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
};

const BASE_RESPONSE = {
  usageLimitResetsAt: null,
  isDispatchPaused: false,
  autoResumeOnUsageReset: true,
  defaultCooldownMinutes: 60,
  ...IMAGE_FLAGS_DEFAULTS,
  systemPromptTemplate: null,
  workerPromptTemplate: null,
};

const API_KEY_RESPONSE = {
  authMode: 'ApiKey',
  oAuthStatus: 'NotConfigured',
  oAuthAccountEmail: null,
  oAuthAccountOrgName: null,
  maxConcurrent: 3,
  timeoutMinutes: 60,
  expiresAt: null,
  subscriptionType: null,
  ...BASE_RESPONSE,
};

const OAUTH_RESPONSE = {
  authMode: 'OAuth',
  oAuthStatus: 'Present',
  oAuthAccountEmail: 'user@example.com',
  oAuthAccountOrgName: null,
  maxConcurrent: 3,
  timeoutMinutes: 60,
  expiresAt: '2027-01-01T00:00:00Z',
  subscriptionType: 'pro',
  ...BASE_RESPONSE,
};

const OAUTH_NOT_CONFIGURED_RESPONSE = {
  authMode: 'OAuth',
  oAuthStatus: 'NotConfigured',
  oAuthAccountEmail: null,
  oAuthAccountOrgName: null,
  maxConcurrent: 3,
  timeoutMinutes: 60,
  expiresAt: null,
  subscriptionType: null,
  ...BASE_RESPONSE,
};

function setup() {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    imports: [SettingsGeneralComponent],
    providers: [
      SettingsService,
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: SystemSignalRService, useValue: mockSystemSignalR },
    ],
  });

  const service = TestBed.inject(SettingsService);
  service.loadSettings();
  const httpMock = TestBed.inject(HttpTestingController);

  return { service, httpMock };
}

function flushSettings(httpMock: HttpTestingController, response: object = API_KEY_RESPONSE): void {
  httpMock.expectOne('/api/settings').flush(response);
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
    flushSettings(httpMock, OAUTH_RESPONSE);
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
    flushSettings(httpMock, OAUTH_RESPONSE);
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
    httpMock.expectOne('/api/settings/auth').flush('Bad Request', {
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
    const req = httpMock.expectOne('/api/settings/auth');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ mode: 'api_key', apiKey: 'test-key' });
    req.flush(API_KEY_RESPONSE);
  });

  it('should show "Switch account" button when OAuth mode is active and status is Present', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock, OAUTH_RESPONSE);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const switchBtn = el.querySelector('.general-settings__switch-account-btn');

    // Assert
    expect(switchBtn).toBeTruthy();
  });

  it('should not show "Switch account" button when OAuth status is NotConfigured', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock, OAUTH_NOT_CONFIGURED_RESPONSE);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const switchBtn = el.querySelector('.general-settings__switch-account-btn');

    // Assert
    expect(switchBtn).toBeFalsy();
  });

  it('should show confirm group when "Switch account" is clicked', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock, OAUTH_RESPONSE);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const switchBtn = el.querySelector('.general-settings__switch-account-btn') as HTMLButtonElement;
    switchBtn.click();
    fixture.detectChanges();

    // Assert — uses role="group", not role="dialog"
    const group = el.querySelector('[role="group"][aria-label="Confirm account switch"]');
    expect(group?.textContent).toContain('Switching account pauses dispatch');
  });

  it('should use role="group" (not role="dialog") for the inline confirm', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock, OAUTH_RESPONSE);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const switchBtn = el.querySelector('.general-settings__switch-account-btn') as HTMLButtonElement;
    switchBtn.click();
    fixture.detectChanges();

    // Assert — no dialog role
    expect(el.querySelector('[role="dialog"]')).toBeFalsy();
    expect(el.querySelector('[role="group"]')).toBeTruthy();
  });

  it('should return focus to the switch-account button when Cancel is clicked', async () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();
    flushSettings(httpMock, OAUTH_RESPONSE);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const switchBtn = el.querySelector('.general-settings__switch-account-btn') as HTMLButtonElement;
    switchBtn.click();
    fixture.detectChanges();

    // Act
    const cancelBtn = el.querySelector('.general-settings__switch-cancel-btn') as HTMLButtonElement;
    cancelBtn.click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    // Assert — switch account button re-rendered and focused
    const restoredBtn = el.querySelector('.general-settings__switch-account-btn') as HTMLButtonElement;
    expect(document.activeElement).toBe(restoredBtn);

    document.body.removeChild(fixture.nativeElement);
  });

  it('should call pauseDispatch when confirm button is clicked', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock, OAUTH_RESPONSE);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const switchBtn = el.querySelector('.general-settings__switch-account-btn') as HTMLButtonElement;
    switchBtn.click();
    fixture.detectChanges();

    // Act
    const confirmBtn = el.querySelector('.general-settings__switch-confirm-btn') as HTMLButtonElement;
    confirmBtn.click();
    fixture.detectChanges();

    // Assert
    const pauseReq = httpMock.expectOne('/api/settings/dispatch/pause');
    expect(pauseReq.request.method).toBe('POST');
    pauseReq.flush(OAUTH_RESPONSE);
  });

  it('should move focus to the auth heading after confirming account switch', async () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();
    flushSettings(httpMock, OAUTH_RESPONSE);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const switchBtn = el.querySelector('.general-settings__switch-account-btn') as HTMLButtonElement;
    switchBtn.click();
    fixture.detectChanges();

    // Act
    const confirmBtn = el.querySelector('.general-settings__switch-confirm-btn') as HTMLButtonElement;
    confirmBtn.click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    httpMock.expectOne('/api/settings/dispatch/pause').flush(OAUTH_RESPONSE);
    fixture.detectChanges();

    // Assert — focus moves to the auth heading, not lost to body
    const authHeading = Array.from(el.querySelectorAll('.general-settings__section-title'))
      .find(h => h.textContent?.includes('Worker Authentication')) as HTMLElement;
    expect(document.activeElement).toBe(authHeading);

    document.body.removeChild(fixture.nativeElement);
  });

  it('should show drain gate message after confirming account switch', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock, OAUTH_RESPONSE);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const switchBtn = el.querySelector('.general-settings__switch-account-btn') as HTMLButtonElement;
    switchBtn.click();
    fixture.detectChanges();

    // Act
    const confirmBtn = el.querySelector('.general-settings__switch-confirm-btn') as HTMLButtonElement;
    confirmBtn.click();
    fixture.detectChanges();
    httpMock.expectOne('/api/settings/dispatch/pause').flush(OAUTH_RESPONSE);
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
    flushSettings(httpMock, OAUTH_RESPONSE);
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
    flushSettings(httpMock, OAUTH_RESPONSE);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const switchBtn = el.querySelector('.general-settings__switch-account-btn') as HTMLButtonElement;
    switchBtn.click();
    fixture.detectChanges();

    const confirmBtn = el.querySelector('.general-settings__switch-confirm-btn') as HTMLButtonElement;
    confirmBtn.click();
    fixture.detectChanges();
    // Fail the pause so pauseResumeError signal is set
    httpMock.expectOne('/api/settings/dispatch/pause').flush('Server Error', { status: 500, statusText: 'Internal Server Error' });
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

  it('should initialize cooldown number input from settings defaultCooldownMinutes value', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock, { ...API_KEY_RESPONSE, defaultCooldownMinutes: 120 });
    fixture.detectChanges();

    // Act
    const ngModel = fixture.debugElement.query(By.css('#defaultCooldown')).injector.get(NgModel);

    // Assert
    expect(ngModel.model).toBe(120);
  });

  it('should call updateDispatchSettings with current values when Save is clicked', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock, { ...API_KEY_RESPONSE, autoResumeOnUsageReset: true, defaultCooldownMinutes: 90 });
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const dispatchForm = el.querySelector('.general-settings__dispatch-form') as HTMLElement;
    const saveBtn = dispatchForm.querySelector('.general-settings__save-btn') as HTMLButtonElement;
    saveBtn.click();

    // Assert
    const req = httpMock.expectOne('/api/settings/dispatch');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ autoResumeOnUsageReset: true, defaultCooldownMinutes: 90 });
    req.flush(API_KEY_RESPONSE);
  });

  it('should disable Save button when cooldown is out of range', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Act
    const component = fixture.componentInstance as unknown as { _cooldownValue: { set: (v: number) => void } };
    component._cooldownValue.set(0);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const dispatchForm = el.querySelector('.general-settings__dispatch-form') as HTMLElement;
    const saveBtn = dispatchForm.querySelector('.general-settings__save-btn') as HTMLButtonElement;
    expect(saveBtn.disabled).toBe(true);
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
    service.updateDispatchSettings(true, 60);
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
    service.updateDispatchSettings(true, 60);
    httpMock.expectOne('/api/settings/dispatch').flush(API_KEY_RESPONSE);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const successEls = Array.from(el.querySelectorAll('[role="status"]'));
    const dispatchSuccess = successEls.find(e => e.textContent?.includes('Dispatch settings saved successfully'));
    expect(dispatchSuccess).toBeTruthy();
  });

  it('should disable the cooldown input when auto-resume is off', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock, { ...API_KEY_RESPONSE, autoResumeOnUsageReset: false });
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const cooldownInput = el.querySelector('#defaultCooldown') as HTMLInputElement;

    // Assert
    expect(cooldownInput.disabled).toBe(true);
  });

  it('should enable the cooldown input when auto-resume is on', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock, { ...API_KEY_RESPONSE, autoResumeOnUsageReset: true });
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const cooldownInput = el.querySelector('#defaultCooldown') as HTMLInputElement;

    // Assert
    expect(cooldownInput.disabled).toBe(false);
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
    service.updateDispatchSettings(true, 60);
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

      // Assert — persistent polite region present and contains building text
      expect(statusRegions.length).toBe(1);
      const politeRegion = statusRegions[0];
      expect(politeRegion.getAttribute('aria-live')).toBe('polite');
      expect(politeRegion.textContent).toContain('Building worker image');
    });

    it('should have a persistent role="alert" image-status region always in the DOM', () => {
      // Arrange
      const { httpMock } = setup();
      const fixture = TestBed.createComponent(SettingsGeneralComponent);
      fixture.detectChanges();
      flushSettings(httpMock);
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const alertRegion = el.querySelector('.general-settings__image-status[role="alert"]') as HTMLElement;

      // Assert — always present, empty when not failed
      expect(alertRegion).toBeTruthy();
      expect(alertRegion.textContent?.trim()).toBe('');
    });

    it('should show failed text in role="alert" region and build log pre element when Failed', () => {
      // Arrange
      const { httpMock } = setup();
      const fixture = TestBed.createComponent(SettingsGeneralComponent);
      fixture.detectChanges();
      flushSettings(httpMock, { ...API_KEY_RESPONSE, imageBuildStatus: 'Failed', lastImageBuildError: 'Step 2/5 FAILED' });
      fixture.detectChanges();

      // Act
      const el = fixture.nativeElement as HTMLElement;
      const alertRegion = el.querySelector('.general-settings__image-status[role="alert"]') as HTMLElement;
      const politeRegion = el.querySelector('.general-settings__image-status[role="status"]') as HTMLElement;
      const logPre = el.querySelector('.general-settings__image-log') as HTMLElement;

      // Assert
      expect(alertRegion.textContent).toContain('Worker image build failed');
      expect(politeRegion.textContent?.trim()).toBe('');
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
  });
});

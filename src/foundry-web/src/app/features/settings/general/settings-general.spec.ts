import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { By } from '@angular/platform-browser';
import { NgModel } from '@angular/forms';
import { SettingsGeneralComponent } from './settings-general';
import { SettingsService } from '../settings.service';

const API_KEY_RESPONSE = {
  authMode: 'ApiKey',
  maxConcurrent: 3,
  timeoutMinutes: 60,
  accessTokenPresent: false,
  refreshTokenPresent: false,
  expiresAt: null,
  subscriptionType: null,
};

const OAUTH_RESPONSE = {
  authMode: 'OAuth',
  maxConcurrent: 3,
  timeoutMinutes: 60,
  accessTokenPresent: true,
  refreshTokenPresent: true,
  expiresAt: '2027-01-01T00:00:00Z',
  subscriptionType: 'pro',
};

function setup() {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    imports: [SettingsGeneralComponent],
    providers: [
      SettingsService,
      provideHttpClient(),
      provideHttpClientTesting(),
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

  it('should render OAuth credential status when mode is oauth', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock, OAUTH_RESPONSE);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const credGrid = el.querySelector('.general-settings__oauth-grid');

    // Assert
    expect(credGrid).toBeTruthy();
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

  it('should call scanOAuthCredentials when Scan & Apply OAuth Credentials is clicked', () => {
    // Arrange
    const { httpMock } = setup();
    const fixture = TestBed.createComponent(SettingsGeneralComponent);
    fixture.detectChanges();
    flushSettings(httpMock, OAUTH_RESPONSE);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const scanBtn = el.querySelector('.general-settings__scan-btn') as HTMLButtonElement;
    scanBtn.click();

    // Assert
    const req = httpMock.expectOne('/api/settings/oauth/scan');
    expect(req.request.method).toBe('GET');
    req.flush({ accessTokenPresent: true, refreshTokenPresent: true, expiresAt: null, subscriptionType: null });
    httpMock.expectOne('/api/settings/auth').flush(OAUTH_RESPONSE);
  });
});

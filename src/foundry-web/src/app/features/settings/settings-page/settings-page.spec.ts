import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { SettingsPageComponent } from './settings-page';
import { SettingsService } from '../settings.service';
import { By } from '@angular/platform-browser';
import { NgModel } from '@angular/forms';
import { AccountService } from '../accounts/account.service';

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

function setupComponent() {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    imports: [SettingsPageComponent],
    providers: [
      SettingsService,
      AccountService,
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter([]),
    ],
  });

  const fixture = TestBed.createComponent(SettingsPageComponent);
  const httpMock = TestBed.inject(HttpTestingController);
  return { fixture, httpMock };
}

function flushInit(httpMock: HttpTestingController, settingsResponse: object = API_KEY_RESPONSE): void {
  httpMock.expectOne('/api/accounts').flush([]);
  httpMock.expectOne('/api/settings').flush(settingsResponse);
}

describe('SettingsPageComponent', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
  });

  // Cycle 1: component creates and renders heading
  it('should create the component', () => {
    // Arrange / Act
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);

    // Assert
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should render the "Settings" heading', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();

    // Act
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const heading = el.querySelector('.settings-page__heading');
    expect(heading?.textContent?.trim()).toBe('Settings');
  });

  // Cycle 2: loadSettings is called on init
  it('should call loadSettings on initialization', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();

    // Act
    fixture.detectChanges();

    // Assert — the HTTP call proves loadSettings was called
    httpMock.expectOne('/api/accounts').flush([]);
    const req = httpMock.expectOne('/api/settings');
    req.flush(API_KEY_RESPONSE);
  });

  // Cycle 3: renders Worker Authentication section
  it('should render the Worker Authentication section', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const titles = Array.from(el.querySelectorAll('.settings-page__section-title'));
    const workerTitle = titles.find(t => t.textContent?.includes('Worker Authentication'));
    expect(workerTitle).toBeTruthy();
  });

  // Cycle 4: shows API key form when mode is api_key
  it('should render the API key input when mode is api_key', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const input = el.querySelector('.settings-page__api-key-input');
    expect(input).toBeTruthy();
  });

  it('should not render the API key input when mode is oauth', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock, OAUTH_RESPONSE);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const input = el.querySelector('.settings-page__api-key-input');
    expect(input).toBeFalsy();
  });

  // Cycle 5: shows OAuth credential grid when mode is oauth
  it('should render OAuth credential status when mode is oauth', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock, OAUTH_RESPONSE);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const credGrid = el.querySelector('.settings-page__oauth-grid');
    expect(credGrid).toBeTruthy();
  });

  // Cycle 6: shows error state when loadSettings fails
  it('should show error when loadSettings fails', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    httpMock.expectOne('/api/accounts').flush([]);
    httpMock.expectOne('/api/settings').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const errorEl = el.querySelector('.settings-page__load-error');
    expect(errorEl).toBeTruthy();
  });

  // Cycle 7: renders auth mode radio buttons
  it('should render API Key and OAuth radio options', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const radios = el.querySelectorAll('input[type="radio"]');
    expect(radios.length).toBe(2);
  });

  // Cycle 8: back link is rendered
  it('should render a back link to issues', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const backLink = el.querySelector('.settings-page__back-link');
    expect(backLink).toBeTruthy();
  });

  // Cycle 9: loading state
  it('should show a loading indicator while settings are loading', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();

    // Act — detectChanges triggers ngOnInit which calls loadSettings
    fixture.detectChanges();

    // Assert — loading spinner visible before response
    const el = fixture.nativeElement as HTMLElement;
    const loadingEl = el.querySelector('.settings-page__loading');
    expect(loadingEl).toBeTruthy();
    expect(loadingEl?.getAttribute('role')).toBe('status');

    flushInit(httpMock);
  });

  it('should hide the loading indicator after settings load', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const loadingEl = el.querySelector('.settings-page__loading');
    expect(loadingEl).toBeFalsy();
  });

  // Cycle 10: fieldset wraps radio group
  it('should wrap the radio group in a fieldset', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const fieldset = el.querySelector('fieldset.settings-page__mode-fieldset');
    expect(fieldset).toBeTruthy();
    const legend = fieldset?.querySelector('legend');
    expect(legend?.classList.contains('sr-only')).toBe(true);
  });

  // Cycle 12: Worker Limits section renders with current values
  it('should render the "Worker Limits" section with values from the service', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock, { ...API_KEY_RESPONSE, maxConcurrent: 5, timeoutMinutes: 90 });

    // Act
    fixture.detectChanges();

    // Assert — inputs are present and their ngModel bindings carry the loaded values.
    // Number inputs in JSDOM do not reflect programmatic value writes to input.value;
    // querying the NgModel directive on each input is the correct way to assert the binding.
    const el = fixture.nativeElement as HTMLElement;
    const maxInput = el.querySelector('#maxConcurrent') as HTMLInputElement;
    const timeoutInput = el.querySelector('#timeoutMinutes') as HTMLInputElement;
    expect(maxInput).toBeTruthy();
    expect(timeoutInput).toBeTruthy();

    const maxNgModel = fixture.debugElement.query(By.css('#maxConcurrent')).injector.get(NgModel);
    const timeoutNgModel = fixture.debugElement.query(By.css('#timeoutMinutes')).injector.get(NgModel);
    expect(maxNgModel.model).toBe(5);
    expect(timeoutNgModel.model).toBe(90);
  });

  // Cycle 13: Save button calls updateWorkerLimits with current values
  it('should call updateWorkerLimits with current input values when Save is clicked', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    const service = TestBed.inject(SettingsService);

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const saveBtn = Array.from(el.querySelectorAll('button')).find(
      btn => btn.textContent?.trim() === 'Save' && btn.closest('.settings-page__limits-form')
    ) as HTMLButtonElement;
    saveBtn.click();

    // Assert
    const req = httpMock.expectOne('/api/settings/limits');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ maxConcurrent: 3, timeoutMinutes: 60 });
    req.flush({
      authMode: 'ApiKey',
      maxConcurrent: 3,
      timeoutMinutes: 60,
      accessTokenPresent: false,
      refreshTokenPresent: false,
      expiresAt: null,
      subscriptionType: null,
    });
  });

  // Cycle 14: success message displays after save
  it('should show "Worker limits saved successfully" after a successful save', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    // Act
    const service = TestBed.inject(SettingsService);
    service.updateWorkerLimits(3, 60);
    httpMock.expectOne('/api/settings/limits').flush({
      authMode: 'ApiKey',
      maxConcurrent: 3,
      timeoutMinutes: 60,
      accessTokenPresent: false,
      refreshTokenPresent: false,
      expiresAt: null,
      subscriptionType: null,
    });
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const successEls = Array.from(el.querySelectorAll('[role="status"]'));
    const limitsSuccess = successEls.find(e => e.textContent?.includes('Worker limits saved successfully'));
    expect(limitsSuccess).toBeTruthy();
  });

  // Cycle 15: error message displays on save error
  it('should show error message when save limits fails', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);
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

  // Cycle 16: Save button is disabled while saving
  it('should disable the Save button while limits are saving', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    // Act — trigger save (in-flight, not yet complete)
    const service = TestBed.inject(SettingsService);
    service.updateWorkerLimits(3, 60);
    fixture.detectChanges();

    // Assert — button disabled while saving
    const el = fixture.nativeElement as HTMLElement;
    const limitsForm = el.querySelector('.settings-page__limits-form') as HTMLElement;
    const saveBtn = limitsForm.querySelector('.settings-page__save-btn') as HTMLButtonElement;
    expect(saveBtn.disabled).toBe(true);
    expect(saveBtn.textContent?.trim()).toBe('Saving...');

    httpMock.expectOne('/api/settings/limits').flush({
      authMode: 'ApiKey',
      maxConcurrent: 3,
      timeoutMinutes: 60,
      accessTokenPresent: false,
      refreshTokenPresent: false,
      expiresAt: null,
      subscriptionType: null,
    });
  });

  it('should render the "Worker Limits" heading', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const headings = Array.from(el.querySelectorAll('h2'));
    const limitsHeading = headings.find(h => h.textContent?.trim() === 'Worker Limits');
    expect(limitsHeading).toBeTruthy();
  });

  // Cycle 11: aria-invalid on input when saveError is set
  it('should set aria-invalid on the API key input when saveError is present', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    // Act — trigger a save error
    const service = TestBed.inject(SettingsService);
    service.updateAuthMode('api_key', 'bad-key');
    const httpMock2 = TestBed.inject(HttpTestingController);
    httpMock2.expectOne('/api/settings/auth').flush('Bad Request', {
      status: 400,
      statusText: 'Bad Request',
    });
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const input = el.querySelector('.settings-page__api-key-input') as HTMLInputElement;
    expect(input.getAttribute('aria-invalid')).toBe('true');
    const errorEl = el.querySelector('#api-key-error');
    expect(errorEl).toBeTruthy();
  });

  // Cycle 12: Accounts section is rendered above Worker Authentication
  it('should render the Accounts section', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const titles = Array.from(el.querySelectorAll('.settings-page__section-title'));
    const accountsTitle = titles.find(t => t.textContent?.includes('Accounts'));
    expect(accountsTitle).toBeTruthy();
  });

  it('should render the Accounts section above the Worker Authentication section', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const titles = Array.from(el.querySelectorAll('.settings-page__section-title'));
    const accountsIndex = titles.findIndex(t => t.textContent?.includes('Accounts'));
    const workerIndex = titles.findIndex(t => t.textContent?.includes('Worker Authentication'));
    expect(accountsIndex).toBeLessThan(workerIndex);
  });

  it('should call loadAccounts on initialization', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();

    // Act
    fixture.detectChanges();

    // Assert — the HTTP call proves loadAccounts was called
    const req = httpMock.expectOne('/api/accounts');
    req.flush([]);
    httpMock.expectOne('/api/settings').flush(API_KEY_RESPONSE);
  });

  it('should render fd-account-list component', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const accountList = el.querySelector('fd-account-list');
    expect(accountList).toBeTruthy();
  });
});

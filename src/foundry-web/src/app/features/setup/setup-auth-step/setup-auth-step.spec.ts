import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { SetupAuthStepComponent } from './setup-auth-step';

const OAUTH_SCAN_RESPONSE = {
  accessTokenPresent: true,
  refreshTokenPresent: true,
  expiresAt: '2026-12-31T00:00:00Z',
  subscriptionType: 'max_5x',
};

const OAUTH_SETTINGS_RESPONSE = {
  authMode: 'OAuth',
  accessTokenPresent: true,
  refreshTokenPresent: true,
  expiresAt: '2026-12-31T00:00:00Z',
  subscriptionType: 'max_5x',
  maxConcurrent: 3,
  timeoutMinutes: 60,
  systemPromptTemplate: null,
  workerPromptTemplate: null,
};

const API_KEY_SETTINGS_RESPONSE = {
  authMode: 'ApiKey',
  accessTokenPresent: false,
  refreshTokenPresent: false,
  expiresAt: null,
  subscriptionType: null,
  maxConcurrent: 3,
  timeoutMinutes: 60,
  systemPromptTemplate: null,
  workerPromptTemplate: null,
};

function setup() {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    imports: [SetupAuthStepComponent],
    providers: [provideHttpClient(), provideHttpClientTesting()],
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
    httpMock.expectOne('/api/settings/auth').flush(API_KEY_SETTINGS_RESPONSE);
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

    httpMock.expectOne('/api/settings/auth').flush(API_KEY_SETTINGS_RESPONSE);
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

    httpMock.expectOne('/api/settings/auth').flush('Server Error', {
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

    httpMock.expectOne('/api/settings/auth').flush('Server Error', {
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
    httpMock.expectNone('/api/settings/auth');
  });

  // Cycle 11: selecting OAuth shows scan instructions and scan button
  it('should show OAuth scan instructions and scan button when OAuth mode is selected', () => {
    // Arrange
    const { fixture } = setup();
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const radios = el.querySelectorAll<HTMLInputElement>('input[type="radio"]');
    radios[1].click();
    fixture.detectChanges();

    // Assert
    const setupBox = el.querySelector('.setup-auth-step__oauth-setup');
    const scanButton = el.querySelector('.setup-auth-step__scan-btn');
    expect(setupBox).toBeTruthy();
    expect(scanButton?.textContent?.trim()).toContain('Scan & Apply OAuth Credentials');
  });

  // Cycle 12: Next button disabled until OAuth scan completes with valid status
  it('should keep Next button disabled when OAuth mode selected but scan not yet done', () => {
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
    expect(nextButton.disabled).toBe(true);
  });

  // Cycle 13: OAuth scan success with valid status shows credential grid and enables Next
  it('should show credential grid and enable Next after OAuth scan returns valid status', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const radios = el.querySelectorAll<HTMLInputElement>('input[type="radio"]');
    radios[1].click();
    fixture.detectChanges();

    // Act
    const scanButton = el.querySelector('.setup-auth-step__scan-btn') as HTMLButtonElement;
    scanButton.click();
    fixture.detectChanges();

    httpMock.expectOne('/api/settings/oauth/scan').flush(OAUTH_SCAN_RESPONSE);
    httpMock.expectOne('/api/settings/auth').flush(OAUTH_SETTINGS_RESPONSE);
    fixture.detectChanges();

    // Assert
    const grid = el.querySelector('.setup-auth-step__oauth-grid');
    const nextButton = el.querySelector('button[class*="next-btn"]') as HTMLButtonElement;
    expect(grid).toBeTruthy();
    expect(nextButton.disabled).toBe(false);
  });

  // Cycle 14: OAuth scan failure shows error message and Next stays disabled
  it('should show an error message and keep Next disabled when OAuth scan fails', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const radios = el.querySelectorAll<HTMLInputElement>('input[type="radio"]');
    radios[1].click();
    fixture.detectChanges();

    // Act
    const scanButton = el.querySelector('.setup-auth-step__scan-btn') as HTMLButtonElement;
    scanButton.click();
    fixture.detectChanges();

    httpMock.expectOne('/api/settings/oauth/scan').flush('Scan Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    fixture.detectChanges();

    // Assert
    const errorEl = el.querySelector('[role="alert"]');
    const nextButton = el.querySelector('button[class*="next-btn"]') as HTMLButtonElement;
    expect(errorEl?.textContent?.trim()).toBeTruthy();
    expect(nextButton.disabled).toBe(true);
  });

  // Cycle 15: scan button disabled while scanning
  it('should disable the scan button while scanning', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const radios = el.querySelectorAll<HTMLInputElement>('input[type="radio"]');
    radios[1].click();
    fixture.detectChanges();

    // Act
    const scanButton = el.querySelector('.setup-auth-step__scan-btn') as HTMLButtonElement;
    scanButton.click();
    fixture.detectChanges();

    // Assert
    expect(scanButton.disabled).toBe(true);

    // Cleanup
    httpMock.expectOne('/api/settings/oauth/scan').flush(OAUTH_SCAN_RESPONSE);
    httpMock.expectOne('/api/settings/auth').flush(OAUTH_SETTINGS_RESPONSE);
  });

  // Cycle 16: emits complete after successful OAuth scan and clicking Next
  it('should emit complete after successful OAuth scan and clicking Next', () => {
    // Arrange
    const { fixture, component, httpMock } = setup();
    fixture.detectChanges();

    let emitted = false;
    component.complete.subscribe(() => (emitted = true));

    const el = fixture.nativeElement as HTMLElement;
    const radios = el.querySelectorAll<HTMLInputElement>('input[type="radio"]');
    radios[1].click();
    fixture.detectChanges();

    const scanButton = el.querySelector('.setup-auth-step__scan-btn') as HTMLButtonElement;
    scanButton.click();
    fixture.detectChanges();

    httpMock.expectOne('/api/settings/oauth/scan').flush(OAUTH_SCAN_RESPONSE);
    httpMock.expectOne('/api/settings/auth').flush(OAUTH_SETTINGS_RESPONSE);
    fixture.detectChanges();

    // Act
    const nextButton = el.querySelector('button[class*="next-btn"]') as HTMLButtonElement;
    nextButton.click();
    fixture.detectChanges();

    // Assert
    expect(emitted).toBe(true);
  });

  // Cycle 17: OAuth scan with expired status keeps Next disabled
  it('should keep Next disabled after OAuth scan returns expired status', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const radios = el.querySelectorAll<HTMLInputElement>('input[type="radio"]');
    radios[1].click();
    fixture.detectChanges();

    // Act
    const scanButton = el.querySelector('.setup-auth-step__scan-btn') as HTMLButtonElement;
    scanButton.click();
    fixture.detectChanges();

    httpMock.expectOne('/api/settings/oauth/scan').flush({
      ...OAUTH_SCAN_RESPONSE,
      expiresAt: '2020-01-01T00:00:00Z',
    });
    httpMock.expectOne('/api/settings/auth').flush({
      ...OAUTH_SETTINGS_RESPONSE,
      expiresAt: '2020-01-01T00:00:00Z',
    });
    fixture.detectChanges();

    // Assert
    const nextButton = el.querySelector('button[class*="next-btn"]') as HTMLButtonElement;
    expect(nextButton.disabled).toBe(true);
  });
});

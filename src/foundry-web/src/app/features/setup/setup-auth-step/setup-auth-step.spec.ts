import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { NEVER } from 'rxjs';
import { SetupAuthStepComponent } from './setup-auth-step';
import { SystemSignalRService } from '../../../core/services/system-signalr.service';

const mockSystemSignalR = { reconnected: NEVER, dispatchStateChanged: NEVER, notifications: [] };

const OAUTH_SETTINGS_RESPONSE = {
  authMode: 'OAuth',
  oAuthStatus: 'Present',
  expiresAt: '2026-12-31T00:00:00Z',
  subscriptionType: 'max_5x',
  maxConcurrent: 3,
  timeoutMinutes: 60,
  systemPromptTemplate: null,
  workerPromptTemplate: null,
  usageLimitResetsAt: null,
  isDispatchPaused: false,
  autoResumeOnUsageReset: true,
  defaultCooldownMinutes: 60,
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

const API_KEY_SETTINGS_RESPONSE = {
  authMode: 'ApiKey',
  oAuthStatus: 'NotConfigured',
  expiresAt: null,
  subscriptionType: null,
  maxConcurrent: 3,
  timeoutMinutes: 60,
  systemPromptTemplate: null,
  workerPromptTemplate: null,
  usageLimitResetsAt: null,
  isDispatchPaused: false,
  autoResumeOnUsageReset: true,
  defaultCooldownMinutes: 60,
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

  // Cycle 11: selecting OAuth shows fd-oauth-panel and fetches login command
  it('should show fd-oauth-panel when OAuth mode is selected', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const radios = el.querySelectorAll<HTMLInputElement>('input[type="radio"]');
    radios[1].click();
    fixture.detectChanges();

    // Assert
    const panel = el.querySelector('fd-oauth-panel');
    expect(panel).toBeTruthy();

    // Cleanup — fetchLoginCommand fires
    httpMock.expectOne('/api/settings/oauth/login-command').flush({ command: 'docker run -it' });
  });

  // Cycle 12: Next button enabled immediately after selecting OAuth mode
  it('should enable Next button when OAuth mode is selected', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const radios = el.querySelectorAll<HTMLInputElement>('input[type="radio"]');
    radios[1].click();
    fixture.detectChanges();

    // Assert
    const nextButton = el.querySelector('button[class*="next-btn"]') as HTMLButtonElement;
    expect(nextButton.disabled).toBe(false);

    // Cleanup
    httpMock.expectOne('/api/settings/oauth/login-command').flush({ command: 'docker run -it' });
  });

  // Cycle 13: shows non-blocking warning note when OAuth status is not Present
  it('should show the not-logged-in note when OAuth status is NotConfigured', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const radios = el.querySelectorAll<HTMLInputElement>('input[type="radio"]');
    radios[1].click();
    fixture.detectChanges();

    // Flush login command
    httpMock.expectOne('/api/settings/oauth/login-command').flush({ command: 'docker run -it' });
    fixture.detectChanges();

    // Assert
    const note = el.querySelector('[role="note"]');
    expect(note?.textContent).toContain("You haven't logged in yet");
  });

  // Cycle 14: no warning note when OAuth status is Present
  it('should not show the not-logged-in note when OAuth status is Present', () => {
    // Arrange
    const { fixture, component, httpMock } = setup();
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const radios = el.querySelectorAll<HTMLInputElement>('input[type="radio"]');
    radios[1].click();
    fixture.detectChanges();

    // Flush login command — simulate that after switching to OAuth, status becomes Present
    httpMock.expectOne('/api/settings/oauth/login-command').flush({ command: 'docker run -it' });

    // Simulate settings loaded with Present status
    const service = component['_settingsService'];
    service.authSettings.set({
      mode: 'oauth',
      apiKeyConfigured: false,
      oauth: { status: 'Present', expiresAt: null, subscriptionType: null },
    });
    fixture.detectChanges();

    // Assert
    const note = el.querySelector('[role="note"]');
    expect(note).toBeFalsy();
  });

  // Cycle 15: emits complete on Next click in OAuth mode (no gating on status)
  it('should emit complete after clicking Next in OAuth mode', () => {
    // Arrange
    const { fixture, component, httpMock } = setup();
    fixture.detectChanges();

    let emitted = false;
    component.complete.subscribe(() => (emitted = true));

    const el = fixture.nativeElement as HTMLElement;
    const radios = el.querySelectorAll<HTMLInputElement>('input[type="radio"]');
    radios[1].click();
    fixture.detectChanges();

    httpMock.expectOne('/api/settings/oauth/login-command').flush({ command: 'docker run -it' });
    fixture.detectChanges();

    // Act
    const nextButton = el.querySelector('button[class*="next-btn"]') as HTMLButtonElement;
    nextButton.click();
    fixture.detectChanges();

    // Assert
    expect(emitted).toBe(true);
  });
});


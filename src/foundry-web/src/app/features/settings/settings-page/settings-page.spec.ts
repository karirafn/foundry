import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { signal } from '@angular/core';
import { SettingsPageComponent } from './settings-page';
import { SettingsService } from '../settings.service';
import { AuthSettings } from '../settings.model';

const mockApiKeySettings: AuthSettings = {
  mode: 'api_key',
  apiKeyConfigured: true,
  oauth: null,
};

const mockOAuthSettings: AuthSettings = {
  mode: 'oauth',
  apiKeyConfigured: false,
  oauth: {
    accessTokenPresent: true,
    refreshTokenPresent: true,
    expiresAt: '2027-01-01T00:00:00Z',
    subscriptionType: 'pro',
    status: 'valid',
  },
};

function setupComponent() {
  TestBed.configureTestingModule({
    imports: [SettingsPageComponent],
    providers: [
      SettingsService,
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter([]),
    ],
  });

  const fixture = TestBed.createComponent(SettingsPageComponent);
  const httpMock = TestBed.inject(HttpTestingController);
  return { fixture, httpMock };
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
    httpMock.expectOne('/api/settings').flush({
      authMode: 'ApiKey',
      maxConcurrent: 3,
      timeoutMinutes: 60,
      accessTokenPresent: false,
      refreshTokenPresent: false,
      expiresAt: null,
      subscriptionType: null,
    });

    // Assert
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should render the "Settings" heading', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();

    // Act
    fixture.detectChanges();
    httpMock.expectOne('/api/settings').flush({
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
    const req = httpMock.expectOne('/api/settings');
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

  // Cycle 3: renders Worker Authentication section
  it('should render the Worker Authentication section', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    httpMock.expectOne('/api/settings').flush({
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
    const section = el.querySelector('.settings-page__section-title');
    expect(section?.textContent).toContain('Worker Authentication');
  });

  // Cycle 4: shows API key form when mode is api_key
  it('should render the API key input when mode is api_key', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    httpMock.expectOne('/api/settings').flush({
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
    const input = el.querySelector('.settings-page__api-key-input');
    expect(input).toBeTruthy();
  });

  it('should not render the API key input when mode is oauth', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    httpMock.expectOne('/api/settings').flush({
      authMode: 'OAuth',
      maxConcurrent: 3,
      timeoutMinutes: 60,
      accessTokenPresent: true,
      refreshTokenPresent: true,
      expiresAt: '2027-01-01T00:00:00Z',
      subscriptionType: 'pro',
    });
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
    httpMock.expectOne('/api/settings').flush({
      authMode: 'OAuth',
      maxConcurrent: 3,
      timeoutMinutes: 60,
      accessTokenPresent: true,
      refreshTokenPresent: true,
      expiresAt: '2027-01-01T00:00:00Z',
      subscriptionType: 'pro',
    });
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
    httpMock.expectOne('/api/settings').flush({
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
    const radios = el.querySelectorAll('input[type="radio"]');
    expect(radios.length).toBe(2);
  });

  // Cycle 8: back link is rendered
  it('should render a back link to issues', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    httpMock.expectOne('/api/settings').flush({
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
    const backLink = el.querySelector('.settings-page__back-link');
    expect(backLink).toBeTruthy();
  });
});

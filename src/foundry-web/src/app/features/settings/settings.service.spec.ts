import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SettingsService } from './settings.service';
import { AuthSettings } from './settings.model';

const mockAuthSettings: AuthSettings = {
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

function setupService() {
  TestBed.configureTestingModule({
    providers: [
      SettingsService,
      provideHttpClient(),
      provideHttpClientTesting(),
    ],
  });
  return {
    service: TestBed.inject(SettingsService),
    httpMock: TestBed.inject(HttpTestingController),
  };
}

describe('SettingsService', () => {
  let service: SettingsService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    const setup = setupService();
    service = setup.service;
    httpMock = setup.httpMock;
  });

  afterEach(() => httpMock.verify());

  // Cycle 1: initial signal state
  it('should start with null authSettings and loading false', () => {
    // Arrange / Act — no calls yet

    // Assert
    expect(service.authSettings()).toBeNull();
    expect(service.loading()).toBe(false);
    expect(service.loadError()).toBeNull();
  });

  // Cycle 2: loadSettings populates authSettings signal
  it('should populate authSettings after loadSettings succeeds', () => {
    // Arrange / Act
    service.loadSettings();
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

    // Assert
    const settings = service.authSettings();
    expect(settings).not.toBeNull();
    expect(settings!.mode).toBe('api_key');
    expect(settings!.apiKeyConfigured).toBe(true);
  });

  // Cycle 3: loadSettings sets loading true during request
  it('should set loading to true while loadSettings is in flight', () => {
    // Arrange / Act
    service.loadSettings();

    // Assert — before flush
    expect(service.loading()).toBe(true);
    httpMock.expectOne('/api/settings').flush({
      authMode: 'ApiKey',
      maxConcurrent: 3,
      timeoutMinutes: 60,
      accessTokenPresent: false,
      refreshTokenPresent: false,
      expiresAt: null,
      subscriptionType: null,
    });
  });

  it('should set loading to false after loadSettings succeeds', () => {
    // Arrange
    service.loadSettings();
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
    expect(service.loading()).toBe(false);
  });

  // Cycle 4: loadSettings sets loadError on failure
  it('should set loadError when loadSettings fails', () => {
    // Arrange / Act
    service.loadSettings();
    httpMock.expectOne('/api/settings').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });

    // Assert
    expect(service.loadError()).not.toBeNull();
    expect(service.loading()).toBe(false);
  });

  it('should set loadError to a fixed user-facing string when loadSettings fails', () => {
    // Arrange / Act
    service.loadSettings();
    httpMock.expectOne('/api/settings').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });

    // Assert
    expect(service.loadError()).toBe('Failed to load settings');
  });

  // Cycle 5: loadSettings maps OAuth mode
  it('should map OAuth authMode to oauth mode string', () => {
    // Arrange / Act
    service.loadSettings();
    httpMock.expectOne('/api/settings').flush({
      authMode: 'OAuth',
      maxConcurrent: 3,
      timeoutMinutes: 60,
      accessTokenPresent: true,
      refreshTokenPresent: true,
      expiresAt: '2027-01-01T00:00:00Z',
      subscriptionType: 'pro',
    });

    // Assert
    const settings = service.authSettings();
    expect(settings!.mode).toBe('oauth');
    expect(settings!.oauth).not.toBeNull();
    expect(settings!.oauth!.accessTokenPresent).toBe(true);
  });

  // Cycle 6: updateAuthMode calls PUT /api/settings/auth
  it('should PUT to /api/settings/auth when updateAuthMode is called', () => {
    // Arrange / Act
    service.updateAuthMode('api_key', 'my-api-key');
    const req = httpMock.expectOne('/api/settings/auth');

    // Assert
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ mode: 'api_key', apiKey: 'my-api-key' });
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

  it('should set saving to true while updateAuthMode is in flight', () => {
    // Arrange / Act
    service.updateAuthMode('api_key', 'my-api-key');

    // Assert — before flush
    expect(service.saving()).toBe(true);
    httpMock.expectOne('/api/settings/auth').flush({
      authMode: 'ApiKey',
      maxConcurrent: 3,
      timeoutMinutes: 60,
      accessTokenPresent: false,
      refreshTokenPresent: false,
      expiresAt: null,
      subscriptionType: null,
    });
  });

  it('should set saving to false and saveSuccess to true after updateAuthMode succeeds', () => {
    // Arrange
    service.updateAuthMode('api_key', 'my-api-key');
    httpMock.expectOne('/api/settings/auth').flush({
      authMode: 'ApiKey',
      maxConcurrent: 3,
      timeoutMinutes: 60,
      accessTokenPresent: false,
      refreshTokenPresent: false,
      expiresAt: null,
      subscriptionType: null,
    });

    // Assert
    expect(service.saving()).toBe(false);
    expect(service.saveSuccess()).toBe(true);
    expect(service.saveError()).toBeNull();
  });

  it('should set saveError when updateAuthMode fails', () => {
    // Arrange
    service.updateAuthMode('api_key', 'my-api-key');
    httpMock.expectOne('/api/settings/auth').flush('Bad Request', {
      status: 400,
      statusText: 'Bad Request',
    });

    // Assert
    expect(service.saveError()).not.toBeNull();
    expect(service.saving()).toBe(false);
    expect(service.saveSuccess()).toBe(false);
  });

  it('should set saveError to a fixed user-facing string when updateAuthMode fails', () => {
    // Arrange
    service.updateAuthMode('api_key', 'my-api-key');
    httpMock.expectOne('/api/settings/auth').flush('Bad Request', {
      status: 400,
      statusText: 'Bad Request',
    });

    // Assert
    expect(service.saveError()).toBe('Failed to save settings');
  });

  // Cycle 7: updateAuthMode without apiKey (oauth mode)
  it('should PUT without apiKey when updateAuthMode is called with oauth mode', () => {
    // Arrange / Act
    service.updateAuthMode('oauth');
    const req = httpMock.expectOne('/api/settings/auth');

    // Assert
    expect(req.request.body).toEqual({ mode: 'oauth' });
    req.flush({
      authMode: 'OAuth',
      maxConcurrent: 3,
      timeoutMinutes: 60,
      accessTokenPresent: true,
      refreshTokenPresent: true,
      expiresAt: '2027-01-01T00:00:00Z',
      subscriptionType: 'pro',
    });
  });

  // Cycle 8: scanOAuthCredentials calls GET /api/settings/oauth/scan
  it('should GET /api/settings/oauth/scan when scanOAuthCredentials is called', () => {
    // Arrange / Act
    service.scanOAuthCredentials();
    const req = httpMock.expectOne('/api/settings/oauth/scan');

    // Assert
    expect(req.request.method).toBe('GET');
    req.flush({
      accessToken: 'token',
      refreshToken: 'refresh',
      expiresAt: '2027-01-01T00:00:00Z',
      subscriptionType: 'pro',
    });
  });

  it('should set switching to true while scanOAuthCredentials is in flight', () => {
    // Arrange / Act
    service.scanOAuthCredentials();

    // Assert — before flush
    expect(service.switching()).toBe(true);
    httpMock.expectOne('/api/settings/oauth/scan').flush({
      accessToken: 'token',
      refreshToken: 'refresh',
      expiresAt: '2027-01-01T00:00:00Z',
      subscriptionType: 'pro',
    });
  });

  it('should set switchError when scanOAuthCredentials fails', () => {
    // Arrange
    service.scanOAuthCredentials();
    httpMock.expectOne('/api/settings/oauth/scan').flush('Not Found', {
      status: 404,
      statusText: 'Not Found',
    });

    // Assert
    expect(service.switchError()).not.toBeNull();
    expect(service.switching()).toBe(false);
  });

  it('should set switchError to a fixed user-facing string when scanOAuthCredentials fails', () => {
    // Arrange
    service.scanOAuthCredentials();
    httpMock.expectOne('/api/settings/oauth/scan').flush('Not Found', {
      status: 404,
      statusText: 'Not Found',
    });

    // Assert
    expect(service.switchError()).toBe('Failed to scan for OAuth credentials');
  });

  // Cycle 9: updateAuthMode updates authSettings on success
  it('should update authSettings signal after updateAuthMode succeeds', () => {
    // Arrange — first load settings
    service.loadSettings();
    httpMock.expectOne('/api/settings').flush({
      authMode: 'ApiKey',
      maxConcurrent: 3,
      timeoutMinutes: 60,
      accessTokenPresent: false,
      refreshTokenPresent: false,
      expiresAt: null,
      subscriptionType: null,
    });
    expect(service.authSettings()!.mode).toBe('api_key');

    // Act — switch to oauth
    service.updateAuthMode('oauth');
    httpMock.expectOne('/api/settings/auth').flush({
      authMode: 'OAuth',
      maxConcurrent: 3,
      timeoutMinutes: 60,
      accessTokenPresent: true,
      refreshTokenPresent: true,
      expiresAt: '2027-01-01T00:00:00Z',
      subscriptionType: 'pro',
    });

    // Assert
    expect(service.authSettings()!.mode).toBe('oauth');
  });
});

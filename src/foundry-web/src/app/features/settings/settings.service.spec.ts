import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { WritableSignal } from '@angular/core';
import { SettingsService } from './settings.service';
import { AuthSettings } from './settings.model';

const mockAuthSettings: AuthSettings = {
  mode: 'api_key',
  apiKeyConfigured: false,
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
    // Arrange
    // (service initialized by test setup)

    // Act
    // (no action — testing initial state)

    // Assert
    expect(service.authSettings()).toBeNull();
    expect(service.loading()).toBe(false);
    expect(service.loadError()).toBeNull();
  });

  // Cycle 2: loadSettings populates authSettings signal
  it('should populate authSettings after loadSettings succeeds', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
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
    expect(settings!.apiKeyConfigured).toBe(false);
  });

  // Cycle 3: loadSettings sets loading true during request
  it('should set loading to true while loadSettings is in flight', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
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
    // Arrange
    // (service initialized by test setup)

    // Act
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
    // Arrange
    // (service initialized by test setup)

    // Act
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
    // Arrange
    // (service initialized by test setup)

    // Act
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
    // Arrange
    // (service initialized by test setup)

    // Act
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
    // Arrange
    // (service initialized by test setup)

    // Act
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
    // Arrange
    // (service initialized by test setup)

    // Act
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

  // Cycle 8: scanOAuthCredentials calls GET /api/settings/oauth/scan and applies result
  it('should GET /api/settings/oauth/scan when scanOAuthCredentials is called', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
    service.scanOAuthCredentials();
    const req = httpMock.expectOne('/api/settings/oauth/scan');

    // Assert
    expect(req.request.method).toBe('GET');
    req.flush({
      accessTokenPresent: true,
      refreshTokenPresent: true,
      expiresAt: '2027-01-01T00:00:00Z',
      subscriptionType: 'pro',
    });
    httpMock.expectOne('/api/settings/auth').flush({
      authMode: 'OAuth',
      maxConcurrent: 3,
      timeoutMinutes: 60,
      accessTokenPresent: true,
      refreshTokenPresent: true,
      expiresAt: '2027-01-01T00:00:00Z',
      subscriptionType: 'pro',
    });
  });

  it('should set switching to true while scanOAuthCredentials is in flight', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
    service.scanOAuthCredentials();

    // Assert — before flush
    expect(service.switching()).toBe(true);
    httpMock.expectOne('/api/settings/oauth/scan').flush({
      accessTokenPresent: true,
      refreshTokenPresent: true,
      expiresAt: '2027-01-01T00:00:00Z',
      subscriptionType: 'pro',
    });
    httpMock.expectOne('/api/settings/auth').flush({
      authMode: 'OAuth',
      maxConcurrent: 3,
      timeoutMinutes: 60,
      accessTokenPresent: true,
      refreshTokenPresent: true,
      expiresAt: '2027-01-01T00:00:00Z',
      subscriptionType: 'pro',
    });
  });

  it('should PUT to /api/settings/auth with oauth mode after scan succeeds', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
    service.scanOAuthCredentials();
    httpMock.expectOne('/api/settings/oauth/scan').flush({
      accessTokenPresent: true,
      refreshTokenPresent: true,
      expiresAt: '2027-01-01T00:00:00Z',
      subscriptionType: 'pro',
    });
    const putReq = httpMock.expectOne('/api/settings/auth');

    // Assert
    expect(putReq.request.method).toBe('PUT');
    expect(putReq.request.body).toEqual({ mode: 'oauth' });
    putReq.flush({
      authMode: 'OAuth',
      maxConcurrent: 3,
      timeoutMinutes: 60,
      accessTokenPresent: true,
      refreshTokenPresent: true,
      expiresAt: '2027-01-01T00:00:00Z',
      subscriptionType: 'pro',
    });
  });

  it('should update authSettings and set saveSuccess after scanOAuthCredentials succeeds', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
    service.scanOAuthCredentials();
    httpMock.expectOne('/api/settings/oauth/scan').flush({
      accessTokenPresent: true,
      refreshTokenPresent: true,
      expiresAt: '2027-01-01T00:00:00Z',
      subscriptionType: 'pro',
    });
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
    expect(service.switching()).toBe(false);
    expect(service.saveSuccess()).toBe(true);
    expect(service.authSettings()!.mode).toBe('oauth');
  });

  it('should set switchError when scanOAuthCredentials scan fails', () => {
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

  it('should set switchError to a fixed user-facing string when scan fails', () => {
    // Arrange
    service.scanOAuthCredentials();
    httpMock.expectOne('/api/settings/oauth/scan').flush('Not Found', {
      status: 404,
      statusText: 'Not Found',
    });

    // Assert
    expect(service.switchError()).toBe('Failed to switch to OAuth mode');
  });

  it('should set switchError when updateAuthMode call fails after scan', () => {
    // Arrange
    service.scanOAuthCredentials();
    httpMock.expectOne('/api/settings/oauth/scan').flush({
      accessTokenPresent: true,
      refreshTokenPresent: true,
      expiresAt: '2027-01-01T00:00:00Z',
      subscriptionType: 'pro',
    });
    httpMock.expectOne('/api/settings/auth').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });

    // Assert
    expect(service.switchError()).toBe('Failed to switch to OAuth mode');
    expect(service.switching()).toBe(false);
  });

  // Cycle 8b: loadSettings resets stale signals
  it('should reset saveSuccess, saveError, switchError, saving, and switching when loadSettings is called', () => {
    // Arrange — put signals into a dirty state
    service.saveSuccess.set(true);
    service.saving.set(true);
    service.switching.set(true);
    (service as unknown as { _saveErrorSignal: WritableSignal<string | null> })._saveErrorSignal.set('old error');
    (service as unknown as { _switchErrorSignal: WritableSignal<string | null> })._switchErrorSignal.set('old switch error');

    // Act
    service.loadSettings();

    // Assert — all cleared before the response arrives
    expect(service.saveSuccess()).toBe(false);
    expect(service.saving()).toBe(false);
    expect(service.switching()).toBe(false);
    expect(service.saveError()).toBeNull();
    expect(service.switchError()).toBeNull();

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

  // Cycle 10: loadSettings populates workerLimits signal
  it('should populate workerLimits from loadSettings response', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
    service.loadSettings();
    httpMock.expectOne('/api/settings').flush({
      authMode: 'ApiKey',
      maxConcurrent: 5,
      timeoutMinutes: 90,
      accessTokenPresent: false,
      refreshTokenPresent: false,
      expiresAt: null,
      subscriptionType: null,
    });

    // Assert
    const limits = service.workerLimits();
    expect(limits).not.toBeNull();
    expect(limits!.maxConcurrent).toBe(5);
    expect(limits!.timeoutMinutes).toBe(90);
  });

  it('should start with null workerLimits', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
    // (no action — testing initial state)

    // Assert
    expect(service.workerLimits()).toBeNull();
  });

  // Cycle 11: updateWorkerLimits sends PUT and updates signal on success
  it('should PUT to /api/settings/limits when updateWorkerLimits is called', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
    service.updateWorkerLimits(3, 120);
    const req = httpMock.expectOne('/api/settings/limits');

    // Assert
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ maxConcurrent: 3, timeoutMinutes: 120 });
    req.flush({
      authMode: 'ApiKey',
      maxConcurrent: 3,
      timeoutMinutes: 120,
      accessTokenPresent: false,
      refreshTokenPresent: false,
      expiresAt: null,
      subscriptionType: null,
    });
  });

  it('should set savingLimits to true while updateWorkerLimits is in flight', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
    service.updateWorkerLimits(3, 120);

    // Assert — before flush
    expect(service.savingLimits()).toBe(true);
    httpMock.expectOne('/api/settings/limits').flush({
      authMode: 'ApiKey',
      maxConcurrent: 3,
      timeoutMinutes: 120,
      accessTokenPresent: false,
      refreshTokenPresent: false,
      expiresAt: null,
      subscriptionType: null,
    });
  });

  it('should update workerLimits and set saveLimitsSuccess to true after updateWorkerLimits succeeds', () => {
    // Arrange
    service.updateWorkerLimits(3, 120);
    httpMock.expectOne('/api/settings/limits').flush({
      authMode: 'ApiKey',
      maxConcurrent: 3,
      timeoutMinutes: 120,
      accessTokenPresent: false,
      refreshTokenPresent: false,
      expiresAt: null,
      subscriptionType: null,
    });

    // Assert
    expect(service.savingLimits()).toBe(false);
    expect(service.saveLimitsSuccess()).toBe(true);
    expect(service.saveLimitsError()).toBeNull();
    expect(service.workerLimits()!.maxConcurrent).toBe(3);
    expect(service.workerLimits()!.timeoutMinutes).toBe(120);
  });

  // Cycle 12: updateWorkerLimits sets error signal on failure
  it('should set saveLimitsError when updateWorkerLimits fails', () => {
    // Arrange
    service.updateWorkerLimits(3, 120);
    httpMock.expectOne('/api/settings/limits').flush('Bad Request', {
      status: 400,
      statusText: 'Bad Request',
    });

    // Assert
    expect(service.saveLimitsError()).not.toBeNull();
    expect(service.savingLimits()).toBe(false);
    expect(service.saveLimitsSuccess()).toBe(false);
  });

  it('should set saveLimitsError to a fixed user-facing string when updateWorkerLimits fails', () => {
    // Arrange
    service.updateWorkerLimits(3, 120);
    httpMock.expectOne('/api/settings/limits').flush('Bad Request', {
      status: 400,
      statusText: 'Bad Request',
    });

    // Assert
    expect(service.saveLimitsError()).toBe('Failed to save worker limits');
  });

  // Cycle 13: signal state transitions for limits
  it('should clear saveLimitsError and saveLimitsSuccess when updateWorkerLimits is called', () => {
    // Arrange — put signals into dirty state
    service.updateWorkerLimits(1, 60);
    httpMock.expectOne('/api/settings/limits').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    expect(service.saveLimitsError()).not.toBeNull();

    // Act — call again
    service.updateWorkerLimits(2, 90);

    // Assert — cleared before request completes
    expect(service.saveLimitsError()).toBeNull();
    expect(service.saveLimitsSuccess()).toBe(false);
    expect(service.savingLimits()).toBe(true);
    httpMock.expectOne('/api/settings/limits').flush({
      authMode: 'ApiKey',
      maxConcurrent: 2,
      timeoutMinutes: 90,
      accessTokenPresent: false,
      refreshTokenPresent: false,
      expiresAt: null,
      subscriptionType: null,
    });
  });

  it('should reset limits signals in loadSettings', () => {
    // Arrange — put signals into dirty state via public API
    service.updateWorkerLimits(1, 60);
    httpMock.expectOne('/api/settings/limits').flush({
      authMode: 'ApiKey',
      maxConcurrent: 1,
      timeoutMinutes: 60,
      accessTokenPresent: false,
      refreshTokenPresent: false,
      expiresAt: null,
      subscriptionType: null,
    });
    // saveLimitsSuccess is now true; force an error state for saveLimitsError
    service.updateWorkerLimits(99999, 60);
    httpMock.expectOne('/api/settings/limits').flush('Bad Request', {
      status: 400,
      statusText: 'Bad Request',
    });

    // Act
    service.loadSettings();

    // Assert — cleared before the response arrives
    expect(service.saveLimitsSuccess()).toBe(false);
    expect(service.savingLimits()).toBe(false);
    expect(service.saveLimitsError()).toBeNull();

    httpMock.expectOne('/api/settings').flush({
      authMode: 'ApiKey',
      maxConcurrent: 1,
      timeoutMinutes: 60,
      accessTokenPresent: false,
      refreshTokenPresent: false,
      expiresAt: null,
      subscriptionType: null,
    });
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
      systemPromptTemplate: null,
      workerPromptTemplate: null,
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
      systemPromptTemplate: null,
      workerPromptTemplate: null,
    });

    // Assert
    expect(service.authSettings()!.mode).toBe('oauth');
  });

  // Cycle 14: loadSettings populates prompt template signals
  it('should start with null systemPromptTemplate and workerPromptTemplate', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
    // (no action — testing initial state)

    // Assert
    expect(service.systemPromptTemplate()).toBeNull();
    expect(service.workerPromptTemplate()).toBeNull();
  });

  it('should populate systemPromptTemplate from loadSettings response', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
    service.loadSettings();
    httpMock.expectOne('/api/settings').flush({
      authMode: 'ApiKey',
      maxConcurrent: 3,
      timeoutMinutes: 60,
      accessTokenPresent: false,
      refreshTokenPresent: false,
      expiresAt: null,
      subscriptionType: null,
      systemPromptTemplate: 'You are a helpful assistant.',
      workerPromptTemplate: null,
    });

    // Assert
    expect(service.systemPromptTemplate()).toBe('You are a helpful assistant.');
    expect(service.workerPromptTemplate()).toBeNull();
  });

  it('should populate workerPromptTemplate from loadSettings response', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
    service.loadSettings();
    httpMock.expectOne('/api/settings').flush({
      authMode: 'ApiKey',
      maxConcurrent: 3,
      timeoutMinutes: 60,
      accessTokenPresent: false,
      refreshTokenPresent: false,
      expiresAt: null,
      subscriptionType: null,
      systemPromptTemplate: null,
      workerPromptTemplate: 'Fix the issue.',
    });

    // Assert
    expect(service.systemPromptTemplate()).toBeNull();
    expect(service.workerPromptTemplate()).toBe('Fix the issue.');
  });

  // Cycle 15: updatePromptTemplates calls PUT /api/settings/prompts
  it('should PUT to /api/settings/prompts when updatePromptTemplates is called', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
    service.updatePromptTemplates({ systemPromptTemplate: 'sys', workerPromptTemplate: 'worker' });
    const req = httpMock.expectOne('/api/settings/prompts');

    // Assert
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ systemPromptTemplate: 'sys', workerPromptTemplate: 'worker' });
    req.flush({
      authMode: 'ApiKey',
      maxConcurrent: 3,
      timeoutMinutes: 60,
      accessTokenPresent: false,
      refreshTokenPresent: false,
      expiresAt: null,
      subscriptionType: null,
      systemPromptTemplate: 'sys',
      workerPromptTemplate: 'worker',
    });
  });

  it('should set savingPrompts to true while updatePromptTemplates is in flight', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
    service.updatePromptTemplates({ systemPromptTemplate: 'sys', workerPromptTemplate: null });

    // Assert — before flush
    expect(service.savingPrompts()).toBe(true);
    httpMock.expectOne('/api/settings/prompts').flush({
      authMode: 'ApiKey',
      maxConcurrent: 3,
      timeoutMinutes: 60,
      accessTokenPresent: false,
      refreshTokenPresent: false,
      expiresAt: null,
      subscriptionType: null,
      systemPromptTemplate: 'sys',
      workerPromptTemplate: null,
    });
  });

  it('should update prompt template signals and set savePromptsSuccess to true after updatePromptTemplates succeeds', () => {
    // Arrange
    service.updatePromptTemplates({ systemPromptTemplate: 'sys', workerPromptTemplate: 'worker' });
    httpMock.expectOne('/api/settings/prompts').flush({
      authMode: 'ApiKey',
      maxConcurrent: 3,
      timeoutMinutes: 60,
      accessTokenPresent: false,
      refreshTokenPresent: false,
      expiresAt: null,
      subscriptionType: null,
      systemPromptTemplate: 'sys',
      workerPromptTemplate: 'worker',
    });

    // Assert
    expect(service.savingPrompts()).toBe(false);
    expect(service.savePromptsSuccess()).toBe(true);
    expect(service.savePromptsError()).toBeNull();
    expect(service.systemPromptTemplate()).toBe('sys');
    expect(service.workerPromptTemplate()).toBe('worker');
  });

  // Cycle 16: updatePromptTemplates error handling
  it('should set savePromptsError when updatePromptTemplates fails', () => {
    // Arrange
    service.updatePromptTemplates({ systemPromptTemplate: 'sys', workerPromptTemplate: null });
    httpMock.expectOne('/api/settings/prompts').flush('Bad Request', {
      status: 400,
      statusText: 'Bad Request',
    });

    // Assert
    expect(service.savePromptsError()).not.toBeNull();
    expect(service.savingPrompts()).toBe(false);
    expect(service.savePromptsSuccess()).toBe(false);
  });

  it('should set savePromptsError to a fixed user-facing string when updatePromptTemplates fails', () => {
    // Arrange
    service.updatePromptTemplates({ systemPromptTemplate: 'sys', workerPromptTemplate: null });
    httpMock.expectOne('/api/settings/prompts').flush('Bad Request', {
      status: 400,
      statusText: 'Bad Request',
    });

    // Assert
    expect(service.savePromptsError()).toBe('Failed to save prompt templates');
  });

  it('should clear savePromptsError and savePromptsSuccess when updatePromptTemplates is called again', () => {
    // Arrange — put signals into dirty state
    service.updatePromptTemplates({ systemPromptTemplate: 'bad', workerPromptTemplate: null });
    httpMock.expectOne('/api/settings/prompts').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    expect(service.savePromptsError()).not.toBeNull();

    // Act — call again
    service.updatePromptTemplates({ systemPromptTemplate: 'good', workerPromptTemplate: null });

    // Assert — cleared before request completes
    expect(service.savePromptsError()).toBeNull();
    expect(service.savePromptsSuccess()).toBe(false);
    expect(service.savingPrompts()).toBe(true);
    httpMock.expectOne('/api/settings/prompts').flush({
      authMode: 'ApiKey',
      maxConcurrent: 3,
      timeoutMinutes: 60,
      accessTokenPresent: false,
      refreshTokenPresent: false,
      expiresAt: null,
      subscriptionType: null,
      systemPromptTemplate: 'good',
      workerPromptTemplate: null,
    });
  });

  it('should reset prompt signals in loadSettings', () => {
    // Arrange — put signals into dirty state
    service.updatePromptTemplates({ systemPromptTemplate: 'sys', workerPromptTemplate: 'worker' });
    httpMock.expectOne('/api/settings/prompts').flush({
      authMode: 'ApiKey',
      maxConcurrent: 3,
      timeoutMinutes: 60,
      accessTokenPresent: false,
      refreshTokenPresent: false,
      expiresAt: null,
      subscriptionType: null,
      systemPromptTemplate: 'sys',
      workerPromptTemplate: 'worker',
    });
    expect(service.savePromptsSuccess()).toBe(true);

    // Act
    service.loadSettings();

    // Assert — cleared before the response arrives
    expect(service.savePromptsSuccess()).toBe(false);
    expect(service.savingPrompts()).toBe(false);
    expect(service.savePromptsError()).toBeNull();

    httpMock.expectOne('/api/settings').flush({
      authMode: 'ApiKey',
      maxConcurrent: 3,
      timeoutMinutes: 60,
      accessTokenPresent: false,
      refreshTokenPresent: false,
      expiresAt: null,
      subscriptionType: null,
      systemPromptTemplate: null,
      workerPromptTemplate: null,
    });
  });
});

import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { WritableSignal } from '@angular/core';
import { Subject } from 'rxjs';
import { vi } from 'vitest';
import { SettingsService } from './settings.service';
import { LoginSessionUpdate } from './settings.model';
import { DispatchService } from '../../core/services/dispatch.service';
import { SystemSignalRService } from '../../core/services/system-signalr.service';

function createMockSignalRService() {
  return {
    reconnected: new Subject<void>(),
    dispatchStateChanged: new Subject<void>(),
    loginSessionUpdate: new Subject<LoginSessionUpdate>(),
    notifications: [] as never,
  };
}

function setupService(signalROverride?: ReturnType<typeof createMockSignalRService>) {
  const mockSignalR = signalROverride ?? createMockSignalRService();

  TestBed.configureTestingModule({
    providers: [
      SettingsService,
      DispatchService,
      { provide: SystemSignalRService, useValue: mockSignalR },
      provideHttpClient(),
      provideHttpClientTesting(),
    ],
  });
  return {
    service: TestBed.inject(SettingsService),
    dispatchService: TestBed.inject(DispatchService),
    httpMock: TestBed.inject(HttpTestingController),
    mockSignalR,
  };
}

function buildSettingsResponse(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    authMode: 'ApiKey',
    oAuthStatus: 'NotConfigured',
    oAuthAccountEmail: null,
    oAuthAccountOrgName: null,
    maxConcurrent: 3,
    timeoutMinutes: 60,
    expiresAt: null,
    subscriptionType: null,
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
    ...overrides,
  };
}

describe('SettingsService', () => {
  let service: SettingsService;
  let dispatchService: DispatchService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    const setup = setupService();
    service = setup.service;
    dispatchService = setup.dispatchService;
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
    req.flush(buildSettingsResponse({ authMode: 'ApiKey', oAuthStatus: 'NotConfigured' }));

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
    httpMock.expectOne('/api/settings').flush(buildSettingsResponse());
  });

  it('should set loading to false after loadSettings succeeds', () => {
    // Arrange
    service.loadSettings();
    httpMock.expectOne('/api/settings').flush(buildSettingsResponse());

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

  // Cycle 5: loadSettings maps OAuth mode — maps oAuthStatus straight through
  it('should map OAuth authMode to oauth mode string with oAuthStatus passed through', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
    service.loadSettings();
    httpMock.expectOne('/api/settings').flush(buildSettingsResponse({
      authMode: 'OAuth',
      oAuthStatus: 'Present',
      expiresAt: '2027-01-01T00:00:00Z',
      subscriptionType: 'pro',
    }));

    // Assert
    const settings = service.authSettings();
    expect(settings!.mode).toBe('oauth');
    expect(settings!.oauth).not.toBeNull();
    expect(settings!.oauth!.status).toBe('Present');
    expect(settings!.oauth!.expiresAt).toBe('2027-01-01T00:00:00Z');
    expect(settings!.oauth!.subscriptionType).toBe('pro');
  });

  it('should map oAuthStatus ReLoginNeeded straight through', () => {
    // Arrange
    service.loadSettings();
    httpMock.expectOne('/api/settings').flush(buildSettingsResponse({
      authMode: 'OAuth',
      oAuthStatus: 'ReLoginNeeded',
    }));

    // Assert
    const settings = service.authSettings();
    expect(settings!.oauth!.status).toBe('ReLoginNeeded');
  });

  it('should map oAuthStatus NotConfigured straight through', () => {
    // Arrange
    service.loadSettings();
    httpMock.expectOne('/api/settings').flush(buildSettingsResponse({
      authMode: 'OAuth',
      oAuthStatus: 'NotConfigured',
    }));

    // Assert
    const settings = service.authSettings();
    expect(settings!.oauth!.status).toBe('NotConfigured');
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
    req.flush(buildSettingsResponse());
  });

  it('should set saving to true while updateAuthMode is in flight', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
    service.updateAuthMode('api_key', 'my-api-key');

    // Assert — before flush
    expect(service.saving()).toBe(true);
    httpMock.expectOne('/api/settings/auth').flush(buildSettingsResponse());
  });

  it('should set saving to false and saveSuccess to true after updateAuthMode succeeds', () => {
    // Arrange
    service.updateAuthMode('api_key', 'my-api-key');
    httpMock.expectOne('/api/settings/auth').flush(buildSettingsResponse());

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
    req.flush(buildSettingsResponse({ authMode: 'OAuth', oAuthStatus: 'Present' }));
  });

  // markOAuthConfigured: calls PUT /api/settings/auth with { mode: 'oauth' }
  it('should PUT to /api/settings/auth with mode oauth when markOAuthConfigured is called', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
    service.markOAuthConfigured();
    const req = httpMock.expectOne('/api/settings/auth');

    // Assert
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ mode: 'oauth' });
    req.flush(buildSettingsResponse({ authMode: 'OAuth', oAuthStatus: 'NotConfigured' }));
  });

  it('should update authSettings after markOAuthConfigured succeeds', () => {
    // Arrange
    service.markOAuthConfigured();
    httpMock.expectOne('/api/settings/auth').flush(buildSettingsResponse({
      authMode: 'OAuth',
      oAuthStatus: 'Present',
    }));

    // Assert
    const settings = service.authSettings();
    expect(settings!.mode).toBe('oauth');
    expect(settings!.oauth!.status).toBe('Present');
  });

  // Cycle 8b: loadSettings resets stale signals
  it('should reset saveSuccess, saveError, saving, and switching when loadSettings is called', () => {
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

    httpMock.expectOne('/api/settings').flush(buildSettingsResponse());
  });

  // Cycle 10: loadSettings populates workerLimits signal
  it('should populate workerLimits from loadSettings response', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
    service.loadSettings();
    httpMock.expectOne('/api/settings').flush(buildSettingsResponse({ maxConcurrent: 5, timeoutMinutes: 90 }));

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
    req.flush(buildSettingsResponse({ maxConcurrent: 3, timeoutMinutes: 120 }));
  });

  it('should set savingLimits to true while updateWorkerLimits is in flight', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
    service.updateWorkerLimits(3, 120);

    // Assert — before flush
    expect(service.savingLimits()).toBe(true);
    httpMock.expectOne('/api/settings/limits').flush(buildSettingsResponse({ maxConcurrent: 3, timeoutMinutes: 120 }));
  });

  it('should update workerLimits and set saveLimitsSuccess to true after updateWorkerLimits succeeds', () => {
    // Arrange
    service.updateWorkerLimits(3, 120);
    httpMock.expectOne('/api/settings/limits').flush(buildSettingsResponse({ maxConcurrent: 3, timeoutMinutes: 120 }));

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
    httpMock.expectOne('/api/settings/limits').flush(buildSettingsResponse({ maxConcurrent: 2, timeoutMinutes: 90 }));
  });

  it('should reset limits signals in loadSettings', () => {
    // Arrange — put signals into dirty state via public API
    service.updateWorkerLimits(1, 60);
    httpMock.expectOne('/api/settings/limits').flush(buildSettingsResponse({ maxConcurrent: 1, timeoutMinutes: 60 }));
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

    httpMock.expectOne('/api/settings').flush(buildSettingsResponse({ maxConcurrent: 1, timeoutMinutes: 60 }));
  });

  // Cycle 9: updateAuthMode updates authSettings on success
  it('should update authSettings signal after updateAuthMode succeeds', () => {
    // Arrange — first load settings
    service.loadSettings();
    httpMock.expectOne('/api/settings').flush(buildSettingsResponse({ authMode: 'ApiKey', oAuthStatus: 'NotConfigured' }));
    expect(service.authSettings()!.mode).toBe('api_key');

    // Act — switch to oauth
    service.updateAuthMode('oauth');
    httpMock.expectOne('/api/settings/auth').flush(buildSettingsResponse({
      authMode: 'OAuth',
      oAuthStatus: 'Present',
      expiresAt: '2027-01-01T00:00:00Z',
      subscriptionType: 'pro',
    }));

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
    httpMock.expectOne('/api/settings').flush(buildSettingsResponse({ systemPromptTemplate: 'You are a helpful assistant.' }));

    // Assert
    expect(service.systemPromptTemplate()).toBe('You are a helpful assistant.');
    expect(service.workerPromptTemplate()).toBeNull();
  });

  it('should populate workerPromptTemplate from loadSettings response', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
    service.loadSettings();
    httpMock.expectOne('/api/settings').flush(buildSettingsResponse({ workerPromptTemplate: 'Fix the issue.' }));

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
    req.flush(buildSettingsResponse({ systemPromptTemplate: 'sys', workerPromptTemplate: 'worker' }));
  });

  it('should set savingPrompts to true while updatePromptTemplates is in flight', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
    service.updatePromptTemplates({ systemPromptTemplate: 'sys', workerPromptTemplate: null });

    // Assert — before flush
    expect(service.savingPrompts()).toBe(true);
    httpMock.expectOne('/api/settings/prompts').flush(buildSettingsResponse({ systemPromptTemplate: 'sys' }));
  });

  it('should update prompt template signals and set savePromptsSuccess to true after updatePromptTemplates succeeds', () => {
    // Arrange
    service.updatePromptTemplates({ systemPromptTemplate: 'sys', workerPromptTemplate: 'worker' });
    httpMock.expectOne('/api/settings/prompts').flush(buildSettingsResponse({
      systemPromptTemplate: 'sys',
      workerPromptTemplate: 'worker',
    }));

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
    httpMock.expectOne('/api/settings/prompts').flush(buildSettingsResponse({ systemPromptTemplate: 'good' }));
  });

  it('should reset prompt signals in loadSettings', () => {
    // Arrange — put signals into dirty state
    service.updatePromptTemplates({ systemPromptTemplate: 'sys', workerPromptTemplate: 'worker' });
    httpMock.expectOne('/api/settings/prompts').flush(buildSettingsResponse({
      systemPromptTemplate: 'sys',
      workerPromptTemplate: 'worker',
    }));
    expect(service.savePromptsSuccess()).toBe(true);

    // Act
    service.loadSettings();

    // Assert — cleared before the response arrives
    expect(service.savePromptsSuccess()).toBe(false);
    expect(service.savingPrompts()).toBe(false);
    expect(service.savePromptsError()).toBeNull();

    httpMock.expectOne('/api/settings').flush(buildSettingsResponse());
  });

  // Dispatch integration: loadSettings calls dispatchService.updateFromSettings
  it('should update dispatchService state after loadSettings succeeds', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
    service.loadSettings();
    httpMock.expectOne('/api/settings').flush(buildSettingsResponse({
      isDispatchPaused: true,
      usageLimitResetsAt: '2026-07-01T12:00:00Z',
    }));

    // Assert
    expect(dispatchService.isDispatchPaused()).toBe(true);
    expect(dispatchService.usageLimitResetsAt()).toBe('2026-07-01T12:00:00Z');
  });

  // Dispatch settings signals: initial state
  it('should start with false savingDispatch, false saveDispatchSuccess, and null saveDispatchError', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
    // (no action — testing initial state)

    // Assert
    expect(service.savingDispatch()).toBe(false);
    expect(service.saveDispatchSuccess()).toBe(false);
    expect(service.saveDispatchError()).toBeNull();
  });

  // updateDispatchSettings: sends PUT to /api/settings/dispatch
  it('should PUT to /api/settings/dispatch when updateDispatchSettings is called', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
    service.updateDispatchSettings(true, 90);
    const req = httpMock.expectOne('/api/settings/dispatch');

    // Assert
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ autoResumeOnUsageReset: true, defaultCooldownMinutes: 90 });
    req.flush(buildSettingsResponse({ autoResumeOnUsageReset: true, defaultCooldownMinutes: 90 }));
  });

  it('should set savingDispatch to true while updateDispatchSettings is in flight', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
    service.updateDispatchSettings(false, 30);

    // Assert — before flush
    expect(service.savingDispatch()).toBe(true);
    httpMock.expectOne('/api/settings/dispatch').flush(buildSettingsResponse());
  });

  it('should set saveDispatchSuccess to true and savingDispatch false after updateDispatchSettings succeeds', () => {
    // Arrange
    service.updateDispatchSettings(true, 60);
    httpMock.expectOne('/api/settings/dispatch').flush(buildSettingsResponse({
      autoResumeOnUsageReset: true,
      defaultCooldownMinutes: 60,
      isDispatchPaused: false,
    }));

    // Assert
    expect(service.savingDispatch()).toBe(false);
    expect(service.saveDispatchSuccess()).toBe(true);
    expect(service.saveDispatchError()).toBeNull();
  });

  it('should update dispatchService state after updateDispatchSettings succeeds', () => {
    // Arrange
    service.updateDispatchSettings(true, 60);
    httpMock.expectOne('/api/settings/dispatch').flush(buildSettingsResponse({
      isDispatchPaused: true,
      usageLimitResetsAt: '2026-08-01T00:00:00Z',
    }));

    // Assert
    expect(dispatchService.isDispatchPaused()).toBe(true);
    expect(dispatchService.usageLimitResetsAt()).toBe('2026-08-01T00:00:00Z');
  });

  it('should set saveDispatchError when updateDispatchSettings fails', () => {
    // Arrange
    service.updateDispatchSettings(true, 60);
    httpMock.expectOne('/api/settings/dispatch').flush('Bad Request', {
      status: 400,
      statusText: 'Bad Request',
    });

    // Assert
    expect(service.saveDispatchError()).not.toBeNull();
    expect(service.savingDispatch()).toBe(false);
    expect(service.saveDispatchSuccess()).toBe(false);
  });

  it('should set saveDispatchError to a fixed user-facing string when updateDispatchSettings fails', () => {
    // Arrange
    service.updateDispatchSettings(true, 60);
    httpMock.expectOne('/api/settings/dispatch').flush('Bad Request', {
      status: 400,
      statusText: 'Bad Request',
    });

    // Assert
    expect(service.saveDispatchError()).toBe('Failed to save dispatch settings');
  });

  it('should reset dispatch signals in loadSettings', () => {
    // Arrange — put signals into dirty state
    service.updateDispatchSettings(true, 60);
    httpMock.expectOne('/api/settings/dispatch').flush(buildSettingsResponse());
    expect(service.saveDispatchSuccess()).toBe(true);

    // Act
    service.loadSettings();

    // Assert — cleared before the response arrives
    expect(service.saveDispatchSuccess()).toBe(false);
    expect(service.savingDispatch()).toBe(false);
    expect(service.saveDispatchError()).toBeNull();

    httpMock.expectOne('/api/settings').flush(buildSettingsResponse());
  });

  // Worker image flags: initial signal state
  it('should start with null workerImageFlags, Idle imageBuildStatus, null imageBuildLogTail', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
    // (no action — testing initial state)

    // Assert
    expect(service.workerImageFlags()).toBeNull();
    expect(service.imageBuildStatus()).toBe('Idle');
    expect(service.imageBuildLogTail()).toBeNull();
    expect(service.savingImageFlags()).toBe(false);
    expect(service.saveImageFlagsError()).toBeNull();
  });

  // loadSettings populates workerImageFlags from response
  it('should populate workerImageFlags from loadSettings response', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
    service.loadSettings();
    httpMock.expectOne('/api/settings').flush(buildSettingsResponse({
      installDotnet: true,
      installAngular: false,
      installGlab: true,
      installGh: false,
      imageBuildStatus: 'Idle',
    }));

    // Assert
    const flags = service.workerImageFlags();
    expect(flags).not.toBeNull();
    expect(flags!.installDotnet).toBe(true);
    expect(flags!.installAngular).toBe(false);
    expect(flags!.installGlab).toBe(true);
    expect(flags!.installGh).toBe(false);
  });

  it('should populate installChromium and installDocker from loadSettings response', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
    service.loadSettings();
    httpMock.expectOne('/api/settings').flush(buildSettingsResponse({
      installChromium: true,
      installDocker: true,
    }));

    // Assert
    const flags = service.workerImageFlags();
    expect(flags!.installChromium).toBe(true);
    expect(flags!.installDocker).toBe(true);
  });

  // loadSettings sets imageBuildStatus from response
  it('should populate imageBuildStatus from loadSettings response', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
    service.loadSettings();
    httpMock.expectOne('/api/settings').flush(buildSettingsResponse({ imageBuildStatus: 'Building' }));

    // Assert
    expect(service.imageBuildStatus()).toBe('Building');
  });

  // loadSettings sets imageBuildLogTail from lastImageBuildError when status is Failed
  it('should set imageBuildLogTail from lastImageBuildError when status is Failed', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
    service.loadSettings();
    httpMock.expectOne('/api/settings').flush(buildSettingsResponse({
      imageBuildStatus: 'Failed',
      lastImageBuildError: 'Step 3/5 FAILED',
    }));

    // Assert
    expect(service.imageBuildLogTail()).toBe('Step 3/5 FAILED');
  });

  // updateWorkerImageFlags calls PUT /api/settings/worker-image
  it('should PUT to /api/settings/worker-image when updateWorkerImageFlags is called', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
    service.updateWorkerImageFlags({ installDotnet: true, installAngular: false, installGlab: true, installGh: false, installChromium: false, installDocker: false });
    const req = httpMock.expectOne('/api/settings/worker-image');

    // Assert
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ installDotnet: true, installAngular: false, installGlab: true, installGh: false, installChromium: false, installDocker: false });
    req.flush(buildSettingsResponse({ installDotnet: true, installGlab: true }));
  });

  it('should set savingImageFlags to true while updateWorkerImageFlags is in flight', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
    service.updateWorkerImageFlags({ installDotnet: false, installAngular: false, installGlab: false, installGh: false, installChromium: false, installDocker: false });

    // Assert — before flush
    expect(service.savingImageFlags()).toBe(true);
    httpMock.expectOne('/api/settings/worker-image').flush(buildSettingsResponse());
  });

  it('should update workerImageFlags and clear savingImageFlags after updateWorkerImageFlags succeeds', () => {
    // Arrange
    service.updateWorkerImageFlags({ installDotnet: true, installAngular: true, installGlab: false, installGh: false, installChromium: false, installDocker: false });
    httpMock.expectOne('/api/settings/worker-image').flush(buildSettingsResponse({
      installDotnet: true,
      installAngular: true,
      installGlab: false,
      installGh: false,
    }));

    // Assert
    expect(service.savingImageFlags()).toBe(false);
    expect(service.saveImageFlagsError()).toBeNull();
    expect(service.workerImageFlags()!.installDotnet).toBe(true);
    expect(service.workerImageFlags()!.installAngular).toBe(true);
  });

  it('should set saveImageFlagsSuccess to true after updateWorkerImageFlags succeeds', () => {
    // Arrange
    service.updateWorkerImageFlags({ installDotnet: true, installAngular: false, installGlab: false, installGh: false, installChromium: false, installDocker: false });
    httpMock.expectOne('/api/settings/worker-image').flush(buildSettingsResponse({ installDotnet: true }));

    // Assert
    expect(service.saveImageFlagsSuccess()).toBe(true);
  });

  it('should keep saveImageFlagsSuccess false when updateWorkerImageFlags fails', () => {
    // Arrange
    service.updateWorkerImageFlags({ installDotnet: false, installAngular: false, installGlab: false, installGh: false, installChromium: false, installDocker: false });
    httpMock.expectOne('/api/settings/worker-image').flush('Server Error', { status: 500, statusText: 'Internal Server Error' });

    // Assert
    expect(service.saveImageFlagsSuccess()).toBe(false);
  });

  it('should reset saveImageFlagsSuccess in loadSettings', () => {
    // Arrange — put success signal into true state
    service.updateWorkerImageFlags({ installDotnet: false, installAngular: false, installGlab: false, installGh: false, installChromium: false, installDocker: false });
    httpMock.expectOne('/api/settings/worker-image').flush(buildSettingsResponse());
    expect(service.saveImageFlagsSuccess()).toBe(true);

    // Act
    service.loadSettings();

    // Assert — cleared before the response arrives
    expect(service.saveImageFlagsSuccess()).toBe(false);
    httpMock.expectOne('/api/settings').flush(buildSettingsResponse());
  });

  it('should set saveImageFlagsError when updateWorkerImageFlags fails', () => {
    // Arrange
    service.updateWorkerImageFlags({ installDotnet: false, installAngular: false, installGlab: false, installGh: false, installChromium: false, installDocker: false });
    httpMock.expectOne('/api/settings/worker-image').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });

    // Assert
    expect(service.saveImageFlagsError()).not.toBeNull();
    expect(service.savingImageFlags()).toBe(false);
  });

  it('should set saveImageFlagsError to a fixed user-facing string when updateWorkerImageFlags fails', () => {
    // Arrange
    service.updateWorkerImageFlags({ installDotnet: false, installAngular: false, installGlab: false, installGh: false, installChromium: false, installDocker: false });
    httpMock.expectOne('/api/settings/worker-image').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });

    // Assert
    expect(service.saveImageFlagsError()).toBe('Failed to save worker image settings');
  });

  // retryImageBuild calls POST /api/settings/worker-image/retry
  it('should POST to /api/settings/worker-image/retry when retryImageBuild is called', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
    service.retryImageBuild();
    const req = httpMock.expectOne('/api/settings/worker-image/retry');

    // Assert
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toBeNull();
    req.flush(buildSettingsResponse({ imageBuildStatus: 'Building' }));
  });

  it('should update imageBuildStatus after retryImageBuild succeeds', () => {
    // Arrange
    service.retryImageBuild();
    httpMock.expectOne('/api/settings/worker-image/retry').flush(buildSettingsResponse({ imageBuildStatus: 'Building' }));

    // Assert
    expect(service.imageBuildStatus()).toBe('Building');
  });

  it('should set savingImageFlags to true while retryImageBuild is in flight', () => {
    // Arrange / Act
    service.retryImageBuild();

    // Assert — before flush
    expect(service.savingImageFlags()).toBe(true);
    httpMock.expectOne('/api/settings/worker-image/retry').flush(buildSettingsResponse({ imageBuildStatus: 'Building' }));
  });

  it('should clear savingImageFlags after retryImageBuild succeeds', () => {
    // Arrange
    service.retryImageBuild();
    httpMock.expectOne('/api/settings/worker-image/retry').flush(buildSettingsResponse({ imageBuildStatus: 'Building' }));

    // Assert
    expect(service.savingImageFlags()).toBe(false);
  });

  it('should set saveImageFlagsError when retryImageBuild fails', () => {
    // Arrange
    service.retryImageBuild();
    httpMock.expectOne('/api/settings/worker-image/retry').flush('Server Error', { status: 500, statusText: 'Internal Server Error' });

    // Assert
    expect(service.saveImageFlagsError()).toBe('Failed to save worker image settings');
    expect(service.savingImageFlags()).toBe(false);
  });

  // setImageBuildStatus updates status and logTail signals
  it('should update imageBuildStatus and imageBuildLogTail when setImageBuildStatus is called', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
    service.setImageBuildStatus('Failed', 'Build failed at step 2');

    // Assert
    expect(service.imageBuildStatus()).toBe('Failed');
    expect(service.imageBuildLogTail()).toBe('Build failed at step 2');
  });

  it('should clear imageBuildLogTail when setImageBuildStatus is called with Idle', () => {
    // Arrange
    service.setImageBuildStatus('Failed', 'some error');

    // Act
    service.setImageBuildStatus('Idle', null);

    // Assert
    expect(service.imageBuildStatus()).toBe('Idle');
    expect(service.imageBuildLogTail()).toBeNull();
  });

  // retryImageBuild resets saveImageFlagsSuccess
  it('should reset saveImageFlagsSuccess to false when retryImageBuild is called', () => {
    // Arrange — drive saveImageFlagsSuccess to true via updateWorkerImageFlags
    service.updateWorkerImageFlags({ installDotnet: false, installAngular: false, installGlab: false, installGh: false, installChromium: false, installDocker: false });
    httpMock.expectOne('/api/settings/worker-image').flush(buildSettingsResponse());
    expect(service.saveImageFlagsSuccess()).toBe(true);

    // Act
    service.retryImageBuild();

    // Assert — cleared before the response arrives
    expect(service.saveImageFlagsSuccess()).toBe(false);
    httpMock.expectOne('/api/settings/worker-image/retry').flush(buildSettingsResponse({ imageBuildStatus: 'Building' }));
  });

  // hasUsableImage signal
  it('should start with hasUsableImage false', () => {
    // Arrange / Act — (no action — testing initial state)

    // Assert
    expect(service.hasUsableImage()).toBe(false);
  });

  it('should set hasUsableImage true when loadSettings response has hasUsableImage true', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
    service.loadSettings();
    httpMock.expectOne('/api/settings').flush(buildSettingsResponse({ hasUsableImage: true }));

    // Assert
    expect(service.hasUsableImage()).toBe(true);
  });

  it('should keep hasUsableImage false when loadSettings response has hasUsableImage false', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
    service.loadSettings();
    httpMock.expectOne('/api/settings').flush(buildSettingsResponse({ hasUsableImage: false }));

    // Assert
    expect(service.hasUsableImage()).toBe(false);
  });

  it('should set hasUsableImage true after retryImageBuild response has hasUsableImage true', () => {
    // Arrange
    service.retryImageBuild();

    // Act
    httpMock.expectOne('/api/settings/worker-image/retry').flush(
      buildSettingsResponse({ imageBuildStatus: 'Idle', hasUsableImage: true })
    );

    // Assert
    expect(service.hasUsableImage()).toBe(true);
  });

  it('should set hasUsableImage from updateWorkerImageFlags response', () => {
    // Arrange
    service.updateWorkerImageFlags({ installDotnet: false, installAngular: false, installGlab: false, installGh: false, installChromium: false, installDocker: false });

    // Act
    httpMock.expectOne('/api/settings/worker-image').flush(buildSettingsResponse({ hasUsableImage: true }));

    // Assert
    expect(service.hasUsableImage()).toBe(true);
  });

  // loadSettings resets image signals
  it('should reset image signals in loadSettings', () => {
    // Arrange — put signals into dirty state
    service.setImageBuildStatus('Failed', 'error text');

    // Act
    service.loadSettings();

    // Assert — cleared before the response arrives
    expect(service.savingImageFlags()).toBe(false);
    expect(service.saveImageFlagsError()).toBeNull();

    httpMock.expectOne('/api/settings').flush(buildSettingsResponse());
  });

  // Login-flow signals: initial state
  it('should start with null loginPhase, null loginUrl, null loginError, false codeSubmitting', () => {
    // Arrange / Act — (no action — testing initial state)

    // Assert
    expect(service.loginPhase()).toBeNull();
    expect(service.loginUrl()).toBeNull();
    expect(service.loginError()).toBeNull();
    expect(service.codeSubmitting()).toBe(false);
  });

  // startLogin: POSTs /api/settings/oauth/login/start
  it('should POST to /api/settings/oauth/login/start when startLogin is called', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
    service.startLogin();
    const req = httpMock.expectOne('/api/settings/oauth/login/start');

    // Assert
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toBeNull();
    req.flush({ sessionId: 'session-1' }, { status: 202, statusText: 'Accepted' });
  });

  it('should set loginPhase to Starting optimistically when startLogin is called', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
    service.startLogin();

    // Assert — before flush
    expect(service.loginPhase()).toBe('Starting');
    httpMock.expectOne('/api/settings/oauth/login/start').flush({ sessionId: 'session-1' }, { status: 202, statusText: 'Accepted' });
  });

  it('should reset loginPhase to null and set startLoginError when startLogin fails', () => {
    // Arrange
    service.startLogin();
    httpMock.expectOne('/api/settings/oauth/login/start').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });

    // Assert
    expect(service.loginPhase()).toBeNull();
    expect(service.startLoginError()).toBe('Failed to start login');
  });


  // submitLoginCode: POSTs /api/settings/oauth/login/code
  it('should POST to /api/settings/oauth/login/code when submitLoginCode is called', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
    service.submitLoginCode('my-code-123');
    const req = httpMock.expectOne('/api/settings/oauth/login/code');

    // Assert
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ code: 'my-code-123' });
    req.flush(null);
  });

  it('should set codeSubmitting to true and loginPhase to SigningIn optimistically when submitLoginCode is called', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
    service.submitLoginCode('my-code-123');

    // Assert — before flush
    expect(service.codeSubmitting()).toBe(true);
    expect(service.loginPhase()).toBe('SigningIn');
    httpMock.expectOne('/api/settings/oauth/login/code').flush(null);
  });


});

describe('SettingsService — SignalR login session', () => {
  afterEach(() => TestBed.resetTestingModule());

  function setupWithSignalR() {
    const mockSignalR = createMockSignalRService();
    TestBed.configureTestingModule({
      providers: [
        SettingsService,
        DispatchService,
        { provide: SystemSignalRService, useValue: mockSignalR },
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    return {
      service: TestBed.inject(SettingsService),
      httpMock: TestBed.inject(HttpTestingController),
      mockSignalR,
    };
  }

  // Cycle: SignalR WaitingForAuthorization update sets loginPhase and loginUrl
  it('should set loginPhase and loginUrl when SignalR pushes WaitingForAuthorization', () => {
    // Arrange
    const { service, httpMock, mockSignalR } = setupWithSignalR();

    // Act
    mockSignalR.loginSessionUpdate.next({
      sessionId: 'session-1',
      phase: 'WaitingForAuthorization',
      url: 'https://claude.ai/oauth/authorize?q=abc',
      failure: null,
      message: null,
    });

    // Assert
    expect(service.loginPhase()).toBe('WaitingForAuthorization');
    expect(service.loginUrl()).toBe('https://claude.ai/oauth/authorize?q=abc');
    httpMock.verify();
  });

  // Cycle: SignalR Failed sets loginError
  it('should set loginError when SignalR pushes Failed with failure reason', () => {
    // Arrange
    const { service, httpMock, mockSignalR } = setupWithSignalR();

    // Act
    mockSignalR.loginSessionUpdate.next({
      sessionId: 'session-1',
      phase: 'Failed',
      url: null,
      failure: 'InvalidCode',
      message: null,
    });

    // Assert
    expect(service.loginPhase()).toBe('Failed');
    expect(service.loginError()).toBe('InvalidCode');
    httpMock.verify();
  });

  // Cycle: startLogin clears stale loginError and loginUrl before the POST
  it('should clear loginUrl and loginError when startLogin is called', () => {
    // Arrange
    const { service, httpMock, mockSignalR } = setupWithSignalR();
    mockSignalR.loginSessionUpdate.next({
      sessionId: 's1',
      phase: 'Failed',
      url: null,
      failure: 'InvalidCode',
      message: null,
    });
    expect(service.loginError()).toBe('InvalidCode');

    // Act
    service.startLogin();

    // Assert
    expect(service.loginUrl()).toBeNull();
    expect(service.loginError()).toBeNull();
    httpMock.expectOne('/api/settings/oauth/login/start').flush({ sessionId: 'session-1' }, { status: 202, statusText: 'Accepted' });
  });

  // Cycle: SignalR Succeeded triggers a settings reload
  it('should reload settings when SignalR pushes Succeeded', () => {
    // Arrange
    const { service, httpMock, mockSignalR } = setupWithSignalR();

    // Act
    mockSignalR.loginSessionUpdate.next({
      sessionId: 'session-1',
      phase: 'Succeeded',
      url: null,
      failure: null,
      message: null,
    });

    // Assert — settings reload is triggered
    httpMock.expectOne('/api/settings').flush(buildSettingsResponse());
  });

  // Finding 1: loginPhase cleared after Succeeded settings reload
  it('should clear loginPhase to null after the settings reload completes on Succeeded', () => {
    // Arrange
    const { service, httpMock, mockSignalR } = setupWithSignalR();
    service.startLogin();
    httpMock.expectOne('/api/settings/oauth/login/start').flush({ sessionId: 's1' }, { status: 202, statusText: 'Accepted' });

    // Act — SignalR Succeeded
    mockSignalR.loginSessionUpdate.next({
      sessionId: 's1',
      phase: 'Succeeded',
      url: null,
      failure: null,
      message: null,
    });

    // loginPhase is set to Succeeded before the reload
    expect(service.loginPhase()).toBe('Succeeded');

    // Flush the settings reload
    httpMock.expectOne('/api/settings').flush(buildSettingsResponse({
      authMode: 'OAuth',
      oAuthStatus: 'Present',
    }));

    // Assert — phase cleared to null after reload
    expect(service.loginPhase()).toBeNull();
  });

  // Cycle: SignalR SigningIn clears codeSubmitting (because server confirmed it)
  it('should clear codeSubmitting when SignalR pushes SigningIn', () => {
    // Arrange
    const { service, httpMock, mockSignalR } = setupWithSignalR();

    // Act — submit code (sets codeSubmitting optimistically)
    service.submitLoginCode('a-code');
    httpMock.expectOne('/api/settings/oauth/login/code').flush(null);

    // Simulate SignalR echo of SigningIn
    mockSignalR.loginSessionUpdate.next({
      sessionId: 'session-1',
      phase: 'SigningIn',
      url: null,
      failure: null,
      message: null,
    });

    // Assert
    expect(service.codeSubmitting()).toBe(false);
    expect(service.loginPhase()).toBe('SigningIn');
    httpMock.verify();
  });

  // Cycle: submitLoginCode HTTP error reverts optimistic SigningIn phase
  it('should revert loginPhase to WaitingForAuthorization when submitLoginCode HTTP call fails', () => {
    // Arrange
    const { service, httpMock, mockSignalR } = setupWithSignalR();
    mockSignalR.loginSessionUpdate.next({
      sessionId: 's1',
      phase: 'WaitingForAuthorization',
      url: 'https://claude.ai',
      failure: null,
      message: null,
    });

    // Act
    service.submitLoginCode('bad-code');
    httpMock.expectOne('/api/settings/oauth/login/code').flush('Unprocessable', {
      status: 422,
      statusText: 'Unprocessable Entity',
    });

    // Assert
    expect(service.codeSubmitting()).toBe(false);
    expect(service.loginPhase()).toBe('WaitingForAuthorization');
  });

  // Cycle: cancelLogin clears all login signals
  it('should clear all login phase signals when cancelLogin is called', () => {
    // Arrange
    const { service, httpMock, mockSignalR } = setupWithSignalR();
    mockSignalR.loginSessionUpdate.next({
      sessionId: 's1',
      phase: 'WaitingForAuthorization',
      url: 'https://claude.ai/oauth/authorize?q=1',
      failure: null,
      message: null,
    });
    expect(service.loginPhase()).toBe('WaitingForAuthorization');
    expect(service.loginUrl()).toBe('https://claude.ai/oauth/authorize?q=1');

    // Act
    service.cancelLogin();

    // Assert
    expect(service.loginPhase()).toBeNull();
    expect(service.loginUrl()).toBeNull();
    expect(service.loginError()).toBeNull();
    expect(service.codeSubmitting()).toBe(false);
    httpMock.verify();
  });
});

describe('SettingsService — SignalR re-sync', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => {
    vi.useRealTimers();
    TestBed.resetTestingModule();
  });

  // Cycle: reconnected triggers a GET /api/settings after debounce window
  it('should reload settings when SystemSignalRService.reconnected emits (after debounce)', () => {
    // Arrange
    const mockSignalR = createMockSignalRService();
    const { httpMock } = setupService(mockSignalR);

    // Act
    mockSignalR.reconnected.next();
    vi.advanceTimersByTime(300);

    // Assert — exactly one GET request fired
    httpMock.expectOne('/api/settings').flush(buildSettingsResponse());
  });

  // Cycle: dispatchStateChanged triggers a GET /api/settings after debounce window
  it('should reload settings when SystemSignalRService.dispatchStateChanged emits (after debounce)', () => {
    // Arrange
    const mockSignalR = createMockSignalRService();
    const { httpMock } = setupService(mockSignalR);

    // Act
    mockSignalR.dispatchStateChanged.next();
    vi.advanceTimersByTime(300);

    // Assert — exactly one GET request fired
    httpMock.expectOne('/api/settings').flush(buildSettingsResponse());
  });

  // Cycle: separate emissions (outside debounce window) each trigger a reload
  it('should reload settings each time reconnected emits outside the debounce window', () => {
    // Arrange
    const mockSignalR = createMockSignalRService();
    const { httpMock } = setupService(mockSignalR);

    // Act — first emission + advance past debounce
    mockSignalR.reconnected.next();
    vi.advanceTimersByTime(300);
    httpMock.expectOne('/api/settings').flush(buildSettingsResponse());

    // Second emission + advance past debounce
    mockSignalR.reconnected.next();
    vi.advanceTimersByTime(300);

    // Assert — second GET fires
    httpMock.expectOne('/api/settings').flush(buildSettingsResponse());
  });

  // Cycle: rapid burst of emissions collapses to a single reload (debounce coalescing)
  it('should coalesce a rapid burst of dispatchStateChanged emissions into a single reload', () => {
    // Arrange
    const mockSignalR = createMockSignalRService();
    const { httpMock } = setupService(mockSignalR);

    // Act — fire three emissions rapidly (within the debounce window)
    mockSignalR.dispatchStateChanged.next();
    mockSignalR.dispatchStateChanged.next();
    mockSignalR.dispatchStateChanged.next();

    // No reload should have fired yet (debounce window not elapsed)
    httpMock.expectNone('/api/settings');

    // Advance past the debounce window
    vi.advanceTimersByTime(300);

    // Assert — exactly one reload, not three
    httpMock.expectOne('/api/settings').flush(buildSettingsResponse());
  });

  // Cycle: a new trigger cancels the in-flight reload (switchMap)
  it('should cancel the in-flight reload when a new trigger arrives before the previous response completes', () => {
    // Arrange
    const mockSignalR = createMockSignalRService();
    const { httpMock } = setupService(mockSignalR);

    // Act — first emission, advance past debounce, request is in-flight
    mockSignalR.reconnected.next();
    vi.advanceTimersByTime(300);
    httpMock.expectOne('/api/settings'); // first request pending — do NOT flush

    // Second emission cancels first and starts a new one after debounce
    mockSignalR.reconnected.next();
    vi.advanceTimersByTime(300);

    // Assert — only the second request is active; first was cancelled
    httpMock.expectOne('/api/settings').flush(buildSettingsResponse());
  });
});

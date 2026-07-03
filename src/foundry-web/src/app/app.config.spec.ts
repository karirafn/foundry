import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { NEVER } from 'rxjs';
import { SettingsService } from './features/settings/settings.service';
import { AccountService } from './features/settings/accounts/account.service';
import { SystemSignalRService } from './core/services/system-signalr.service';

const mockSystemSignalR = { reconnected: NEVER, dispatchStateChanged: NEVER, loginSessionUpdate: NEVER, notifications: [] };

function buildSettingsResponse(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    authMode: 'ApiKey',
    maxConcurrent: 3,
    timeoutMinutes: 60,
    accessTokenPresent: false,
    refreshTokenPresent: false,
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

describe('app initializer', () => {
  afterEach(() => TestBed.resetTestingModule());

  // Cycle 1: initializer resolves even when both API calls error
  it('should resolve even when loadSettings and loadAccounts both error', async () => {
    // Arrange
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: SystemSignalRService, useValue: mockSystemSignalR },
      ],
    });

    const httpMock = TestBed.inject(HttpTestingController);
    const settingsService = TestBed.inject(SettingsService);
    const accountService = TestBed.inject(AccountService);

    // Act
    const p = Promise.all([
      settingsService.loadSettings(),
      accountService.loadAccounts(),
    ]);

    httpMock.expectOne('/api/settings').flush('error', { status: 500, statusText: 'Internal Server Error' });
    httpMock.expectOne('/api/accounts').flush('error', { status: 500, statusText: 'Internal Server Error' });

    // Assert — promise resolves without throwing even when the API errors
    await p;
    expect(settingsService.loadError()).not.toBeNull();
    expect(accountService.loadError()).not.toBeNull();
  });

  // Cycle 2: initializer resolves successfully when both API calls succeed
  it('should resolve and populate signals when loadSettings and loadAccounts both succeed', async () => {
    // Arrange
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: SystemSignalRService, useValue: mockSystemSignalR },
      ],
    });

    const httpMock = TestBed.inject(HttpTestingController);
    const settingsService = TestBed.inject(SettingsService);
    const accountService = TestBed.inject(AccountService);

    // Act
    const p = Promise.all([
      settingsService.loadSettings(),
      accountService.loadAccounts(),
    ]);

    httpMock.expectOne('/api/settings').flush(buildSettingsResponse({ hasUsableImage: true }));
    httpMock.expectOne('/api/accounts').flush([{ id: '1', name: 'Test', providerType: 'github', baseUrl: 'https://api.github.com/', hasToken: true }]);

    // Assert — promise resolves and signals are populated
    await p;
    expect(settingsService.hasUsableImage()).toBe(true);
    expect(accountService.accounts().length).toBe(1);
  });
});

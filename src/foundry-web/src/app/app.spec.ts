import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Subject } from 'rxjs';
import { App } from './app';
import { routes } from './app.routes';
import { SYSTEM_HUB_FACTORY, SystemHub, SystemSignalRService } from './core/services/system-signalr.service';
import { SettingsService } from './features/settings/settings.service';
import { AccountService } from './features/settings/accounts/account.service';
import { DispatchService } from './core/services/dispatch.service';

const mockSystemHubFactory = (): SystemHub => ({
  on: () => {},
  onReconnected: () => {},
  start: () => Promise.resolve(),
});

function createMockSettingsService() {
  return {
    hasUsableImage: signal(true).asReadonly(),
    imageBuildStatus: signal('Idle').asReadonly(),
    imageBuildLogTail: signal(null).asReadonly(),
    settings: signal(null).asReadonly(),
    authSettings: signal(null),
    loading: signal(false),
    saving: signal(false),
    switching: signal(false),
    saveSuccess: signal(false),
    workerLimits: signal(null).asReadonly(),
    savingLimits: signal(false).asReadonly(),
    saveLimitsSuccess: signal(false).asReadonly(),
    loadError: signal(null).asReadonly(),
    saveError: signal(null).asReadonly(),
    switchError: signal(null).asReadonly(),
    saveLimitsError: signal(null).asReadonly(),
    systemPromptTemplate: signal(null).asReadonly(),
    workerPromptTemplate: signal(null).asReadonly(),
    savingPrompts: signal(false).asReadonly(),
    savePromptsSuccess: signal(false).asReadonly(),
    savePromptsError: signal(null).asReadonly(),
    savingDispatch: signal(false).asReadonly(),
    saveDispatchSuccess: signal(false).asReadonly(),
    saveDispatchError: signal(null).asReadonly(),
    workerImageFlags: signal(null).asReadonly(),
    savingImageFlags: signal(false).asReadonly(),
    saveImageFlagsSuccess: signal(false).asReadonly(),
    saveImageFlagsError: signal(null).asReadonly(),
    loadSettings: () => {},
    retryImageBuild: () => {},
    setImageBuildStatus: () => {},
    updateAuthMode: () => {},
    updateWorkerLimits: () => {},
    updatePromptTemplates: () => {},
    updateDispatchSettings: () => {},
    scanOAuthCredentials: () => {},
    updateWorkerImageFlags: () => {},
  };
}

function createMockAccountService() {
  return {
    accounts: signal([{ id: '1' }]).asReadonly(),
    loading: signal(false).asReadonly(),
    saving: signal(false).asReadonly(),
    deleting: signal(false).asReadonly(),
    validating: signal(false).asReadonly(),
    saveSuccess: signal(false).asReadonly(),
    validationResult: signal(null).asReadonly(),
    saveError: signal(null).asReadonly(),
    deleteError: signal(null).asReadonly(),
    loadError: signal(null).asReadonly(),
    validationError: signal(null).asReadonly(),
    loadAccounts: () => {},
    createAccount: () => {},
    updateAccount: () => {},
    deleteAccount: () => {},
    validateToken: () => {},
  };
}

function createMockDispatchService() {
  return {
    isDispatchPaused: signal(false).asReadonly(),
    usageLimitResetsAt: signal(null).asReadonly(),
    resuming: signal(false).asReadonly(),
    resumeDispatch: () => {},
    updateFromSettings: () => {},
  };
}

function createMockSignalRService() {
  return {
    notifications: signal([]).asReadonly(),
    reconnected: new Subject<void>(),
  };
}

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideRouter(routes),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: SYSTEM_HUB_FACTORY, useValue: mockSystemHubFactory },
        { provide: SettingsService, useValue: createMockSettingsService() },
        { provide: AccountService, useValue: createMockAccountService() },
        { provide: DispatchService, useValue: createMockDispatchService() },
        { provide: SystemSignalRService, useValue: createMockSignalRService() },
      ],
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });

  it('should render header with Foundry logo', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.app-header__logo')?.textContent).toContain('Foundry');
  });

  it('should include the forge overlay component', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('fd-forge-overlay')).not.toBeNull();
  });
});

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

function createMockSettingsService(isColdBuildBlocking = false) {
  return {
    isColdBuildBlocking: signal(isColdBuildBlocking).asReadonly(),
    hasUsableImage: signal(!isColdBuildBlocking).asReadonly(),
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

function setupApp(isColdBuildBlocking = false) {
  TestBed.configureTestingModule({
    imports: [App],
    providers: [
      provideRouter(routes),
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: SYSTEM_HUB_FACTORY, useValue: mockSystemHubFactory },
      { provide: SettingsService, useValue: createMockSettingsService(isColdBuildBlocking) },
      { provide: AccountService, useValue: createMockAccountService() },
      { provide: DispatchService, useValue: createMockDispatchService() },
      { provide: SystemSignalRService, useValue: createMockSignalRService() },
    ],
  });
  const fixture = TestBed.createComponent(App);
  fixture.detectChanges();
  return fixture;
}

describe('App', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('should create the app', () => {
    const fixture = setupApp();
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });

  it('should render header with Foundry logo', async () => {
    const fixture = setupApp();
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.app-header__logo')?.textContent).toContain('Foundry');
  });

  it('should include the forge overlay component', () => {
    const fixture = setupApp();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('fd-forge-overlay')).not.toBeNull();
  });

  // F1: header, system banner, and main are not inert when overlay is not blocking
  it('should not set inert on header, system-banner, or main when overlay is not blocking', () => {
    // Arrange
    const fixture = setupApp(false);

    // Act
    const compiled = fixture.nativeElement as HTMLElement;
    const header = compiled.querySelector('header');
    const banner = compiled.querySelector('fd-system-banner');
    const main = compiled.querySelector('main');

    // Assert
    expect(header?.hasAttribute('inert')).toBe(false);
    expect(banner?.hasAttribute('inert')).toBe(false);
    expect(main?.hasAttribute('inert')).toBe(false);
  });

  // Step 3: toast host is mounted exactly once at app root, outside inert regions
  it('should render exactly one fd-toast host', () => {
    // Arrange
    const fixture = setupApp();

    // Act
    const compiled = fixture.nativeElement as HTMLElement;
    const toastHosts = compiled.querySelectorAll('fd-toast');

    // Assert
    expect(toastHosts.length).toBe(1);
  });

  it('should render fd-toast outside inert-gated regions', () => {
    // Arrange
    const fixture = setupApp(true);

    // Act
    const compiled = fixture.nativeElement as HTMLElement;
    const toastHost = compiled.querySelector('fd-toast');
    const main = compiled.querySelector('main');
    const header = compiled.querySelector('header');
    const banner = compiled.querySelector('fd-system-banner');

    // Assert — fd-toast must not be a descendant of any inert element
    expect(toastHost).not.toBeNull();
    expect(main?.contains(toastHost)).toBe(false);
    expect(header?.contains(toastHost)).toBe(false);
    expect(banner?.contains(toastHost)).toBe(false);
  });

  // F1: header, system banner, and main are inert when overlay is blocking
  it('should set inert on header, system-banner, and main when overlay is blocking', () => {
    // Arrange
    const fixture = setupApp(true);

    // Act
    const compiled = fixture.nativeElement as HTMLElement;
    const header = compiled.querySelector('header');
    const banner = compiled.querySelector('fd-system-banner');
    const main = compiled.querySelector('main');

    // Assert
    expect(header?.hasAttribute('inert')).toBe(true);
    expect(banner?.hasAttribute('inert')).toBe(true);
    expect(main?.hasAttribute('inert')).toBe(true);
  });

  // Step 3: fd-account-chip renders before the settings gear link
  it('should render fd-account-chip before .app-header__settings-link in .app-header__nav', () => {
    // Arrange / Act
    const fixture = setupApp();
    const compiled = fixture.nativeElement as HTMLElement;
    const nav = compiled.querySelector('.app-header__nav');

    // Assert
    expect(nav).not.toBeNull();
    const chip = nav?.querySelector('fd-account-chip');
    const settingsLink = nav?.querySelector('.app-header__settings-link');
    expect(chip).not.toBeNull();
    expect(settingsLink).not.toBeNull();

    const navChildren = Array.from(nav?.children ?? []);
    const chipIndex = navChildren.indexOf(chip as Element);
    const settingsIndex = navChildren.indexOf(settingsLink as Element);
    expect(chipIndex).toBeLessThan(settingsIndex);
  });
});

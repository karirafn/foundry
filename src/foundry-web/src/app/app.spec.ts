import { TestBed } from '@angular/core/testing';
import { signal, WritableSignal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Subject } from 'rxjs';
import { App } from './app';
import { routes } from './app.routes';
import { SystemSignalRService } from './core/services/system-signalr.service';
import { SettingsService } from './core/services/settings.service';
import { AccountService } from './features/settings/accounts/account.service';
import { DispatchService } from './core/services/dispatch.service';
import { CreditsService } from './core/services/credits.service';
import { UsageService } from './core/services/usage.service';
import { UsageSnapshot } from './core/models/usage.model';
import { FakeHubTransport, provideFakeHubs } from './core/testing/fake-hub-transport';
import { FakeConnectionGate, provideFakeConnectionGate } from './core/testing/fake-connection-gate';
import { AgentActivity } from './core/models/agent-activity.model';

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
    savingDispatch: signal(false).asReadonly(),
    saveDispatchSuccess: signal(false).asReadonly(),
    saveDispatchError: signal(null).asReadonly(),
    workerImageFlags: signal(null).asReadonly(),
    savingImageFlags: signal(false).asReadonly(),
    saveImageFlagsSuccess: signal(false).asReadonly(),
    saveImageFlagsError: signal(null).asReadonly(),
    loadSettings: () => {},
    retryImageBuild: () => {},
    updateAuthMode: () => {},
    updateWorkerLimits: () => {},
    updateDispatchSettings: () => {},
    scanOAuthCredentials: () => {},
    updateWorkerImageFlags: () => {},
  };
}

function createMockAccountService() {
  const srAnnouncementSignal: WritableSignal<string> = signal('');
  return {
    accounts: signal([{ id: '1' }]).asReadonly(),
    loading: signal(false).asReadonly(),
    saving: signal(false).asReadonly(),
    deletingAccountId: signal<string | null>(null).asReadonly(),
    validating: signal(false).asReadonly(),
    saveSuccess: signal(false).asReadonly(),
    validationResult: signal(null).asReadonly(),
    saveError: signal(null).asReadonly(),
    deleteError: signal(null).asReadonly(),
    loadError: signal(null).asReadonly(),
    validationError: signal(null).asReadonly(),
    srAnnouncement: srAnnouncementSignal.asReadonly(),
    _srAnnouncementSignal: srAnnouncementSignal,
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
    pausedHosts: signal([]).asReadonly(),
    resumeDispatch: () => {},
    updateFromSettings: () => {},
  };
}

function createMockSignalRService() {
  return {
    notifications: signal([]).asReadonly(),
    reconnected: new Subject<void>(),
    creditsNotification: new Subject<void>(),
    applyDockerAvailability: () => {},
  };
}

function createMockCreditsService() {
  return {
    nextProbeAt: signal<string | null>(null).asReadonly(),
    isChecking: signal<boolean>(false).asReadonly(),
    checkNow: () => {},
    updateFromCredentials: () => {},
  };
}

function buildTestSnapshot(overrides: Partial<UsageSnapshot> = {}): UsageSnapshot {
  return {
    limits: [
      { kind: 'session', percent: 42, resetsAt: '2026-10-30T06:00:00Z', modelDisplayName: null, severity: 'normal', isActive: true },
      { kind: 'weekly_all', percent: 78, resetsAt: '2026-11-03T09:00:00Z', modelDisplayName: null, severity: 'warning', isActive: false },
    ],
    extraUsage: null,
    capturedAt: '2026-10-30T01:00:00Z',
    accountEmail: 'ops@acme.com',
    lastRefreshFailedAt: null,
    source: 'usageReport',
    ...overrides,
  };
}

function createMockUsageService(snapshot: UsageSnapshot | null = null) {
  return {
    snapshot: signal(snapshot).asReadonly(),
    refreshState: signal('idle').asReadonly(),
    refresh: vi.fn(),
  };
}

function buildAgentActivity(workerRunId: string, issueId: string): AgentActivity {
  return {
    workerRunId,
    issueId,
    role: 'implement',
    lastEventAt: null,
    tasks: [],
    currentTask: null,
    completedTaskCount: 0,
    subagents: [],
    activeSkill: null,
    inFlightTool: null,
    tokens: { inputTokens: 0, outputTokens: 0 },
  };
}

function setupApp(
  isColdBuildBlocking = false,
  usageSnapshot: UsageSnapshot | null = buildTestSnapshot(),
  gate = new FakeConnectionGate(),
) {
  const mockAccountService = createMockAccountService();
  const workerHub = new FakeHubTransport();
  TestBed.configureTestingModule({
    imports: [App],
    providers: [
      provideRouter(routes),
      provideHttpClient(),
      provideHttpClientTesting(),
      provideFakeHubs({ workers: workerHub }),
      provideFakeConnectionGate(gate),
      { provide: SettingsService, useValue: createMockSettingsService(isColdBuildBlocking) },
      { provide: AccountService, useValue: mockAccountService },
      { provide: DispatchService, useValue: createMockDispatchService() },
      { provide: SystemSignalRService, useValue: createMockSignalRService() },
      { provide: CreditsService, useValue: createMockCreditsService() },
      { provide: UsageService, useValue: createMockUsageService(usageSnapshot) },
    ],
  });
  const fixture = TestBed.createComponent(App);
  fixture.detectChanges();
  return { fixture, mockAccountService, workerHub };
}

describe('App', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('should create the app', () => {
    const { fixture } = setupApp();
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });

  it('should render header with Foundry logo', async () => {
    const { fixture } = setupApp();
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.app-header__logo')?.textContent).toContain('Foundry');
  });

  it('links the Foundry logo to the board', async () => {
    const { fixture } = setupApp();
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('a.app-header__logo')?.getAttribute('href')).toBe('/');
  });

  it('navigates between Board, Floor and History from the main nav', async () => {
    // Arrange
    const { fixture } = setupApp();
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;

    // Act
    const nav = compiled.querySelector('nav[aria-label="Main"]');
    const links = [...(nav?.querySelectorAll('a') ?? [])];

    // Assert
    expect(links.map((a) => [a.textContent?.trim(), a.getAttribute('href')])).toEqual([
      ['Board', '/'],
      ['Floor', '/floor'],
      ['History', '/issues'],
    ]);
  });

  it('keeps the status cluster outside the main nav', async () => {
    // Arrange
    const { fixture } = setupApp();
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;

    // Act
    const status = compiled.querySelector('.app-header__status');

    // Assert
    expect(status?.tagName).toBe('DIV');
    expect(status?.closest('nav')).toBeNull();
    expect(status?.querySelector('fd-agents-chip')).not.toBeNull();
  });

  it('should include the forge overlay component', () => {
    const { fixture } = setupApp();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('fd-forge-overlay')).not.toBeNull();
  });

  // F1: header, system banner, and main are not inert when overlay is not blocking
  it('should not set inert on header, system-banner, or main when overlay is not blocking', () => {
    // Arrange
    const { fixture } = setupApp(false);

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
    const { fixture } = setupApp();

    // Act
    const compiled = fixture.nativeElement as HTMLElement;
    const toastHosts = compiled.querySelectorAll('fd-toast');

    // Assert
    expect(toastHosts.length).toBe(1);
  });

  it('should render fd-toast outside inert-gated regions', () => {
    // Arrange
    const { fixture } = setupApp(true);

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
    const { fixture } = setupApp(true);

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

  // Step 7: exactly one polite live region exists in the app shell
  it('should render exactly one aria-live="polite" region in the shell', () => {
    // Arrange
    const { fixture } = setupApp();

    // Act
    const compiled = fixture.nativeElement as HTMLElement;
    const liveRegions = compiled.querySelectorAll('[aria-live="polite"]');

    // Assert
    expect(liveRegions.length).toBe(1);
  });

  it('should render the sr-announcer region outside any inert subtree (sibling of main)', () => {
    // Arrange
    const { fixture } = setupApp(true);

    // Act
    const compiled = fixture.nativeElement as HTMLElement;
    const announcer = compiled.querySelector('.app__sr-announcer');
    const main = compiled.querySelector('main');
    const header = compiled.querySelector('header');
    const banner = compiled.querySelector('fd-system-banner');

    // Assert — announcer must not be a descendant of any inert element
    expect(announcer).not.toBeNull();
    expect(main?.contains(announcer)).toBe(false);
    expect(header?.contains(announcer)).toBe(false);
    expect(banner?.contains(announcer)).toBe(false);
  });

  it('should update the sr-announcer text when srAnnouncement signal changes', () => {
    // Arrange
    const { fixture, mockAccountService } = setupApp();
    const compiled = fixture.nativeElement as HTMLElement;

    // Assert initial state
    const announcer = compiled.querySelector('.app__sr-announcer');
    expect(announcer?.textContent?.trim()).toBe('');

    // Act — push a non-empty value through the writable signal
    mockAccountService._srAnnouncementSignal.set('Account added.');
    fixture.detectChanges();

    // Assert — binding reflects the updated signal value
    expect(announcer?.textContent?.trim()).toBe('Account added.');
  });

  it('should render sr-announcer with aria-atomic="true"', () => {
    // Arrange
    const { fixture } = setupApp();

    // Act
    const compiled = fixture.nativeElement as HTMLElement;
    const announcer = compiled.querySelector('.app__sr-announcer');

    // Assert
    expect(announcer?.getAttribute('aria-atomic')).toBe('true');
  });

  // Behavior f: nav order is widget → chip → settings link
  it('should render fd-usage-widget before fd-account-chip before .app-header__settings-link', () => {
    // Arrange — provide a non-null snapshot so the widget renders
    const { fixture } = setupApp(false, buildTestSnapshot());
    const compiled = fixture.nativeElement as HTMLElement;
    const nav = compiled.querySelector('.app-header__status');

    // Assert
    expect(nav).not.toBeNull();
    const widget = nav?.querySelector('fd-usage-widget');
    const chip = nav?.querySelector('fd-account-chip');
    const settingsLink = nav?.querySelector('.app-header__settings-link');
    expect(widget).not.toBeNull();
    expect(chip).not.toBeNull();
    expect(settingsLink).not.toBeNull();

    const navChildren = Array.from(nav?.children ?? []);
    const widgetIndex = navChildren.indexOf(widget as Element);
    const chipIndex = navChildren.indexOf(chip as Element);
    const settingsIndex = navChildren.indexOf(settingsLink as Element);
    // widget < chip < settings
    expect(widgetIndex).toBeLessThan(chipIndex);
    expect(chipIndex).toBeLessThan(settingsIndex);
  });

  // Behavior f: fd-usage-widget emits no DOM content when snapshot is null
  it('should not render the usage widget trigger when usage snapshot is null', () => {
    // Arrange — null snapshot means widget renders no trigger
    const { fixture } = setupApp(false, null);
    const compiled = fixture.nativeElement as HTMLElement;

    // Assert — no trigger button rendered
    expect(compiled.querySelector('.usage-widget__trigger')).toBeNull();
  });

  describe('active-agents chip', () => {
    it('should render the chip before the usage widget, linking to the Floor', () => {
      // Arrange
      const { fixture } = setupApp();
      const compiled = fixture.nativeElement as HTMLElement;

      // Act
      const nav = compiled.querySelector('.app-header__status');
      const navChildren = Array.from(nav?.children ?? []);
      const agentsChip = nav?.querySelector('fd-agents-chip');
      const link = agentsChip?.querySelector('a');

      // Assert
      expect(link?.getAttribute('href')).toBe('/floor');
      expect(navChildren.indexOf(agentsChip as Element)).toBeLessThan(
        navChildren.indexOf(nav?.querySelector('fd-usage-widget') as Element));
    });

    it('should show the idle state when no agents are working', () => {
      // Arrange
      const { fixture } = setupApp();
      const compiled = fixture.nativeElement as HTMLElement;

      // Act
      const link = compiled.querySelector('.agents-chip');

      // Assert
      expect(link?.classList).toContain('agents-chip--idle');
      expect(link?.textContent).toContain('Agents idle');
      expect(link?.getAttribute('aria-label')).toBe('Agents idle. Open the Floor');
    });

    it('should update the count live as agent activity arrives', () => {
      // Arrange
      const { fixture, workerHub } = setupApp();
      const compiled = fixture.nativeElement as HTMLElement;

      // Act
      workerHub.emit('AgentActivity', buildAgentActivity('run-1', 'issue-1'));
      workerHub.emit('AgentActivity', buildAgentActivity('run-2', 'issue-2'));
      fixture.detectChanges();

      // Assert
      const link = compiled.querySelector('.agents-chip');
      expect(link?.classList).not.toContain('agents-chip--idle');
      expect(link?.textContent).toContain('2 agents working');
    });

    it('should announce count changes through the shell announcer but not the initial count', () => {
      // Arrange
      const { fixture, workerHub } = setupApp();
      const compiled = fixture.nativeElement as HTMLElement;
      const announcer = compiled.querySelector('.app__sr-announcer');
      expect(announcer?.textContent?.trim()).toBe('');

      // Act
      workerHub.emit('AgentActivity', buildAgentActivity('run-1', 'issue-1'));
      fixture.detectChanges();

      // Assert
      expect(announcer?.textContent?.trim()).toBe('1 agent working');
    });

    it('should not announce count changes while the connection gate is up', () => {
      // Arrange
      const gate = new FakeConnectionGate();
      gate.phase.set('reloading');
      const { fixture, workerHub } = setupApp(false, buildTestSnapshot(), gate);

      // Act
      workerHub.emit('AgentActivity', buildAgentActivity('run-1', 'issue-1'));
      fixture.detectChanges();

      // Assert
      expect((fixture.nativeElement as HTMLElement).querySelector('.app__sr-announcer')?.textContent?.trim()).toBe('');
    });

    it('should not announce token-only updates for an already counted run', () => {
      // Arrange
      const { fixture, mockAccountService, workerHub } = setupApp();
      const compiled = fixture.nativeElement as HTMLElement;
      workerHub.emit('AgentActivity', buildAgentActivity('run-1', 'issue-1'));
      fixture.detectChanges();
      mockAccountService._srAnnouncementSignal.set('Account added.');
      fixture.detectChanges();

      // Act — a later payload for the same run changes nothing countable.
      workerHub.emit('AgentActivity', {
        ...buildAgentActivity('run-1', 'issue-1'),
        tokens: { inputTokens: 10, outputTokens: 20 },
      });
      fixture.detectChanges();

      // Assert
      expect(compiled.querySelector('.app__sr-announcer')?.textContent?.trim()).toBe('Account added.');
    });
  });
});

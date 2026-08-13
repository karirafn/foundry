import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { signal } from '@angular/core';
import { NEVER } from 'rxjs';
import { SettingsLayoutComponent } from './settings-layout';
import { SettingsService } from '../../../core/services/settings.service';
import { SystemSignalRService } from '../../../core/services/system-signalr.service';
import { SETTINGS_ROUTES } from '../settings.routes';

const mockSystemSignalR = { reconnected: NEVER, reloadTrigger: NEVER, loginSessionUpdate: NEVER, notifications: signal([]).asReadonly() };

const SETTINGS_RESPONSE = {
  maxConcurrent: 3,
  timeoutMinutes: 60,
  probeIntervalMinutes: 60,
  systemPromptTemplate: null,
  workerPromptTemplate: null,
  usageLimitResetsAt: null,
  isDispatchPaused: false,
  autoResumeOnUsageReset: true,
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

const CREDENTIALS_RESPONSE = {
  accountId: '00000000-0000-0000-0000-000000000001',
  authMode: 'ApiKey',
  oAuthStatus: 'NotConfigured',
  subscriptionType: null,
  oAuthAccountEmail: null,
  oAuthAccountOrgName: null,
};

function setup() {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    imports: [SettingsLayoutComponent],
    providers: [
      SettingsService,
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter([]),
      { provide: SystemSignalRService, useValue: mockSystemSignalR },
    ],
  });

  const fixture = TestBed.createComponent(SettingsLayoutComponent);
  const httpMock = TestBed.inject(HttpTestingController);
  return { fixture, httpMock };
}

function flushSettings(httpMock: HttpTestingController, response: object = SETTINGS_RESPONSE): void {
  httpMock.expectOne('/api/settings').flush(response);
  httpMock.expectOne('/api/credentials').flush(CREDENTIALS_RESPONSE);
}

function flushSettingsError(httpMock: HttpTestingController): void {
  // forkJoin cancels /api/credentials once /api/settings errors
  httpMock.expectOne('/api/settings').flush('Server Error', {
    status: 500,
    statusText: 'Internal Server Error',
  });
  httpMock.match('/api/credentials'); // consume the cancelled request
}

describe('SettingsLayoutComponent', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
  });

  it('should render the "Settings" heading', () => {
    // Arrange
    const { fixture, httpMock } = setup();

    // Act
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const heading = el.querySelector('.settings-layout__heading');
    expect(heading?.textContent?.trim()).toBe('Settings');
  });

  it('should render sidebar nav with General, Accounts, and Repositories links', () => {
    // Arrange
    const { fixture, httpMock } = setup();

    // Act
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const navLinks = el.querySelectorAll('.settings-layout__nav-link');
    const linkTexts = Array.from(navLinks).map(link => link.textContent?.trim());
    expect(linkTexts).toEqual(['General', 'Accounts', 'Repositories']);
  });

  it('should wrap nav links in a nav element with aria-label', () => {
    // Arrange
    const { fixture, httpMock } = setup();

    // Act
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const nav = el.querySelector('nav.settings-layout__sidebar');
    expect(nav).toBeTruthy();
    expect(nav?.getAttribute('aria-label')).toBe('Settings navigation');
  });

  it('should call loadSettings on initialization', () => {
    // Arrange
    const { fixture, httpMock } = setup();

    // Act
    fixture.detectChanges();

    // Assert
    const req = httpMock.expectOne('/api/settings');
    expect(req.request.method).toBe('GET');
    req.flush(SETTINGS_RESPONSE);
    httpMock.expectOne('/api/credentials').flush(CREDENTIALS_RESPONSE);
  });

  it('should show a loading indicator while settings are loading', () => {
    // Arrange
    const { fixture, httpMock } = setup();

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const loadingEl = el.querySelector('.settings-layout__loading');
    expect(loadingEl).toBeTruthy();
    expect(loadingEl?.getAttribute('role')).toBe('status');

    flushSettings(httpMock);
  });

  it('should hide loading indicator after settings load', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    flushSettings(httpMock);

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const loadingEl = el.querySelector('.settings-layout__loading');
    expect(loadingEl).toBeFalsy();
  });

  it('should show error state when loadSettings fails', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();

    // Act
    flushSettingsError(httpMock);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const errorEl = el.querySelector('.settings-layout__load-error');
    expect(errorEl).toBeTruthy();
    expect(errorEl?.getAttribute('role')).toBe('alert');
  });

  it('should render a retry button in the error state', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    flushSettingsError(httpMock);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const retryBtn = el.querySelector('.settings-layout__retry-btn') as HTMLButtonElement;

    // Assert
    expect(retryBtn).toBeTruthy();
    expect(retryBtn.textContent?.trim()).toBe('Retry');
  });

  it('should call loadSettings when retry button is clicked', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    flushSettingsError(httpMock);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const retryBtn = el.querySelector('.settings-layout__retry-btn') as HTMLButtonElement;
    retryBtn.click();

    // Assert — retry triggers both endpoints again
    flushSettings(httpMock);
  });

  it('should not render sidebar or router-outlet while loading', () => {
    // Arrange
    const { fixture, httpMock } = setup();

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.settings-layout__sidebar')).toBeFalsy();
    expect(el.querySelector('router-outlet')).toBeFalsy();

    flushSettings(httpMock);
  });

  it('should not render sidebar or router-outlet when in error state', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();

    // Act
    flushSettingsError(httpMock);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.settings-layout__sidebar')).toBeFalsy();
    expect(el.querySelector('router-outlet')).toBeFalsy();
  });

  it('should render sidebar and router-outlet after settings load', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    flushSettings(httpMock);

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.settings-layout__sidebar')).toBeTruthy();
    expect(el.querySelector('router-outlet')).toBeTruthy();
  });

  it('should render a back link to issues', () => {
    // Arrange
    const { fixture, httpMock } = setup();

    // Act
    fixture.detectChanges();
    flushSettings(httpMock);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const backLink = el.querySelector('.settings-layout__back-link');
    expect(backLink).toBeTruthy();
    expect(backLink?.textContent).toContain('Back to issues');
  });

  it('should redirect from /settings to /settings/general', async () => {
    // Arrange
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        SettingsService,
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          { path: 'settings', children: SETTINGS_ROUTES },
        ]),
        { provide: SystemSignalRService, useValue: mockSystemSignalR },
      ],
    });
    const router = TestBed.inject(Router);

    // Act
    await router.navigateByUrl('/settings');

    // Assert
    expect(router.url).toBe('/settings/general');

    const hm = TestBed.inject(HttpTestingController);
    hm.match('/api/settings');
    hm.match('/api/credentials');
  });

  it('should set aria-current="page" on the active nav link', async () => {
    // Arrange
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        SettingsService,
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          { path: 'settings', children: SETTINGS_ROUTES },
        ]),
        { provide: SystemSignalRService, useValue: mockSystemSignalR },
      ],
    });
    const httpMock = TestBed.inject(HttpTestingController);
    const harness = await RouterTestingHarness.create('/settings/general');
    httpMock.expectOne('/api/settings').flush(SETTINGS_RESPONSE);
    httpMock.expectOne('/api/credentials').flush(CREDENTIALS_RESPONSE);
    harness.detectChanges();
    await harness.fixture.whenStable();
    harness.detectChanges();

    // Act
    const rootEl = harness.fixture.nativeElement as HTMLElement;

    // Assert
    const activeLink = rootEl.querySelector('.settings-layout__nav-link--active');
    expect(activeLink).toBeTruthy();
    expect(activeLink?.textContent?.trim()).toBe('General');
    expect(activeLink?.getAttribute('aria-current')).toBe('page');

    httpMock.verify();
  });
});

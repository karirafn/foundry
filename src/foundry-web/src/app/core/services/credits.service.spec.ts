import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Subject } from 'rxjs';
import { CreditsService } from './credits.service';
import { SystemSignalRService } from './system-signalr.service';
import { SystemNotification, CREDITS_NOTIFICATION_CATEGORY } from '../models/system-notification.model';
import { ClaudeAccountSummary } from '../models/settings.model';

function buildSummary(overrides: Partial<ClaudeAccountSummary> = {}): ClaudeAccountSummary {
  return {
    accountId: '00000000-0000-0000-0000-000000000001',
    authMode: 'ApiKey',
    oAuthStatus: 'NotConfigured',
    subscriptionType: null,
    oAuthAccountEmail: null,
    oAuthAccountOrgName: null,
    nextProbeAt: null,
    ...overrides,
  };
}

function buildProbeResponse(overrides: Partial<{ inFlight: boolean; outcome: string | null }> = {}): { inFlight: boolean; outcome: string | null } {
  return {
    inFlight: false,
    outcome: null,
    ...overrides,
  };
}

function setup() {
  TestBed.resetTestingModule();

  const creditsNotification = new Subject<SystemNotification>();

  const mockSignalR = {
    reconnected: new Subject<void>(),
    reloadTrigger: new Subject<void>(),
    loginSessionUpdate: new Subject<void>(),
    notifications: (() => []) as unknown as SystemSignalRService['notifications'],
    creditsNotification: creditsNotification.asObservable(),
  };

  TestBed.configureTestingModule({
    providers: [
      CreditsService,
      { provide: SystemSignalRService, useValue: mockSignalR },
      provideHttpClient(),
      provideHttpClientTesting(),
    ],
  });

  const service = TestBed.inject(CreditsService);
  const httpMock = TestBed.inject(HttpTestingController);

  return { service, httpMock, creditsNotification };
}

describe('CreditsService', () => {
  let service: CreditsService;
  let httpMock: HttpTestingController;
  let creditsNotification: Subject<SystemNotification>;

  beforeEach(() => {
    const s = setup();
    service = s.service;
    httpMock = s.httpMock;
    creditsNotification = s.creditsNotification;
  });

  afterEach(() => httpMock.verify());

  // Cycle 1: initial signal state
  it('should start with null nextProbeAt and false isChecking', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
    // (no action — testing initial state)

    // Assert
    expect(service.nextProbeAt()).toBeNull();
    expect(service.isChecking()).toBe(false);
  });

  // Cycle 2: updateFromCredentials sets nextProbeAt when account has a probe time
  it('should set nextProbeAt from a summary with a non-null nextProbeAt', () => {
    // Arrange
    const summary = buildSummary({ nextProbeAt: '2026-08-14T10:00:00Z' });

    // Act
    service.updateFromCredentials(summary);

    // Assert
    expect(service.nextProbeAt()).toBe('2026-08-14T10:00:00Z');
  });

  // Cycle 3: updateFromCredentials clears nextProbeAt when null
  it('should clear nextProbeAt when summary has null nextProbeAt', () => {
    // Arrange
    service.updateFromCredentials(buildSummary({ nextProbeAt: '2026-08-14T10:00:00Z' }));
    expect(service.nextProbeAt()).not.toBeNull();

    // Act
    service.updateFromCredentials(buildSummary({ nextProbeAt: null }));

    // Assert
    expect(service.nextProbeAt()).toBeNull();
  });

  // Cycle 4: checkNow POSTs to /api/credentials/probe
  it('should POST to /api/credentials/probe when checkNow is called', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
    service.checkNow();
    const req = httpMock.expectOne('/api/credentials/probe');

    // Assert
    expect(req.request.method).toBe('POST');
    req.flush(buildProbeResponse());
  });

  // Cycle 5: checkNow sets isChecking true while in flight
  it('should set isChecking to true while the probe is in flight', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
    service.checkNow();

    // Assert — before flush
    expect(service.isChecking()).toBe(true);
    httpMock.expectOne('/api/credentials/probe').flush(buildProbeResponse());
  });

  // Cycle 6: checkNow clears isChecking when probe completes (inFlight: false, outcome non-null)
  it('should clear isChecking when the probe completes with an outcome', () => {
    // Arrange
    service.checkNow();
    httpMock.expectOne('/api/credentials/probe').flush(buildProbeResponse({ inFlight: false, outcome: 'Active' }));

    // Assert
    expect(service.isChecking()).toBe(false);
  });

  // Cycle 7: checkNow keeps isChecking true when response is 202 inFlight: true
  it('should keep isChecking true when the probe response indicates it is already in flight', () => {
    // Arrange
    service.checkNow();
    httpMock.expectOne('/api/credentials/probe').flush(
      buildProbeResponse({ inFlight: true, outcome: null }),
      { status: 202, statusText: 'Accepted' }
    );

    // Assert
    expect(service.isChecking()).toBe(true);
  });

  // Cycle 8: checkNow clears isChecking on HTTP error
  it('should clear isChecking when the probe request fails', () => {
    // Arrange
    service.checkNow();
    httpMock.expectOne('/api/credentials/probe').flush('Server Error', { status: 500, statusText: 'Internal Server Error' });

    // Assert
    expect(service.isChecking()).toBe(false);
  });

  // Cycle 9: credits IsActive:true triggers a credentials refetch
  it('should refetch /api/credentials when a credits IsActive:true notification arrives', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
    creditsNotification.next({ category: CREDITS_NOTIFICATION_CATEGORY, isActive: true, message: '' });

    // Assert — a GET to /api/credentials should be pending
    const req = httpMock.expectOne('/api/credentials');
    expect(req.request.method).toBe('GET');
    req.flush(buildSummary());
  });

  // Cycle 10: credits IsActive:true — refetch updates nextProbeAt
  it('should update nextProbeAt from the refetched credentials when credits notification is active', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
    creditsNotification.next({ category: CREDITS_NOTIFICATION_CATEGORY, isActive: true, message: '' });
    httpMock.expectOne('/api/credentials').flush(buildSummary({ nextProbeAt: '2026-08-14T12:00:00Z' }));

    // Assert
    expect(service.nextProbeAt()).toBe('2026-08-14T12:00:00Z');
  });

  // Cycle 11: credits IsActive:true — refetch clears isChecking
  it('should clear isChecking when a credits IsActive:true notification triggers a refetch', () => {
    // Arrange
    service.checkNow();
    httpMock.expectOne('/api/credentials/probe').flush(
      buildProbeResponse({ inFlight: true, outcome: null }),
      { status: 202, statusText: 'Accepted' }
    );
    expect(service.isChecking()).toBe(true);

    // Act
    creditsNotification.next({ category: CREDITS_NOTIFICATION_CATEGORY, isActive: true, message: '' });
    httpMock.expectOne('/api/credentials').flush(buildSummary({ nextProbeAt: '2026-08-14T12:00:00Z' }));

    // Assert
    expect(service.isChecking()).toBe(false);
  });

  // Cycle 12: credits IsActive:false — clears nextProbeAt without refetch
  it('should clear nextProbeAt when a credits IsActive:false notification arrives', () => {
    // Arrange
    service.updateFromCredentials(buildSummary({ nextProbeAt: '2026-08-14T10:00:00Z' }));
    expect(service.nextProbeAt()).not.toBeNull();

    // Act
    creditsNotification.next({ category: CREDITS_NOTIFICATION_CATEGORY, isActive: false, message: '' });

    // Assert
    expect(service.nextProbeAt()).toBeNull();
    httpMock.expectNone('/api/credentials');
  });

  // Cycle 13: credits IsActive:false — clears isChecking
  it('should clear isChecking when a credits IsActive:false notification arrives', () => {
    // Arrange
    service.checkNow();
    httpMock.expectOne('/api/credentials/probe').flush(
      buildProbeResponse({ inFlight: true, outcome: null }),
      { status: 202, statusText: 'Accepted' }
    );
    expect(service.isChecking()).toBe(true);

    // Act
    creditsNotification.next({ category: CREDITS_NOTIFICATION_CATEGORY, isActive: false, message: '' });

    // Assert
    expect(service.isChecking()).toBe(false);
  });

  // Cycle 14: re-entrancy guard — second checkNow while checking is a no-op
  it('should not issue a second HTTP request when checkNow is called while already checking', () => {
    // Arrange
    service.checkNow();
    httpMock.expectOne('/api/credentials/probe'); // first call in flight (do not flush yet)

    // Act — call checkNow again while still in flight
    service.checkNow();

    // Assert — no second request is pending
    httpMock.expectNone('/api/credentials/probe');

    // Flush first request to avoid verify() failure
    httpMock.match('/api/credentials/probe').forEach(r => r.flush(buildProbeResponse()));
  });
});

import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { Subject } from 'rxjs';
import { vi } from 'vitest';
import { USAGE_REFRESH_STATUS_MS, USAGE_REFRESH_TIMEOUT_MS, UsageService } from './usage.service';
import { SystemSignalRService } from './system-signalr.service';
import { UsageSnapshot } from '../models/usage.model';

function buildSnapshot(overrides: Partial<UsageSnapshot> = {}): UsageSnapshot {
  return {
    limits: [
      {
        kind: 'session',
        percent: 42,
        resetsAt: '2026-10-30T06:00:00Z',
        modelDisplayName: null,
        severity: 'normal',
        isActive: true,
      },
    ],
    extraUsage: null,
    capturedAt: '2026-10-30T01:00:00Z',
    accountEmail: 'test@example.com',
    lastRefreshFailedAt: null,
    ...overrides,
  };
}

function setup() {
  TestBed.resetTestingModule();

  const reconnected$ = new Subject<void>();
  const usageSnapshotUpdated$ = new Subject<UsageSnapshot | null>();

  const mockSignalR = {
    reconnected: reconnected$.asObservable(),
    usageSnapshotUpdated: usageSnapshotUpdated$.asObservable(),
    notifications: vi.fn().mockReturnValue([]),
    creditsNotification: new Subject<unknown>().asObservable(),
    reloadTrigger: new Subject<void>().asObservable(),
    loginSessionUpdate: new Subject<unknown>().asObservable(),
    applyDockerAvailability: vi.fn(),
    _reconnected$: reconnected$,
    _usageSnapshotUpdated$: usageSnapshotUpdated$,
  };

  TestBed.configureTestingModule({
    providers: [
      UsageService,
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: SystemSignalRService, useValue: mockSignalR },
    ],
  });

  const svc = TestBed.inject(UsageService);
  const http = TestBed.inject(HttpTestingController);

  return { svc, http, mockSignalR };
}

describe('UsageService', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
  });

  // Cycle 1: on construction, snapshot signal is null before seed arrives
  it('should start with a null snapshot signal', () => {
    // Arrange
    const { svc, http } = setup();

    // Act
    // (no action — testing initial state before flush)

    // Assert
    expect(svc.snapshot()).toBeNull();

    // Flush to satisfy verify()
    http.expectOne('/api/credentials/usage').flush(null, { status: 204, statusText: 'No Content' });
  });

  // Cycle 2: on construction, seeds from GET /api/credentials/usage — 200 with snapshot sets signal
  it('should set snapshot signal when GET returns a 200 with a snapshot', () => {
    // Arrange
    const { svc, http } = setup();
    const snapshot = buildSnapshot();

    // Act
    const req = http.expectOne('/api/credentials/usage');
    req.flush(snapshot);

    // Assert
    expect(svc.snapshot()).toEqual(snapshot);
  });

  it('should keep an unavailable snapshot (null capturedAt) distinct from 204', () => {
    // Arrange
    const { svc, http } = setup();
    const unavailable = buildSnapshot({
      limits: [],
      extraUsage: null,
      capturedAt: null,
      lastRefreshFailedAt: '2026-10-30T01:05:00Z',
    });

    // Act
    http.expectOne('/api/credentials/usage').flush(unavailable);

    // Assert
    expect(svc.snapshot()).toEqual(unavailable);
  });

  // Cycle 3: 204 No Content maps to null snapshot
  it('should set snapshot to null when GET returns 204 No Content', () => {
    // Arrange
    const { svc, http } = setup();

    // Act
    const req = http.expectOne('/api/credentials/usage');
    req.flush(null, { status: 204, statusText: 'No Content' });

    // Assert
    expect(svc.snapshot()).toBeNull();
  });

  // Cycle 4: hub UsageSnapshotUpdated push with a snapshot updates the signal
  it('should update snapshot signal when UsageSnapshotUpdated hub message arrives with a snapshot', () => {
    // Arrange
    const { svc, http, mockSignalR } = setup();
    http.expectOne('/api/credentials/usage').flush(null, { status: 204, statusText: 'No Content' });

    const snapshot = buildSnapshot({ accountEmail: 'hub@example.com' });

    // Act
    mockSignalR._usageSnapshotUpdated$.next(snapshot);

    // Assert
    expect(svc.snapshot()).toEqual(snapshot);
  });

  it('should keep an unavailable snapshot (null capturedAt) distinct from 204', () => {
    // Arrange
    const { svc, http } = setup();
    const unavailable = buildSnapshot({
      limits: [],
      extraUsage: null,
      capturedAt: null,
      lastRefreshFailedAt: '2026-10-30T01:05:00Z',
    });

    // Act
    http.expectOne('/api/credentials/usage').flush(unavailable);

    // Assert
    expect(svc.snapshot()).toEqual(unavailable);
  });

  // Cycle 5: hub UsageSnapshotUpdated push with null clears the signal
  it('should clear snapshot signal when UsageSnapshotUpdated hub message arrives with null', () => {
    // Arrange
    const { svc, http, mockSignalR } = setup();
    const snapshot = buildSnapshot();
    http.expectOne('/api/credentials/usage').flush(snapshot);
    expect(svc.snapshot()).toEqual(snapshot);

    // Act
    mockSignalR._usageSnapshotUpdated$.next(null);

    // Assert
    expect(svc.snapshot()).toBeNull();
  });

  // Cycle 6: reconnect triggers a re-seed from REST
  it('should re-seed from GET /api/credentials/usage when SignalR reconnects', () => {
    // Arrange
    const { svc, http, mockSignalR } = setup();
    http.expectOne('/api/credentials/usage').flush(null, { status: 204, statusText: 'No Content' });
    expect(svc.snapshot()).toBeNull();

    // Act
    mockSignalR._reconnected$.next();
    const reconnectReq = http.expectOne('/api/credentials/usage');
    reconnectReq.flush(buildSnapshot());

    // Assert
    expect(svc.snapshot()).not.toBeNull();
  });

  // Cycle 7: reconnect re-seed replaces prior snapshot
  it('should replace snapshot with fresh data when reconnect re-seed returns a different snapshot', () => {
    // Arrange
    const { svc, http, mockSignalR } = setup();
    const initial = buildSnapshot({ accountEmail: 'old@example.com' });
    const refreshed = buildSnapshot({ accountEmail: 'new@example.com' });
    http.expectOne('/api/credentials/usage').flush(initial);

    // Act
    mockSignalR._reconnected$.next();
    http.expectOne('/api/credentials/usage').flush(refreshed);

    // Assert
    expect(svc.snapshot()?.accountEmail).toBe('new@example.com');
  });

  // Cycle 8: HTTP error during initial seed leaves snapshot null and is swallowed
  it('should leave snapshot null and not throw when the seed request fails', () => {
    // Arrange
    const { svc, http } = setup();

    // Act
    http.expectOne('/api/credentials/usage').flush('Error', { status: 500, statusText: 'Internal Server Error' });

    // Assert
    expect(svc.snapshot()).toBeNull();
  });

  describe('refresh', () => {
    const REFRESH_URL = '/api/credentials/usage/refresh';

    function setupSeeded() {
      const ctx = setup();
      ctx.http.expectOne('/api/credentials/usage').flush(buildSnapshot());
      return ctx;
    }

    afterEach(() => {
      vi.useRealTimers();
    });

    it('should start idle', () => {
      // Arrange / Act
      const { svc } = setupSeeded();

      // Assert
      expect(svc.refreshState()).toBe('idle');
    });

    it('should POST to the refresh endpoint and stay refreshing on 202 until the next push', () => {
      // Arrange
      const { svc, http, mockSignalR } = setupSeeded();

      // Act
      svc.refresh();
      const req = http.expectOne(REFRESH_URL);
      expect(req.request.method).toBe('POST');
      req.flush({ status: 'started' }, { status: 202, statusText: 'Accepted' });

      // Assert — still in progress after the 202
      expect(svc.refreshState()).toBe('refreshing');

      // Act — the poll result arrives via the push
      const fresh = buildSnapshot({ capturedAt: '2026-10-30T02:00:00Z' });
      mockSignalR._usageSnapshotUpdated$.next(fresh);

      // Assert
      expect(svc.refreshState()).toBe('idle');
      expect(svc.snapshot()).toEqual(fresh);
    });

    it('should stay refreshing on 202 inFlight until the next push', () => {
      // Arrange
      const { svc, http, mockSignalR } = setupSeeded();

      // Act
      svc.refresh();
      http.expectOne(REFRESH_URL).flush({ status: 'inFlight' }, { status: 202, statusText: 'Accepted' });

      // Assert
      expect(svc.refreshState()).toBe('refreshing');
      mockSignalR._usageSnapshotUpdated$.next(buildSnapshot());
      expect(svc.refreshState()).toBe('idle');
    });

    it('should end refreshing when the push arrives before the 202 response', () => {
      // Arrange
      const { svc, http, mockSignalR } = setupSeeded();
      svc.refresh();
      const req = http.expectOne(REFRESH_URL);

      // Act — push beats the response
      mockSignalR._usageSnapshotUpdated$.next(buildSnapshot());
      req.flush({ status: 'started' }, { status: 202, statusText: 'Accepted' });

      // Assert
      expect(svc.refreshState()).toBe('idle');
    });

    it('should not send a second request while a refresh is in progress', () => {
      // Arrange
      const { svc, http } = setupSeeded();
      svc.refresh();
      http.expectOne(REFRESH_URL).flush({ status: 'started' }, { status: 202, statusText: 'Accepted' });

      // Act
      svc.refresh();

      // Assert
      http.expectNone(REFRESH_URL);
    });

    it('should report upToDate when the server says the data is current, then return to idle', () => {
      // Arrange
      vi.useFakeTimers();
      const { svc, http } = setupSeeded();

      // Act
      svc.refresh();
      http.expectOne(REFRESH_URL).flush({ status: 'current' });

      // Assert
      expect(svc.refreshState()).toBe('upToDate');
      vi.advanceTimersByTime(USAGE_REFRESH_STATUS_MS);
      expect(svc.refreshState()).toBe('idle');
    });

    it('should report unavailable when the server refuses with 409', () => {
      // Arrange
      const { svc, http } = setupSeeded();

      // Act
      svc.refresh();
      http.expectOne(REFRESH_URL).flush({ status: 'loginActive' }, { status: 409, statusText: 'Conflict' });

      // Assert
      expect(svc.refreshState()).toBe('unavailable');
    });

    it('should report error when the request fails unexpectedly', () => {
      // Arrange
      const { svc, http } = setupSeeded();
      const consoleSpy = vi.spyOn(console, 'error').mockImplementation(() => undefined);

      // Act
      svc.refresh();
      http.expectOne(REFRESH_URL).flush('Error', { status: 500, statusText: 'Internal Server Error' });

      // Assert
      expect(svc.refreshState()).toBe('error');
      consoleSpy.mockRestore();
    });

    it('should fall back to idle when no push arrives within the timeout', () => {
      // Arrange
      vi.useFakeTimers();
      const { svc, http } = setupSeeded();
      svc.refresh();
      http.expectOne(REFRESH_URL).flush({ status: 'started' }, { status: 202, statusText: 'Accepted' });

      // Act
      vi.advanceTimersByTime(USAGE_REFRESH_TIMEOUT_MS);

      // Assert
      expect(svc.refreshState()).toBe('idle');
    });
  });
});

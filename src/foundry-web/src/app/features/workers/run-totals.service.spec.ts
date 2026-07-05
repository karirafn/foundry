import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { WorkerRunTotalsService } from './run-totals.service';
import { IssueSignalRService } from '../../core/services/issue-signalr.service';
import { RunTotals } from './run-totals.model';

const mockSignalR = {
  on: () => {},
  onReconnected: () => {},
  connectionStatus: signal<'connected' | 'reconnecting' | 'disconnected'>('disconnected'),
};

const mockTotals: RunTotals = {
  runCount: 5,
  durationMs: 300000,
  numTurns: 20,
  totalCostUsd: 4.31,
  inputTokens: 10000,
  outputTokens: 5000,
};

const zeroTotals: RunTotals = {
  runCount: 0,
  durationMs: 0,
  numTurns: 0,
  totalCostUsd: 0,
  inputTokens: 0,
  outputTokens: 0,
};

function setup() {
  TestBed.configureTestingModule({
    providers: [
      WorkerRunTotalsService,
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: IssueSignalRService, useValue: mockSignalR },
    ],
  });
  const service = TestBed.inject(WorkerRunTotalsService);
  const httpMock = TestBed.inject(HttpTestingController);
  return { service, httpMock };
}

describe('WorkerRunTotalsService', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
  });

  // Cycle 1: initial state
  it('should have default scope of "month"', () => {
    // Arrange / Act
    const { service, httpMock } = setup();

    // Assert
    expect(service.scope()).toBe('month');

    // Cleanup
    httpMock.expectNone('/api/workers/run-totals');
  });

  it('should start with loading=false before fetch is called', () => {
    // Arrange / Act
    const { service, httpMock } = setup();

    // Assert
    expect(service.loading()).toBe(false);

    // Cleanup
    httpMock.expectNone('/api/workers/run-totals');
  });

  it('should start with no totals', () => {
    // Arrange / Act
    const { service, httpMock } = setup();

    // Assert
    expect(service.totals()).toBeNull();

    // Cleanup
    httpMock.expectNone('/api/workers/run-totals');
  });

  // Cycle 2: fetch sets loading=true then returns data
  it('should set loading to true while request is in flight', () => {
    // Arrange
    const { service, httpMock } = setup();

    // Act
    service.fetch();

    // Assert
    expect(service.loading()).toBe(true);

    // Cleanup
    httpMock.expectOne((r) => r.url === '/api/workers/run-totals').flush(mockTotals);
  });

  it('should set totals and clear loading on success', () => {
    // Arrange
    const { service, httpMock } = setup();

    // Act
    service.fetch();
    httpMock.expectOne((r) => r.url === '/api/workers/run-totals').flush(mockTotals);

    // Assert
    expect(service.totals()).toEqual(mockTotals);
    expect(service.loading()).toBe(false);
    expect(service.error()).toBeNull();
  });

  // Cycle 3: month scope sends from/to params
  it('should send from and to params when scope is "month"', () => {
    // Arrange
    const { service, httpMock } = setup();

    // Act
    service.fetch();
    const req = httpMock.expectOne((r) => r.url === '/api/workers/run-totals');

    // Assert
    expect(req.request.params.has('from')).toBe(true);
    expect(req.request.params.has('to')).toBe(true);

    // Cleanup
    req.flush(mockTotals);
  });

  it('should send from as the first day of the current local month', () => {
    // Arrange
    const { service, httpMock } = setup();
    const now = new Date();
    const expectedFrom = new Date(now.getFullYear(), now.getMonth(), 1);

    // Act
    service.fetch();
    const req = httpMock.expectOne((r) => r.url === '/api/workers/run-totals');
    const fromParam = req.request.params.get('from')!;

    // Assert — same date (ignoring milliseconds precision)
    const fromDate = new Date(fromParam);
    expect(fromDate.getFullYear()).toBe(expectedFrom.getFullYear());
    expect(fromDate.getMonth()).toBe(expectedFrom.getMonth());
    expect(fromDate.getDate()).toBe(expectedFrom.getDate());

    // Cleanup
    req.flush(mockTotals);
  });

  // Cycle 4: all scope omits from/to params
  it('should omit from and to params when scope is "all"', () => {
    // Arrange
    const { service, httpMock } = setup();

    // Act
    service.setScope('all');
    const req = httpMock.expectOne((r) => r.url === '/api/workers/run-totals');

    // Assert
    expect(req.request.params.has('from')).toBe(false);
    expect(req.request.params.has('to')).toBe(false);

    // Cleanup
    req.flush(mockTotals);
  });

  // Cycle 5: setScope switches + refetches (loading raised on switch)
  it('should update scope signal when setScope is called', () => {
    // Arrange
    const { service, httpMock } = setup();

    // Act
    service.setScope('all');

    // Assert
    expect(service.scope()).toBe('all');

    // Cleanup
    httpMock.expectOne((r) => r.url === '/api/workers/run-totals').flush(mockTotals);
  });

  it('should set loading to true when setScope is called (skeleton during switch)', () => {
    // Arrange
    const { service, httpMock } = setup();
    // Prime totals first
    service.fetch();
    httpMock.expectOne((r) => r.url === '/api/workers/run-totals').flush(mockTotals);
    expect(service.totals()).not.toBeNull();

    // Act
    service.setScope('all');

    // Assert — loading raised immediately on scope switch
    expect(service.loading()).toBe(true);

    // Cleanup
    httpMock.expectOne((r) => r.url === '/api/workers/run-totals').flush(mockTotals);
  });

  // Cycle 6: error state
  it('should set error and clear loading on HTTP failure', () => {
    // Arrange
    const { service, httpMock } = setup();

    // Act
    service.fetch();
    httpMock.expectOne((r) => r.url === '/api/workers/run-totals').flush('Server error', {
      status: 500,
      statusText: 'Internal Server Error',
    });

    // Assert
    expect(service.error()).toBeTruthy();
    expect(service.loading()).toBe(false);
  });

  it('should clear error on successful refetch', () => {
    // Arrange
    const { service, httpMock } = setup();
    service.fetch();
    httpMock.expectOne((r) => r.url === '/api/workers/run-totals').flush('Server error', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    expect(service.error()).toBeTruthy();

    // Act
    service.fetch();
    httpMock.expectOne((r) => r.url === '/api/workers/run-totals').flush(mockTotals);

    // Assert
    expect(service.error()).toBeNull();
  });

  // Cycle 7: zero totals renders (no empty-message behaviour needed)
  it('should accept zero totals without error', () => {
    // Arrange
    const { service, httpMock } = setup();

    // Act
    service.fetch();
    httpMock.expectOne((r) => r.url === '/api/workers/run-totals').flush(zeroTotals);

    // Assert
    expect(service.totals()).toEqual(zeroTotals);
    expect(service.error()).toBeNull();
  });

  // Cycle 8: stale-request guard — late response from old scope is discarded
  it('should discard a stale response when scope switches before the first response arrives', () => {
    // Arrange
    const { service, httpMock } = setup();

    // Act — start month fetch, then switch to all before it responds
    service.fetch();
    const staleReq = httpMock.expectOne((r) => r.url === '/api/workers/run-totals' && r.params.has('from'));

    service.setScope('all');
    const freshReq = httpMock.expectOne((r) => r.url === '/api/workers/run-totals' && !r.params.has('from'));

    // Flush fresh first, then stale
    freshReq.flush(mockTotals);
    const staleTotals: RunTotals = { ...mockTotals, runCount: 999 };
    staleReq.flush(staleTotals);

    // Assert — stale response (runCount: 999) did not overwrite the fresh response
    expect(service.totals()?.runCount).toBe(mockTotals.runCount);
  });
});

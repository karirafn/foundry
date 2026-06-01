import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { WorkerLogService, WorkerLogHub, WORKER_LOG_HUB_FACTORY } from './worker-log.service';
import { WorkerReportSummary } from './worker-report.model';

const mockReport: WorkerReportSummary = {
  id: 'report-1',
  workerRunId: 'run-1',
  sequenceNumber: 1,
  reportType: 'progress',
  content: 'Building...',
  ingestedAt: '2026-01-01T00:00:00Z',
};

interface CapturedHubCallbacks {
  onReportReceived: ((report: WorkerReportSummary) => void) | null;
  invocations: string[];
}

function buildMockHub(captured: CapturedHubCallbacks): WorkerLogHub {
  return {
    on: (_method: string, cb: (report: WorkerReportSummary) => void) => {
      captured.onReportReceived = cb;
    },
    off: () => {},
    start: () => Promise.resolve(),
    stop: () => Promise.resolve(),
    invoke: (method: string, ...args: unknown[]) => {
      captured.invocations.push(`${method}(${args.join(',')})`);
      return Promise.resolve();
    },
  };
}

function setup(useMockHub = false) {
  TestBed.resetTestingModule();

  const captured: CapturedHubCallbacks = { onReportReceived: null, invocations: [] };
  const mockHubFactory = () => buildMockHub(captured);

  TestBed.configureTestingModule({
    providers: [
      WorkerLogService,
      provideHttpClient(),
      provideHttpClientTesting(),
      ...(useMockHub ? [{ provide: WORKER_LOG_HUB_FACTORY, useValue: mockHubFactory }] : []),
    ],
  });

  return {
    svc: TestBed.inject(WorkerLogService),
    http: TestBed.inject(HttpTestingController),
    captured,
  };
}

describe('WorkerLogService', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
  });

  // Cycle 1: reports signal starts empty
  it('should start with an empty reports signal', () => {
    // Arrange / Act
    const { svc } = setup();

    // Assert
    expect(svc.reports()).toEqual([]);
  });

  // Cycle 2: loading signal starts false
  it('should start with loading false', () => {
    // Arrange / Act
    const { svc } = setup();

    // Assert
    expect(svc.loading()).toBe(false);
  });

  // Cycle 3: error signal starts null
  it('should start with error null', () => {
    // Arrange / Act
    const { svc } = setup();

    // Assert
    expect(svc.error()).toBeNull();
  });

  // Cycle 4: isLive starts false
  it('should start with isLive false', () => {
    // Arrange / Act
    const { svc } = setup();

    // Assert
    expect(svc.isLive()).toBe(false);
  });

  // Cycle 5: activeWorkerRunId starts null
  it('should start with activeWorkerRunId null', () => {
    // Arrange / Act
    const { svc } = setup();

    // Assert
    expect(svc.activeWorkerRunId()).toBeNull();
  });

  // Cycle 6: open() sets loading true while request is pending
  it('should set loading to true while fetching reports', () => {
    // Arrange
    const { svc, http } = setup();

    // Act
    svc.open('run-1', 'issue-1', 'completed');

    // Assert
    expect(svc.loading()).toBe(true);
    http.expectOne('/api/workers/run-1/reports').flush([]);
  });

  // Cycle 7: open() populates reports signal sorted by sequenceNumber
  it('should populate reports signal sorted by sequenceNumber after open', () => {
    // Arrange
    const { svc, http } = setup();
    const report2: WorkerReportSummary = { ...mockReport, id: 'report-2', sequenceNumber: 2 };
    const report1: WorkerReportSummary = { ...mockReport, id: 'report-1', sequenceNumber: 1 };

    // Act
    svc.open('run-1', 'issue-1', 'completed');
    http.expectOne('/api/workers/run-1/reports').flush([report2, report1]);

    // Assert
    expect(svc.reports()[0].sequenceNumber).toBe(1);
    expect(svc.reports()[1].sequenceNumber).toBe(2);
  });

  // Cycle 8: open() sets loading false after response
  it('should set loading to false after reports are fetched', () => {
    // Arrange
    const { svc, http } = setup();

    // Act
    svc.open('run-1', 'issue-1', 'completed');
    http.expectOne('/api/workers/run-1/reports').flush([mockReport]);

    // Assert
    expect(svc.loading()).toBe(false);
  });

  // Cycle 9: open() sets activeWorkerRunId
  it('should set activeWorkerRunId when open is called', () => {
    // Arrange
    const { svc, http } = setup();

    // Act
    svc.open('run-42', 'issue-1', 'completed');
    http.expectOne('/api/workers/run-42/reports').flush([]);

    // Assert
    expect(svc.activeWorkerRunId()).toBe('run-42');
  });

  // Cycle 10: isLive is true for in_progress state
  it('should set isLive to true when issueState is in_progress', () => {
    // Arrange
    const { svc, http } = setup(true);

    // Act
    svc.open('run-1', 'issue-1', 'in_progress');
    http.expectOne('/api/workers/run-1/reports').flush([]);

    // Assert
    expect(svc.isLive()).toBe(true);
  });

  // Cycle 11: isLive is true for revision_in_progress state
  it('should set isLive to true when issueState is revision_in_progress', () => {
    // Arrange
    const { svc, http } = setup(true);

    // Act
    svc.open('run-1', 'issue-1', 'revision_in_progress');
    http.expectOne('/api/workers/run-1/reports').flush([]);

    // Assert
    expect(svc.isLive()).toBe(true);
  });

  // Cycle 12: isLive is false for non-live states
  it('should set isLive to false when issueState is completed', () => {
    // Arrange
    const { svc, http } = setup();

    // Act
    svc.open('run-1', 'issue-1', 'completed');
    http.expectOne('/api/workers/run-1/reports').flush([]);

    // Assert
    expect(svc.isLive()).toBe(false);
  });

  // Cycle 13: open() sets user-facing error on HTTP failure
  it('should set user-facing error message for 500 when fetching reports fails', () => {
    // Arrange
    const { svc, http } = setup();

    // Act
    svc.open('run-1', 'issue-1', 'completed');
    http.expectOne('/api/workers/run-1/reports').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });

    // Assert
    expect(svc.error()).toBe('Failed to load logs — try again');
    expect(svc.loading()).toBe(false);
  });

  it('should set "Worker run not found" error for 404', () => {
    // Arrange
    const { svc, http } = setup();

    // Act
    svc.open('run-1', 'issue-1', 'completed');
    http.expectOne('/api/workers/run-1/reports').flush('Not Found', {
      status: 404,
      statusText: 'Not Found',
    });

    // Assert
    expect(svc.error()).toBe('Worker run not found');
  });

  // Cycle 13b: hub start failure sets isLive to false
  it('should set isLive to false when hub start fails', async () => {
    // Arrange
    const captured: CapturedHubCallbacks = { onReportReceived: null, invocations: [] };
    const failingHub: WorkerLogHub = {
      ...buildMockHub(captured),
      start: () => Promise.reject(new Error('Connection refused')),
    };
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        WorkerLogService,
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: WORKER_LOG_HUB_FACTORY, useValue: () => failingHub },
      ],
    });
    const svc = TestBed.inject(WorkerLogService);
    const http = TestBed.inject(HttpTestingController);

    // Act
    svc.open('run-1', 'issue-1', 'in_progress');
    http.expectOne('/api/workers/run-1/reports').flush([]);
    // Flush all pending microtasks (rejected promise + catch handler)
    await new Promise<void>((resolve) => setTimeout(resolve, 0));

    // Assert
    expect(svc.isLive()).toBe(false);
  });

  // Cycle 14: close() resets all state
  it('should reset all signals after close', () => {
    // Arrange
    const { svc, http } = setup(true);
    svc.open('run-1', 'issue-1', 'in_progress');
    http.expectOne('/api/workers/run-1/reports').flush([mockReport]);

    // Act
    svc.close();

    // Assert
    expect(svc.reports()).toEqual([]);
    expect(svc.loading()).toBe(false);
    expect(svc.error()).toBeNull();
    expect(svc.isLive()).toBe(false);
    expect(svc.activeWorkerRunId()).toBeNull();
  });

  // Cycle 15: SignalR ReportReceived appends to reports signal
  it('should append received report to reports when SignalR ReportReceived fires', () => {
    // Arrange
    const { svc, http, captured } = setup(true);
    svc.open('run-1', 'issue-1', 'in_progress');
    http.expectOne('/api/workers/run-1/reports').flush([mockReport]);

    const liveReport: WorkerReportSummary = {
      ...mockReport,
      id: 'report-live',
      sequenceNumber: 2,
      content: 'Tests passing...',
    };

    // Act
    captured.onReportReceived!(liveReport);

    // Assert
    expect(svc.reports().length).toBe(2);
    expect(svc.reports()[1].id).toBe('report-live');
  });

  // Cycle 16: SignalR ReportReceived maintains sort order
  it('should maintain sequence order when live report arrives out of order', () => {
    // Arrange
    const { svc, http, captured } = setup(true);
    svc.open('run-1', 'issue-1', 'in_progress');
    const report3: WorkerReportSummary = { ...mockReport, id: 'report-3', sequenceNumber: 3 };
    http.expectOne('/api/workers/run-1/reports').flush([report3]);

    const report2: WorkerReportSummary = { ...mockReport, id: 'report-2', sequenceNumber: 2 };

    // Act
    captured.onReportReceived!(report2);

    // Assert
    expect(svc.reports()[0].sequenceNumber).toBe(2);
    expect(svc.reports()[1].sequenceNumber).toBe(3);
  });
});

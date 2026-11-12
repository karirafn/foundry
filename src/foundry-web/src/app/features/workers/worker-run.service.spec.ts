import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { WorkerRunService } from './worker-run.service';
import { WorkerRunDetail } from './worker-run.model';

const mockDetail: WorkerRunDetail = {
  workerRunId: 'run-abc',
  role: 'implement',
  issueId: 'issue-1',
  state: 'failed',
  failureCategory: 'non_zero_exit',
  failureSummary: 'Non-zero exit code: 1',
  resultText: null,
  subtype: null,
  isError: null,
  durationMs: 60000,
  numTurns: 5,
  totalCostUsd: 0.05,
  inputTokens: 1000,
  outputTokens: 500,
  lastActivityAt: '2026-01-01T00:01:00Z',
  hasStoredLog: true,
  cancelRequestedAt: null,
  publish: null,
  publishRetry: null,
  discardsAt: null,
};

function setup() {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    providers: [
      WorkerRunService,
      provideHttpClient(),
      provideHttpClientTesting(),
    ],
  });
  return {
    svc: TestBed.inject(WorkerRunService),
    controller: TestBed.inject(HttpTestingController),
  };
}

describe('WorkerRunService', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
  });

  // Cycle 1: getDetail fetches the correct URL
  it('should fetch worker run detail from the correct URL', () => {
    // Arrange
    const { svc, controller } = setup();

    // Act
    svc.getDetail('run-abc').subscribe();
    const req = controller.expectOne('/api/workers/runs/run-abc');

    // Assert
    expect(req.request.method).toBe('GET');
    req.flush(mockDetail);
  });

  // Cycle 2: getDetail emits the returned WorkerRunDetail
  it('should emit the WorkerRunDetail on successful fetch', () => {
    // Arrange
    const { svc, controller } = setup();
    let result: WorkerRunDetail | null = null;

    // Act
    svc.getDetail('run-abc').subscribe((d) => (result = d));
    controller.expectOne('/api/workers/runs/run-abc').flush(mockDetail);

    // Assert
    expect(result).toEqual(mockDetail);
  });

  // Cycle 3: getDetail emits null on 404
  it('should emit null when the server returns 404', () => {
    // Arrange
    const { svc, controller } = setup();
    let result: WorkerRunDetail | null | undefined = undefined;

    // Act
    svc.getDetail('run-missing').subscribe((d) => (result = d));
    controller.expectOne('/api/workers/runs/run-missing').flush(null, { status: 404, statusText: 'Not Found' });

    // Assert
    expect(result).toBeNull();
  });

  // Cycle 4: getLog fetches the log text URL
  it('should fetch log text from the correct URL', () => {
    // Arrange
    const { svc, controller } = setup();

    // Act
    svc.getLog('run-abc').subscribe();
    const req = controller.expectOne('/api/workers/runs/run-abc/log');

    // Assert
    expect(req.request.method).toBe('GET');
    req.flush('some log text');
  });

  // Cycle 5: getLog emits the log text on 200
  it('should emit log text on successful fetch', () => {
    // Arrange
    const { svc, controller } = setup();
    let result: string | null | undefined = undefined;

    // Act
    svc.getLog('run-abc').subscribe((t) => (result = t));
    controller.expectOne('/api/workers/runs/run-abc/log').flush('log line 1\nlog line 2');

    // Assert
    expect(result).toBe('log line 1\nlog line 2');
  });

  // Cycle 6: getLog emits null on 204
  it('should emit null when log returns 204 No Content', () => {
    // Arrange
    const { svc, controller } = setup();
    let result: string | null | undefined = undefined;

    // Act
    svc.getLog('run-abc').subscribe((t) => (result = t));
    controller.expectOne('/api/workers/runs/run-abc/log').flush(null, { status: 204, statusText: 'No Content' });

    // Assert
    expect(result).toBeNull();
  });

  // Cycle 7: getLog emits null on 404
  it('should emit null when log returns 404', () => {
    // Arrange
    const { svc, controller } = setup();
    let result: string | null | undefined = undefined;

    // Act
    svc.getLog('run-abc').subscribe((t) => (result = t));
    controller.expectOne('/api/workers/runs/run-abc/log').flush(null, { status: 404, statusText: 'Not Found' });

    // Assert
    expect(result).toBeNull();
  });

  // requestCancel posts to the cancellation endpoint and emits null on 202
  it('should POST to the cancellation endpoint and emit null on success', () => {
    // Arrange
    const { svc, controller } = setup();
    let result: string | null | undefined = undefined;

    // Act
    svc.requestCancel('run-abc').subscribe((r) => (result = r));
    const req = controller.expectOne('/api/workers/runs/run-abc/cancellation');
    req.flush({ workerRunId: 'run-abc', cancelRequestedAt: '2026-11-04T10:00:00Z' }, { status: 202, statusText: 'Accepted' });

    // Assert
    expect(req.request.method).toBe('POST');
    expect(result).toBeNull();
  });

  it('should emit a not-running message when cancellation returns 409', () => {
    // Arrange
    const { svc, controller } = setup();
    let result: string | null | undefined = undefined;

    // Act
    svc.requestCancel('run-abc').subscribe((r) => (result = r));
    controller.expectOne('/api/workers/runs/run-abc/cancellation').flush(null, { status: 409, statusText: 'Conflict' });

    // Assert
    expect(result).toBe('The run is no longer running.');
  });

  it('should emit a not-found message when cancellation returns 404', () => {
    // Arrange
    const { svc, controller } = setup();
    let result: string | null | undefined = undefined;

    // Act
    svc.requestCancel('run-abc').subscribe((r) => (result = r));
    controller.expectOne('/api/workers/runs/run-abc/cancellation').flush(null, { status: 404, statusText: 'Not Found' });

    // Assert
    expect(result).toBe('The run was not found.');
  });

  // requestPublishRetry (#787)
  it('should POST to the publish-retry endpoint and emit null on 202', () => {
    // Arrange
    const { svc, controller } = setup();
    let result: string | null | undefined = undefined;

    // Act
    svc.requestPublishRetry('run-abc').subscribe((r) => (result = r));
    const req = controller.expectOne('/api/workers/runs/run-abc/publish-retry');
    req.flush(null, { status: 202, statusText: 'Accepted' });

    // Assert
    expect(req.request.method).toBe('POST');
    expect(result).toBeNull();
  });

  it('should emit a not-waiting message when publish-retry returns 409', () => {
    // Arrange
    const { svc, controller } = setup();
    let result: string | null | undefined = undefined;

    // Act
    svc.requestPublishRetry('run-abc').subscribe((r) => (result = r));
    controller.expectOne('/api/workers/runs/run-abc/publish-retry').flush(null, { status: 409, statusText: 'Conflict' });

    // Assert
    expect(result).toBe('This run is no longer waiting to publish.');
  });

  it('should emit a not-found message when publish-retry returns 404', () => {
    // Arrange
    const { svc, controller } = setup();
    let result: string | null | undefined = undefined;

    // Act
    svc.requestPublishRetry('run-abc').subscribe((r) => (result = r));
    controller.expectOne('/api/workers/runs/run-abc/publish-retry').flush(null, { status: 404, statusText: 'Not Found' });

    // Assert
    expect(result).toBe('The run was not found.');
  });

  it('should emit a generic message when publish-retry fails otherwise', () => {
    // Arrange
    const { svc, controller } = setup();
    let result: string | null | undefined = undefined;
    const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => undefined);

    // Act
    svc.requestPublishRetry('run-abc').subscribe((r) => (result = r));
    controller.expectOne('/api/workers/runs/run-abc/publish-retry').flush(null, { status: 500, statusText: 'Server Error' });

    // Assert
    expect(result).toBe('Failed to retry publish.');
    errorSpy.mockRestore();
  });
});

import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { IssueService } from './issue.service';
import { SignalRService } from '../../core/services/signalr.service';
import { IssueSummary, IssueDetail } from './issue.model';

const mockSignalRService = {
  on: () => {},
  onReconnected: () => {},
};

const mockSummary: IssueSummary = {
  id: 'abc123',
  issueNumber: 42,
  title: 'Fix the bug',
  state: 'detected',
  repositorySlug: 'owner/repo',
  detectedAt: '2026-01-01T00:00:00Z',
  url: 'https://github.com/owner/repo/issues/42',
};

function setupWithCapturingSignalR(callbacks: Record<string, (data: IssueSummary) => void>, reconnectCallbacks: Array<() => void>) {
  const capturingSignalR = {
    on: (method: string, cb: (data: IssueSummary) => void) => { callbacks[method] = cb; },
    onReconnected: (cb: () => void) => { reconnectCallbacks.push(cb); },
  };

  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    providers: [
      IssueService,
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: SignalRService, useValue: capturingSignalR },
    ],
  });

  return {
    svc: TestBed.inject(IssueService),
    http: TestBed.inject(HttpTestingController),
  };
}

describe('IssueService', () => {
  let service: IssueService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        IssueService,
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: SignalRService, useValue: mockSignalRService },
      ],
    });
    service = TestBed.inject(IssueService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify({ ignoreCancelled: true }));

  // Cycle 1: loadIssues populates the issues signal
  it('should populate issues signal after loadIssues', () => {
    // Arrange
    const mockIssues: IssueSummary[] = [mockSummary];

    // Act
    service.loadIssues();
    const req = httpMock.expectOne('/api/issues');
    req.flush(mockIssues);

    // Assert
    expect(service.issues()).toEqual(mockIssues);
  });

  // Cycle 2: loadIssues with repositoryId appends query param
  it('should append repositoryId query param when provided to loadIssues', () => {
    // Arrange / Act
    service.loadIssues('repo-id-1');
    const req = httpMock.expectOne('/api/issues?repositoryId=repo-id-1');
    req.flush([]);

    // Assert
    expect(req.request.method).toBe('GET');
  });

  // Cycle 3: sortedIssues computed signal returns issues sorted by detectedAt descending
  it('should sort issues by detectedAt descending in sortedIssues', () => {
    // Arrange
    const older: IssueSummary = { ...mockSummary, id: 'older', detectedAt: '2026-01-01T00:00:00Z' };
    const newer: IssueSummary = { ...mockSummary, id: 'newer', detectedAt: '2026-06-01T00:00:00Z' };

    // Act
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush([older, newer]);

    // Assert
    expect(service.sortedIssues()[0].id).toBe('newer');
    expect(service.sortedIssues()[1].id).toBe('older');
  });

  // Cycle 4: isEmpty computed signal reflects issue count
  it('should report isEmpty as true when no issues are loaded', () => {
    // Arrange / Act / Assert
    expect(service.isEmpty()).toBe(true);
  });

  it('should report isEmpty as false after issues are loaded', () => {
    // Arrange / Act
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush([mockSummary]);

    // Assert
    expect(service.isEmpty()).toBe(false);
  });

  // Cycle 5: loadDetail fetches issue detail and updates signals
  it('should fetch issue detail and update issueDetail signal', () => {
    // Arrange
    const detail: IssueDetail = {
      ...mockSummary,
      author: 'dev',
      labels: ['bug'],
      stateDetails: {
        workerRunId: null,
        branchName: null,
        pullRequestUrl: null,
        feedbackCutoffAt: null,
        failureReason: null,
        failedAt: null,
        completedAt: null,
        blockedBy: null,
      },
    };

    // Act
    service.loadDetail('abc123');
    const req = httpMock.expectOne('/api/issues/abc123');
    req.flush(detail);

    // Assert
    expect(service.issueDetail()).toEqual(detail);
    expect(service.detailLoading()).toBe(false);
  });

  it('should set detailLoading to true before the detail request resolves', () => {
    // Arrange / Act
    service.loadDetail('abc123');

    // Assert (before flush, loading is true)
    expect(service.detailLoading()).toBe(true);
    httpMock.expectOne('/api/issues/abc123').flush({});
  });

  // Cycle 6: toggleExpand — collapse when same id
  it('should collapse when toggleExpand is called with the currently expanded id', () => {
    // Arrange — first expand
    service.toggleExpand('abc123');
    httpMock.expectOne('/api/issues/abc123').flush({ ...mockSummary });
    expect(service.expandedIssueId()).toBe('abc123');

    // Act — collapse
    service.toggleExpand('abc123');

    // Assert
    expect(service.expandedIssueId()).toBeNull();
    expect(service.issueDetail()).toBeNull();
  });

  // Cycle 7: toggleExpand — expand new and fetch detail
  it('should expand a new issue and fetch its detail when toggleExpand is called', () => {
    // Arrange / Act
    service.toggleExpand('abc123');

    // Assert
    expect(service.expandedIssueId()).toBe('abc123');
    expect(service.detailLoading()).toBe(true);
    httpMock.expectOne('/api/issues/abc123').flush({ ...mockSummary });
  });

  // Cycle 8: toggleExpand — switch from one to another
  it('should switch to a new issue when toggleExpand is called with a different id', () => {
    // Arrange
    service.toggleExpand('abc123');
    httpMock.expectOne('/api/issues/abc123').flush({ ...mockSummary });

    // Act
    service.toggleExpand('def456');

    // Assert
    expect(service.expandedIssueId()).toBe('def456');
    httpMock.expectOne('/api/issues/def456').flush({ ...mockSummary, id: 'def456' });
  });

  // Cycle 8b: toggleExpand — switching clears stale detail immediately
  it('should clear issueDetail and set detailLoading to true immediately when switching to a different issue', () => {
    // Arrange — expand first issue and let its detail load
    const ineligibleDetail = { ...mockSummary, state: 'ineligible' };
    service.toggleExpand('abc123');
    httpMock.expectOne('/api/issues/abc123').flush(ineligibleDetail);
    expect(service.issueDetail()).toEqual(ineligibleDetail);

    // Act — switch to a different issue (do NOT flush the second request yet)
    service.toggleExpand('def456');

    // Assert — stale detail is cleared and loading is true before the new response arrives
    expect(service.issueDetail()).toBeNull();
    expect(service.detailLoading()).toBe(true);

    // Cleanup
    httpMock.expectOne('/api/issues/def456').flush({ ...mockSummary, id: 'def456' });
  });

  // Cycle 8c: toggleExpand — rapid switching does not leak stale state
  it('should not retain stale detail when rapidly switching between issues', () => {
    // Arrange — expand first issue, let detail load
    service.toggleExpand('abc123');
    httpMock.expectOne('/api/issues/abc123').flush({ ...mockSummary, state: 'ineligible' });
    expect(service.issueDetail()).not.toBeNull();

    // Act — switch to issue B without waiting for response, then switch to issue C
    service.toggleExpand('def456');
    // At this point the def456 request is in flight; switch again
    service.toggleExpand('ghi789');

    // Assert — issueDetail is null (cleared) and detailLoading is true
    expect(service.issueDetail()).toBeNull();
    expect(service.detailLoading()).toBe(true);

    // Cleanup — the def456 request was cancelled by unsubscribe; only ghi789 is still in flight
    httpMock.expectOne('/api/issues/ghi789').flush({ ...mockSummary, id: 'ghi789' });
  });

  // Cycle 8d: late-arriving detail response for a previous issue must not overwrite current detail
  // This tests the in-callback guard: even if a response arrives after expandedIssueId changed
  // (e.g. via SignalR triggering loadDetail while the user has already switched), it is discarded.
  it('should discard a late-arriving detail response when expandedIssueId no longer matches', () => {
    // Arrange — simulate issue B already loaded as the active issue
    const detailB: IssueDetail = {
      ...mockSummary,
      id: 'def456',
      author: 'dev',
      labels: [],
      stateDetails: {
        workerRunId: null,
        branchName: null,
        pullRequestUrl: null,
        feedbackCutoffAt: null,
        failureReason: null,
        failedAt: null,
        completedAt: null,
        blockedBy: null,
      },
    };

    // Expand B so its detail is loaded first
    service.toggleExpand('def456');
    httpMock.expectOne('/api/issues/def456').flush(detailB);
    expect(service.issueDetail()).toEqual(detailB);

    // Simulate an in-flight loadDetail for issue A (e.g. triggered by a SignalR event before
    // the user switched), but do not flush it yet
    service.loadDetail('abc123');
    const reqA = httpMock.expectOne('/api/issues/abc123');

    // User switches to issue B again (or any other change that sets expandedIssueId away from A)
    // Here we directly set expandedIssueId to simulate switching away from A
    service.expandedIssueId.set('def456');

    // Act — the stale response for issue A now arrives
    reqA.flush({ ...mockSummary, id: 'abc123', body: 'Issue A body (stale)' });

    // Assert — issue B's detail is still shown; the stale A response was discarded by the guard
    expect(service.issueDetail()).toEqual(detailB);
    expect(service.expandedIssueId()).toBe('def456');
  });

  // Cycle 9: SignalR upsert — updates existing issue in list
  it('should upsert an existing issue when IssueUpdated SignalR event is received', () => {
    // Arrange
    const callbacks: Record<string, (data: IssueSummary) => void> = {};
    const { svc, http } = setupWithCapturingSignalR(callbacks, []);

    svc.loadIssues();
    http.expectOne('/api/issues').flush([mockSummary]);

    const updated: IssueSummary = { ...mockSummary, state: 'in_progress' };

    // Act
    callbacks['IssueUpdated'](updated);

    // Assert
    expect(svc.issues().find((i: IssueSummary) => i.id === 'abc123')?.state).toBe('in_progress');
    http.verify();
  });

  // Cycle 10: SignalR upsert — appends new issue not in list
  it('should append a new issue when IssueUpdated event is received for unknown id', () => {
    // Arrange
    const callbacks: Record<string, (data: IssueSummary) => void> = {};
    const { svc, http } = setupWithCapturingSignalR(callbacks, []);

    svc.loadIssues();
    http.expectOne('/api/issues').flush([]);

    const newIssue: IssueSummary = { ...mockSummary, id: 'brand-new' };

    // Act
    callbacks['IssueUpdated'](newIssue);

    // Assert
    expect(svc.issues().some((i: IssueSummary) => i.id === 'brand-new')).toBe(true);
    http.verify();
  });

  // Cycle 11: SignalR — re-fetch detail when expanded issue is updated
  it('should re-fetch detail when the expanded issue receives an IssueUpdated event', () => {
    // Arrange
    const callbacks: Record<string, (data: IssueSummary) => void> = {};
    const { svc, http } = setupWithCapturingSignalR(callbacks, []);

    svc.toggleExpand('abc123');
    http.expectOne('/api/issues/abc123').flush({ ...mockSummary });
    expect(svc.expandedIssueId()).toBe('abc123');

    // Act — signal update for the currently expanded issue
    callbacks['IssueUpdated']({ ...mockSummary, state: 'in_progress' });

    // Assert — a second detail fetch is triggered
    http.expectOne('/api/issues/abc123').flush({ ...mockSummary, state: 'in_progress' });
    http.verify();
  });

  // Cycle 12a: initialLoading starts true, becomes false after first loadIssues response
  it('should have initialLoading true before first loadIssues response', () => {
    // Arrange / Act
    service.loadIssues();

    // Assert — before the response, still loading
    expect(service.initialLoading()).toBe(true);
    httpMock.expectOne('/api/issues').flush([]);
  });

  it('should set initialLoading to false after loadIssues succeeds', () => {
    // Arrange / Act
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush([]);

    // Assert
    expect(service.initialLoading()).toBe(false);
  });

  it('should set initialLoading to false after loadIssues fails', () => {
    // Arrange / Act
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });

    // Assert
    expect(service.initialLoading()).toBe(false);
  });

  it('should preserve existing issues when loadIssues fails', () => {
    // Arrange — load initial issues
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush([mockSummary]);
    expect(service.issues().length).toBe(1);

    // Act — reload with error
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });

    // Assert — original issues remain
    expect(service.issues().length).toBe(1);
  });

  it('should set loadError when loadIssues fails', () => {
    // Arrange / Act
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });

    // Assert
    expect(service.loadError()).not.toBeNull();
  });

  it('should set loadError to a fixed user-facing string when loadIssues fails', () => {
    // Arrange / Act
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });

    // Assert — must not contain server-influenced text such as err.message
    expect(service.loadError()).toBe('Failed to load issues');
  });

  it('should set detailError to a fixed user-facing string when loadDetail fails', () => {
    // Arrange / Act
    service.loadDetail('abc123');
    httpMock.expectOne('/api/issues/abc123').flush('Not Found', {
      status: 404,
      statusText: 'Not Found',
    });

    // Assert — must not contain server-influenced text such as err.message
    expect(service.detailError()).toBe('Failed to load issue details');
  });

  it('should set detailError to a fixed user-facing string when retryEligibility fails', () => {
    // Arrange / Act
    service.retryEligibility('abc123');
    httpMock.expectOne('/api/issues/abc123/retry-eligibility').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });

    // Assert — must not contain server-influenced text such as err.message
    expect(service.detailError()).toBe('Failed to load issue details');
  });

  it('should clear loadError on successful loadIssues', () => {
    // Arrange — cause an error first
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    expect(service.loadError()).not.toBeNull();

    // Act — successful reload
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush([mockSummary]);

    // Assert
    expect(service.loadError()).toBeNull();
  });

  it('should set detailLoading to false when loadDetail fails', () => {
    // Arrange
    service.loadDetail('abc123');
    expect(service.detailLoading()).toBe(true);

    // Act
    httpMock.expectOne('/api/issues/abc123').flush('Not Found', {
      status: 404,
      statusText: 'Not Found',
    });

    // Assert
    expect(service.detailLoading()).toBe(false);
  });

  it('should set detailError when loadDetail fails', () => {
    // Arrange / Act
    service.loadDetail('abc123');
    httpMock.expectOne('/api/issues/abc123').flush('Not Found', {
      status: 404,
      statusText: 'Not Found',
    });

    // Assert
    expect(service.detailError()).not.toBeNull();
  });

  // Cycle 13: retryEligibility posts to retry-eligibility endpoint and reloads detail
  it('should POST to retry-eligibility endpoint when retryEligibility is called', () => {
    // Arrange / Act
    service.retryEligibility('abc123');

    // Assert
    const req = httpMock.expectOne('/api/issues/abc123/retry-eligibility');
    expect(req.request.method).toBe('POST');
    req.flush(null);
    httpMock.expectOne('/api/issues/abc123').flush({});
  });

  it('should reload issue detail after retryEligibility succeeds', () => {
    // Arrange
    service.retryEligibility('abc123');
    httpMock.expectOne('/api/issues/abc123/retry-eligibility').flush(null);

    // Act / Assert — loadDetail is called and fetches updated detail
    const req = httpMock.expectOne('/api/issues/abc123');
    expect(req.request.method).toBe('GET');
    req.flush({ ...mockSummary });
  });

  it('should set retryingEligibility to true before the retry request resolves', () => {
    // Arrange / Act
    service.retryEligibility('abc123');

    // Assert — signal is true while the request is in flight
    expect(service.retryingEligibility()).toBe(true);
    httpMock.expectOne('/api/issues/abc123/retry-eligibility').flush(null);
    httpMock.expectOne('/api/issues/abc123').flush({});
  });

  it('should set retryingEligibility to false after retryEligibility succeeds', () => {
    // Arrange
    service.retryEligibility('abc123');

    // Act
    httpMock.expectOne('/api/issues/abc123/retry-eligibility').flush(null);
    httpMock.expectOne('/api/issues/abc123').flush({});

    // Assert
    expect(service.retryingEligibility()).toBe(false);
  });

  it('should set retryingEligibility to false and detailError when retryEligibility fails', () => {
    // Arrange
    service.retryEligibility('abc123');

    // Act
    httpMock.expectOne('/api/issues/abc123/retry-eligibility').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });

    // Assert
    expect(service.retryingEligibility()).toBe(false);
    expect(service.detailError()).not.toBeNull();
  });

  // Cycle 12: Reconnect backfill calls loadIssues
  it('should call loadIssues on reconnect to backfill missed events', () => {
    // Arrange
    const reconnectCallbacks: Array<() => void> = [];
    const { svc, http } = setupWithCapturingSignalR({}, reconnectCallbacks);

    // Act — simulate reconnect
    reconnectCallbacks[0]();

    // Assert — loadIssues was triggered (HTTP request sent)
    const req = http.expectOne('/api/issues');
    req.flush([]);
    expect(svc.issues()).toEqual([]);
    http.verify();
  });
});

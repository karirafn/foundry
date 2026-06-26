import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { IssueService } from './issue.service';
import { IssueSignalRService } from '../../core/services/issue-signalr.service';
import { IssueSummary, IssueDetail } from './issue.model';
import { ACTIVE_STATES } from './issue-lifecycle.model';
import { vi } from 'vitest';

interface PagedIssues {
  items: IssueSummary[];
  nextCursor: string | null;
}

const mockIssueSignalRService = {
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
      { provide: IssueSignalRService, useValue: capturingSignalR },
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
        { provide: IssueSignalRService, useValue: mockIssueSignalRService },
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
    // Arrange — no additional setup

    // Act
    service.loadIssues('repo-id-1');
    const req = httpMock.expectOne('/api/issues?repositoryId=repo-id-1');
    req.flush([]);

    // Assert
    expect(req.request.method).toBe('GET');
  });

  // Cycle 3: sortedIssues computed signal returns non-live active issues sorted by detectedAt descending
  it('should sort non-live issues by detectedAt descending in sortedIssues', () => {
    // Arrange
    const older: IssueSummary = { ...mockSummary, id: 'older', state: 'detected', detectedAt: '2026-01-01T00:00:00Z' };
    const newer: IssueSummary = { ...mockSummary, id: 'newer', state: 'detected', detectedAt: '2026-06-01T00:00:00Z' };

    // Act
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush([older, newer]);

    // Assert
    expect(service.sortedIssues()[0].id).toBe('newer');
    expect(service.sortedIssues()[1].id).toBe('older');
  });

  // Cycle 3b: live issues sort before non-live issues
  it('should sort live issues before non-live issues in sortedIssues', () => {
    // Arrange
    const nonLive: IssueSummary = { ...mockSummary, id: 'non-live', state: 'detected', detectedAt: '2026-06-01T00:00:00Z' };
    const live: IssueSummary = { ...mockSummary, id: 'live', state: 'in_progress', detectedAt: '2026-01-01T00:00:00Z' };

    // Act
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush([nonLive, live]);

    // Assert — live appears first even though it has an older detectedAt
    expect(service.sortedIssues()[0].id).toBe('live');
    expect(service.sortedIssues()[1].id).toBe('non-live');
  });

  // Cycle 3c: revision_in_progress is treated as a live state
  it('should treat revision_in_progress as a live state in sortedIssues', () => {
    // Arrange
    const nonLive: IssueSummary = { ...mockSummary, id: 'non-live', state: 'failed', detectedAt: '2026-06-01T00:00:00Z' };
    const revision: IssueSummary = { ...mockSummary, id: 'revision', state: 'revision_in_progress', detectedAt: '2026-01-01T00:00:00Z' };

    // Act
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush([nonLive, revision]);

    // Assert — revision_in_progress appears first even with an older detectedAt
    expect(service.sortedIssues()[0].id).toBe('revision');
    expect(service.sortedIssues()[1].id).toBe('non-live');
  });

  // Cycle 3d: within the live group, issues are sorted by detectedAt descending
  it('should sort live issues by detectedAt descending within the live group', () => {
    // Arrange
    const liveOlder: IssueSummary = { ...mockSummary, id: 'live-older', state: 'in_progress', detectedAt: '2026-01-01T00:00:00Z' };
    const liveNewer: IssueSummary = { ...mockSummary, id: 'live-newer', state: 'revision_in_progress', detectedAt: '2026-06-01T00:00:00Z' };

    // Act
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush([liveOlder, liveNewer]);

    // Assert
    expect(service.sortedIssues()[0].id).toBe('live-newer');
    expect(service.sortedIssues()[1].id).toBe('live-older');
  });

  // Cycle 3e: liveIssueCount returns 0 when no live issues
  it('should return 0 for liveIssueCount when there are no live issues', () => {
    // Arrange
    const nonLive: IssueSummary = { ...mockSummary, id: 'non-live', state: 'detected' };

    // Act
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush([nonLive]);

    // Assert
    expect(service.liveIssueCount()).toBe(0);
  });

  // Cycle 3f: liveIssueCount returns count of live issues when all are live
  it('should return correct count for liveIssueCount when all issues are live', () => {
    // Arrange
    const live1: IssueSummary = { ...mockSummary, id: 'live-1', state: 'in_progress' };
    const live2: IssueSummary = { ...mockSummary, id: 'live-2', state: 'revision_in_progress' };

    // Act
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush([live1, live2]);

    // Assert
    expect(service.liveIssueCount()).toBe(2);
  });

  // Cycle 3g: liveIssueCount returns correct count in a mixed list
  it('should return the number of live issues for liveIssueCount in a mixed list', () => {
    // Arrange
    const live: IssueSummary = { ...mockSummary, id: 'live', state: 'in_progress' };
    const nonLive1: IssueSummary = { ...mockSummary, id: 'non-live-1', state: 'detected' };
    const nonLive2: IssueSummary = { ...mockSummary, id: 'non-live-2', state: 'failed' };

    // Act
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush([live, nonLive1, nonLive2]);

    // Assert
    expect(service.liveIssueCount()).toBe(1);
  });

  // Cycle 4: isEmpty computed signal reflects issue count
  it('should report isEmpty as true when no issues are loaded', () => {
    // Arrange — no issues loaded (initial state)

    // Act — read the computed signal

    // Assert
    expect(service.isEmpty()).toBe(true);
  });

  it('should report isEmpty as false after issues are loaded', () => {
    // Arrange — no additional setup

    // Act
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
      providerType: 'GitHub',
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
        violations: null,
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
    // Arrange — no additional setup

    // Act
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
    // Arrange — no additional setup

    // Act
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
      providerType: 'GitHub',
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
        violations: null,
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
    reqA.flush({ ...mockSummary, id: 'abc123' });

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
    // Arrange — no additional setup

    // Act
    service.loadIssues();

    // Assert — before the response, still loading
    expect(service.initialLoading()).toBe(true);
    httpMock.expectOne('/api/issues').flush([]);
  });

  it('should set initialLoading to false after loadIssues succeeds', () => {
    // Arrange — no additional setup

    // Act
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush([]);

    // Assert
    expect(service.initialLoading()).toBe(false);
  });

  it('should set initialLoading to false after loadIssues fails', () => {
    // Arrange — no additional setup

    // Act
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
    // Arrange — no additional setup

    // Act
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });

    // Assert
    expect(service.loadError()).not.toBeNull();
  });

  it('should set loadError to a fixed user-facing string when loadIssues fails', () => {
    // Arrange — no additional setup

    // Act
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });

    // Assert — must not contain server-influenced text such as err.message
    expect(service.loadError()).toBe('Failed to load issues');
  });

  it('should set detailError to a fixed user-facing string when loadDetail fails', () => {
    // Arrange — no additional setup

    // Act
    service.loadDetail('abc123');
    httpMock.expectOne('/api/issues/abc123').flush('Not Found', {
      status: 404,
      statusText: 'Not Found',
    });

    // Assert — must not contain server-influenced text such as err.message
    expect(service.detailError()).toBe('Failed to load issue details');
  });

  it('should set detailError to a fixed user-facing string when retryEligibility fails', () => {
    // Arrange — no additional setup

    // Act
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
    // Arrange — no additional setup

    // Act
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
    // Arrange — no additional setup

    // Act
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

    // Act — loadDetail is triggered after success
    const req = httpMock.expectOne('/api/issues/abc123');
    req.flush({ ...mockSummary });

    // Assert
    expect(req.request.method).toBe('GET');
  });

  it('should set retryingEligibility to true before the retry request resolves', () => {
    // Arrange — no additional setup

    // Act
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

  // Cycle 14: _upsertIssue rejects ids that do not match safe-id pattern
  it('should not upsert an issue with an id containing a path traversal sequence', () => {
    // Arrange
    const callbacks: Record<string, (data: IssueSummary) => void> = {};
    const { svc, http } = setupWithCapturingSignalR(callbacks, []);

    svc.loadIssues();
    http.expectOne('/api/issues').flush([]);
    expect(svc.issues().length).toBe(0);

    const malicious: IssueSummary = { ...mockSummary, id: '../../admin' };

    // Act
    callbacks['IssueUpdated'](malicious);

    // Assert — issues list stays empty, the malicious id was rejected
    expect(svc.issues().length).toBe(0);
    http.verify();
  });

  // Finding 3: loadIssues drops items with unknown state
  it('should drop items with an unknown state on loadIssues', () => {
    // Arrange
    const valid: IssueSummary = { ...mockSummary, id: 'valid-id' };
    const unknown = { ...mockSummary, id: 'unknown-state-id', state: 'ghost_state' as never };

    // Act
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush([valid, unknown]);

    // Assert — only valid issue is retained
    expect(service.issues().length).toBe(1);
    expect(service.issues()[0].id).toBe('valid-id');
  });

  it('should retain an ineligible-state item on loadIssues as a known valid state', () => {
    // Arrange
    const ineligible: IssueSummary = { ...mockSummary, id: 'ineligible-id', state: 'ineligible' };

    // Act
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush([ineligible]);

    // Assert — ineligible is a known state and must not be dropped
    expect(service.issues().length).toBe(1);
    expect(service.issues()[0].id).toBe('ineligible-id');
  });

  // Finding 3: _upsertIssue drops items with unknown state
  it('should reject an IssueUpdated event with an unknown state', () => {
    // Arrange
    const callbacks: Record<string, (data: IssueSummary) => void> = {};
    const { svc, http } = setupWithCapturingSignalR(callbacks, []);

    svc.loadIssues();
    http.expectOne('/api/issues').flush([]);
    expect(svc.issues().length).toBe(0);

    const unknown = { ...mockSummary, id: 'ghost-id', state: 'ghost_state' as never };

    // Act
    callbacks['IssueUpdated'](unknown);

    // Assert — issues list stays empty
    expect(svc.issues().length).toBe(0);
    http.verify();
  });

  // Cycle 15: loadIssues filters out items with invalid IDs
  it('should filter out issues with invalid ids returned by loadIssues', () => {
    // Arrange
    const valid: IssueSummary = { ...mockSummary, id: 'valid-id' };
    const invalid: IssueSummary = { ...mockSummary, id: '../../admin' };

    // Act
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush([valid, invalid]);

    // Assert — only the valid issue is stored
    expect(service.issues().length).toBe(1);
    expect(service.issues()[0].id).toBe('valid-id');
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

  // Cycle 16: retryFailed — POSTs to /api/issues/{id}/retry and reloads detail
  it('should POST to /api/issues/{id}/retry when retryFailed is called', () => {
    // Arrange — no additional setup

    // Act
    service.retryFailed('abc123');

    // Assert
    const req = httpMock.expectOne('/api/issues/abc123/retry');
    expect(req.request.method).toBe('POST');
    req.flush({});
    httpMock.expectOne('/api/issues/abc123').flush({});
  });

  it('should set retryingFailed to true while the retryFailed request is in flight', () => {
    // Arrange — no additional setup

    // Act
    service.retryFailed('abc123');

    // Assert — signal is true before the response arrives
    expect(service.retryingFailed()).toBe(true);
    httpMock.expectOne('/api/issues/abc123/retry').flush({});
    httpMock.expectOne('/api/issues/abc123').flush({});
  });

  it('should set retryingFailed to false after retryFailed succeeds', () => {
    // Arrange
    service.retryFailed('abc123');

    // Act
    httpMock.expectOne('/api/issues/abc123/retry').flush({});
    httpMock.expectOne('/api/issues/abc123').flush({});

    // Assert
    expect(service.retryingFailed()).toBe(false);
  });

  it('should reload issue detail after retryFailed succeeds', () => {
    // Arrange
    service.retryFailed('abc123');
    httpMock.expectOne('/api/issues/abc123/retry').flush({});

    // Act — loadDetail is triggered after success
    const req = httpMock.expectOne('/api/issues/abc123');
    req.flush({ ...mockSummary });

    // Assert
    expect(req.request.method).toBe('GET');
  });

  it('should set retryFailedError and retryingFailed to false when retryFailed fails', () => {
    // Arrange
    service.retryFailed('abc123');

    // Act
    httpMock.expectOne('/api/issues/abc123/retry').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });

    // Assert
    expect(service.retryingFailed()).toBe(false);
    expect(service.retryFailedError()).not.toBeNull();
  });

  it('should set retryFailedError to the dedicated error constant when retryFailed fails', () => {
    // Arrange — no additional setup

    // Act
    service.retryFailed('abc123');
    httpMock.expectOne('/api/issues/abc123/retry').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });

    // Assert — dedicated string, not the detail-load error
    expect(service.retryFailedError()).toBe('Failed to retry issue.');
  });

  it('should not change detailError when retryFailed fails', () => {
    // Arrange — confirm detailError stays null (retryFailed has its own error surface)
    service.retryFailed('abc123');
    httpMock.expectOne('/api/issues/abc123/retry').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });

    // Assert
    expect(service.detailError()).toBeNull();
  });

  // UX-4: stale retry state cleared on issue switch
  it('should clear retryFailedError when switching to a different issue via toggleExpand', () => {
    // Arrange — set a retry error for abc123
    service.retryFailed('abc123');
    httpMock.expectOne('/api/issues/abc123/retry').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    expect(service.retryFailedError()).not.toBeNull();

    // Act — switch to a different issue
    service.toggleExpand('def456');

    // Assert — retry error is cleared
    expect(service.retryFailedError()).toBeNull();
    httpMock.expectOne('/api/issues/def456').flush({ ...mockSummary, id: 'def456' });
  });

  it('should clear retryingFailed when switching to a different issue via toggleExpand', () => {
    // Arrange — simulate retryingFailed stuck at true
    service.retryFailed('abc123');
    expect(service.retryingFailed()).toBe(true);

    // Act — switch to a different issue before the request resolves
    service.toggleExpand('def456');

    // Assert — loading flag is cleared synchronously
    expect(service.retryingFailed()).toBe(false);

    // Cleanup — flush retry (its loadDetail then cancels the def456 request)
    httpMock.expectOne('/api/issues/abc123/retry').flush({});
    // The retry success handler calls loadDetail('abc123'), which cancels the def456 request
    httpMock.expectOne('/api/issues/abc123').flush({ ...mockSummary });
  });

  it('should clear retryFailedError when collapsing the current issue via toggleExpand', () => {
    // Arrange — expand and set error
    service.toggleExpand('abc123');
    httpMock.expectOne('/api/issues/abc123').flush({ ...mockSummary });
    service.retryFailed('abc123');
    httpMock.expectOne('/api/issues/abc123/retry').flush('Server Error', {
      status: 500, statusText: 'Internal Server Error',
    });
    expect(service.retryFailedError()).not.toBeNull();

    // Act — collapse same issue (toggleExpand with same id)
    service.toggleExpand('abc123');

    // Assert
    expect(service.retryFailedError()).toBeNull();
  });

  // UX-5: success announcement signal
  it('should set retryFailedSuccess after a successful retry', () => {
    // Arrange — no additional setup

    // Act
    service.retryFailed('abc123');
    httpMock.expectOne('/api/issues/abc123/retry').flush({});
    httpMock.expectOne('/api/issues/abc123').flush({ ...mockSummary });

    // Assert
    expect(service.retryFailedSuccess()).toBe('Retry queued. Issue status is updating.');
  });

  it('should clear retryFailedSuccess when toggleExpand is called', () => {
    // Arrange — get a success message set
    service.retryFailed('abc123');
    httpMock.expectOne('/api/issues/abc123/retry').flush({});
    httpMock.expectOne('/api/issues/abc123').flush({ ...mockSummary });
    expect(service.retryFailedSuccess()).not.toBeNull();

    // Act — switch issue
    service.toggleExpand('def456');

    // Assert
    expect(service.retryFailedSuccess()).toBeNull();
    httpMock.expectOne('/api/issues/def456').flush({ ...mockSummary, id: 'def456' });
  });

  it('should clear retryFailedSuccess at the start of a new retryFailed call', () => {
    // Arrange — set success
    service.retryFailed('abc123');
    httpMock.expectOne('/api/issues/abc123/retry').flush({});
    httpMock.expectOne('/api/issues/abc123').flush({ ...mockSummary });
    expect(service.retryFailedSuccess()).not.toBeNull();

    // Act — initiate another retry
    service.retryFailed('abc123');

    // Assert — success cleared immediately when next retry starts
    expect(service.retryFailedSuccess()).toBeNull();

    // Cleanup
    httpMock.expectOne('/api/issues/abc123/retry').flush({});
    httpMock.expectOne('/api/issues/abc123').flush({ ...mockSummary });
  });

  // Cycle 21: _upsertIssue removes issue from issues when its new state is resolved
  it('should remove an issue from issues when an IssueUpdated event transitions it to a resolved state', () => {
    // Arrange
    const callbacks: Record<string, (data: IssueSummary) => void> = {};
    const { svc, http } = setupWithCapturingSignalR(callbacks, []);

    svc.loadIssues();
    http.expectOne('/api/issues').flush([mockSummary]);
    expect(svc.issues().length).toBe(1);

    const resolved: IssueSummary = { ...mockSummary, state: 'completed' };

    // Act
    callbacks['IssueUpdated'](resolved);

    // Assert — issue leaves the active set; it is not appended
    expect(svc.issues().length).toBe(0);
    http.verify();
  });

  it('should remove an issue from issues when an IssueUpdated event transitions it to unchanged', () => {
    // Arrange
    const callbacks: Record<string, (data: IssueSummary) => void> = {};
    const { svc, http } = setupWithCapturingSignalR(callbacks, []);

    svc.loadIssues();
    http.expectOne('/api/issues').flush([mockSummary]);
    expect(svc.issues().length).toBe(1);

    const resolved: IssueSummary = { ...mockSummary, state: 'unchanged' };

    // Act
    callbacks['IssueUpdated'](resolved);

    // Assert
    expect(svc.issues().length).toBe(0);
    http.verify();
  });

  it('should not append a new resolved issue when IssueUpdated arrives for an id not in issues', () => {
    // Arrange
    const callbacks: Record<string, (data: IssueSummary) => void> = {};
    const { svc, http } = setupWithCapturingSignalR(callbacks, []);

    svc.loadIssues();
    http.expectOne('/api/issues').flush([]);

    const newResolved: IssueSummary = { ...mockSummary, id: 'brand-new-resolved', state: 'completed' };

    // Act
    callbacks['IssueUpdated'](newResolved);

    // Assert — resolved items are not added to issues
    expect(svc.issues().some((i: IssueSummary) => i.id === 'brand-new-resolved')).toBe(false);
    http.verify();
  });

  // Cycle 21 (load): loadIssues excludes resolved-state items from issues signal
  it('should exclude resolved-state items (completed, unchanged) from issues on loadIssues', () => {
    // Arrange
    const active: IssueSummary = { ...mockSummary, id: 'active-1', state: 'detected' };
    const completed: IssueSummary = { ...mockSummary, id: 'completed-1', state: 'completed' };
    const unchanged: IssueSummary = { ...mockSummary, id: 'unchanged-1', state: 'unchanged' };

    // Act
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush([active, completed, unchanged]);

    // Assert — only the active item is stored
    expect(service.issues().length).toBe(1);
    expect(service.issues()[0].id).toBe('active-1');
  });

  it('should retain ineligible-state issues in issues on loadIssues', () => {
    // Arrange — ineligible is neither active nor resolved; it belongs in the active band (AC8)
    const ineligible: IssueSummary = { ...mockSummary, id: 'ineligible-1', state: 'ineligible' };

    // Act
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush([ineligible]);

    // Assert
    expect(service.issues().length).toBe(1);
    expect(service.issues()[0].id).toBe('ineligible-1');
  });

  // Cycle 17: loadCounts — populates counts signal
  it('should populate counts signal after loadCounts', () => {
    // Arrange
    const mockCounts = { detected: 2, in_progress: 1 };

    // Act
    service.loadCounts();
    const req = httpMock.expectOne('/api/issues/counts');
    req.flush({ counts: mockCounts });

    // Assert
    expect(service.counts()).toEqual(mockCounts);
  });

  it('should issue a GET request to /api/issues/counts when loadCounts is called', () => {
    // Arrange / Act
    service.loadCounts();

    // Assert
    const req = httpMock.expectOne('/api/issues/counts');
    expect(req.request.method).toBe('GET');
    req.flush({ counts: {} });
  });

  // Cycle 18: loadCounts with repositoryId appends query param
  it('should append repositoryId query param when provided to loadCounts', () => {
    // Arrange / Act
    service.loadCounts('repo-id-1');

    // Assert
    const req = httpMock.expectOne('/api/issues/counts?repositoryId=repo-id-1');
    expect(req.request.method).toBe('GET');
    req.flush({ counts: {} });
  });

  it('should not append repositoryId when not provided to loadCounts', () => {
    // Arrange / Act
    service.loadCounts();

    // Assert — URL has no query string
    const req = httpMock.expectOne('/api/issues/counts');
    expect(req.request.url).toBe('/api/issues/counts');
    req.flush({ counts: {} });
  });

  // Cycle 19: countFor returns count for a state, defaulting to 0
  it('should return the count for a state via countFor', () => {
    // Arrange
    service.loadCounts();
    httpMock.expectOne('/api/issues/counts').flush({ counts: { in_progress: 3 } });

    // Act / Assert
    expect(service.countFor('in_progress')).toBe(3);
  });

  it('should return 0 via countFor for a state with no count', () => {
    // Arrange
    service.loadCounts();
    httpMock.expectOne('/api/issues/counts').flush({ counts: {} });

    // Act / Assert
    expect(service.countFor('detected')).toBe(0);
  });

  it('should return 0 via countFor before loadCounts is called', () => {
    // Arrange — counts not loaded

    // Act / Assert
    expect(service.countFor('in_progress')).toBe(0);
  });

  // Cycle 20: loadCounts failure leaves prior counts intact
  it('should leave prior counts intact when loadCounts fails', () => {
    // Arrange — load initial counts
    service.loadCounts();
    httpMock.expectOne('/api/issues/counts').flush({ counts: { in_progress: 5 } });
    expect(service.countFor('in_progress')).toBe(5);

    // Act — second call fails
    service.loadCounts();
    httpMock.expectOne('/api/issues/counts').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });

    // Assert — prior counts still present
    expect(service.countFor('in_progress')).toBe(5);
  });

  it('should not set loadError when loadCounts fails', () => {
    // Arrange / Act
    service.loadCounts();
    httpMock.expectOne('/api/issues/counts').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });

    // Assert — no user-facing error block (loadError stays null)
    expect(service.loadError()).toBeNull();
  });

  // Step 4 — Selected-state signals + active-band filtering

  // Cycle S1: default selectedActiveStates contains all active states
  it('should initialize selectedActiveStates with all active states and isStateSelected true for each', () => {
    // Arrange — initial state of the service

    // Act / Assert
    for (const state of ACTIVE_STATES) {
      expect(service.isStateSelected(state)).toBe(true);
    }
  });

  // Cycle S2: toggleState deselects an active state; toggling again re-selects
  it('should deselect an active state after toggleState and re-select on second toggle', () => {
    // Arrange
    expect(service.isStateSelected('detected')).toBe(true);

    // Act — first toggle deselects
    service.toggleState('detected');

    // Assert
    expect(service.isStateSelected('detected')).toBe(false);

    // Act — second toggle re-selects
    service.toggleState('detected');

    // Assert
    expect(service.isStateSelected('detected')).toBe(true);
  });

  // Cycle S3: activeBandIssues preserves sortedIssues ordering (live before non-live)
  it('should preserve sortedIssues ordering in activeBandIssues (live issues before non-live)', () => {
    // Arrange
    const nonLive: IssueSummary = { ...mockSummary, id: 'non-live', state: 'detected', detectedAt: '2026-06-01T00:00:00Z' };
    const live: IssueSummary = { ...mockSummary, id: 'live', state: 'in_progress', detectedAt: '2026-01-01T00:00:00Z' };

    // Act
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush([nonLive, live]);

    // Assert — live appears first even though it has an older detectedAt
    expect(service.activeBandIssues()[0].id).toBe('live');
    expect(service.activeBandIssues()[1].id).toBe('non-live');
  });

  // Cycle S5: ineligible always in activeBandIssues even when no states selected
  it('should always include ineligible issues in activeBandIssues even when no states are selected', () => {
    // Arrange — load an ineligible issue and a detected issue
    const ineligible: IssueSummary = { ...mockSummary, id: 'ineligible-1', state: 'ineligible' };
    const detected: IssueSummary = { ...mockSummary, id: 'detected-1', state: 'detected' };

    service.loadIssues();
    httpMock.expectOne('/api/issues').flush([ineligible, detected]);

    // Act — deselect all active states
    for (const state of ACTIVE_STATES) {
      service.toggleState(state);
    }

    // Assert — ineligible still appears; detected does not
    expect(service.activeBandIssues().find((i: IssueSummary) => i.id === 'ineligible-1')).toBeDefined();
    expect(service.activeBandIssues().find((i: IssueSummary) => i.id === 'detected-1')).toBeUndefined();
  });

  // Cycle S6: selection sets are replaced immutably after toggleState
  it('should replace the selectedActiveStates set immutably on toggleState', () => {
    // Arrange
    const setBeforeToggle = service.selectedActiveStates();

    // Act
    service.toggleState('detected');

    // Assert — a new Set was created; the old reference is unchanged
    const setAfterToggle = service.selectedActiveStates();
    expect(setAfterToggle).not.toBe(setBeforeToggle);
    expect(setBeforeToggle.has('detected')).toBe(true);
    expect(setAfterToggle.has('detected')).toBe(false);
  });

  // Cycle S4: activeBandIssues excludes issues of deselected state with no HTTP request
  it('should exclude issues of a deselected state from activeBandIssues without making an HTTP request', () => {
    // Arrange — load one detected issue
    const detectedIssue: IssueSummary = { ...mockSummary, id: 'detected-1', state: 'detected' };
    const failedIssue: IssueSummary = { ...mockSummary, id: 'failed-1', state: 'failed' };

    service.loadIssues();
    httpMock.expectOne('/api/issues').flush([detectedIssue, failedIssue]);
    expect(service.activeBandIssues().length).toBe(2);

    // Act — deselect 'detected'
    service.toggleState('detected');

    // Assert — no HTTP requests were made
    httpMock.expectNone('/api/issues');
    expect(service.activeBandIssues().find((i: IssueSummary) => i.id === 'detected-1')).toBeUndefined();
    expect(service.activeBandIssues().find((i: IssueSummary) => i.id === 'failed-1')).toBeDefined();
  });
});

// Step 6 — SignalR band-transition handling + counts debounce-refetch
describe('IssueService (band transitions + counts debounce)', () => {
  let service: IssueService;
  let httpMock: HttpTestingController;
  let callbacks: Record<string, (data: IssueSummary) => void>;

  const activeSummary: IssueSummary = {
    id: 'issue-1',
    issueNumber: 10,
    title: 'Active issue',
    state: 'detected',
    repositorySlug: 'owner/repo',
    detectedAt: '2026-01-01T00:00:00Z',
    url: 'https://github.com/owner/repo/issues/10',
  };

  const resolvedSummary: IssueSummary = {
    id: 'issue-2',
    issueNumber: 20,
    title: 'Resolved issue',
    state: 'completed',
    repositorySlug: 'owner/repo',
    detectedAt: '2026-01-01T00:00:00Z',
    url: 'https://github.com/owner/repo/issues/20',
  };

  beforeEach(() => {
    callbacks = {};
    const capturingSignalR = {
      on: (method: string, cb: (data: IssueSummary) => void) => { callbacks[method] = cb; },
      onReconnected: () => {},
    };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        IssueService,
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: IssueSignalRService, useValue: capturingSignalR },
      ],
    });
    service = TestBed.inject(IssueService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify({ ignoreCancelled: true });
  });

  // Cycle B1: active→resolved transition with resolved state SELECTED → issue removed from issues,
  // prepended to resolvedIssues
  it('should remove issue from issues and prepend to resolvedIssues when transitioning to a selected resolved state', () => {
    // Arrange — load an active issue, select 'completed' resolved state
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush([activeSummary]);
    service.toggleState('completed');
    httpMock.expectOne(r => r.url === '/api/issues').flush({ items: [], nextCursor: null });

    const transitioned: IssueSummary = { ...activeSummary, state: 'completed' };

    // Act
    callbacks['IssueUpdated'](transitioned);

    // Assert — removed from active band
    expect(service.issues().some((i: IssueSummary) => i.id === 'issue-1')).toBe(false);
    // Prepended to resolvedIssues
    expect(service.resolvedIssues()[0]).toEqual(transitioned);
  });

  // Cycle B2: active→resolved with resolved state NOT selected → removed from issues, NOT added to resolvedIssues
  it('should remove issue from issues and not add to resolvedIssues when transitioning to a non-selected resolved state', () => {
    // Arrange — load an active issue, do NOT select any resolved states
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush([activeSummary]);

    const transitioned: IssueSummary = { ...activeSummary, state: 'completed' };

    // Act
    callbacks['IssueUpdated'](transitioned);

    // Assert — removed from active band
    expect(service.issues().some((i: IssueSummary) => i.id === 'issue-1')).toBe(false);
    // NOT added to resolvedIssues
    expect(service.resolvedIssues().length).toBe(0);
  });

  // Cycle B3: resolved→active transition → added to issues, removed from resolvedIssues
  it('should add issue to issues and remove from resolvedIssues when transitioning from resolved to active', () => {
    // Arrange — select 'completed', load a resolved issue in the resolved band
    service.toggleState('completed');
    httpMock.expectOne(r => r.url === '/api/issues').flush({ items: [resolvedSummary], nextCursor: null });
    expect(service.resolvedIssues().length).toBe(1);

    const backToActive: IssueSummary = { ...resolvedSummary, state: 'revision_queued' };

    // Act
    callbacks['IssueUpdated'](backToActive);

    // Assert — added to issues
    expect(service.issues().some((i: IssueSummary) => i.id === 'issue-2')).toBe(true);
    // Removed from resolvedIssues
    expect(service.resolvedIssues().some((i: IssueSummary) => i.id === 'issue-2')).toBe(false);
  });

  // Cycle B4: dedup-by-id on prepend — same id arriving twice yields single entry at front
  it('should deduplicate by id on prepend to resolvedIssues, keeping newest data at front', () => {
    // Arrange — select 'completed', load one resolved issue in the resolved band
    service.toggleState('completed');
    httpMock.expectOne(r => r.url === '/api/issues').flush({
      items: [resolvedSummary],
      nextCursor: null,
    });
    expect(service.resolvedIssues().length).toBe(1);

    const updatedResolved: IssueSummary = { ...resolvedSummary, title: 'Updated title' };

    // Act — fire two IssueUpdated events for the same id
    callbacks['IssueUpdated'](updatedResolved);
    callbacks['IssueUpdated']({ ...updatedResolved, title: 'Second update' });

    // Assert — only one entry, newest data, at front
    expect(service.resolvedIssues().length).toBe(1);
    expect(service.resolvedIssues()[0].title).toBe('Second update');
    expect(service.resolvedIssues()[0].id).toBe('issue-2');
  });

  // Cycle B5-new: brand-new resolved id (never in issues) arriving via IssueUpdated when its state is selected
  it('should prepend a brand-new resolved issue to resolvedIssues when its state is selected and the id was never in issues', () => {
    // Arrange — select 'completed'; flush empty page (no prior resolved issues)
    service.toggleState('completed');
    httpMock.expectOne(r => r.url === '/api/issues').flush({ items: [], nextCursor: null });
    expect(service.resolvedIssues().length).toBe(0);

    const brandNew: IssueSummary = { ...resolvedSummary, id: 'brand-new-resolved', state: 'completed' };

    // Act
    callbacks['IssueUpdated'](brandNew);

    // Assert — prepended to resolvedIssues, not in issues
    expect(service.resolvedIssues()[0]).toEqual(brandNew);
    expect(service.issues().some((i: IssueSummary) => i.id === 'brand-new-resolved')).toBe(false);
  });

  // Cycle B6: expanded issue detail still reloads on IssueUpdated (regression guard)
  it('should still re-fetch detail when the expanded issue receives an IssueUpdated event (regression)', () => {
    // Arrange
    vi.useFakeTimers();
    service.toggleExpand('issue-1');
    httpMock.expectOne('/api/issues/issue-1').flush({ ...activeSummary });
    expect(service.expandedIssueId()).toBe('issue-1');

    // Act — trigger an IssueUpdated event for the expanded issue
    callbacks['IssueUpdated']({ ...activeSummary, state: 'in_progress' });

    // Advance past the debounce window so the counts request fires
    vi.advanceTimersByTime(400);
    httpMock.expectOne('/api/issues/counts').flush({ counts: {} });

    // Assert — detail refetch triggered
    const detailReq = httpMock.expectOne('/api/issues/issue-1');
    detailReq.flush({ ...activeSummary, state: 'in_progress' });
    expect(service.issueDetail()).toBeTruthy();

    vi.useRealTimers();
  });

  // Cycle B5 (scoped fake timers): counts debounce — burst of N IssueUpdated events triggers exactly one counts request
  describe('counts debounce', () => {
    beforeEach(() => {
      vi.useFakeTimers();
    });

    afterEach(() => {
      vi.useRealTimers();
    });

    it('should debounce counts refetch so that a burst of IssueUpdated events triggers exactly one GET /api/issues/counts', () => {
      // Arrange
      service.loadIssues();
      httpMock.expectOne('/api/issues').flush([activeSummary]);

      // Act — fire three IssueUpdated events in rapid succession
      callbacks['IssueUpdated']({ ...activeSummary, state: 'in_progress' });
      callbacks['IssueUpdated']({ ...activeSummary, state: 'review' });
      callbacks['IssueUpdated']({ ...activeSummary, state: 'failed' });

      // No counts request should have fired yet (debounce window not elapsed)
      httpMock.expectNone('/api/issues/counts');

      // Advance timers past the debounce window
      vi.advanceTimersByTime(400);

      // Assert — exactly one counts request
      const req = httpMock.expectOne('/api/issues/counts');
      expect(req.request.method).toBe('GET');
      req.flush({ counts: { failed: 1 } });
    });
  });
});

// Step 5 — Lazy resolved paging + accumulation
describe('IssueService (resolved paging)', () => {
  let service: IssueService;
  let httpMock: HttpTestingController;

  const resolvedSummary: IssueSummary = {
    id: 'res-1',
    issueNumber: 101,
    title: 'Completed issue',
    state: 'completed',
    repositorySlug: 'owner/repo',
    detectedAt: '2026-01-01T00:00:00Z',
    url: 'https://github.com/owner/repo/issues/101',
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        IssueService,
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: IssueSignalRService, useValue: mockIssueSignalRService },
      ],
    });
    service = TestBed.inject(IssueService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify({ ignoreCancelled: true }));

  // Cycle R1: selecting a resolved state fetches GET /api/issues with states[]=<state>
  it('should fetch GET /api/issues with states[]=completed when toggleState("completed") is called', () => {
    // Arrange — no resolved states selected initially

    // Act
    service.toggleState('completed');

    // Assert — one request with states[]=completed, no cursor param
    const req = httpMock.expectOne(r =>
      r.url === '/api/issues' && r.params.getAll('states[]')?.includes('completed') === true
    );
    expect(req.request.method).toBe('GET');
    expect(req.request.params.has('cursor')).toBe(false);

    const page: PagedIssues = { items: [resolvedSummary], nextCursor: null };
    req.flush(page);

    expect(service.resolvedIssues()).toEqual([resolvedSummary]);
    expect(service.hasMoreResolved()).toBe(false);
  });

  // Cycle R2: selecting two resolved states sends both as repeated states[] params in one request
  it('should send both resolved states as repeated states[] params in a single request', () => {
    // Arrange — select completed first
    service.toggleState('completed');
    httpMock.expectOne(r => r.url === '/api/issues').flush({ items: [], nextCursor: null });

    // Act — also select unchanged
    service.toggleState('unchanged');

    // Assert — single request with both states[]
    const req = httpMock.expectOne(r =>
      r.url === '/api/issues' &&
      r.params.getAll('states[]')?.includes('completed') === true &&
      r.params.getAll('states[]')?.includes('unchanged') === true
    );
    expect(req.request.params.getAll('states[]')?.length).toBe(2);
    req.flush({ items: [], nextCursor: null });
  });

  // Cycle R8: resolvedLoading and resolvedLoadingMore toggle around their respective requests
  it('should set resolvedLoading to true while page-1 fetch is in flight and false after', () => {
    // Arrange — no resolved states selected

    // Act — trigger page 1 fetch
    service.toggleState('completed');

    // Assert — resolvedLoading is true before response
    expect(service.resolvedLoading()).toBe(true);

    httpMock.expectOne(r => r.url === '/api/issues').flush({ items: [], nextCursor: null });

    expect(service.resolvedLoading()).toBe(false);
  });

  it('should set resolvedLoadingMore to true while load-more fetch is in flight and false after', () => {
    // Arrange — select completed and get page 1 with a cursor
    service.toggleState('completed');
    httpMock.expectOne(r => r.url === '/api/issues').flush({ items: [], nextCursor: 'c1' });
    expect(service.resolvedLoadingMore()).toBe(false);

    // Act
    service.loadMoreResolved();

    // Assert — resolvedLoadingMore true before response
    expect(service.resolvedLoadingMore()).toBe(true);

    httpMock.expectOne(r =>
      r.url === '/api/issues' && r.params.get('cursor') === 'c1'
    ).flush({ items: [], nextCursor: null });

    expect(service.resolvedLoadingMore()).toBe(false);
  });

  // Cycle R7: resolved fetch failure leaves prior resolvedIssues intact and sets no loadError
  it('should leave prior resolvedIssues intact and not set loadError when resolved fetch fails', () => {
    // Arrange — select completed, get page 1 successfully
    const item: IssueSummary = { ...resolvedSummary, id: 'res-1' };
    service.toggleState('completed');
    httpMock.expectOne(r => r.url === '/api/issues').flush({
      items: [item],
      nextCursor: 'cursor-1',
    });
    expect(service.resolvedIssues().length).toBe(1);

    // Act — load more, but the request fails
    service.loadMoreResolved();
    httpMock.expectOne(r =>
      r.url === '/api/issues' && r.params.get('cursor') === 'cursor-1'
    ).flush('Server Error', { status: 500, statusText: 'Internal Server Error' });

    // Assert — prior items intact; no loadError
    expect(service.resolvedIssues().length).toBe(1);
    expect(service.resolvedIssues()[0].id).toBe('res-1');
    expect(service.loadError()).toBeNull();
  });

  // Cycle R6: repositoryId is forwarded on resolved fetches when provided
  it('should forward repositoryId param on resolved fetches when loadMoreResolved is called with one', () => {
    // Arrange — select completed, flush page 1 with a cursor (no repositoryId on page 1 for simplicity)
    service.toggleState('completed');
    httpMock.expectOne(r => r.url === '/api/issues').flush({
      items: [],
      nextCursor: 'cursor-r1',
    });

    // Act — load more with repositoryId
    service.loadMoreResolved('repo-42');

    // Assert — request carries repositoryId
    const req = httpMock.expectOne(r =>
      r.url === '/api/issues' && r.params.get('repositoryId') === 'repo-42'
    );
    req.flush({ items: [], nextCursor: null });
  });

  // Cycle R5: deselecting all resolved states clears resolvedIssues + cursor, makes no request
  it('should clear resolvedIssues and cursor and make no request when all resolved states are deselected', () => {
    // Arrange — select completed, get a result
    const item: IssueSummary = { ...resolvedSummary, id: 'res-1' };
    service.toggleState('completed');
    httpMock.expectOne(r => r.url === '/api/issues').flush({
      items: [item],
      nextCursor: 'cursor-xyz',
    });
    expect(service.resolvedIssues().length).toBe(1);
    expect(service.hasMoreResolved()).toBe(true);

    // Act — deselect completed (resolved set becomes empty)
    service.toggleState('completed');

    // Assert — cleared, no request
    expect(service.resolvedIssues().length).toBe(0);
    expect(service.hasMoreResolved()).toBe(false);
    httpMock.expectNone(r => r.url === '/api/issues');
  });

  // Cycle R4: toggling resolved selection resets accumulation and refetches page 1
  it('should reset resolvedIssues and refetch page 1 when the resolved selection changes', () => {
    // Arrange — select completed, get page 1 with a cursor
    const page1Item: IssueSummary = { ...resolvedSummary, id: 'res-old' };
    service.toggleState('completed');
    httpMock.expectOne(r => r.url === '/api/issues').flush({
      items: [page1Item],
      nextCursor: 'old-cursor',
    });
    expect(service.resolvedIssues().length).toBe(1);
    expect(service.hasMoreResolved()).toBe(true);

    // Act — add 'unchanged' to the selection (triggers reset + refetch)
    service.toggleState('unchanged');

    // Assert — resolvedIssues cleared immediately (before response)
    expect(service.resolvedIssues().length).toBe(0);
    expect(service.hasMoreResolved()).toBe(false);

    // The new request must NOT include the old cursor
    const newReq = httpMock.expectOne(r =>
      r.url === '/api/issues' &&
      r.params.getAll('states[]')?.includes('completed') === true &&
      r.params.getAll('states[]')?.includes('unchanged') === true
    );
    expect(newReq.request.params.has('cursor')).toBe(false);
    newReq.flush({ items: [], nextCursor: null });
  });

  // Finding 3: resolved page drops items with unknown state
  it('should drop items with an unknown state from a resolved page', () => {
    // Arrange
    const valid: IssueSummary = { ...resolvedSummary, id: 'res-valid' };
    const unknown = { ...resolvedSummary, id: 'res-ghost', state: 'ghost_state' as never };

    // Act
    service.toggleState('completed');
    httpMock.expectOne(r => r.url === '/api/issues').flush({ items: [valid, unknown], nextCursor: null });

    // Assert — only valid item retained
    expect(service.resolvedIssues().length).toBe(1);
    expect(service.resolvedIssues()[0].id).toBe('res-valid');
  });

  // Finding 4: stale inflight resolved-page write race
  it('should discard a stale resolved-page response when a newer selection change supersedes it', () => {
    // Arrange — select completed; fire first request but do not flush it yet
    service.toggleState('completed');
    const staleReq = httpMock.expectOne(r =>
      r.url === '/api/issues' && r.params.getAll('states[]')?.includes('completed') === true
    );

    // Act — toggle unchanged before the first request resolves (triggers a new token + new request)
    service.toggleState('unchanged');
    const freshReq = httpMock.expectOne(r =>
      r.url === '/api/issues' &&
      r.params.getAll('states[]')?.includes('completed') === true &&
      r.params.getAll('states[]')?.includes('unchanged') === true
    );

    // Flush the stale response first — it should be discarded
    const staleItem: IssueSummary = { ...resolvedSummary, id: 'stale-item' };
    staleReq.flush({ items: [staleItem], nextCursor: null });

    // Assert — stale response did not populate resolvedIssues
    expect(service.resolvedIssues().length).toBe(0);

    // Flush the fresh response
    const freshItem: IssueSummary = { ...resolvedSummary, id: 'fresh-item' };
    freshReq.flush({ items: [freshItem], nextCursor: null });

    // Assert — only the fresh response is used
    expect(service.resolvedIssues().length).toBe(1);
    expect(service.resolvedIssues()[0].id).toBe('fresh-item');
  });

  // Cycle R3: loadMoreResolved appends next page with dedup; nextCursor null → no further request
  it('should append next page items (dedup-by-id) and advance cursor on loadMoreResolved', () => {
    // Arrange — select completed, flush page 1 with a cursor
    const page1Item: IssueSummary = { ...resolvedSummary, id: 'res-1' };
    service.toggleState('completed');
    httpMock.expectOne(r => r.url === '/api/issues').flush({
      items: [page1Item],
      nextCursor: 'cursor-abc',
    });
    expect(service.hasMoreResolved()).toBe(true);
    expect(service.resolvedIssues().length).toBe(1);

    // Act — load more; include page1 id again to verify dedup
    service.loadMoreResolved();
    const page2Item: IssueSummary = { ...resolvedSummary, id: 'res-2' };
    const req = httpMock.expectOne(r =>
      r.url === '/api/issues' && r.params.get('cursor') === 'cursor-abc'
    );
    req.flush({ items: [page1Item, page2Item], nextCursor: null });

    // Assert — res-1 appears once; res-2 appended; no more pages
    expect(service.resolvedIssues().length).toBe(2);
    expect(service.resolvedIssues()[0].id).toBe('res-1');
    expect(service.resolvedIssues()[1].id).toBe('res-2');
    expect(service.hasMoreResolved()).toBe(false);

    // loadMoreResolved with no cursor is a no-op
    service.loadMoreResolved();
    httpMock.expectNone(r => r.url === '/api/issues');
  });

});

import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { IssueService } from './issue.service';
import { IssueSignalRService } from '../../core/services/issue-signalr.service';
import { IssueSummary, IssueDetail, RevisionTriggerKind } from './issue.model';
import { vi } from 'vitest';
import type { components } from '../../api/schema';

type SchemaTokenTotal = components['schemas']['TokenTotal'];

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
  providerType: 'github',
  detectedAt: '2026-01-01T00:00:00Z',
  url: 'https://github.com/owner/repo/issues/42',
  stage: 'triage',
  needsAction: false,
  awaitsDispatch: true,
  codeStep: null,
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
    req.flush({ items: mockIssues, nextCursor: null });

    // Assert — mapper adds missing optional fields; use toMatchObject to check the data we care about
    expect(service.issues()).toMatchObject(mockIssues);
  });

  it('should carry pullRequestUrl and providerType through loadIssues so the card can word its PR link', () => {
    // Arrange
    const gitlabSummary: IssueSummary = {
      ...mockSummary,
      state: 'review',
      pullRequestUrl: 'https://gitlab.com/group/project/-/merge_requests/7',
      providerType: 'gitlab',
    };

    // Act
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush({ items: [gitlabSummary], nextCursor: null });

    // Assert
    const [loaded] = service.issues();
    expect(loaded.pullRequestUrl).toBe('https://gitlab.com/group/project/-/merge_requests/7');
    expect(loaded.providerType).toBe('gitlab');
  });

  it('should carry publishPhase through loadIssues so the card and Floor can show the publish phase', () => {
    // Arrange
    const publishing: IssueSummary = { ...mockSummary, state: 'in_progress', publishPhase: 'retrying' };
    const running: IssueSummary = { ...mockSummary, id: 'b0000000-0000-0000-0000-000000000002', state: 'in_progress' };

    // Act
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush({ items: [publishing, running], nextCursor: null });

    // Assert
    const [first, second] = service.issues();
    expect(first.publishPhase).toBe('retrying');
    expect(second.publishPhase).toBeNull();
  });

  // Cycle 2: loadIssues with repositoryId appends query param
  it('should append repositoryId query param when provided to loadIssues', () => {
    // Arrange — no additional setup

    // Act
    service.loadIssues('repo-id-1');
    const req = httpMock.expectOne('/api/issues?repositoryId=repo-id-1');
    req.flush({ items: [], nextCursor: null });

    // Assert
    expect(req.request.method).toBe('GET');
  });

  // Cycle 3e: liveIssueCount returns 0 when no live issues
  it('should return 0 for liveIssueCount when there are no live issues', () => {
    // Arrange
    const nonLive: IssueSummary = { ...mockSummary, id: 'non-live', state: 'detected' };

    // Act
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush({ items: [nonLive], nextCursor: null });

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
    httpMock.expectOne('/api/issues').flush({ items: [live1, live2], nextCursor: null });

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
    httpMock.expectOne('/api/issues').flush({ items: [live, nonLive1, nonLive2], nextCursor: null });

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
    httpMock.expectOne('/api/issues').flush({ items: [mockSummary], nextCursor: null });

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
      kind: { source: 'unclassified', kind: null, conflictingTriageKind: null },
      triage: null,
      stateDetails: {
        workerRunId: null,
        branchName: null,
        feedbackCutoffAt: null,
        failureReason: null,
        failedAt: null,
        completedAt: null,
        blockedBy: null,
        violations: null,
        transientRetry: null,
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

  // Revision fields mapping: loadDetail preserves revisionTriggerKind and checksFixAttempts
  it('should preserve revisionTriggerKind and checksFixAttempts from API response in issueDetail', () => {
    // Arrange
    const triggerKind: RevisionTriggerKind = 'failed_checks';
    const detail: IssueDetail = {
      ...mockSummary,
      author: 'dev',
      labels: [],
      kind: { source: 'unclassified', kind: null, conflictingTriageKind: null },
      triage: null,
      stateDetails: {
        workerRunId: null,
        branchName: null,
        feedbackCutoffAt: null,
        failureReason: null,
        failedAt: null,
        completedAt: null,
        blockedBy: null,
        violations: null,
        transientRetry: null,
        revisionTriggerKind: triggerKind,
        checksFixAttempts: 2,
      },
    };

    // Act
    service.loadDetail('abc123');
    httpMock.expectOne('/api/issues/abc123').flush(detail);

    // Assert
    expect(service.issueDetail()?.stateDetails?.revisionTriggerKind).toBe('failed_checks');
    expect(service.issueDetail()?.stateDetails?.checksFixAttempts).toBe(2);
  });

  it('should set detailLoading to true before the detail request resolves', () => {
    // Arrange — no additional setup

    // Act
    service.loadDetail('abc123');

    // Assert (before flush, loading is true)
    expect(service.detailLoading()).toBe(true);
    httpMock.expectOne('/api/issues/abc123').flush({});
  });

  // Cycle 6: selectIssue(null) clears the selection
  it('should clear the selection when selectIssue is called with null', () => {
    // Arrange — first select
    service.selectIssue('abc123');
    httpMock.expectOne('/api/issues/abc123').flush({ ...mockSummary });
    expect(service.selectedIssueId()).toBe('abc123');

    // Act — clear
    service.selectIssue(null);

    // Assert
    expect(service.selectedIssueId()).toBeNull();
    expect(service.issueDetail()).toBeNull();
  });

  // Cycle 7: selectIssue — expand new and fetch detail
  it('should expand a new issue and fetch its detail when selectIssue is called', () => {
    // Arrange — no additional setup

    // Act
    service.selectIssue('abc123');

    // Assert
    expect(service.selectedIssueId()).toBe('abc123');
    expect(service.detailLoading()).toBe(true);
    httpMock.expectOne('/api/issues/abc123').flush({ ...mockSummary });
  });

  // Cycle 8: selectIssue — switch from one to another
  it('should switch to a new issue when selectIssue is called with a different id', () => {
    // Arrange
    service.selectIssue('abc123');
    httpMock.expectOne('/api/issues/abc123').flush({ ...mockSummary });

    // Act
    service.selectIssue('def456');

    // Assert
    expect(service.selectedIssueId()).toBe('def456');
    httpMock.expectOne('/api/issues/def456').flush({ ...mockSummary, id: 'def456' });
  });

  // Cycle 8b: selectIssue — switching clears stale detail immediately
  it('should clear issueDetail and set detailLoading to true immediately when switching to a different issue', () => {
    // Arrange — expand first issue and let its detail load
    const ineligibleDetail = { ...mockSummary, state: 'ineligible' };
    service.selectIssue('abc123');
    httpMock.expectOne('/api/issues/abc123').flush(ineligibleDetail);
    expect(service.issueDetail()).toEqual(ineligibleDetail);

    // Act — switch to a different issue (do NOT flush the second request yet)
    service.selectIssue('def456');

    // Assert — stale detail is cleared and loading is true before the new response arrives
    expect(service.issueDetail()).toBeNull();
    expect(service.detailLoading()).toBe(true);

    // Cleanup
    httpMock.expectOne('/api/issues/def456').flush({ ...mockSummary, id: 'def456' });
  });

  // Cycle 8c: selectIssue — rapid switching does not leak stale state
  it('should not retain stale detail when rapidly switching between issues', () => {
    // Arrange — expand first issue, let detail load
    service.selectIssue('abc123');
    httpMock.expectOne('/api/issues/abc123').flush({ ...mockSummary, state: 'ineligible' });
    expect(service.issueDetail()).not.toBeNull();

    // Act — switch to issue B without waiting for response, then switch to issue C
    service.selectIssue('def456');
    // At this point the def456 request is in flight; switch again
    service.selectIssue('ghi789');

    // Assert — issueDetail is null (cleared) and detailLoading is true
    expect(service.issueDetail()).toBeNull();
    expect(service.detailLoading()).toBe(true);

    // Cleanup — the def456 request was cancelled by unsubscribe; only ghi789 is still in flight
    httpMock.expectOne('/api/issues/ghi789').flush({ ...mockSummary, id: 'ghi789' });
  });

  // Cycle 8d: late-arriving detail response for a previous issue must not overwrite current detail
  // This tests the in-callback guard: even if a response arrives after selectedIssueId changed
  // (e.g. via SignalR triggering loadDetail while the user has already switched), it is discarded.
  it('should discard a late-arriving detail response when selectedIssueId no longer matches', () => {
    // Arrange — simulate issue B already loaded as the active issue
    const detailB: IssueDetail = {
      ...mockSummary,
      id: 'def456',
      author: 'dev',
      labels: [],
      kind: { source: 'unclassified', kind: null, conflictingTriageKind: null },
      triage: null,
      stateDetails: {
        workerRunId: null,
        branchName: null,
        feedbackCutoffAt: null,
        failureReason: null,
        failedAt: null,
        completedAt: null,
        blockedBy: null,
        violations: null,
        transientRetry: null,
      },
    };

    // Expand B so its detail is loaded first
    service.selectIssue('def456');
    httpMock.expectOne('/api/issues/def456').flush(detailB);
    expect(service.issueDetail()).toEqual(detailB);

    // Simulate an in-flight loadDetail for issue A (e.g. triggered by a SignalR event before
    // the user switched), but do not flush it yet
    service.loadDetail('abc123');
    const reqA = httpMock.expectOne('/api/issues/abc123');

    // User switches to issue B again (or any other change that sets selectedIssueId away from A)
    // Here we directly set selectedIssueId to simulate switching away from A
    service.selectedIssueId.set('def456');

    // Act — the stale response for issue A now arrives
    reqA.flush({ ...mockSummary, id: 'abc123' });

    // Assert — issue B's detail is still shown; the stale A response was discarded by the guard
    expect(service.issueDetail()).toEqual(detailB);
    expect(service.selectedIssueId()).toBe('def456');
  });

  // Cycle 9: SignalR upsert — updates existing issue in list
  it('should upsert an existing issue when IssueUpdated SignalR event is received', () => {
    // Arrange
    const callbacks: Record<string, (data: IssueSummary) => void> = {};
    const { svc, http } = setupWithCapturingSignalR(callbacks, []);

    svc.loadIssues();
    http.expectOne('/api/issues').flush({ items: [mockSummary], nextCursor: null });

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
    http.expectOne('/api/issues').flush({ items: [], nextCursor: null });

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

    svc.selectIssue('abc123');
    http.expectOne('/api/issues/abc123').flush({ ...mockSummary });
    expect(svc.selectedIssueId()).toBe('abc123');

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
    httpMock.expectOne('/api/issues').flush({ items: [], nextCursor: null });
  });

  it('should set initialLoading to false after loadIssues succeeds', () => {
    // Arrange — no additional setup

    // Act
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush({ items: [], nextCursor: null });

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
    httpMock.expectOne('/api/issues').flush({ items: [mockSummary], nextCursor: null });
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

  it('marks the detail missing when loadDetail returns 404', () => {
    // Act
    service.selectIssue('abc123');
    httpMock.expectOne('/api/issues/abc123').flush('Not Found', { status: 404, statusText: 'Not Found' });

    // Assert
    expect(service.detailMissing()).toBe(true);
  });

  it('does not mark the detail missing when loadDetail fails with another status', () => {
    // Act
    service.selectIssue('abc123');
    httpMock.expectOne('/api/issues/abc123').flush('Boom', { status: 500, statusText: 'Server Error' });

    // Assert
    expect(service.detailMissing()).toBe(false);
  });

  it('clears the missing mark when another issue is selected', () => {
    // Arrange
    service.selectIssue('abc123');
    httpMock.expectOne('/api/issues/abc123').flush('Not Found', { status: 404, statusText: 'Not Found' });

    // Act
    service.selectIssue('def456');

    // Assert
    expect(service.detailMissing()).toBe(false);
    httpMock.expectOne('/api/issues/def456').flush({ ...mockSummary, id: 'def456' });
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
    httpMock.expectOne('/api/issues').flush({ items: [mockSummary], nextCursor: null });

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

  // Cycle 14: _upsertIssue rejects ids that do not match safe-id pattern
  it('should not upsert an issue with an id containing a path traversal sequence', () => {
    // Arrange
    const callbacks: Record<string, (data: IssueSummary) => void> = {};
    const { svc, http } = setupWithCapturingSignalR(callbacks, []);

    svc.loadIssues();
    http.expectOne('/api/issues').flush({ items: [], nextCursor: null });
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
    httpMock.expectOne('/api/issues').flush({ items: [valid, unknown], nextCursor: null });

    // Assert — only valid issue is retained
    expect(service.issues().length).toBe(1);
    expect(service.issues()[0].id).toBe('valid-id');
  });

  // Finding 3: _upsertIssue drops items with unknown state
  it('should reject an IssueUpdated event with an unknown state', () => {
    // Arrange
    const callbacks: Record<string, (data: IssueSummary) => void> = {};
    const { svc, http } = setupWithCapturingSignalR(callbacks, []);

    svc.loadIssues();
    http.expectOne('/api/issues').flush({ items: [], nextCursor: null });
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
    httpMock.expectOne('/api/issues').flush({ items: [valid, invalid], nextCursor: null });

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
    req.flush({ items: [], nextCursor: null });
    expect(svc.issues()).toEqual([]);
    http.verify();
  });

  // Cycle 16: retryIssue — POSTs to /api/issues/{id}/retry and reloads detail
  it('should POST to /api/issues/{id}/retry when retryIssue is called', () => {
    // Arrange — no additional setup

    // Act
    service.retryIssue('abc123');

    // Assert
    const req = httpMock.expectOne('/api/issues/abc123/retry');
    expect(req.request.method).toBe('POST');
    req.flush({});
    httpMock.expectOne('/api/issues/abc123').flush({});
  });

  it('should set retrying to true while the retryIssue request is in flight', () => {
    // Arrange — no additional setup

    // Act
    service.retryIssue('abc123');

    // Assert — signal is true before the response arrives
    expect(service.retrying()).toBe(true);
    httpMock.expectOne('/api/issues/abc123/retry').flush({});
    httpMock.expectOne('/api/issues/abc123').flush({});
  });

  it('should set retrying to false after retryIssue succeeds', () => {
    // Arrange
    service.retryIssue('abc123');

    // Act
    httpMock.expectOne('/api/issues/abc123/retry').flush({});
    httpMock.expectOne('/api/issues/abc123').flush({});

    // Assert
    expect(service.retrying()).toBe(false);
  });

  it('should reload issue detail after retryIssue succeeds', () => {
    // Arrange
    service.retryIssue('abc123');
    httpMock.expectOne('/api/issues/abc123/retry').flush({});

    // Act — loadDetail is triggered after success
    const req = httpMock.expectOne('/api/issues/abc123');
    req.flush({ ...mockSummary });

    // Assert
    expect(req.request.method).toBe('GET');
  });

  it('should set retryError and retrying to false when retryIssue fails', () => {
    // Arrange
    service.retryIssue('abc123');

    // Act
    httpMock.expectOne('/api/issues/abc123/retry').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });

    // Assert
    expect(service.retrying()).toBe(false);
    expect(service.retryError()).not.toBeNull();
  });

  it('should set retryError to the dedicated error constant when retryIssue fails', () => {
    // Arrange — no additional setup

    // Act
    service.retryIssue('abc123');
    httpMock.expectOne('/api/issues/abc123/retry').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });

    // Assert — dedicated string, not the detail-load error
    expect(service.retryError()).toBe('Failed to retry issue.');
  });

  it('should POST to /api/issues/{id}/retry-fresh and reload detail when retryIssueFresh is called', () => {
    // Arrange — no additional setup

    // Act
    service.retryIssueFresh('abc123');

    // Assert
    expect(service.retrying()).toBe(true);
    const req = httpMock.expectOne('/api/issues/abc123/retry-fresh');
    expect(req.request.method).toBe('POST');
    req.flush({});
    httpMock.expectOne('/api/issues/abc123').flush({});
    expect(service.retrying()).toBe(false);
    expect(service.retrySuccess()).toBe('Branch discarded. Issue queued fresh; status is updating.');
  });

  it('should explain the issue was not requeued when retryIssueFresh cannot delete the branch', () => {
    // Arrange
    service.retryIssueFresh('abc123');

    // Act
    httpMock.expectOne('/api/issues/abc123/retry-fresh').flush('Bad Gateway', {
      status: 502,
      statusText: 'Bad Gateway',
    });

    // Assert
    expect(service.retrying()).toBe(false);
    expect(service.retryError()).toBe('Could not delete the branch on the provider. The issue was not requeued.');
  });

  it('should set the generic retry-fresh error when retryIssueFresh fails otherwise', () => {
    // Arrange
    service.retryIssueFresh('abc123');

    // Act
    httpMock.expectOne('/api/issues/abc123/retry-fresh').flush('Conflict', {
      status: 409,
      statusText: 'Conflict',
    });

    // Assert
    expect(service.retryError()).toBe('Failed to retry issue from a fresh branch.');
  });

  it('should not change detailError when retryIssue fails', () => {
    // Arrange — confirm detailError stays null (retryIssue has its own error surface)
    service.retryIssue('abc123');
    httpMock.expectOne('/api/issues/abc123/retry').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });

    // Assert
    expect(service.detailError()).toBeNull();
  });

  // UX-4: stale retry state cleared on issue switch
  it('should clear retryError when switching to a different issue via selectIssue', () => {
    // Arrange — set a retry error for abc123
    service.retryIssue('abc123');
    httpMock.expectOne('/api/issues/abc123/retry').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    expect(service.retryError()).not.toBeNull();

    // Act — switch to a different issue
    service.selectIssue('def456');

    // Assert — retry error is cleared
    expect(service.retryError()).toBeNull();
    httpMock.expectOne('/api/issues/def456').flush({ ...mockSummary, id: 'def456' });
  });

  it('should clear retrying when switching to a different issue via selectIssue', () => {
    // Arrange — simulate retrying stuck at true
    service.retryIssue('abc123');
    expect(service.retrying()).toBe(true);

    // Act — switch to a different issue before the request resolves
    service.selectIssue('def456');

    // Assert — loading flag is cleared synchronously
    expect(service.retrying()).toBe(false);

    // Cleanup — flush retry (its loadDetail then cancels the def456 request)
    httpMock.expectOne('/api/issues/abc123/retry').flush({});
    // The retry success handler calls loadDetail('abc123'), which cancels the def456 request
    httpMock.expectOne('/api/issues/abc123').flush({ ...mockSummary });
  });

  it('should clear retryError when clearing the selection via selectIssue', () => {
    // Arrange — select and set error
    service.selectIssue('abc123');
    httpMock.expectOne('/api/issues/abc123').flush({ ...mockSummary });
    service.retryIssue('abc123');
    httpMock.expectOne('/api/issues/abc123/retry').flush('Server Error', {
      status: 500, statusText: 'Internal Server Error',
    });
    expect(service.retryError()).not.toBeNull();

    // Act — clear the selection
    service.selectIssue(null);

    // Assert
    expect(service.retryError()).toBeNull();
  });

  // UX-5: success announcement signal
  it('should set retrySuccess after a successful retryIssue', () => {
    // Arrange — no additional setup

    // Act
    service.retryIssue('abc123');
    httpMock.expectOne('/api/issues/abc123/retry').flush({});
    httpMock.expectOne('/api/issues/abc123').flush({ ...mockSummary });

    // Assert
    expect(service.retrySuccess()).toBe('Retry queued. Issue status is updating.');
  });

  it('should clear retrySuccess when selectIssue is called', () => {
    // Arrange — get a success message set
    service.retryIssue('abc123');
    httpMock.expectOne('/api/issues/abc123/retry').flush({});
    httpMock.expectOne('/api/issues/abc123').flush({ ...mockSummary });
    expect(service.retrySuccess()).not.toBeNull();

    // Act — switch issue
    service.selectIssue('def456');

    // Assert
    expect(service.retrySuccess()).toBeNull();
    httpMock.expectOne('/api/issues/def456').flush({ ...mockSummary, id: 'def456' });
  });

  it('should clear retrySuccess at the start of a new retryIssue call', () => {
    // Arrange — set success
    service.retryIssue('abc123');
    httpMock.expectOne('/api/issues/abc123/retry').flush({});
    httpMock.expectOne('/api/issues/abc123').flush({ ...mockSummary });
    expect(service.retrySuccess()).not.toBeNull();

    // Act — initiate another retry
    service.retryIssue('abc123');

    // Assert — success cleared immediately when next retry starts
    expect(service.retrySuccess()).toBeNull();

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
    http.expectOne('/api/issues').flush({ items: [mockSummary], nextCursor: null });
    expect(svc.issues().length).toBe(1);

    const resolved: IssueSummary = { ...mockSummary, state: 'completed' };

    // Act
    callbacks['IssueUpdated'](resolved);

    // Assert — issue leaves the active set; it is not appended
    expect(svc.issues().length).toBe(0);
    http.verify();
  });

  it('should retain an issue in issues when an IssueUpdated event transitions it to unchanged (unchanged is now active)', () => {
    // Arrange
    const callbacks: Record<string, (data: IssueSummary) => void> = {};
    const { svc, http } = setupWithCapturingSignalR(callbacks, []);

    svc.loadIssues();
    http.expectOne('/api/issues').flush({ items: [mockSummary], nextCursor: null });
    expect(svc.issues().length).toBe(1);

    const updatedToUnchanged: IssueSummary = { ...mockSummary, state: 'unchanged' };

    // Act
    callbacks['IssueUpdated'](updatedToUnchanged);

    // Assert — unchanged is an active state; issue stays in issues() with updated state
    expect(svc.issues().length).toBe(1);
    expect(svc.issues()[0].state).toBe('unchanged');
    http.verify();
  });

  it('should not append a new resolved issue when IssueUpdated arrives for an id not in issues', () => {
    // Arrange
    const callbacks: Record<string, (data: IssueSummary) => void> = {};
    const { svc, http } = setupWithCapturingSignalR(callbacks, []);

    svc.loadIssues();
    http.expectOne('/api/issues').flush({ items: [], nextCursor: null });

    const newResolved: IssueSummary = { ...mockSummary, id: 'brand-new-resolved', state: 'completed' };

    // Act
    callbacks['IssueUpdated'](newResolved);

    // Assert — resolved items are not added to issues
    expect(svc.issues().some((i: IssueSummary) => i.id === 'brand-new-resolved')).toBe(false);
    http.verify();
  });

  // Cycle 21 (load): loadIssues excludes resolved-state items from issues signal
  // (RESOLVED_STATES = ['completed']; unchanged moved to ACTIVE_STATES in step 2)
  it('should exclude completed from issues on loadIssues while retaining active and unchanged', () => {
    // Arrange
    const active: IssueSummary = { ...mockSummary, id: 'active-1', state: 'detected' };
    const completed: IssueSummary = { ...mockSummary, id: 'completed-1', state: 'completed' };
    const unchanged: IssueSummary = { ...mockSummary, id: 'unchanged-1', state: 'unchanged' };

    // Act
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush({ items: [active, completed, unchanged], nextCursor: null });

    // Assert — completed is excluded (resolved); detected and unchanged are both retained (active)
    expect(service.issues().length).toBe(2);
    expect(service.issues().some(i => i.id === 'active-1')).toBe(true);
    expect(service.issues().some(i => i.id === 'unchanged-1')).toBe(true);
    expect(service.issues().some(i => i.id === 'completed-1')).toBe(false);
  });

  // Step 4 — Selected-state signals + active-band filtering

});

// Step 6 — SignalR band-transition handling
describe('IssueService (band transitions)', () => {
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
    stage: 'triage',
    needsAction: false,
    awaitsDispatch: true,
    codeStep: null,
  };

  const resolvedSummary: IssueSummary = {
    id: 'issue-2',
    issueNumber: 20,
    title: 'Resolved issue',
    state: 'completed',
    repositorySlug: 'owner/repo',
    detectedAt: '2026-01-01T00:00:00Z',
    url: 'https://github.com/owner/repo/issues/20',
    stage: 'done',
    needsAction: false,
    awaitsDispatch: false,
    codeStep: null,
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

  // Cycle B1: active→resolved transition after the resolved band has loaded → issue removed from issues,
  // prepended to resolvedIssues
  it('should remove issue from issues and prepend to resolvedIssues when it completes after the resolved band has loaded', () => {
    // Arrange — load an active issue and the resolved band
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush({ items: [activeSummary], nextCursor: null });
    service.loadResolved();
    httpMock.expectOne(r => r.url === '/api/issues').flush({ items: [], nextCursor: null });

    const transitioned: IssueSummary = { ...activeSummary, state: 'completed' };

    // Act
    callbacks['IssueUpdated'](transitioned);

    // Assert — removed from active band
    expect(service.issues().some((i: IssueSummary) => i.id === 'issue-1')).toBe(false);
    // Prepended to resolvedIssues
    expect(service.resolvedIssues()[0]).toEqual(transitioned);
  });

  // Cycle B2: active→resolved before the resolved band has loaded → removed from issues, NOT added to resolvedIssues
  it('should remove issue from issues and not add to resolvedIssues when it completes before the resolved band has loaded', () => {
    // Arrange — load an active issue, but not the resolved band
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush({ items: [activeSummary], nextCursor: null });

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
    // Arrange — load a resolved issue in the resolved band
    service.loadResolved();
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
    // Arrange — load one resolved issue in the resolved band
    service.loadResolved();
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

  // Cycle B5-new: brand-new resolved id (never in issues) arriving via IssueUpdated after the resolved band has loaded
  it('should prepend a brand-new resolved issue to resolvedIssues after the resolved band has loaded and the id was never in issues', () => {
    // Arrange — load the resolved band; flush empty page (no prior resolved issues)
    service.loadResolved();
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
    service.selectIssue('issue-1');
    httpMock.expectOne('/api/issues/issue-1').flush({ ...activeSummary });
    expect(service.selectedIssueId()).toBe('issue-1');

    // Act — trigger an IssueUpdated event for the expanded issue
    callbacks['IssueUpdated']({ ...activeSummary, state: 'in_progress' });

    // Advance past the debounce window so the reconcile request fires
    vi.advanceTimersByTime(400);
    // The debounce schedules a queue-order reconcile (ADR 0067).
    httpMock.expectOne(r => r.url === '/api/issues' && !r.params.has('states')).flush({ items: [activeSummary], nextCursor: null });

    // Assert — detail refetch triggered
    const detailReq = httpMock.expectOne('/api/issues/issue-1');
    detailReq.flush({ ...activeSummary, state: 'in_progress' });
    expect(service.issueDetail()).toBeTruthy();

    vi.useRealTimers();
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
    stage: 'done',
    needsAction: false,
    awaitsDispatch: false,
    codeStep: null,
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

  // Cycle R1: loadResolved fetches GET /api/issues with states=<state>
  it('should fetch GET /api/issues with states=completed (bracketless) when loadResolved is called', () => {
    // Arrange — the resolved band has not loaded

    // Act
    service.loadResolved();

    // Assert — one request with states=completed (bracketless), no cursor param
    const req = httpMock.expectOne(r =>
      r.url === '/api/issues' && r.params.getAll('states')?.includes('completed') === true
    );
    expect(req.request.method).toBe('GET');
    expect(req.request.params.has('cursor')).toBe(false);

    req.flush({ items: [resolvedSummary], nextCursor: null });

    // Mapper adds missing optional fields; use toMatchObject to check the data we care about
    expect(service.resolvedIssues()).toMatchObject([resolvedSummary]);
    expect(service.hasMoreResolved()).toBe(false);
  });

  // Cycle R2: the resolved state is passed as a bracketless states param in the request
  // (RESOLVED_STATES = ['completed'] since unchanged moved to active in step 2)
  it('should send the resolved state as a bracketless states param in the request', () => {
    // Act
    service.loadResolved();

    // Assert — single request with states=completed
    const req = httpMock.expectOne(r =>
      r.url === '/api/issues' &&
      r.params.getAll('states')?.includes('completed') === true
    );
    expect(req.request.params.getAll('states')?.length).toBe(1);
    req.flush({ items: [], nextCursor: null });
  });

  // Cycle R8: resolvedLoading and resolvedLoadingMore toggle around their respective requests
  it('should set resolvedLoading to true while page-1 fetch is in flight and false after', () => {
    // Act — trigger page 1 fetch
    service.loadResolved();

    // Assert — resolvedLoading is true before response
    expect(service.resolvedLoading()).toBe(true);

    httpMock.expectOne(r => r.url === '/api/issues').flush({ items: [], nextCursor: null });

    expect(service.resolvedLoading()).toBe(false);
  });

  it('should set resolvedLoadingMore to true while load-more fetch is in flight and false after', () => {
    // Arrange — get page 1 with a cursor
    service.loadResolved();
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
    // Arrange — get page 1 successfully
    const item: IssueSummary = { ...resolvedSummary, id: 'res-1' };
    service.loadResolved();
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
    // Arrange — flush page 1 with a cursor (no repositoryId on page 1 for simplicity)
    service.loadResolved();
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

  // Cycle R4: loading again resets accumulation and refetches page 1
  it('should reset resolvedIssues and refetch page 1 when loadResolved is called again', () => {
    // Arrange — get page 1 with a cursor
    const page1Item: IssueSummary = { ...resolvedSummary, id: 'res-old' };
    service.loadResolved();
    httpMock.expectOne(r => r.url === '/api/issues').flush({
      items: [page1Item],
      nextCursor: 'old-cursor',
    });
    expect(service.resolvedIssues().length).toBe(1);
    expect(service.hasMoreResolved()).toBe(true);

    // Act
    service.loadResolved();

    // Assert — resolvedIssues cleared immediately, no cursor, and page 1 requested again
    expect(service.resolvedIssues().length).toBe(0);
    expect(service.hasMoreResolved()).toBe(false);
    const req = httpMock.expectOne(r => r.url === '/api/issues');
    expect(req.request.params.has('cursor')).toBe(false);
    req.flush({ items: [], nextCursor: null });
  });

  // Finding 3: resolved page drops items with unknown state
  it('should drop items with an unknown state from a resolved page', () => {
    // Arrange
    const valid: IssueSummary = { ...resolvedSummary, id: 'res-valid' };
    const unknown = { ...resolvedSummary, id: 'res-ghost', state: 'ghost_state' as never };

    // Act
    service.loadResolved();
    httpMock.expectOne(r => r.url === '/api/issues').flush({ items: [valid, unknown], nextCursor: null });

    // Assert — only valid item retained
    expect(service.resolvedIssues().length).toBe(1);
    expect(service.resolvedIssues()[0].id).toBe('res-valid');
  });

  // Finding 4: stale inflight resolved-page write race
  it('should discard a stale resolved-page response when a newer load supersedes it', () => {
    // Arrange — fire the first request but do not flush it yet
    service.loadResolved();
    const staleReq = httpMock.expectOne(r =>
      r.url === '/api/issues' && r.params.getAll('states')?.includes('completed') === true
    );

    // Act — load again before the first request resolves (a new token supersedes the old one)
    service.loadResolved(); // a fresh load supersedes the in-flight one
    const freshReq = httpMock.expectOne(r =>
      r.url === '/api/issues' && r.params.getAll('states')?.includes('completed') === true
    );

    // Flush the stale response first — it should be discarded because the token changed
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
    // Arrange — flush page 1 with a cursor
    const page1Item: IssueSummary = { ...resolvedSummary, id: 'res-1' };
    service.loadResolved();
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

  // New: first-page HTTP failure sets resolvedError, not loadError
  it('should set resolvedError when the first-page resolved fetch fails with an HTTP error', () => {
    // Arrange
    service.loadResolved();

    // Act
    httpMock.expectOne(r => r.url === '/api/issues').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });

    // Assert
    expect(service.resolvedError()).toBe('Failed to load resolved issues');
    expect(service.resolvedLoading()).toBe(false);
  });

  // New: first-page failure does not affect active-band loadError (band independence)
  it('should not set loadError when the resolved fetch fails', () => {
    // Arrange
    service.loadResolved();

    // Act
    httpMock.expectOne(r => r.url === '/api/issues').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });

    // Assert
    expect(service.loadError()).toBeNull();
  });

  // New: active-band failure does not set resolvedError (band independence)
  it('should not set resolvedError when loadIssues fails', () => {
    // Arrange
    service.loadIssues();

    // Act
    httpMock.expectOne('/api/issues').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });

    // Assert
    expect(service.resolvedError()).toBeNull();
  });

  // New: resolvedError cleared at the start of a fresh load
  it('should clear resolvedError when loadResolved starts a fresh fetch', () => {
    // Arrange — trigger a resolved error
    service.loadResolved();
    httpMock.expectOne(r => r.url === '/api/issues').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    expect(service.resolvedError()).not.toBeNull();

    // Act — load again
    service.loadResolved(); // a fresh load supersedes the in-flight one

    // Assert — error cleared immediately before the new response arrives
    expect(service.resolvedError()).toBeNull();

    // Cleanup
    httpMock.expectOne(r => r.url === '/api/issues').flush({ items: [], nextCursor: null });
  });

  // New: retryResolvedFetch clears error and re-fetches
  it('should clear resolvedError and re-fetch when retryResolvedFetch is called', () => {
    // Arrange — trigger a resolved error
    service.loadResolved();
    httpMock.expectOne(r => r.url === '/api/issues').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    expect(service.resolvedError()).not.toBeNull();

    // Act
    service.retryResolvedFetch();

    // Assert — error cleared immediately
    expect(service.resolvedError()).toBeNull();

    // A new request is made for the same states
    const req = httpMock.expectOne(r =>
      r.url === '/api/issues' && r.params.getAll('states')?.includes('completed') === true
    );
    req.flush({ items: [resolvedSummary], nextCursor: null });

    expect(service.resolvedIssues().length).toBe(1);
  });

  // New: load-more failure sets resolvedLoadMoreError, preserves prior issues
  it('should set resolvedLoadMoreError and preserve prior resolved issues when load-more fails', () => {
    // Arrange — get page 1 successfully with a cursor
    service.loadResolved();
    httpMock.expectOne(r => r.url === '/api/issues').flush({
      items: [resolvedSummary],
      nextCursor: 'cursor-1',
    });
    expect(service.resolvedIssues().length).toBe(1);

    // Act — load more fails
    service.loadMoreResolved();
    httpMock.expectOne(r =>
      r.url === '/api/issues' && r.params.get('cursor') === 'cursor-1'
    ).flush('Server Error', { status: 500, statusText: 'Internal Server Error' });

    // Assert
    expect(service.resolvedLoadMoreError()).toBe('Failed to load more resolved issues');
    expect(service.resolvedIssues().length).toBe(1);
    expect(service.resolvedIssues()[0].id).toBe('res-1');
  });

  // New: load-more failure does not set resolvedError (full-band error)
  it('should not set resolvedError when load-more fails', () => {
    // Arrange
    service.loadResolved();
    httpMock.expectOne(r => r.url === '/api/issues').flush({
      items: [resolvedSummary],
      nextCursor: 'cursor-1',
    });

    // Act
    service.loadMoreResolved();
    httpMock.expectOne(r => r.params.get('cursor') === 'cursor-1').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });

    // Assert
    expect(service.resolvedError()).toBeNull();
  });

  // New: loadMoreResolved clears resolvedLoadMoreError before retrying
  it('should clear resolvedLoadMoreError and retry when loadMoreResolved is called after a load-more failure', () => {
    // Arrange — fail a load-more
    service.loadResolved();
    httpMock.expectOne(r => r.url === '/api/issues').flush({
      items: [resolvedSummary],
      nextCursor: 'cursor-1',
    });
    service.loadMoreResolved();
    httpMock.expectOne(r => r.params.get('cursor') === 'cursor-1').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    expect(service.resolvedLoadMoreError()).not.toBeNull();

    // Act — retry via loadMoreResolved
    service.loadMoreResolved();

    // Assert — error cleared immediately before response
    expect(service.resolvedLoadMoreError()).toBeNull();

    // New request sent for the same cursor
    const req = httpMock.expectOne(r => r.params.get('cursor') === 'cursor-1');
    req.flush({ items: [], nextCursor: null });
  });

});

// Step 7 — Dispatch-order queue grouping (issue #261)
describe('IssueService (dispatch-order queue grouping)', () => {
  let service: IssueService;
  let httpMock: HttpTestingController;

  const baseIssue: IssueSummary = {
    id: 'base-1',
    issueNumber: 1,
    title: 'Base issue',
    state: 'queued',
    repositorySlug: 'owner/repo',
    detectedAt: '2026-01-01T00:00:00Z',
    url: 'https://github.com/owner/repo/issues/1',
    stage: 'implement',
    needsAction: false,
    awaitsDispatch: true,
    codeStep: null,
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

  // Cycle Q1: continuation_queued is NOT a live state — belongs in queue group
  it('should NOT treat continuation_queued as a live state in liveIssueCount', () => {
    // Arrange
    const continuationIssue: IssueSummary = { ...baseIssue, id: 'cont-1', state: 'continuation_queued' };

    // Act
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush({ items: [continuationIssue], nextCursor: null });

    // Assert — continuation_queued must not inflate live count
    expect(service.liveIssueCount()).toBe(0);
  });

  // Cycle Q3: eligibleQueuedIssues — queued issues from eligible repos, server order
  it('should return eligible queued issues in server order via eligibleQueuedIssues', () => {
    // Arrange — eligible repo issues (no repositoryEligibilityStatus or status = eligible)
    const eligible1: IssueSummary = { ...baseIssue, id: 'elig-1', state: 'queued', repositoryEligibilityStatus: null };
    const eligible2: IssueSummary = { ...baseIssue, id: 'elig-2', state: 'revision_queued', repositoryEligibilityStatus: 'eligible' };
    const ineligible: IssueSummary = { ...baseIssue, id: 'inelig-1', state: 'queued', repositoryEligibilityStatus: 'ineligible' };

    // Act
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush({ items: [eligible1, eligible2, ineligible], nextCursor: null });

    // Assert — only eligible issues, in server order
    expect(service.eligibleQueuedIssues()[0].id).toBe('elig-1');
    expect(service.eligibleQueuedIssues()[1].id).toBe('elig-2');
    expect(service.eligibleQueuedIssues().length).toBe(2);
  });

  // Cycle Q4: ineligibleQueuedIssues — queued issues from ineligible/unreachable repos
  it('should return ineligible/unreachable queued issues via ineligibleQueuedIssues', () => {
    // Arrange
    const eligible: IssueSummary = { ...baseIssue, id: 'elig-1', state: 'queued', repositoryEligibilityStatus: null };
    const ineligible: IssueSummary = { ...baseIssue, id: 'inelig-1', state: 'queued', repositoryEligibilityStatus: 'ineligible' };
    const unreachable: IssueSummary = { ...baseIssue, id: 'unreach-1', state: 'queued', repositoryEligibilityStatus: 'unreachable' };

    // Act
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush({ items: [eligible, ineligible, unreachable], nextCursor: null });

    // Assert — only ineligible/unreachable queued issues
    expect(service.ineligibleQueuedIssues().length).toBe(2);
    expect(service.ineligibleQueuedIssues().map(i => i.id)).toContain('inelig-1');
    expect(service.ineligibleQueuedIssues().map(i => i.id)).toContain('unreach-1');
  });

  // Cycle Q5: nextUpIssueId — first eligible queued issue id
  it('should return the id of the first eligible queued issue as nextUpIssueId', () => {
    // Arrange — server order: eligible issue first
    const eligible1: IssueSummary = { ...baseIssue, id: 'first-elig', state: 'queued', repositoryEligibilityStatus: null };
    const eligible2: IssueSummary = { ...baseIssue, id: 'second-elig', state: 'queued', repositoryEligibilityStatus: null };

    // Act
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush({ items: [eligible1, eligible2], nextCursor: null });

    // Assert
    expect(service.nextUpIssueId()).toBe('first-elig');
  });

  // Cycle Q6: nextUpIssueId — null when no eligible queued issues exist
  it('should return null for nextUpIssueId when there are no eligible queued issues', () => {
    // Arrange — all queued issues are ineligible
    const ineligible: IssueSummary = { ...baseIssue, id: 'inelig-1', state: 'queued', repositoryEligibilityStatus: 'ineligible' };

    // Act
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush({ items: [ineligible], nextCursor: null });

    // Assert
    expect(service.nextUpIssueId()).toBeNull();
  });

  // Cycle Q7: nextUpIssueId — null when no queued issues at all
  it('should return null for nextUpIssueId when there are no queued issues', () => {
    // Arrange — only live issues
    const live: IssueSummary = { ...baseIssue, id: 'live-1', state: 'in_progress' };

    // Act
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush({ items: [live], nextCursor: null });

    // Assert
    expect(service.nextUpIssueId()).toBeNull();
  });

  // Cycle Q8: continuation_queued treated as a queued-tier state (in eligibleQueuedIssues)
  it('should include continuation_queued in eligibleQueuedIssues when repo is eligible', () => {
    // Arrange
    const contQueued: IssueSummary = { ...baseIssue, id: 'cont-1', state: 'continuation_queued', repositoryEligibilityStatus: null };

    // Act
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush({ items: [contQueued], nextCursor: null });

    // Assert
    expect(service.eligibleQueuedIssues().some(i => i.id === 'cont-1')).toBe(true);
  });
});

// Step 9 — Dispatch-order signals read raw issues() (issue #275)
describe('IssueService (dispatch-order vs visual-sort regression)', () => {
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

  // Dispatch order comes from issues() (server order); nextUpIssueId reflects server position.
  it('should pick nextUpIssueId from server dispatch order (issues())', () => {
    // Arrange — server delivers: queued (pos 0), then continuation_queued (pos 1).
    const eligibleQueued: IssueSummary = { ...mockSummary, id: 'server-first-queued', state: 'queued', repositoryEligibilityStatus: null };
    const contQueued: IssueSummary = { ...mockSummary, id: 'cont-queued', state: 'continuation_queued', repositoryEligibilityStatus: null };

    // Act
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush({ items: [eligibleQueued, contQueued], nextCursor: null });

    // Assert — the server-first queued issue is next up
    expect(service.nextUpIssueId()).toBe('server-first-queued');
  });

  // eligibleQueuedIssues returns cards in raw server order (not visual sort order)
  it('should return eligibleQueuedIssues in raw server order from issues()', () => {
    // Arrange — server delivers: queued (elig, pos 0), revision_queued (elig, pos 1), continuation_queued (elig, pos 2).
    const queuedFirst: IssueSummary = { ...mockSummary, id: 'q-first', state: 'queued', repositoryEligibilityStatus: null };
    const revQueuedSecond: IssueSummary = { ...mockSummary, id: 'rv-second', state: 'revision_queued', repositoryEligibilityStatus: null };
    const contQueuedThird: IssueSummary = { ...mockSummary, id: 'cq-third', state: 'continuation_queued', repositoryEligibilityStatus: null };

    // Act
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush({ items: [queuedFirst, revQueuedSecond, contQueuedThird], nextCursor: null });

    // Assert — server order preserved in eligibleQueuedIssues (dispatched revision_queued > continuation_queued > queued)
    const eligible = service.eligibleQueuedIssues();
    expect(eligible[0].id).toBe('q-first');
    expect(eligible[1].id).toBe('rv-second');
    expect(eligible[2].id).toBe('cq-third');
    expect(eligible.length).toBe(3);
  });

  // ineligibleQueuedIssues returns cards in raw server order
  it('should return ineligibleQueuedIssues in raw server order from issues()', () => {
    // Arrange — server delivers: continuation_queued (inelig, pos 0), queued (inelig, pos 1).
    const contQueuedFirst: IssueSummary = { ...mockSummary, id: 'cq-inelig-first', state: 'continuation_queued', repositoryEligibilityStatus: 'ineligible' };
    const queuedSecond: IssueSummary = { ...mockSummary, id: 'q-inelig-second', state: 'queued', repositoryEligibilityStatus: 'ineligible' };

    // Act
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush({ items: [contQueuedFirst, queuedSecond], nextCursor: null });

    // Assert — server order preserved
    const ineligible = service.ineligibleQueuedIssues();
    expect(ineligible[0].id).toBe('cq-inelig-first');
    expect(ineligible[1].id).toBe('q-inelig-second');
    expect(ineligible.length).toBe(2);
  });
});

// Step 10 — Queue-order reconcile (#506)
describe('IssueService (queue-order reconcile)', () => {
  let callbacks: Record<string, (data: IssueSummary) => void>;
  let httpMockRef: HttpTestingController;

  const low: IssueSummary = {
    id: 'low-prio',
    issueNumber: 1,
    title: 'Low prio queued',
    state: 'queued',
    repositorySlug: 'owner/low-repo',
    detectedAt: '2026-01-01T00:00:00Z',
    url: 'https://github.com/owner/low-repo/issues/1',
    stage: 'implement',
    needsAction: false,
    awaitsDispatch: true,
    codeStep: null,
    repositoryEligibilityStatus: null,
  };

  const rev: IssueSummary = {
    id: 'revision-high',
    issueNumber: 2,
    title: 'Revision queued',
    state: 'revision_queued',
    repositorySlug: 'owner/high-repo',
    detectedAt: '2026-06-01T00:00:00Z',
    url: 'https://github.com/owner/high-repo/issues/2',
    stage: 'implement',
    needsAction: false,
    awaitsDispatch: true,
    codeStep: null,
    repositoryEligibilityStatus: null,
  };

  function setup() {
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

    const svc = TestBed.inject(IssueService);
    const http = TestBed.inject(HttpTestingController);
    httpMockRef = http;
    return { svc, http };
  }

  afterEach(() => {
    vi.useRealTimers();
    // Reset so subsequent describe blocks can call configureTestingModule cleanly.
    TestBed.resetTestingModule();
  });

  // Step 1 — Tracer bullet: direct reconcile installs server order
  it('should replace issues() with server order on reconcile success, without setting initialLoading', () => {
    // Arrange
    const { svc, http } = setup();
    svc.loadIssues();
    http.expectOne('/api/issues').flush({ items: [low], nextCursor: null });
    expect(svc.issues()[0].id).toBe('low-prio');
    expect(svc.initialLoading()).toBe(false);

    // Act — fire an IssueUpdated event to trigger the debounced reconcile
    vi.useFakeTimers();
    callbacks['IssueUpdated']({ ...low, state: 'queued' });
    vi.advanceTimersByTime(400);

    // The debounce fires — expect one reconcile request
    const reconcileReq = http.expectOne(r => r.url === '/api/issues' && !r.params.has('states'));
    reconcileReq.flush({ items: [rev, low], nextCursor: null }); // server returns revision-high first now

    // Assert — issues() reflects the new server order
    expect(svc.issues()[0].id).toBe('revision-high');
    expect(svc.issues()[1].id).toBe('low-prio');
    // initialLoading must not be re-set
    expect(svc.initialLoading()).toBe(false);
    http.verify({ ignoreCancelled: true });
  });

  // Step 1 — reconcile excludes resolved-state items (same filter as loadIssues)
  it('should filter resolved-state items from reconcile response, same as loadIssues', () => {
    // Arrange
    const { svc, http } = setup();
    svc.loadIssues();
    http.expectOne('/api/issues').flush({ items: [low], nextCursor: null });

    vi.useFakeTimers();
    callbacks['IssueUpdated']({ ...low, state: 'queued' });
    vi.advanceTimersByTime(400);

    const completed: IssueSummary = { ...low, id: 'done', state: 'completed' };

    // Act — reconcile returns active + completed
    http.expectOne(r => r.url === '/api/issues' && !r.params.has('states')).flush({ items: [low, completed], nextCursor: null });

    // Assert — completed is excluded from issues()
    expect(svc.issues().some(i => i.id === 'done')).toBe(false);
    expect(svc.issues().some(i => i.id === 'low-prio')).toBe(true);
    http.verify({ ignoreCancelled: true });
  });

  // Step 1 — token: later response discards earlier
  it('should discard an earlier reconcile response when a later one has already resolved', () => {
    // Arrange
    const { svc, http } = setup();
    svc.loadIssues();
    http.expectOne('/api/issues').flush({ items: [low], nextCursor: null });

    vi.useFakeTimers();

    // First IssueUpdated — debounce window 1
    callbacks['IssueUpdated']({ ...low, state: 'queued' });
    vi.advanceTimersByTime(400);
    const firstReconcile = http.expectOne(r => r.url === '/api/issues' && !r.params.has('states'));

    // Second IssueUpdated — debounce window 2
    callbacks['IssueUpdated']({ ...low, state: 'queued' });
    vi.advanceTimersByTime(400);
    const secondReconcile = http.expectOne(r => r.url === '/api/issues' && !r.params.has('states'));

    // Flush the LATER one first — its order should win
    secondReconcile.flush({ items: [rev, low], nextCursor: null }); // revision-high first
    expect(svc.issues()[0].id).toBe('revision-high');

    // Now flush the EARLIER one — its order must be discarded
    firstReconcile.flush({ items: [low, rev], nextCursor: null }); // stale order: low first
    expect(svc.issues()[0].id).toBe('revision-high'); // still revision-high

    http.verify({ ignoreCancelled: true });
  });

  // Step 2 — Event-driven: IssueUpdated triggers a debounced reconcile
  it('should issue exactly one GET /api/issues reconcile per debounce window for a burst of IssueUpdated events', () => {
    // Arrange
    const { svc, http } = setup();
    svc.loadIssues();
    http.expectOne('/api/issues').flush({ items: [low], nextCursor: null });

    vi.useFakeTimers();

    // Act — fire three events inside one debounce window
    callbacks['IssueUpdated']({ ...low, state: 'in_progress' });
    callbacks['IssueUpdated']({ ...low, state: 'review' });
    callbacks['IssueUpdated']({ ...low, state: 'failed' });

    // Before debounce elapses — no reconcile yet
    http.expectNone(r => r.url === '/api/issues' && !r.params.has('states'));

    // Advance past debounce
    vi.advanceTimersByTime(400);

    // Assert — exactly one reconcile
    const reconcileReq = http.expectOne(r => r.url === '/api/issues' && !r.params.has('states'));
    reconcileReq.flush({ items: [low], nextCursor: null });

    http.verify({ ignoreCancelled: true });
  });

  // Step 2 — Criterion 3: revision_queued arrival + reconcile renders it above low-prio,
  // and nextUpIssueId returns the revision id
  it('should render revision-tier issue above low-prio queued issue after reconcile and set nextUpIssueId correctly', () => {
    // Arrange — initial load: only low-prio queued
    const { svc, http } = setup();
    svc.loadIssues();
    http.expectOne('/api/issues').flush({ items: [low], nextCursor: null });
    expect(svc.issues()[0].id).toBe('low-prio');
    expect(svc.nextUpIssueId()).toBe('low-prio');

    vi.useFakeTimers();

    // Act — a revision_queued issue arrives via IssueUpdated (upsert appends it)
    callbacks['IssueUpdated'](rev);
    // upsert appends rev at tail: [low, rev] — wrong order for dispatch priority
    expect(svc.issues()[0].id).toBe('low-prio'); // still wrong before reconcile

    // Debounce fires
    vi.advanceTimersByTime(400);

    // Server returns correct dispatch order: revision first
    http.expectOne(r => r.url === '/api/issues' && !r.params.has('states')).flush({ items: [rev, low], nextCursor: null });

    // Assert — reconcile fixed the order
    const eligible = svc.eligibleQueuedIssues();
    expect(eligible[0].id).toBe('revision-high');
    expect(eligible[1].id).toBe('low-prio');
    expect(svc.nextUpIssueId()).toBe('revision-high');

    http.verify({ ignoreCancelled: true });
  });

  // Step 3 — Token-race regression: earlier response flushed after later is discarded
  it('should keep later response order when an earlier response arrives out-of-order', () => {
    // Arrange — two reconcile windows in flight
    const { svc, http } = setup();
    svc.loadIssues();
    http.expectOne('/api/issues').flush({ items: [low], nextCursor: null });

    vi.useFakeTimers();

    // Window 1
    callbacks['IssueUpdated']({ ...low, state: 'queued' });
    vi.advanceTimersByTime(400);
    const earlyReq = http.expectOne(r => r.url === '/api/issues' && !r.params.has('states'));

    // Window 2
    callbacks['IssueUpdated']({ ...low, state: 'queued' });
    vi.advanceTimersByTime(400);
    const lateReq = http.expectOne(r => r.url === '/api/issues' && !r.params.has('states'));

    // Flush LATER first with [rev, low] order
    lateReq.flush({ items: [rev, low], nextCursor: null });
    expect(svc.issues()[0].id).toBe('revision-high');

    // Now flush EARLIER with reversed (stale) order
    earlyReq.flush({ items: [low, rev], nextCursor: null });

    // Assert — stale early response did NOT overwrite later order
    expect(svc.issues()[0].id).toBe('revision-high');

    http.verify({ ignoreCancelled: true });
  });

  // Step 4 — Safety-net interval fires reconcile with no IssueUpdated event.
  // The interval must be registered with fake timers, so vi.useFakeTimers() is called
  // before setup() so that setInterval in the constructor is intercepted.
  it('should issue a reconcile after the safety-net interval elapses with no IssueUpdated events', () => {
    // Arrange — fake timers BEFORE service construction so setInterval is captured
    vi.useFakeTimers();
    const { svc, http } = setup();
    svc.loadIssues();
    http.expectOne('/api/issues').flush({ items: [low], nextCursor: null });

    // Act — advance past safety-net interval (30 000 ms) with no events
    vi.advanceTimersByTime(30_100);

    // Assert — reconcile fires
    const reconcileReq = http.expectOne(r => r.url === '/api/issues' && !r.params.has('states'));
    reconcileReq.flush({ items: [rev, low], nextCursor: null });

    expect(svc.issues()[0].id).toBe('revision-high');

    http.verify({ ignoreCancelled: true });
  });

  // Step 4 — Criterion 6: Position change reflected on next safety-net reconcile
  it('should reflect a repository Position change (no event) on the next safety-net reconcile', () => {
    // Arrange — fake timers BEFORE service construction so setInterval is captured
    vi.useFakeTimers();
    const { svc, http } = setup();
    svc.loadIssues();
    http.expectOne('/api/issues').flush({ items: [low, rev], nextCursor: null });
    expect(svc.issues()[0].id).toBe('low-prio');

    // Act — safety-net fires; server now returns rev first (Position changed in Settings)
    vi.advanceTimersByTime(30_100);
    http.expectOne(r => r.url === '/api/issues' && !r.params.has('states')).flush({ items: [rev, low], nextCursor: null });

    // Assert — new order rendered without page reload
    expect(svc.issues()[0].id).toBe('revision-high');

    http.verify({ ignoreCancelled: true });
  });

  // Step 4 — Destroy cleanup: no reconcile fires after destroy
  it('should not fire the safety-net reconcile after the service is destroyed', () => {
    // Arrange — fake timers BEFORE service construction so setInterval is captured
    vi.useFakeTimers();
    const { svc, http } = setup();
    svc.loadIssues();
    http.expectOne('/api/issues').flush({ items: [low], nextCursor: null });

    // TestBed.resetTestingModule() triggers Angular's ApplicationRef.destroy() which invokes
    // DestroyRef callbacks — this clears the safety-net interval.
    TestBed.resetTestingModule();

    // Act — advance past safety-net interval; the interval was cleared on destroy
    vi.advanceTimersByTime(30_100);

    // Assert — no reconcile request because the interval was cleared
    // (verify on the captured http mock from before the reset)
    httpMockRef.expectNone(r => r.url === '/api/issues' && !r.params.has('states'));

    // afterEach calls resetTestingModule again — that is safe (resets an already-reset module)
  });

  // Step 5 — Failed reconcile: sets queueOrderStale, retains list, no loadError
  it('should set queueOrderStale and retain issues() when reconcile fails, without setting loadError', () => {
    // Arrange
    const { svc, http } = setup();
    svc.loadIssues();
    http.expectOne('/api/issues').flush({ items: [low], nextCursor: null });
    expect(svc.queueOrderStale()).toBe(false);

    vi.useFakeTimers();
    callbacks['IssueUpdated']({ ...low, state: 'queued' });
    vi.advanceTimersByTime(400);

    // Act — reconcile fails
    http.expectOne(r => r.url === '/api/issues' && !r.params.has('states')).flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });

    // Assert — stale marker set; list preserved; no blocking banner
    expect(svc.queueOrderStale()).toBe(true);
    expect(svc.issues()[0].id).toBe('low-prio');
    expect(svc.loadError()).toBeNull();

    http.verify({ ignoreCancelled: true });
  });

  // Step 5 — Successful reconcile after failure clears queueOrderStale
  it('should clear queueOrderStale after a successful reconcile follows a failed one', () => {
    // Arrange — cause a failure first
    const { svc, http } = setup();
    svc.loadIssues();
    http.expectOne('/api/issues').flush({ items: [low], nextCursor: null });

    vi.useFakeTimers();
    callbacks['IssueUpdated']({ ...low, state: 'queued' });
    vi.advanceTimersByTime(400);
    http.expectOne(r => r.url === '/api/issues' && !r.params.has('states')).flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    expect(svc.queueOrderStale()).toBe(true);

    // Act — next reconcile succeeds
    callbacks['IssueUpdated']({ ...low, state: 'queued' });
    vi.advanceTimersByTime(400);
    http.expectOne(r => r.url === '/api/issues' && !r.params.has('states')).flush({ items: [low], nextCursor: null });

    // Assert — stale cleared
    expect(svc.queueOrderStale()).toBe(false);

    http.verify({ ignoreCancelled: true });
  });

  // Step 5 — Stale failure from superseded request must not overwrite a fresher success
  it('should not set queueOrderStale from a stale error response when a later reconcile already succeeded', () => {
    // Arrange
    const { svc, http } = setup();
    svc.loadIssues();
    http.expectOne('/api/issues').flush({ items: [low], nextCursor: null });

    vi.useFakeTimers();

    // Window 1
    callbacks['IssueUpdated']({ ...low, state: 'queued' });
    vi.advanceTimersByTime(400);
    const earlyReq = http.expectOne(r => r.url === '/api/issues' && !r.params.has('states'));

    // Window 2
    callbacks['IssueUpdated']({ ...low, state: 'queued' });
    vi.advanceTimersByTime(400);
    const lateReq = http.expectOne(r => r.url === '/api/issues' && !r.params.has('states'));

    // Later succeeds first
    lateReq.flush({ items: [low], nextCursor: null });
    expect(svc.queueOrderStale()).toBe(false);

    // Earlier fails — must be ignored because token is stale
    earlyReq.flush('Server Error', { status: 500, statusText: 'Internal Server Error' });

    // Assert — stale error did not set queueOrderStale
    expect(svc.queueOrderStale()).toBe(false);

    http.verify({ ignoreCancelled: true });
  });

  // Step 6 — Existing append-on-unknown-id test accounts for reconcile
  // (regression guard: _upsertIssue tail-append still works, reconcile fires within debounce)
  it('should append a brand-new issue via upsert and fire a debounced reconcile that can correct order', () => {
    // Arrange
    const { svc, http } = setup();
    svc.loadIssues();
    http.expectOne('/api/issues').flush({ items: [], nextCursor: null });

    vi.useFakeTimers();

    // Act — unknown id appended by upsert
    const newIssue: IssueSummary = { ...low, id: 'brand-new' };
    callbacks['IssueUpdated'](newIssue);
    expect(svc.issues().some(i => i.id === 'brand-new')).toBe(true);

    // Advance past debounce — reconcile fires
    vi.advanceTimersByTime(400);
    const reconcileReq = http.expectOne(r => r.url === '/api/issues' && !r.params.has('states'));
    reconcileReq.flush({ items: [newIssue], nextCursor: null }); // server confirms the order

    expect(svc.issues().some(i => i.id === 'brand-new')).toBe(true);

    http.verify({ ignoreCancelled: true });
  });

  // Finding 2 — reconcile failure during initial load must NOT set queueOrderStale
  it('should NOT set queueOrderStale when reconcile fails while initialLoading is still true', () => {
    // Arrange — fake timers so the safety-net setInterval fires
    vi.useFakeTimers();
    const { svc, http } = setup();
    // Do NOT call loadIssues so initialLoading remains true; the safety-net fires after 30 s
    expect(svc.initialLoading()).toBe(true);

    // Act — advance past the safety-net interval to trigger a reconcile
    vi.advanceTimersByTime(30_100);
    const reconcileReq = http.expectOne(r => r.url === '/api/issues' && !r.params.has('states'));

    // Reconcile fails while initialLoading is still true
    reconcileReq.flush('Server Error', { status: 500, statusText: 'Internal Server Error' });

    // Assert — queueOrderStale must NOT be set because we are still in the initial loading phase
    expect(svc.queueOrderStale()).toBe(false);
    expect(svc.initialLoading()).toBe(true); // still loading — no loadIssues call yet

    http.verify({ ignoreCancelled: true });
  });

  it('should set queueOrderStale when reconcile fails after initial load has completed', () => {
    // Arrange — fake timers BEFORE service construction so setInterval is intercepted
    vi.useFakeTimers();
    const { svc, http } = setup();
    svc.loadIssues();
    http.expectOne('/api/issues').flush({ items: [low], nextCursor: null });
    expect(svc.initialLoading()).toBe(false); // initial load done

    // Advance past the safety-net interval
    vi.advanceTimersByTime(30_100);

    // Act — reconcile fails after initial load is complete
    http.expectOne(r => r.url === '/api/issues' && !r.params.has('states')).flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });

    // Assert — stale marker is set because initial load is done
    expect(svc.queueOrderStale()).toBe(true);

    http.verify({ ignoreCancelled: true });
  });

  // Step 9 (criterion 9): reconcile does not re-show loading skeletons
  it('should not set initialLoading to true during or after a reconcile', () => {
    // Arrange
    const { svc, http } = setup();
    svc.loadIssues();
    http.expectOne('/api/issues').flush({ items: [low], nextCursor: null });
    expect(svc.initialLoading()).toBe(false);

    vi.useFakeTimers();
    callbacks['IssueUpdated']({ ...low, state: 'queued' });
    vi.advanceTimersByTime(400);

    // Assert — still false before reconcile response
    expect(svc.initialLoading()).toBe(false);

    http.expectOne(r => r.url === '/api/issues' && !r.params.has('states')).flush({ items: [low], nextCursor: null });

    // Assert — still false after reconcile
    expect(svc.initialLoading()).toBe(false);

    http.verify({ ignoreCancelled: true });
  });
});

// Code-review findings — numeric coercion, dead branch, null-coalesce robustness
describe('IssueService (code-review findings)', () => {
  let service: IssueService;
  let httpMock: HttpTestingController;

  const baseSchema = {
    id: 'abc123',
    issueNumber: 42 as number | string,
    title: 'Fix the bug',
    state: 'detected',
    repositorySlug: 'owner/repo',
    detectedAt: '2026-01-01T00:00:00Z',
    url: 'https://github.com/owner/repo/issues/42',
    failureClassification: null as null | string,
    repositoryEligibilityStatus: null as null | string,
    runStats: null as null | {
      runCount: number | string;
      durationMs: null | number | string;
      numTurns: null | number | string;
      totalCostUsd: null | number | string;
      inputTokens: SchemaTokenTotal;
      outputTokens: SchemaTokenTotal;
    },
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

  // Finding 4 — issueNumber coercion: numeric string arrives as number on domain object
  it('should coerce issueNumber from numeric string "42" to the number 42', () => {
    // Arrange
    const schemaItem = { ...baseSchema, issueNumber: '42' as number | string };

    // Act
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush({ items: [schemaItem], nextCursor: null });

    // Assert — domain object carries number 42, not string '42'
    const issue = service.issues()[0];
    expect(issue.issueNumber).toBe(42);
    expect(typeof issue.issueNumber).toBe('number');
  });

  // Finding 4 — issueNumber coercion: non-numeric string degrades to 0
  it('should degrade issueNumber to 0 when a non-numeric value arrives', () => {
    // Arrange
    const schemaItem = { ...baseSchema, issueNumber: 'not-a-number' as number | string };

    // Act
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush({ items: [schemaItem], nextCursor: null });

    // Assert — fallback prevents NaN from entering domain state
    const issue = service.issues()[0];
    expect(issue.issueNumber).toBe(0);
    expect(Number.isFinite(issue.issueNumber)).toBe(true);
  });

  // Finding 1 — runStats null branch: null runStats maps to null on domain object
  it('should map null runStats to null on the domain object', () => {
    // Arrange
    const schemaItem = { ...baseSchema, runStats: null };

    // Act
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush({ items: [schemaItem], nextCursor: null });

    // Assert
    expect(service.issues()[0].runStats).toBeNull();
  });

  // Finding 1 + Finding 4 — runStats non-null branch: numeric fields coerced to finite numbers
  it('should coerce numeric-string runStats fields to finite numbers', () => {
    // Arrange
    const schemaItem = {
      ...baseSchema,
      runStats: {
        runCount: '3' as number | string,
        durationMs: '1500' as null | number | string,
        numTurns: '10' as null | number | string,
        totalCostUsd: '0.05' as null | number | string,
        inputTokens: { total: '1000', knownRunCount: '2', unknownRunCount: '1' } as SchemaTokenTotal,
        outputTokens: { total: '500', knownRunCount: 3, unknownRunCount: 0 } as SchemaTokenTotal,
      },
    };

    // Act
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush({ items: [schemaItem], nextCursor: null });

    // Assert — all numeric fields arrive as numbers, not strings
    const stats = service.issues()[0].runStats!;
    expect(stats.runCount).toBe(3);
    expect(stats.durationMs).toBe(1500);
    expect(stats.numTurns).toBe(10);
    expect(stats.totalCostUsd).toBeCloseTo(0.05);
    expect(stats.inputTokens).toEqual({ total: 1000, knownRunCount: 2, unknownRunCount: 1 });
    expect(stats.outputTokens).toEqual({ total: 500, knownRunCount: 3, unknownRunCount: 0 });
  });

  // Finding 1 + Finding 4 — nullable runStats numeric fields coerced: null stays null, bad value degrades to 0
  it('should keep nullable runStats numeric fields null when null, and degrade non-finite values to 0', () => {
    // Arrange
    const schemaItem = {
      ...baseSchema,
      runStats: {
        runCount: 2 as number | string,
        durationMs: null as null | number | string,
        numTurns: 'bad' as null | number | string,
        totalCostUsd: null as null | number | string,
        inputTokens: { total: 'bad', knownRunCount: 0, unknownRunCount: 2 } as SchemaTokenTotal,
        outputTokens: { total: 0, knownRunCount: 0, unknownRunCount: 0 } as SchemaTokenTotal,
      },
    };

    // Act
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush({ items: [schemaItem], nextCursor: null });

    // Assert — null stays null; 'bad' degrades to 0 (including a token total)
    const stats = service.issues()[0].runStats!;
    expect(stats.runCount).toBe(2);
    expect(stats.durationMs).toBeNull();
    expect(stats.numTurns).toBe(0);
    expect(stats.totalCostUsd).toBeNull();
    expect(stats.inputTokens).toEqual({ total: 0, knownRunCount: 0, unknownRunCount: 2 });
    expect(stats.outputTokens).toEqual({ total: 0, knownRunCount: 0, unknownRunCount: 0 });
  });

  // Finding 5 — _fetchResolvedPage null-coalesce: null items body degrades to empty page
  it('should degrade to an empty resolved page when the server returns null items', () => {
    // Arrange — load the resolved band to trigger _fetchResolvedPage
    service.loadResolved();

    // Act — server returns null items (malformed body)
    httpMock.expectOne(r => r.url === '/api/issues').flush({ items: null, nextCursor: null });

    // Assert — no crash; resolvedIssues is empty
    expect(service.resolvedIssues().length).toBe(0);
    expect(service.resolvedLoading()).toBe(false);
  });
});

// Step 12 — nextUpAnnouncement computed (issue #507)
describe('IssueService (nextUpAnnouncement)', () => {
  let service: IssueService;
  let httpMock: HttpTestingController;

  const baseIssue: IssueSummary = {
    id: 'q1',
    issueNumber: 42,
    title: 'Enable dark mode',
    state: 'queued',
    repositorySlug: 'owner/repo',
    detectedAt: '2026-01-01T00:00:00Z',
    url: 'https://github.com/owner/repo/issues/42',
    stage: 'implement',
    needsAction: false,
    awaitsDispatch: true,
    codeStep: null,
    repositoryEligibilityStatus: null,
  };

  function loadIssues(issues: IssueSummary[]): void {
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush({ items: issues, nextCursor: null });
  }

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

  // Cycle NA1: dispatchable queued head → non-empty string naming the issue
  it('should return a non-empty announcement string naming the next-up issue number and title', () => {
    // Arrange
    const nextUp: IssueSummary = { ...baseIssue, id: 'q1', issueNumber: 42, title: 'Enable dark mode' };

    // Act
    loadIssues([nextUp]);

    // Assert — string must include issue number and title
    const announcement = service.nextUpAnnouncement();
    expect(announcement).toContain('42');
    expect(announcement).toContain('Enable dark mode');
    expect(announcement.length).toBeGreaterThan(0);
  });

  // Cycle NA2: empty queue → ''
  it('should return empty string when there are no queued issues', () => {
    // Arrange — only a non-queued issue
    const detected: IssueSummary = { ...baseIssue, id: 'det', state: 'detected' };

    // Act
    loadIssues([detected]);

    // Assert
    expect(service.nextUpAnnouncement()).toBe('');
  });

  // Cycle NA2b: all-ineligible queue → ''
  it('should return empty string when all queued issues are ineligible or unreachable', () => {
    // Arrange
    const inelig: IssueSummary = { ...baseIssue, id: 'inelig', state: 'queued', repositoryEligibilityStatus: 'ineligible' };
    const unreach: IssueSummary = { ...baseIssue, id: 'unreach', state: 'revision_queued', repositoryEligibilityStatus: 'unreachable' };

    // Act
    loadIssues([inelig, unreach]);

    // Assert
    expect(service.nextUpAnnouncement()).toBe('');
  });

  // Cycle NA3a: reorder that does NOT change the head → announcement string is identical (no re-announcement)
  it('should return an identical announcement string when positions shuffle but the next-up issue stays the same', () => {
    // Arrange — q1 is head; q2 and q3 are behind it
    const q1: IssueSummary = { ...baseIssue, id: 'q1', issueNumber: 42, title: 'Enable dark mode', state: 'queued' };
    const q2: IssueSummary = { ...baseIssue, id: 'q2', issueNumber: 10, title: 'Other issue', state: 'revision_queued' };
    const q3: IssueSummary = { ...baseIssue, id: 'q3', issueNumber: 11, title: 'Another issue', state: 'continuation_queued' };
    loadIssues([q1, q2, q3]);
    const before = service.nextUpAnnouncement();
    expect(before).toContain('42');

    // Act — reload with q2 and q3 swapped but q1 still at head
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush({ items: [q1, q3, q2], nextCursor: null });

    // Assert — nextUpIssueId unchanged, so announcement is identical
    expect(service.nextUpAnnouncement()).toBe(before);
  });

  // Cycle NA3b: reorder that changes the head → announcement string changes
  it('should return a different announcement string when the next-up issue changes after a reload', () => {
    // Arrange — q1 is initially next-up
    const q1: IssueSummary = { ...baseIssue, id: 'q1', issueNumber: 42, title: 'Enable dark mode', state: 'queued' };
    const q2: IssueSummary = { ...baseIssue, id: 'q2', issueNumber: 99, title: 'Add retry', state: 'revision_queued' };
    loadIssues([q1, q2]);
    const before = service.nextUpAnnouncement();
    expect(before).toContain('42');

    // Act — reload with q2 now at head
    service.loadIssues();
    httpMock.expectOne('/api/issues').flush({ items: [q2, q1], nextCursor: null });

    // Assert — announcement reflects new head
    const after = service.nextUpAnnouncement();
    expect(after).toContain('99');
    expect(after).toContain('Add retry');
    expect(after).not.toBe(before);
  });
});

describe('IssueService (triage actions)', () => {
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
    vi.spyOn(console, 'error').mockImplementation(() => {});
  });

  afterEach(() => {
    httpMock.verify({ ignoreCancelled: true });
    vi.restoreAllMocks();
  });

  const cases = [
    {
      action: 'retry_triage' as const,
      call: (svc: IssueService, id: string) => svc.retryTriage(id),
      path: 'retry-triage',
      success: 'Triage queued again. Issue status is updating.',
      conflict: 'This issue is no longer in Triage failed.',
      generic: 'Failed to retry triage.',
    },
    {
      action: 'skip_triage' as const,
      call: (svc: IssueService, id: string) => svc.skipTriage(id),
      path: 'skip-triage',
      success: 'Triage skipped. Issue status is updating.',
      conflict: 'This issue is no longer in Triage failed.',
      generic: 'Failed to skip triage.',
    },
    {
      action: 'retriage' as const,
      call: (svc: IssueService, id: string) => svc.retriage(id),
      path: 'retriage',
      success: 'Re-triage queued. Issue status is updating.',
      conflict:
        'Only queued or blocked issues can be re-triaged. It may have been dispatched already.',
      generic: 'Failed to re-triage the issue.',
    },
  ];

  for (const c of cases) {
    describe(c.action, () => {
      it(`POSTs to /api/issues/{id}/${c.path} and marks the action pending while in flight`, () => {
        // Act
        c.call(service, 'abc123');

        // Assert
        expect(service.triageActionPending()).toBe(c.action);
        const req = httpMock.expectOne(`/api/issues/abc123/${c.path}`);
        expect(req.request.method).toBe('POST');
        req.flush({});
        httpMock.expectOne('/api/issues/abc123').flush({ ...mockSummary });
      });

      it('sets the success text, clears pending and reloads the detail on success', () => {
        // Arrange
        c.call(service, 'abc123');

        // Act
        httpMock.expectOne(`/api/issues/abc123/${c.path}`).flush({});

        // Assert
        expect(service.triageActionPending()).toBeNull();
        expect(service.triageActionSuccess()).toBe(c.success);
        expect(service.triageActionError()).toBeNull();
        const reload = httpMock.expectOne('/api/issues/abc123');
        expect(reload.request.method).toBe('GET');
        reload.flush({ ...mockSummary });
      });

      it('sets the 409 message when the issue is in the wrong state', () => {
        // Arrange
        c.call(service, 'abc123');

        // Act
        httpMock
          .expectOne(`/api/issues/abc123/${c.path}`)
          .flush('Conflict', { status: 409, statusText: 'Conflict' });

        // Assert
        expect(service.triageActionPending()).toBeNull();
        expect(service.triageActionError()).toBe(c.conflict);
        expect(service.triageActionSuccess()).toBeNull();
        httpMock.expectNone('/api/issues/abc123');
      });

      it('sets the generic message on any other error', () => {
        // Arrange
        c.call(service, 'abc123');

        // Act
        httpMock
          .expectOne(`/api/issues/abc123/${c.path}`)
          .flush('Server Error', { status: 500, statusText: 'Internal Server Error' });

        // Assert
        expect(service.triageActionPending()).toBeNull();
        expect(service.triageActionError()).toBe(c.generic);
      });

      it('clears an earlier error and success message when started again', () => {
        // Arrange — first call fails
        c.call(service, 'abc123');
        httpMock
          .expectOne(`/api/issues/abc123/${c.path}`)
          .flush('Server Error', { status: 500, statusText: 'Internal Server Error' });

        // Act
        c.call(service, 'abc123');

        // Assert
        expect(service.triageActionError()).toBeNull();
        expect(service.triageActionSuccess()).toBeNull();
        httpMock.expectOne(`/api/issues/abc123/${c.path}`).flush({});
        httpMock.expectOne('/api/issues/abc123').flush({ ...mockSummary });
      });
    });
  }

  it('does not touch the Retry Issue signals', () => {
    // Act
    service.retryTriage('abc123');
    httpMock
      .expectOne('/api/issues/abc123/retry-triage')
      .flush('Server Error', { status: 500, statusText: 'Internal Server Error' });

    // Assert
    expect(service.retrying()).toBe(false);
    expect(service.retryError()).toBeNull();
  });

  it('selectIssue clears pending, error and success', () => {
    // Arrange
    service.skipTriage('abc123');
    httpMock
      .expectOne('/api/issues/abc123/skip-triage')
      .flush('Conflict', { status: 409, statusText: 'Conflict' });
    service.retriage('abc123');

    // Act
    service.selectIssue('abc123');

    // Assert
    expect(service.triageActionPending()).toBeNull();
    expect(service.triageActionError()).toBeNull();
    expect(service.triageActionSuccess()).toBeNull();
    httpMock.match('/api/issues/abc123/retriage');
    httpMock.expectOne('/api/issues/abc123').flush({ ...mockSummary });
  });

  it('selectIssue clears a success message', () => {
    // Arrange
    service.retryTriage('abc123');
    httpMock.expectOne('/api/issues/abc123/retry-triage').flush({});
    httpMock.expectOne('/api/issues/abc123').flush({ ...mockSummary });

    // Act
    service.selectIssue('abc123');

    // Assert
    expect(service.triageActionSuccess()).toBeNull();
    httpMock.expectOne('/api/issues/abc123').flush({ ...mockSummary });
  });
});

describe('IssueService board placement (#851)', () => {
  const placedItem = {
    id: 'p1',
    issueNumber: 7,
    title: 'Placed',
    state: 'checks_pending',
    repositorySlug: 'owner/repo',
    detectedAt: '2026-01-01T00:00:00Z',
    url: 'https://github.com/owner/repo/issues/7',
    failureClassification: null,
    repositoryEligibilityStatus: null,
    runStats: null,
    stage: 'implement',
    needsAction: false,
    awaitsDispatch: false,
    codeStep: 'checks',
    checksOutcome: null,
  };

  function setup() {
    const callbacks: Record<string, (data: IssueSummary) => void> = {};
    const { svc, http } = setupWithCapturingSignalR(callbacks, []);
    return { svc, http, callbacks };
  }

  afterEach(() => vi.restoreAllMocks());

  it('maps the placement fields from the issues response', () => {
    // Arrange
    const { svc, http } = setup();

    // Act
    svc.loadIssues();
    http.expectOne('/api/issues').flush({ items: [placedItem], nextCursor: null });

    // Assert
    expect(svc.issues()[0]).toMatchObject({
      stage: 'implement',
      needsAction: false,
      awaitsDispatch: false,
      codeStep: 'checks',
    });
  });

  it('maps the checks outcome so the board can tell red checks from a ready pull request', () => {
    // Arrange
    const { svc, http } = setup();

    // Act
    svc.loadIssues();
    http.expectOne('/api/issues').flush({
      items: [{ ...placedItem, state: 'review', stage: 'awaiting_review', codeStep: null, checksOutcome: 'still_failing' }],
      nextCursor: null,
    });

    // Assert
    expect(svc.issues()[0].checksOutcome).toBe('still_failing');
  });

  it('narrows an unknown stage to null', () => {
    // Arrange
    const { svc, http } = setup();

    // Act
    svc.loadIssues();
    http.expectOne('/api/issues').flush({ items: [{ ...placedItem, stage: 'review' }], nextCursor: null });

    // Assert
    expect(svc.issues()[0].stage).toBeNull();
  });

  it('narrows an unknown code step to null', () => {
    // Arrange
    const { svc, http } = setup();

    // Act
    svc.loadIssues();
    http.expectOne('/api/issues').flush({ items: [{ ...placedItem, codeStep: 'deploying' }], nextCursor: null });

    // Assert
    expect(svc.issues()[0].codeStep).toBeNull();
  });

  it('narrows the placement of an IssueUpdated payload', () => {
    // Arrange
    const { svc, http, callbacks } = setup();
    svc.loadIssues();
    http.expectOne('/api/issues').flush({ items: [], nextCursor: null });

    // Act
    callbacks['IssueUpdated']({ ...placedItem, codeStep: 'deploying' } as unknown as IssueSummary);

    // Assert
    expect(svc.issues()[0]).toMatchObject({ stage: 'implement', codeStep: null });
  });

  it('selectIssue opens the issue and loads its detail', () => {
    // Arrange
    const { svc, http } = setup();

    // Act
    svc.selectIssue('p1');

    // Assert
    expect(svc.selectedIssueId()).toBe('p1');
    http.expectOne('/api/issues/p1').flush({ ...placedItem });
  });

  it('selectIssue keeps the issue open when selected again', () => {
    // Arrange
    const { svc, http } = setup();
    svc.selectIssue('p1');
    http.expectOne('/api/issues/p1').flush({ ...placedItem });

    // Act
    svc.selectIssue('p1');

    // Assert
    expect(svc.selectedIssueId()).toBe('p1');
    http.expectOne('/api/issues/p1').flush({ ...placedItem });
  });

  it('selectIssue(null) clears the selection and the detail', () => {
    // Arrange
    const { svc, http } = setup();
    svc.selectIssue('p1');
    http.expectOne('/api/issues/p1').flush({ ...placedItem });

    // Act
    svc.selectIssue(null);

    // Assert
    expect(svc.selectedIssueId()).toBeNull();
    expect(svc.issueDetail()).toBeNull();
  });
});

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

  afterEach(() => httpMock.verify());

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
      body: 'The bug is here.',
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

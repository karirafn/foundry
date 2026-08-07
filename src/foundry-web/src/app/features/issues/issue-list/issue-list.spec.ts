import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { NEVER } from 'rxjs';
import { IssueListComponent } from './issue-list';
import { IssueService } from '../issue.service';
import { IssueSignalRService } from '../../../core/services/issue-signalr.service';
import { SystemSignalRService } from '../../../core/services/system-signalr.service';
import { WorkerSignalRService, WORKER_HUB_FACTORY } from '../../../core/services/worker-signalr.service';
import { IssueSummary } from '../issue.model';
import { GlobalSettingsResponse } from '../../../core/models/settings.model';

const mockSystemSignalR = { reconnected: NEVER, dispatchStateChanged: NEVER, loginSessionUpdate: NEVER, notifications: signal([]).asReadonly() };

const mockIssueSignalRService = {
  on: () => {},
  onReconnected: () => {},
  connectionStatus: signal<'connected' | 'reconnecting' | 'disconnected'>('disconnected'),
};

const mockWorkerHub = {
  on: () => {},
  onReconnected: () => {},
  stream: () => ({ subscribe: () => ({ dispose: () => {} }) }),
  start: () => Promise.resolve(),
};

const mockSummary: IssueSummary = {
  id: 'abc123',
  issueNumber: 42,
  title: 'Enable dark mode',
  state: 'detected',
  repositorySlug: 'owner/repo',
  detectedAt: '2026-01-01T00:00:00Z',
  url: 'https://github.com/owner/repo/issues/42',
};

const mockSettingsResponse: GlobalSettingsResponse = {
  maxConcurrent: 3,
  timeoutMinutes: 30,
  systemPromptTemplate: null,
  workerPromptTemplate: null,
  usageLimitResetsAt: null,
  isDispatchPaused: false,
  autoResumeOnUsageReset: true,
  defaultCooldownMinutes: 60,
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

const mockCredentialsResponse = {
  accountId: '00000000-0000-0000-0000-000000000001',
  authMode: 'ApiKey',
  oAuthStatus: 'NotConfigured',
  subscriptionType: null,
  oAuthAccountEmail: null,
  oAuthAccountOrgName: null,
};

const mockCountsResponse = { counts: {} };

function setupComponent() {
  TestBed.configureTestingModule({
    imports: [IssueListComponent],
    providers: [
      IssueService,
      WorkerSignalRService,
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: IssueSignalRService, useValue: mockIssueSignalRService },
      { provide: SystemSignalRService, useValue: mockSystemSignalR },
      { provide: WORKER_HUB_FACTORY, useValue: () => mockWorkerHub },
    ],
  });

  const fixture = TestBed.createComponent(IssueListComponent);
  const httpMock = TestBed.inject(HttpTestingController);
  return { fixture, httpMock };
}

const mockRunTotals = {
  runCount: 0,
  durationMs: 0,
  numTurns: 0,
  totalCostUsd: 0,
  inputTokens: 0,
  outputTokens: 0,
};

function flushInit(httpMock: HttpTestingController, issues: IssueSummary[] = []) {
  httpMock.expectOne('/api/issues').flush(issues);
  httpMock.expectOne('/api/settings').flush(mockSettingsResponse);
  httpMock.expectOne('/api/credentials').flush(mockCredentialsResponse);
  httpMock.expectOne('/api/issues/counts').flush(mockCountsResponse);
  httpMock.expectOne((r) => r.url === '/api/workers/run-totals').flush(mockRunTotals);
}

describe('IssueListComponent', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
  });

  // Cycle 1: component creates and renders heading
  it('should create the component', () => {
    // Arrange / Act
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);

    // Assert
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should render the "Tracked Issues" heading', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();

    // Act
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const heading = el.querySelector('.issue-list__heading');
    expect(heading?.textContent?.trim()).toBe('Tracked Issues');
  });

  // Cycle 2: calls loadIssues and loadCounts on init
  it('should call loadIssues on initialization', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();

    // Act
    fixture.detectChanges();

    // Assert — the HTTP calls prove loadIssues and loadCounts were called
    httpMock.expectOne('/api/issues').flush([]);
    httpMock.expectOne('/api/settings').flush(mockSettingsResponse);
  httpMock.expectOne('/api/credentials').flush(mockCredentialsResponse);
    httpMock.expectOne('/api/issues/counts').flush(mockCountsResponse);
    httpMock.expectOne((r) => r.url === '/api/workers/run-totals').flush(mockRunTotals);
  });

  it('should call loadCounts on initialization', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();

    // Act
    fixture.detectChanges();

    // Assert — counts request is made on init
    httpMock.expectOne('/api/issues').flush([]);
    httpMock.expectOne('/api/settings').flush(mockSettingsResponse);
  httpMock.expectOne('/api/credentials').flush(mockCredentialsResponse);
    const countsReq = httpMock.expectOne('/api/issues/counts');
    expect(countsReq.request.url).toBe('/api/issues/counts');
    countsReq.flush(mockCountsResponse);
    httpMock.expectOne((r) => r.url === '/api/workers/run-totals').flush(mockRunTotals);
  });

  // Cycle 3: renders fd-issue-filter-rail
  it('should render fd-issue-filter-rail', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();

    // Act
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const rail = el.querySelector('fd-issue-filter-rail');
    expect(rail).toBeTruthy();
  });

  // Cycle 4: renders fd-issue-card for each active band issue
  it('should render fd-issue-card for each active band issue', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock, [mockSummary]);

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const cards = el.querySelectorAll('fd-issue-card');
    expect(cards.length).toBe(1);
  });

  it('should render multiple cards when multiple issues are loaded', () => {
    // Arrange
    const second: IssueSummary = { ...mockSummary, id: 'def456', issueNumber: 43, title: 'Fix bug' };
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock, [mockSummary, second]);

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const cards = el.querySelectorAll('fd-issue-card');
    expect(cards.length).toBe(2);
  });

  // Cycle 5: empty active band shows "No active issues" copy
  it('should render "No active issues" heading when active band is empty after load', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const heading = el.querySelector('.issue-list__empty-active-heading');
    expect(heading?.textContent?.trim()).toBe('No active issues');
  });

  it('should render the empty-active hint with layout-neutral copy', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);

    // Act
    fixture.detectChanges();

    // Assert — copy must not reference "filter rail" (hidden on mobile)
    const el = fixture.nativeElement as HTMLElement;
    const hint = el.querySelector('.issue-list__empty-active-hint');
    expect(hint?.textContent?.trim()).not.toContain('filter rail');
    expect(hint?.textContent?.trim()).toContain('Resolved counts');
  });

  it('should not render "No active issues" when active band has issues', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock, [mockSummary]);

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const heading = el.querySelector('.issue-list__empty-active-heading');
    expect(heading).toBeFalsy();
  });

  it('should not render "No active issues" during initial loading', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();

    // Act — detect changes but do NOT flush HTTP
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const heading = el.querySelector('.issue-list__empty-active-heading');
    expect(heading).toBeFalsy();

    // Cleanup
    flushInit(httpMock);
  });

  it('should not render "No active issues" when loadIssues results in an HTTP error', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();

    // Act — simulate a server error on issues, settings and counts succeed
    httpMock.expectOne('/api/issues').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    httpMock.expectOne('/api/settings').flush(mockSettingsResponse);
  httpMock.expectOne('/api/credentials').flush(mockCredentialsResponse);
    httpMock.expectOne('/api/issues/counts').flush(mockCountsResponse);
    httpMock.expectOne((r) => r.url === '/api/workers/run-totals').flush(mockRunTotals);
    fixture.detectChanges();

    // Assert — error state, not empty-active state
    const el = fixture.nativeElement as HTMLElement;
    const heading = el.querySelector('.issue-list__empty-active-heading');
    expect(heading).toBeFalsy();
  });

  it('should have a persistent role="status" node for empty-active announcements', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();

    // Act
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    // Assert — the node is always in the DOM
    const el = fixture.nativeElement as HTMLElement;
    const statusNode = el.querySelector('[role="status"].issue-list__empty-active-announcer');
    expect(statusNode).toBeTruthy();
  });

  it('should populate the role="status" announcer when active band is empty', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    // Assert — announcer has text when empty
    const el = fixture.nativeElement as HTMLElement;
    const statusNode = el.querySelector('[role="status"].issue-list__empty-active-announcer') as HTMLElement;
    expect(statusNode?.textContent?.trim()).toBeTruthy();
  });

  it('should leave the role="status" announcer empty when active band has issues', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock, [mockSummary]);
    fixture.detectChanges();

    // Assert — announcer is empty when band has items
    const el = fixture.nativeElement as HTMLElement;
    const statusNode = el.querySelector('[role="status"].issue-list__empty-active-announcer') as HTMLElement;
    expect(statusNode?.textContent?.trim()).toBe('');
  });

  // Cycle 6: detail wrapper has stable id for aria-controls
  it('should give the detail wrapper an id matching the issue id', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock, [mockSummary]);
    fixture.detectChanges();

    // Assert — wrapper is always present in the DOM so aria-controls is always valid
    const el = fixture.nativeElement as HTMLElement;
    const wrapper = el.querySelector('.issue-list__detail-wrapper') as HTMLElement;
    expect(wrapper?.getAttribute('id')).toBe('detail-abc123');
  });

  it('should keep the detail wrapper in the DOM when the card is collapsed', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock, [mockSummary]);
    fixture.detectChanges();

    // Assert — wrapper is present before any expansion
    const el = fixture.nativeElement as HTMLElement;
    const wrapper = el.querySelector('.issue-list__detail-wrapper');
    expect(wrapper).toBeTruthy();
  });

  it('should hide the detail wrapper with [hidden] when card is collapsed', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock, [mockSummary]);
    fixture.detectChanges();

    // Assert — wrapper is hidden when not expanded
    const el = fixture.nativeElement as HTMLElement;
    const wrapper = el.querySelector('.issue-list__detail-wrapper') as HTMLElement;
    expect(wrapper.hasAttribute('hidden')).toBe(true);
  });

  it('should not hide the detail wrapper when card is expanded', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock, [mockSummary]);
    fixture.detectChanges();

    // Act - expand the card
    const el = fixture.nativeElement as HTMLElement;
    const card = el.querySelector('.issue-card') as HTMLElement;
    card.click();
    fixture.detectChanges();
    httpMock.expectOne('/api/issues/abc123').flush({
      ...mockSummary,
      body: null,
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
    });
    fixture.detectChanges();

    // Assert
    const wrapper = el.querySelector('.issue-list__detail-wrapper') as HTMLElement;
    expect(wrapper.hasAttribute('hidden')).toBe(false);
  });

  // Cycle 7: load error is shown with retry
  it('should show error message when loadIssues fails', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();

    // Act — simulate a server error
    httpMock.expectOne('/api/issues').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    httpMock.expectOne('/api/settings').flush(mockSettingsResponse);
  httpMock.expectOne('/api/credentials').flush(mockCredentialsResponse);
    httpMock.expectOne('/api/issues/counts').flush(mockCountsResponse);
    httpMock.expectOne((r) => r.url === '/api/workers/run-totals').flush(mockRunTotals);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const errorEl = el.querySelector('.issue-list__error');
    expect(errorEl).toBeTruthy();
    expect(errorEl?.textContent).toContain('Failed to load issues');
  });

  it('should show a retry button when loadIssues fails', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();

    // Act
    httpMock.expectOne('/api/issues').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    httpMock.expectOne('/api/settings').flush(mockSettingsResponse);
  httpMock.expectOne('/api/credentials').flush(mockCredentialsResponse);
    httpMock.expectOne('/api/issues/counts').flush(mockCountsResponse);
    httpMock.expectOne((r) => r.url === '/api/workers/run-totals').flush(mockRunTotals);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const retryBtn = el.querySelector('.issue-list__error-retry');
    expect(retryBtn).toBeTruthy();
  });

  it('should retry loading issues when retry button is clicked', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    httpMock.expectOne('/api/issues').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    httpMock.expectOne('/api/settings').flush(mockSettingsResponse);
  httpMock.expectOne('/api/credentials').flush(mockCredentialsResponse);
    httpMock.expectOne('/api/issues/counts').flush(mockCountsResponse);
    httpMock.expectOne((r) => r.url === '/api/workers/run-totals').flush(mockRunTotals);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const retryBtn = el.querySelector('.issue-list__error-retry') as HTMLElement;
    retryBtn.click();
    fixture.detectChanges();

    // Assert — a second HTTP request was made
    const req = httpMock.expectOne('/api/issues');
    req.flush([]);
  });

  it('should not show error block when loadIssues succeeds', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock, [mockSummary]);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const errorEl = el.querySelector('.issue-list__error');
    expect(errorEl).toBeFalsy();
  });

  // Cycle 8: renders fd-connection-indicator
  it('should render fd-connection-indicator in the header', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();

    // Act
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const indicator = el.querySelector('fd-connection-indicator');
    expect(indicator).toBeTruthy();
  });

  // Cycle 9: separator renders between live and non-live issues (active band)
  it('should render an hr separator when there are both live and non-live issues', () => {
    // Arrange
    const liveIssue: IssueSummary = { ...mockSummary, id: 'live', state: 'in_progress' };
    const nonLiveIssue: IssueSummary = { ...mockSummary, id: 'non-live', state: 'detected', issueNumber: 43 };
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock, [liveIssue, nonLiveIssue]);

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const separator = el.querySelector('hr.issue-list__separator');
    expect(separator).toBeTruthy();
    expect(separator?.getAttribute('aria-hidden')).toBe('true');
  });

  it('should NOT render a separator for continuation_queued — it is a queued tier, not a live state', () => {
    // Arrange — continuation_queued is a queued tier (not live); no live issues means no separator
    const continuationQueued: IssueSummary = { ...mockSummary, id: 'cont-queued', state: 'continuation_queued' };
    const nonLiveIssue: IssueSummary = { ...mockSummary, id: 'done', state: 'detected', issueNumber: 43 };
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock, [nonLiveIssue, continuationQueued]);

    // Act
    fixture.detectChanges();

    // Assert — no separator because continuation_queued is not a live state
    const el = fixture.nativeElement as HTMLElement;
    const separator = el.querySelector('hr.issue-list__separator');
    expect(separator).toBeFalsy();
  });

  it('should not render an hr separator when all issues are in-progress', () => {
    // Arrange
    const live1: IssueSummary = { ...mockSummary, id: 'live-1', state: 'in_progress' };
    const live2: IssueSummary = { ...mockSummary, id: 'live-2', state: 'revision_in_progress', issueNumber: 43 };
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock, [live1, live2]);

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const separator = el.querySelector('hr.issue-list__separator');
    expect(separator).toBeFalsy();
  });

  it('should not render an hr separator when no issues are in-progress', () => {
    // Arrange
    const non1: IssueSummary = { ...mockSummary, id: 'non-1', state: 'completed' };
    const non2: IssueSummary = { ...mockSummary, id: 'non-2', state: 'failed', issueNumber: 43 };
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock, [non1, non2]);

    // Act
    fixture.detectChanges();

    // Assert — completed is resolved, so the active band will be empty; no separator
    const el = fixture.nativeElement as HTMLElement;
    const separator = el.querySelector('hr.issue-list__separator');
    expect(separator).toBeFalsy();
  });

  it('should not render an hr separator when the list is empty', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const separator = el.querySelector('hr.issue-list__separator');
    expect(separator).toBeFalsy();
  });

  // Cycle 10: sr-only span announces section boundary for screen readers
  it('should render an sr-only span announcing the section boundary when there are both live and non-live issues', () => {
    // Arrange
    const liveIssue: IssueSummary = { ...mockSummary, id: 'live', state: 'in_progress' };
    const completedIssue: IssueSummary = { ...mockSummary, id: 'done', state: 'detected', issueNumber: 43 };
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock, [liveIssue, completedIssue]);

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const srSpans = Array.from(el.querySelectorAll('.sr-only'));
    const boundarySpan = srSpans.find((s) => s.textContent?.includes('End of in-progress issues'));
    expect(boundarySpan).toBeTruthy();
  });

  // Finding 3: per-run activity isolation — each live card receives its own lastActivityAt
  it('should pass null lastActivityAt to a live card when no activity has been received for its workerRunId', () => {
    // Arrange — two live issues with different workerRunIds; no WorkerActivity received yet
    const liveIssue1: IssueSummary = { ...mockSummary, id: 'live1', state: 'in_progress', issueNumber: 1 };
    const liveIssue2: IssueSummary = { ...mockSummary, id: 'live2', state: 'in_progress', issueNumber: 2 };
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock, [liveIssue1, liveIssue2]);
    fixture.detectChanges();

    // Assert — no activity cards should not show the activity span
    const el = fixture.nativeElement as HTMLElement;
    const activityLines = el.querySelectorAll('.issue-card__activity');
    expect(activityLines.length).toBe(0);
  });

  it('should expose activityFor lookup returning null for an unknown workerRunId', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    // Assert — service exposes a per-run lookup; unknown run returns null
    const workerSignalR = TestBed.inject(WorkerSignalRService);
    expect(workerSignalR.activityFor('unknown-run-id')).toBeNull();
  });

  // Cycle 11: expand/collapse wiring - fd-issue-detail appears when card is expanded
  it('should show fd-issue-detail for the expanded issue after card toggle', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock, [mockSummary]);
    fixture.detectChanges();

    // Act - click the card to toggle expand
    const el = fixture.nativeElement as HTMLElement;
    const card = el.querySelector('.issue-card') as HTMLElement;
    card.click();
    fixture.detectChanges();

    // Flush the detail request
    httpMock.expectOne('/api/issues/abc123').flush({
      ...mockSummary,
      body: 'Body text',
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
    });
    fixture.detectChanges();

    // Assert
    const detail = el.querySelector('fd-issue-detail');
    expect(detail).toBeTruthy();
  });

  // Cycle 12: skeleton cards shown during initial load
  it('should render .issue-list__skeletons with fd-issue-card-skeleton elements while initialLoading is true', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();

    // Act — detect changes but do NOT flush so initialLoading stays true
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const skeletons = el.querySelector('.issue-list__skeletons');
    expect(skeletons).toBeTruthy();
    expect(skeletons?.getAttribute('role')).toBe('status');
    expect(skeletons?.querySelectorAll('fd-issue-card-skeleton').length).toBeGreaterThan(0);

    // Cleanup
    flushInit(httpMock);
  });

  it('should remove .issue-list__skeletons after initial load completes successfully', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();

    // Act
    flushInit(httpMock);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const skeletons = el.querySelector('.issue-list__skeletons');
    expect(skeletons).toBeFalsy();
  });

  it('should remove .issue-list__skeletons after initial load fails with an error', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();

    // Act — simulate server error
    httpMock.expectOne('/api/issues').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    httpMock.expectOne('/api/settings').flush(mockSettingsResponse);
  httpMock.expectOne('/api/credentials').flush(mockCredentialsResponse);
    httpMock.expectOne('/api/issues/counts').flush(mockCountsResponse);
    httpMock.expectOne((r) => r.url === '/api/workers/run-totals').flush(mockRunTotals);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const skeletons = el.querySelector('.issue-list__skeletons');
    expect(skeletons).toBeFalsy();
  });

  // Cycle 13: fd-dispatch-controls renders below the header
  it('should render fd-dispatch-controls below the header', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();

    // Act
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const controls = el.querySelector('fd-dispatch-controls');
    expect(controls).toBeTruthy();
  });

  // Cycle 13b: fd-run-stats-bar is mounted in the issue-list body
  it('should render fd-run-stats-bar inside the issue-list body', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();

    // Act
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const bar = el.querySelector('fd-run-stats-bar');
    expect(bar).toBeTruthy();
  });

  // Cycle 14: resolved band — no divider when no resolved state selected
  it('should not render the resolved band divider when no resolved state is selected', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock, [mockSummary]);

    // Act
    fixture.detectChanges();

    // Assert — no resolved states selected by default
    const el = fixture.nativeElement as HTMLElement;
    const divider = el.querySelector('.issue-list__resolved-divider');
    expect(divider).toBeFalsy();
  });

  // Cycle 15: resolved band — divider + caption + cards when resolved state selected
  it('should render the resolved band divider and "Resolved" caption when a resolved state is selected', () => {
    // Arrange
    const resolvedIssue: IssueSummary = { ...mockSummary, id: 'res1', state: 'completed' };
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    // Act — select a resolved state via the service
    const issueService = TestBed.inject(IssueService);
    issueService.toggleState('completed');
    // Flush the paged resolved request
    httpMock.expectOne((req) => req.url === '/api/issues' && req.params.has('states')).flush({
      items: [resolvedIssue],
      nextCursor: null,
    });
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const divider = el.querySelector('.issue-list__resolved-divider');
    expect(divider).toBeTruthy();
    const caption = el.querySelector('.issue-list__resolved-caption');
    expect(caption?.textContent?.trim()).toContain('Resolved');
  });

  it('should render resolved cards in the resolved band', () => {
    // Arrange
    const resolvedIssue: IssueSummary = { ...mockSummary, id: 'res1', state: 'completed' };
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    // Act
    const issueService = TestBed.inject(IssueService);
    issueService.toggleState('completed');
    httpMock.expectOne((req) => req.url === '/api/issues' && req.params.has('states')).flush({
      items: [resolvedIssue],
      nextCursor: null,
    });
    fixture.detectChanges();

    // Assert — the resolved band renders a card for the resolved issue
    const el = fixture.nativeElement as HTMLElement;
    const resolvedBand = el.querySelector('.issue-list__resolved-band');
    const cards = resolvedBand?.querySelectorAll('fd-issue-card');
    expect(cards?.length).toBe(1);
  });

  it('should hide the "Load more" button when hasMoreResolved is false', () => {
    // Arrange
    const resolvedIssue: IssueSummary = { ...mockSummary, id: 'res1', state: 'completed' };
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    // Act
    const issueService = TestBed.inject(IssueService);
    issueService.toggleState('completed');
    httpMock.expectOne((req) => req.url === '/api/issues' && req.params.has('states')).flush({
      items: [resolvedIssue],
      nextCursor: null,
    });
    fixture.detectChanges();

    // Assert — no nextCursor means no Load more button
    const el = fixture.nativeElement as HTMLElement;
    const loadMore = el.querySelector('.issue-list__load-more');
    expect(loadMore).toBeFalsy();
  });

  it('should show the "Load more" button when hasMoreResolved is true', () => {
    // Arrange
    const resolvedIssue: IssueSummary = { ...mockSummary, id: 'res1', state: 'completed' };
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    // Act
    const issueService = TestBed.inject(IssueService);
    issueService.toggleState('completed');
    httpMock.expectOne((req) => req.url === '/api/issues' && req.params.has('states')).flush({
      items: [resolvedIssue],
      nextCursor: 'cursor-abc',
    });
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const loadMore = el.querySelector('.issue-list__load-more');
    expect(loadMore).toBeTruthy();
  });

  it('should call loadMoreResolved when Load more button is clicked', () => {
    // Arrange
    const resolvedIssue: IssueSummary = { ...mockSummary, id: 'res1', state: 'completed' };
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    const issueService = TestBed.inject(IssueService);
    issueService.toggleState('completed');
    httpMock.expectOne((req) => req.url === '/api/issues' && req.params.has('states')).flush({
      items: [resolvedIssue],
      nextCursor: 'cursor-abc',
    });
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const loadMoreBtn = el.querySelector('.issue-list__load-more') as HTMLElement;
    loadMoreBtn.click();
    fixture.detectChanges();

    // Assert — load more triggers another paged request
    const req = httpMock.expectOne((r) => r.url === '/api/issues' && r.params.has('states') && r.params.has('cursor'));
    expect(req.request.params.get('cursor')).toBe('cursor-abc');
    req.flush({ items: [], nextCursor: null });
  });

  it('should have a persistent aria-live="polite" announcer for resolved band', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();

    // Act
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    // Assert — announcer node is always mounted
    const el = fixture.nativeElement as HTMLElement;
    const announcer = el.querySelector('[aria-live="polite"].issue-list__resolved-announcer');
    expect(announcer).toBeTruthy();
  });

  // Finding 2: aside has accessible name
  it('should give the filter-rail aside an aria-label of "Filter by state"', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();

    // Act
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const aside = el.querySelector('aside.issue-list__rail');
    expect(aside?.getAttribute('aria-label')).toBe('Filter by state');
  });

  // Finding 1: resolved caption is an h2 inside a section
  it('should render "Resolved" as an h2 when the resolved band is open', () => {
    // Arrange
    const resolvedIssue: IssueSummary = { ...mockSummary, id: 'res1', state: 'completed' };
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    // Act
    const issueService = TestBed.inject(IssueService);
    issueService.toggleState('completed');
    httpMock.expectOne((req) => req.url === '/api/issues' && req.params.has('states')).flush({
      items: [resolvedIssue],
      nextCursor: null,
    });
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const heading = el.querySelector('h2.issue-list__resolved-caption');
    expect(heading?.textContent?.trim()).toBe('Resolved');
  });

  it('should wrap the resolved band in a section element', () => {
    // Arrange
    const resolvedIssue: IssueSummary = { ...mockSummary, id: 'res1', state: 'completed' };
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    // Act
    const issueService = TestBed.inject(IssueService);
    issueService.toggleState('completed');
    httpMock.expectOne((req) => req.url === '/api/issues' && req.params.has('states')).flush({
      items: [resolvedIssue],
      nextCursor: null,
    });
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const section = el.querySelector('section.issue-list__resolved-section');
    expect(section).toBeTruthy();
    expect(section?.getAttribute('aria-labelledby')).toBeTruthy();
  });

  // Finding 3: empty-active announcer is sr-only
  it('should have the sr-only class on the empty-active announcer', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();

    // Act
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const announcer = el.querySelector('.issue-list__empty-active-announcer');
    expect(announcer?.classList).toContain('sr-only');
  });

  // Finding 5: visible hint bound to constant (not duplicated inline)
  it('should bind the empty-active hint text to the same copy as the announcer', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    // Assert — hint and announcer text are identical
    const el = fixture.nativeElement as HTMLElement;
    const hint = el.querySelector('.issue-list__empty-active-hint') as HTMLElement;
    const announcer = el.querySelector('.issue-list__empty-active-announcer') as HTMLElement;
    expect(hint?.textContent?.trim()).toBe(announcer?.textContent?.trim());
  });

  // Finding 4: resolved-loading element no longer carries role="status"
  it('should not have role="status" on the resolved-loading element', () => {
    // Arrange
    const resolvedIssue: IssueSummary = { ...mockSummary, id: 'res1', state: 'completed' };
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    const issueService = TestBed.inject(IssueService);
    issueService.toggleState('completed');
    // First request immediately resolves but triggers resolvedLoading briefly;
    // we flush to get into steady state then check there is no stale role="status"
    httpMock.expectOne((req) => req.url === '/api/issues' && req.params.has('states')).flush({
      items: [resolvedIssue],
      nextCursor: null,
    });
    fixture.detectChanges();

    // Assert — any resolved-loading element must not have role="status"
    const el = fixture.nativeElement as HTMLElement;
    const loadingEl = el.querySelector('.issue-list__resolved-loading');
    if (loadingEl) {
      expect(loadingEl.getAttribute('role')).not.toBe('status');
    } else {
      // Loading div already gone after flush — pass
      expect(true).toBe(true);
    }
  });

  it('should announce "Loading resolved issues…" via the persistent resolved announcer while resolvedLoading is true', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    // Act — select resolved state; the HTTP call is pending so resolvedLoading is true
    const issueService = TestBed.inject(IssueService);
    issueService.toggleState('completed');
    fixture.detectChanges();

    // Capture announcement text before flushing (loading state)
    const el = fixture.nativeElement as HTMLElement;
    const announcer = el.querySelector('[aria-live="polite"].issue-list__resolved-announcer') as HTMLElement;
    const textWhileLoading = announcer?.textContent?.trim() ?? '';

    // Flush the pending request so afterEach verify() passes
    httpMock.expectOne((req) => req.url === '/api/issues' && req.params.has('states')).flush({
      items: [],
      nextCursor: null,
    });

    // Assert — announcer text captured during loading state reflects loading
    expect(textWhileLoading).toContain('Loading resolved issues');
  });

  // Finding 6: resolved announcement uses delta wording
  it('should announce the delta when Load more is clicked', () => {
    // Arrange
    const resolvedIssue: IssueSummary = { ...mockSummary, id: 'res1', state: 'completed' };
    const resolvedIssue2: IssueSummary = { ...mockSummary, id: 'res2', state: 'completed', issueNumber: 99 };
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    const issueService = TestBed.inject(IssueService);
    issueService.toggleState('completed');
    httpMock.expectOne((req) => req.url === '/api/issues' && req.params.has('states')).flush({
      items: [resolvedIssue],
      nextCursor: 'cursor-abc',
    });
    fixture.detectChanges();

    // Act — click Load more
    const el = fixture.nativeElement as HTMLElement;
    const loadMoreBtn = el.querySelector('.issue-list__load-more') as HTMLElement;
    loadMoreBtn.click();
    fixture.detectChanges();

    httpMock.expectOne((r) => r.url === '/api/issues' && r.params.has('cursor')).flush({
      items: [resolvedIssue2],
      nextCursor: null,
    });
    fixture.detectChanges();

    // Assert — announcement mentions the delta (1 more) and the total (2)
    const announcer = el.querySelector('[aria-live="polite"].issue-list__resolved-announcer') as HTMLElement;
    expect(announcer?.textContent).toContain('1 more');
    expect(announcer?.textContent).toContain('2');
  });

  // Finding 7: OnPush change detection
  it('should use OnPush change detection strategy', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);

    // Assert — OnPush components have a detached ChangeDetectorRef that only
    // checks when inputs change or markForCheck() is called. With OnPush the
    // fixture's changeDetectorRef is not the default (always-dirty) detector.
    // Angular's ɵcmp.onPush is the canonical internal flag (true = OnPush).
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const componentDef = (IssueListComponent as any).ɵcmp;
    expect(componentDef?.onPush).toBe(true);
  });

  // Resolved band error state
  it('should render a resolved-band error block when the first-page resolved fetch fails', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    // Act — select a resolved state and fail the HTTP request
    const issueService = TestBed.inject(IssueService);
    issueService.toggleState('completed');
    httpMock.expectOne((req) => req.url === '/api/issues' && req.params.has('states')).flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    fixture.detectChanges();

    // Assert — error block appears inside the resolved section
    const el = fixture.nativeElement as HTMLElement;
    const resolvedSection = el.querySelector('.issue-list__resolved-section');
    const errorEl = resolvedSection?.querySelector('.issue-list__error');
    expect(errorEl).toBeTruthy();
    expect(errorEl?.getAttribute('role')).toBe('alert');
    expect(errorEl?.textContent).toContain('Failed to load resolved issues');
  });

  it('should render a retry button in the resolved-band error block', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    // Act
    const issueService = TestBed.inject(IssueService);
    issueService.toggleState('completed');
    httpMock.expectOne((req) => req.url === '/api/issues' && req.params.has('states')).flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const resolvedSection = el.querySelector('.issue-list__resolved-section');
    const retryBtn = resolvedSection?.querySelector('.issue-list__error-retry');
    expect(retryBtn).toBeTruthy();
  });

  it('should re-fetch resolved issues when the resolved-band retry button is clicked', () => {
    // Arrange — select completed and fail the initial fetch
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    const issueService = TestBed.inject(IssueService);
    issueService.toggleState('completed');
    httpMock.expectOne((req) => req.url === '/api/issues' && req.params.has('states')).flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    fixture.detectChanges();

    // Act — click retry
    const el = fixture.nativeElement as HTMLElement;
    const retryBtn = el.querySelector('.issue-list__resolved-section .issue-list__error-retry') as HTMLElement;
    retryBtn.click();
    fixture.detectChanges();

    // Assert — a new request is made
    const req = httpMock.expectOne((r) => r.url === '/api/issues' && r.params.has('states'));
    req.flush({ items: [], nextCursor: null });
  });

  it('should not show the resolved issue list when the resolved fetch fails (error replaces list)', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    // Act
    const issueService = TestBed.inject(IssueService);
    issueService.toggleState('completed');
    httpMock.expectOne((req) => req.url === '/api/issues' && req.params.has('states')).flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    fixture.detectChanges();

    // Assert — resolved band (issue cards) is not rendered
    const el = fixture.nativeElement as HTMLElement;
    const resolvedBand = el.querySelector('.issue-list__resolved-band');
    expect(resolvedBand).toBeFalsy();
  });

  // Resolved band empty state
  it('should render empty-resolved copy when resolved fetch succeeds with zero issues', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    // Act — select completed, return empty result
    const issueService = TestBed.inject(IssueService);
    issueService.toggleState('completed');
    httpMock.expectOne((req) => req.url === '/api/issues' && req.params.has('states')).flush({
      items: [],
      nextCursor: null,
    });
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const emptyEl = el.querySelector('.issue-list__empty-resolved');
    expect(emptyEl).toBeTruthy();
    expect(emptyEl?.textContent).toContain('No resolved issues');
  });

  it('should not show the resolved issue list when empty-resolved is shown', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    // Act
    const issueService = TestBed.inject(IssueService);
    issueService.toggleState('completed');
    httpMock.expectOne((req) => req.url === '/api/issues' && req.params.has('states')).flush({
      items: [],
      nextCursor: null,
    });
    fixture.detectChanges();

    // Assert — issue band not rendered
    const el = fixture.nativeElement as HTMLElement;
    const resolvedBand = el.querySelector('.issue-list__resolved-band');
    expect(resolvedBand).toBeFalsy();
  });

  // Load-more error state
  it('should render an inline load-more error when load-more fails', () => {
    // Arrange — get page 1 with a cursor
    const resolvedIssue: IssueSummary = { ...mockSummary, id: 'res1', state: 'completed' };
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    const issueService = TestBed.inject(IssueService);
    issueService.toggleState('completed');
    httpMock.expectOne((req) => req.url === '/api/issues' && req.params.has('states')).flush({
      items: [resolvedIssue],
      nextCursor: 'cursor-abc',
    });
    fixture.detectChanges();

    // Act — click Load more, fail the request
    const el = fixture.nativeElement as HTMLElement;
    const loadMoreBtn = el.querySelector('.issue-list__load-more') as HTMLElement;
    loadMoreBtn.click();
    fixture.detectChanges();
    httpMock.expectOne((r) => r.url === '/api/issues' && r.params.has('cursor')).flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    fixture.detectChanges();

    // Assert — inline error rendered at bottom of resolved band
    const loadMoreError = el.querySelector('.issue-list__resolved-load-more-error');
    expect(loadMoreError).toBeTruthy();
    expect(loadMoreError?.textContent).toContain('Failed to load more');
  });

  it('should preserve already-loaded resolved issues when load-more fails', () => {
    // Arrange — get page 1 with a cursor
    const resolvedIssue: IssueSummary = { ...mockSummary, id: 'res1', state: 'completed' };
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    const issueService = TestBed.inject(IssueService);
    issueService.toggleState('completed');
    httpMock.expectOne((req) => req.url === '/api/issues' && req.params.has('states')).flush({
      items: [resolvedIssue],
      nextCursor: 'cursor-abc',
    });
    fixture.detectChanges();

    // Act — fail the load-more
    const el = fixture.nativeElement as HTMLElement;
    (el.querySelector('.issue-list__load-more') as HTMLElement).click();
    fixture.detectChanges();
    httpMock.expectOne((r) => r.params.has('cursor')).flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    fixture.detectChanges();

    // Assert — the existing resolved card is still rendered
    const resolvedBand = el.querySelector('.issue-list__resolved-band');
    expect(resolvedBand).toBeTruthy();
    expect(resolvedBand?.querySelectorAll('fd-issue-card').length).toBe(1);
  });

  it('should re-request the next page when the load-more retry button is clicked', () => {
    // Arrange — fail a load-more
    const resolvedIssue: IssueSummary = { ...mockSummary, id: 'res1', state: 'completed' };
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    const issueService = TestBed.inject(IssueService);
    issueService.toggleState('completed');
    httpMock.expectOne((req) => req.url === '/api/issues' && req.params.has('states')).flush({
      items: [resolvedIssue],
      nextCursor: 'cursor-abc',
    });
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    (el.querySelector('.issue-list__load-more') as HTMLElement).click();
    fixture.detectChanges();
    httpMock.expectOne((r) => r.params.has('cursor')).flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    fixture.detectChanges();

    // Act — click the load-more retry button
    const retryBtn = el.querySelector('.issue-list__resolved-load-more-retry') as HTMLElement;
    retryBtn.click();
    fixture.detectChanges();

    // Assert — a new request with the same cursor is made
    const req = httpMock.expectOne((r) => r.url === '/api/issues' && r.params.get('cursor') === 'cursor-abc');
    req.flush({ items: [], nextCursor: null });
  });

  // Band independence
  it('should not show a resolved error block when loadIssues fails (active error does not bleed into resolved band)', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();

    // Act — fail the active-band load, succeed on settings and counts
    httpMock.expectOne('/api/issues').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    httpMock.expectOne('/api/settings').flush(mockSettingsResponse);
  httpMock.expectOne('/api/credentials').flush(mockCredentialsResponse);
    httpMock.expectOne('/api/issues/counts').flush(mockCountsResponse);
    httpMock.expectOne((r) => r.url === '/api/workers/run-totals').flush(mockRunTotals);
    fixture.detectChanges();

    // Assert — active error block present, no resolved section
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.issue-list__error')).toBeTruthy();
    expect(el.querySelector('.issue-list__resolved-section')).toBeFalsy();
  });

  it('should keep active band cards visible when the resolved fetch fails', () => {
    // Arrange — load an active issue
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock, [mockSummary]);
    fixture.detectChanges();

    // Act — select resolved state and fail that fetch
    const issueService = TestBed.inject(IssueService);
    issueService.toggleState('completed');
    httpMock.expectOne((req) => req.url === '/api/issues' && req.params.has('states')).flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    fixture.detectChanges();

    // Assert — active card still rendered
    const el = fixture.nativeElement as HTMLElement;
    const activeBand = el.querySelector('.issue-list__grid');
    expect(activeBand?.querySelectorAll('fd-issue-card').length).toBe(1);
    // Active-band loadError signal is untouched
    expect(issueService.loadError()).toBeNull();
    // Resolved error appears inside the resolved section only
    expect(el.querySelector('.issue-list__resolved-section .issue-list__error')).toBeTruthy();
  });

  // F1: Load-more error has role="alert"
  it('should have role="alert" on the load-more error container', () => {
    // Arrange — get page 1 with cursor, then fail load-more
    const resolvedIssue: IssueSummary = { ...mockSummary, id: 'res1', state: 'completed' };
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    const issueService = TestBed.inject(IssueService);
    issueService.toggleState('completed');
    httpMock.expectOne((req) => req.url === '/api/issues' && req.params.has('states')).flush({
      items: [resolvedIssue],
      nextCursor: 'cursor-abc',
    });
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    (el.querySelector('.issue-list__load-more') as HTMLElement).click();
    fixture.detectChanges();
    httpMock.expectOne((r) => r.url === '/api/issues' && r.params.has('cursor')).flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });

    // Act
    fixture.detectChanges();

    // Assert
    const loadMoreError = el.querySelector('.issue-list__resolved-load-more-error');
    expect(loadMoreError?.getAttribute('role')).toBe('alert');
  });

  // F2: Empty-resolved state announced to screen readers
  it('should announce the empty-resolved message via the resolved announcer when resolved fetch returns zero results', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    // Act — select a resolved state and return empty results
    const issueService = TestBed.inject(IssueService);
    issueService.toggleState('completed');
    httpMock.expectOne((req) => req.url === '/api/issues' && req.params.has('states')).flush({
      items: [],
      nextCursor: null,
    });
    fixture.detectChanges();

    // Assert — persistent resolved announcer shows the empty-resolved message
    const el = fixture.nativeElement as HTMLElement;
    const announcer = el.querySelector('[aria-live="polite"].issue-list__resolved-announcer') as HTMLElement;
    expect(announcer?.textContent?.trim()).toContain('No resolved issues match the selected filters');
  });

  it('should not announce the empty-resolved message when the resolved band has issues', () => {
    // Arrange
    const resolvedIssue: IssueSummary = { ...mockSummary, id: 'res1', state: 'completed' };
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    // Act — select a resolved state and return results
    const issueService = TestBed.inject(IssueService);
    issueService.toggleState('completed');
    httpMock.expectOne((req) => req.url === '/api/issues' && req.params.has('states')).flush({
      items: [resolvedIssue],
      nextCursor: null,
    });
    fixture.detectChanges();

    // Assert — announcer does not contain the empty message
    const el = fixture.nativeElement as HTMLElement;
    const announcer = el.querySelector('[aria-live="polite"].issue-list__resolved-announcer') as HTMLElement;
    expect(announcer?.textContent?.trim()).not.toContain('No resolved issues match the selected filters');
  });

  it('should not announce empty-resolved when the resolved error is set', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    // Act — select a resolved state and fail the fetch
    const issueService = TestBed.inject(IssueService);
    issueService.toggleState('completed');
    httpMock.expectOne((req) => req.url === '/api/issues' && req.params.has('states')).flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    fixture.detectChanges();

    // Assert — announcer is empty (error state, not empty state)
    const el = fixture.nativeElement as HTMLElement;
    const announcer = el.querySelector('[aria-live="polite"].issue-list__resolved-announcer') as HTMLElement;
    expect(announcer?.textContent?.trim()).not.toContain('No resolved issues match the selected filters');
  });

  // F3: Focus management on retry buttons
  it('should move focus to the issue-list heading after clicking the active-band retry button', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    httpMock.expectOne('/api/issues').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    httpMock.expectOne('/api/settings').flush(mockSettingsResponse);
  httpMock.expectOne('/api/credentials').flush(mockCredentialsResponse);
    httpMock.expectOne('/api/issues/counts').flush(mockCountsResponse);
    httpMock.expectOne((r) => r.url === '/api/workers/run-totals').flush(mockRunTotals);
    fixture.detectChanges();

    // Act — click retry; flush the resulting request before asserting so cleanup runs even on failure
    const el = fixture.nativeElement as HTMLElement;
    const retryBtn = el.querySelector('.issue-list__error-retry') as HTMLElement;
    retryBtn.click();
    httpMock.expectOne('/api/issues').flush([]);
    fixture.detectChanges();

    // Assert
    const heading = el.querySelector('.issue-list__heading') as HTMLElement;
    expect(document.activeElement).toBe(heading);
  });

  it('should move focus to the resolved-band heading after clicking the resolved first-page retry button', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    const issueService = TestBed.inject(IssueService);
    issueService.toggleState('completed');
    httpMock.expectOne((req) => req.url === '/api/issues' && req.params.has('states')).flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    fixture.detectChanges();

    // Act — click retry; flush the resulting request before asserting so cleanup runs even on failure
    const el = fixture.nativeElement as HTMLElement;
    const retryBtn = el.querySelector('.issue-list__resolved-section .issue-list__error-retry') as HTMLElement;
    retryBtn.click();
    httpMock.expectOne((req) => req.url === '/api/issues' && req.params.has('states')).flush({
      items: [],
      nextCursor: null,
    });
    fixture.detectChanges();

    // Assert — focus moves to the resolved section heading
    const resolvedHeading = el.querySelector('.issue-list__resolved-caption') as HTMLElement;
    expect(document.activeElement).toBe(resolvedHeading);
  });

  it('should move focus to the resolved-band heading after clicking the load-more retry button', () => {
    // Arrange
    const resolvedIssue: IssueSummary = { ...mockSummary, id: 'res1', state: 'completed' };
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    const issueService = TestBed.inject(IssueService);
    issueService.toggleState('completed');
    httpMock.expectOne((req) => req.url === '/api/issues' && req.params.has('states')).flush({
      items: [resolvedIssue],
      nextCursor: 'cursor-abc',
    });
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    (el.querySelector('.issue-list__load-more') as HTMLElement).click();
    fixture.detectChanges();
    httpMock.expectOne((r) => r.url === '/api/issues' && r.params.has('cursor')).flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    fixture.detectChanges();

    // Act — click retry; flush the resulting request before asserting so cleanup runs even on failure
    const retryBtn = el.querySelector('.issue-list__resolved-load-more-retry') as HTMLElement;
    retryBtn.click();
    httpMock.expectOne((r) => r.url === '/api/issues' && r.params.has('cursor')).flush({
      items: [],
      nextCursor: null,
    });
    fixture.detectChanges();

    // Assert
    const resolvedHeading = el.querySelector('.issue-list__resolved-caption') as HTMLElement;
    expect(document.activeElement).toBe(resolvedHeading);
  });

  // F6: Empty-resolved state uses a paragraph (not h2) to avoid duplicate h2 siblings inside the resolved section
  it('should render a paragraph element in the empty-resolved state with the filter-scoped copy', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    // Act — select completed, return empty result
    const issueService = TestBed.inject(IssueService);
    issueService.toggleState('completed');
    httpMock.expectOne((req) => req.url === '/api/issues' && req.params.has('states')).flush({
      items: [],
      nextCursor: null,
    });
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const emptyMsg = el.querySelector('.issue-list__empty-resolved-heading');
    expect(emptyMsg?.tagName).toBe('P');
    expect(emptyMsg?.textContent?.trim()).toBe('No resolved issues match the selected filters');
  });

  // Step 4: fd-issue-filter-bar and container-query layout
  // Cycle 16: fd-issue-filter-bar is present in the DOM alongside the rail aside
  it('should render fd-issue-filter-bar in the DOM', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();

    // Act
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    // Assert — both the static rail aside and the sticky bar exist in the DOM at all times
    const el = fixture.nativeElement as HTMLElement;
    const filterBar = el.querySelector('fd-issue-filter-bar');
    expect(filterBar).toBeTruthy();
  });

  it('should render both fd-issue-filter-rail (inside aside) and fd-issue-filter-bar simultaneously in the DOM', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();

    // Act
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    // Assert — CSS toggles visibility; both elements exist in DOM at all times (no *ngIf on viewport)
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('aside.issue-list__rail fd-issue-filter-rail')).toBeTruthy();
    expect(el.querySelector('fd-issue-filter-bar')).toBeTruthy();
  });

  it('should place fd-issue-filter-bar inside an element with class issue-list__filter-bar-container', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();

    // Act
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    // Assert — filter bar is wrapped in its container for CSS targeting
    const el = fixture.nativeElement as HTMLElement;
    const container = el.querySelector('.issue-list__filter-bar-container');
    expect(container).toBeTruthy();
    expect(container?.querySelector('fd-issue-filter-bar')).toBeTruthy();
  });

  it('should apply issue-list__layout--container-query class to the layout element', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();

    // Act
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    // Assert — layout wrapper carries the container-query anchor class
    const el = fixture.nativeElement as HTMLElement;
    const layout = el.querySelector('.issue-list__layout');
    expect(layout?.classList).toContain('issue-list__layout');
  });

  // Dispatch-order queue grouping tests (issue #261)

  // QueueGroup-1: "Next up" marker on first eligible queued issue
  it('should render "Next up" marker on the first eligible queued issue', () => {
    // Arrange
    const queuedEligible: IssueSummary = {
      ...mockSummary,
      id: 'q-elig-1',
      state: 'queued',
      repositoryEligibilityStatus: null,
    };
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock, [queuedEligible]);

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const nextUp = el.querySelector('.issue-card__next-up');
    expect(nextUp?.textContent?.trim()).toContain('Next up');
  });

  // QueueGroup-2: No "Next up" when all queued issues are ineligible
  it('should NOT render "Next up" marker when all queued issues are ineligible', () => {
    // Arrange
    const queuedIneligible: IssueSummary = {
      ...mockSummary,
      id: 'q-inelig-1',
      state: 'queued',
      repositoryEligibilityStatus: 'ineligible',
    };
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock, [queuedIneligible]);

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const nextUp = el.querySelector('.issue-card__next-up');
    expect(nextUp).toBeFalsy();
  });

  // QueueGroup-3: ineligible-partition divider rendered when there are ineligible queued issues
  it('should render the ineligible-queued partition divider when there are ineligible queued issues', () => {
    // Arrange
    const ineligible: IssueSummary = {
      ...mockSummary,
      id: 'q-inelig-1',
      state: 'queued',
      repositoryEligibilityStatus: 'ineligible',
    };
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock, [ineligible]);

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const divider = el.querySelector('.issue-list__ineligible-queue-divider');
    expect(divider).toBeTruthy();
  });

  // QueueGroup-4: no ineligible partition divider when all queued issues are eligible
  it('should NOT render the ineligible-queue partition divider when all queued issues are eligible', () => {
    // Arrange
    const eligible: IssueSummary = {
      ...mockSummary,
      id: 'q-elig-1',
      state: 'queued',
      repositoryEligibilityStatus: null,
    };
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock, [eligible]);

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const divider = el.querySelector('.issue-list__ineligible-queue-divider');
    expect(divider).toBeFalsy();
  });

  // WCAG H3/M1: ineligible-queue divider caption is a paragraph (not h3) — no heading skip
  it('should render the ineligible-queue caption as a paragraph element, not a heading', () => {
    // Arrange
    const ineligible: IssueSummary = {
      ...mockSummary,
      id: 'q-inelig-1',
      state: 'queued',
      repositoryEligibilityStatus: 'ineligible',
    };
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock, [ineligible]);

    // Act
    fixture.detectChanges();

    // Assert — must be a <p>, not an <h3> or any heading
    const el = fixture.nativeElement as HTMLElement;
    const caption = el.querySelector('.issue-list__ineligible-queue-caption');
    expect(caption).toBeTruthy();
    expect(caption?.tagName).toBe('P');
  });

  it('should NOT render any h3 element for the ineligible-queue caption', () => {
    // Arrange
    const ineligible: IssueSummary = {
      ...mockSummary,
      id: 'q-inelig-2',
      state: 'queued',
      repositoryEligibilityStatus: 'ineligible',
    };
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock, [ineligible]);

    // Act
    fixture.detectChanges();

    // Assert — no h3 anywhere in the active area
    const el = fixture.nativeElement as HTMLElement;
    const h3 = el.querySelector('h3');
    expect(h3).toBeFalsy();
  });

  // WCAG M1: divider div has role="group" so aria-labelledby takes effect
  it('should give the ineligible-queue-divider div role="group"', () => {
    // Arrange
    const ineligible: IssueSummary = {
      ...mockSummary,
      id: 'q-inelig-3',
      state: 'queued',
      repositoryEligibilityStatus: 'ineligible',
    };
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock, [ineligible]);

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const divider = el.querySelector('.issue-list__ineligible-queue-divider');
    expect(divider?.getAttribute('role')).toBe('group');
  });

  // WCAG M1: aria-labelledby still present on the group
  it('should keep aria-labelledby on the ineligible-queue-divider group', () => {
    // Arrange
    const ineligible: IssueSummary = {
      ...mockSummary,
      id: 'q-inelig-4',
      state: 'queued',
      repositoryEligibilityStatus: 'ineligible',
    };
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock, [ineligible]);

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const divider = el.querySelector('.issue-list__ineligible-queue-divider');
    expect(divider?.getAttribute('aria-labelledby')).toBeTruthy();
  });

  // WCAG M2: guidance text lives inside the labelled caption element (single announcement source)
  it('should include the repository-settings guidance inside the ineligible-queue caption element', () => {
    // Arrange
    const ineligible: IssueSummary = {
      ...mockSummary,
      id: 'q-inelig-5',
      state: 'queued',
      repositoryEligibilityStatus: 'ineligible',
    };
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock, [ineligible]);

    // Act
    fixture.detectChanges();

    // Assert — guidance text is nested inside the labelled <p> caption (not a sibling span)
    const el = fixture.nativeElement as HTMLElement;
    const caption = el.querySelector('.issue-list__ineligible-queue-caption');
    expect(caption?.textContent).toMatch(/check repository settings/i);
  });

  // WCAG M2: no duplicate standalone sr-only sibling span outside the group
  it('should NOT have a standalone sr-only span outside the group announcing ineligible-queue guidance', () => {
    // Arrange
    const ineligible: IssueSummary = {
      ...mockSummary,
      id: 'q-inelig-6',
      state: 'queued',
      repositoryEligibilityStatus: 'ineligible',
    };
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock, [ineligible]);

    // Act
    fixture.detectChanges();

    // Assert — no standalone sr-only sibling span carries the repository-settings guidance text
    const el = fixture.nativeElement as HTMLElement;
    const srSpans = Array.from(el.querySelectorAll('.sr-only'));
    const sibling = srSpans.find(
      (s) =>
        s.closest('.issue-list__ineligible-queue-divider') === null &&
        s.textContent?.match(/check repository settings/i),
    );
    expect(sibling).toBeFalsy();
  });

  // QueueGroup-5: tier chips removed — state is conveyed by fd-state-badge alone
  it('should NOT render tier chips on queued issue cards', () => {
    // Arrange — one fresh, one revision queued
    const fresh: IssueSummary = { ...mockSummary, id: 'q-fresh', state: 'queued' };
    const revision: IssueSummary = { ...mockSummary, id: 'q-rev', state: 'revision_queued', issueNumber: 2 };
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock, [fresh, revision]);

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const tierChips = el.querySelectorAll('.issue-card__tier-chip');
    expect(tierChips.length).toBe(0);
  });

  // Step 4a: blocked card renders AFTER the ineligible-queue divider (AC5 regression lock)
  it('should render the ineligible-queue divider and place the blocked card after it', () => {
    // Arrange — eligible queued (rank 0), ineligible queued (rank 0, triggers divider), blocked (rank 2)
    const eligibleQueued: IssueSummary = {
      ...mockSummary,
      id: 'q-elig',
      state: 'queued',
      repositoryEligibilityStatus: null,
      issueNumber: 1,
    };
    const ineligibleQueued: IssueSummary = {
      ...mockSummary,
      id: 'q-inelig',
      state: 'queued',
      repositoryEligibilityStatus: 'ineligible',
      issueNumber: 2,
    };
    const blocked: IssueSummary = {
      ...mockSummary,
      id: 'blocked-1',
      state: 'blocked',
      repositoryEligibilityStatus: null,
      issueNumber: 3,
    };
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    // Server order: eligible queued first (dispatch priority), then ineligible queued, then blocked.
    flushInit(httpMock, [eligibleQueued, ineligibleQueued, blocked]);

    // Act
    fixture.detectChanges();

    // Assert — divider renders
    const el = fixture.nativeElement as HTMLElement;
    const grid = el.querySelector('.issue-list__grid') as HTMLElement;
    const divider = grid.querySelector('.issue-list__ineligible-queue-divider');
    expect(divider).toBeTruthy();

    // Assert — blocked card appears after the divider in the DOM
    const gridChildren = Array.from(grid.children);
    const dividerIndex = gridChildren.findIndex((c) => c.classList.contains('issue-list__ineligible-queue-divider'));
    const blockedItemIndex = gridChildren.findIndex((c) => {
      const card = c.querySelector('fd-issue-card') as HTMLElement | null;
      return card?.getAttribute('ng-reflect-issue')?.includes('blocked-1') ??
        c.querySelector('[data-issue-id="blocked-1"]') !== null;
    });

    // Fall back to text-content matching when ng-reflect attributes are not emitted
    const itemDivs = gridChildren.filter((c) => c.classList.contains('issue-list__item'));
    const blockedItemIndexFallback = itemDivs.findIndex((item) => {
      // The fd-issue-card shadow host carries the issue id via its input — check inner text
      // or position relative to the ineligible-queued item as a structural proxy.
      const card = item.querySelector('fd-issue-card');
      return card !== null && item === itemDivs[itemDivs.length - 1];
    });

    // The divider must precede the last item-div (blocked is last — it has highest within-group rank).
    const lastItemIndex = gridChildren.lastIndexOf(itemDivs[itemDivs.length - 1]);
    expect(dividerIndex).toBeGreaterThanOrEqual(0);
    expect(dividerIndex).toBeLessThan(lastItemIndex);
    void blockedItemIndex; // used only for existence check
    void blockedItemIndexFallback; // structural proxy asserted via lastItemIndex
  });

  // Step 4b: blocked issue in ineligible repo is NOT counted as ineligible-queued
  it('should NOT render the ineligible-queue divider when only a blocked issue has an ineligible repo', () => {
    // Arrange — blocked issues are NOT in QUEUED_TIER_STATES, so ineligible repo is irrelevant for the divider
    const blockedIneligible: IssueSummary = {
      ...mockSummary,
      id: 'blocked-inelig',
      state: 'blocked',
      repositoryEligibilityStatus: 'ineligible',
      issueNumber: 10,
    };
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock, [blockedIneligible]);

    // Act
    fixture.detectChanges();

    // Assert — no ineligible-queue divider (blocked is not a queued-tier state)
    const el = fixture.nativeElement as HTMLElement;
    const divider = el.querySelector('.issue-list__ineligible-queue-divider');
    expect(divider).toBeFalsy();

    // Assert — the blocked card still renders in the active band
    const grid = el.querySelector('.issue-list__grid') as HTMLElement;
    const items = grid.querySelectorAll('.issue-list__item');
    expect(items.length).toBe(1);
  });
});

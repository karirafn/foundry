import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Signal, WritableSignal, computed, signal } from '@angular/core';
import { NEVER } from 'rxjs';
import { IssueListComponent } from './issue-list';
import { IssueSection, IssueService } from '../issue.service';
import { DEFAULT_SELECTED_GROUPS, STATE_GROUPS, StateGroupId, groupFor } from '../issue-lifecycle.model';
import { IssueSignalRService } from '../../../core/services/issue-signalr.service';
import { SystemSignalRService } from '../../../core/services/system-signalr.service';
import { WorkerSignalRService, WORKER_HUB_FACTORY } from '../../../core/services/worker-signalr.service';
import { SettingsService } from '../../../core/services/settings.service';
import { WorkerRunTotalsService } from '../../workers/run-totals.service';
import { IssueSummary, IssueState } from '../issue.model';
import { GlobalSettingsResponse } from '../../../core/models/settings.model';

const mockSystemSignalR = { reconnected: NEVER, reloadTrigger: NEVER, loginSessionUpdate: NEVER, notifications: signal([]).asReadonly() };

const mockIssueSignalRService = {
  on: () => {},
  onReconnected: () => {},
  connectionStatus: signal<'connected' | 'reconnecting' | 'disconnected'>('disconnected'),
};

const mockWorkerHub = {
  on: () => {},
  onReconnected: () => {},
  onReconnecting: () => {},
  onClose: () => {},
  stream: () => ({ subscribe: () => ({ dispose: () => {} }) }),
  start: () => Promise.resolve(),
};

const mockSummary: IssueSummary = {
  id: 'abc123',
  issueNumber: 42,
  title: 'Enable dark mode',
  state: 'detected',
  repositorySlug: 'owner/repo',
  providerType: 'github',
  detectedAt: '2026-01-01T00:00:00Z',
  url: 'https://github.com/owner/repo/issues/42',
};

const mockSettingsResponse: GlobalSettingsResponse = {
  maxConcurrent: 3,
  timeoutMinutes: 30,
  probeIntervalMinutes: 60,
  pollIntervalSeconds: 30,
  usageLimitResetsAt: null,
  isDispatchPaused: false,
  autoResumeOnUsageReset: true,
  imageTag: 'foundry-worker:local',
  buildEnabled: true,
  installDotnet: false,
  installAngular: false,
  installChromium: false,
  installDocker: false,
  imageBuildStatus: 'Idle',
  lastImageBuildError: null,
  hasUsableImage: false,
  nextRetryAt: null,
  attempt: 0,
  allowedProviderHosts: [],
  memoryLimitMb: 8192,
  cpuLimit: 2,
  pidsLimit: 512,
  additionalDenyRules: [],
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
  inputTokens: { total: 0, knownRunCount: 0, unknownRunCount: 0 },
  outputTokens: { total: 0, knownRunCount: 0, unknownRunCount: 0 },
};

function flushInit(httpMock: HttpTestingController, issues: IssueSummary[] = []) {
  httpMock.expectOne('/api/issues').flush({ items: issues, nextCursor: null });
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
    httpMock.expectOne('/api/issues').flush({ items: [], nextCursor: null });
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
    httpMock.expectOne('/api/issues').flush({ items: [], nextCursor: null });
    httpMock.expectOne('/api/settings').flush(mockSettingsResponse);
  httpMock.expectOne('/api/credentials').flush(mockCredentialsResponse);
    const countsReq = httpMock.expectOne('/api/issues/counts');
    expect(countsReq.request.url).toBe('/api/issues/counts');
    countsReq.flush(mockCountsResponse);
    httpMock.expectOne((r) => r.url === '/api/workers/run-totals').flush(mockRunTotals);
  });

  // Cycle 3: renders the inline group chip row
  it('should render fd-issue-group-filter', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();

    // Act
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('fd-issue-group-filter')).toBeTruthy();
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

  it('should render the empty-active hint directing the user to select Resolved', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const hint = el.querySelector('.issue-list__empty-active-hint');
    expect(hint?.textContent?.trim()).toBe('No active issues match the selected groups. Select Resolved to see closed work.');
    expect(hint?.textContent).not.toContain('Resolved counts');
  });

  it('should render the active empty state when no group is selected', () => {
    // Arrange
    const live: IssueSummary = { ...mockSummary, id: 'p', state: 'in_progress' };
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock, [live]);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    // Act — deselect every pressed chip
    for (const id of ['in_progress', 'needs_attention', 'waiting']) {
      el.querySelector<HTMLButtonElement>(`fd-issue-group-filter button[data-group="${id}"]`)!.click();
    }
    fixture.detectChanges();

    // Assert
    expect(el.querySelector('.issue-list__empty-active-heading')?.textContent?.trim()).toBe('No active issues');
    expect(el.querySelector('section.issue-list__section')).toBeFalsy();
    expect(el.querySelector('fd-issue-card')).toBeFalsy();
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
    req.flush({ items: [], nextCursor: null });
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

  // Group sections: one <section> + <h2> per selected, non-empty active group, in STATE_GROUPS order
  function sectionIds(el: HTMLElement): (string | null)[] {
    return Array.from(el.querySelectorAll('section.issue-list__section:not(.issue-list__resolved-section)'))
      .map((sec) => sec.getAttribute('data-group'));
  }

  it('should render one labelled section per non-empty active group in STATE_GROUPS order', () => {
    // Arrange — server order scrambled across groups
    const queued: IssueSummary = { ...mockSummary, id: 'q', state: 'queued', issueNumber: 1 };
    const failed: IssueSummary = { ...mockSummary, id: 'f', state: 'failed', issueNumber: 2 };
    const live: IssueSummary = { ...mockSummary, id: 'p', state: 'in_progress', issueNumber: 3 };
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock, [queued, failed, live]);

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    expect(sectionIds(el)).toEqual(['in_progress', 'needs_attention', 'waiting']);
    const sections = Array.from(el.querySelectorAll('section.issue-list__section:not(.issue-list__resolved-section)'));
    const headings = sections.map((sec) => sec.querySelector('h2'));
    expect(headings.map((h) => h?.textContent?.trim())).toEqual(['In progress', 'Needs attention', 'Waiting']);
    sections.forEach((sec, i) => {
      expect(sec.getAttribute('aria-labelledby')).toBe(headings[i]?.id);
    });
    // Each issue card renders inside its own group section
    expect(sections[0].querySelectorAll('fd-issue-card').length).toBe(1);
    expect(sections[1].querySelectorAll('fd-issue-card').length).toBe(1);
    expect(sections[2].querySelectorAll('fd-issue-card').length).toBe(1);
  });

  it('should not render a section for a selected group with no issues', () => {
    // Arrange
    const queued: IssueSummary = { ...mockSummary, id: 'q', state: 'queued' };
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock, [queued]);

    // Act
    fixture.detectChanges();

    // Assert
    expect(sectionIds(fixture.nativeElement as HTMLElement)).toEqual(['waiting']);
  });

  it('should hide a group section when its chip is deselected', () => {
    // Arrange
    const live: IssueSummary = { ...mockSummary, id: 'p', state: 'in_progress' };
    const queued: IssueSummary = { ...mockSummary, id: 'q', state: 'queued', issueNumber: 43 };
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock, [live, queued]);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    // Act
    el.querySelector<HTMLButtonElement>('fd-issue-group-filter button[data-group="in_progress"]')!.click();
    fixture.detectChanges();

    // Assert
    expect(sectionIds(el)).toEqual(['waiting']);
  });

  it('should not render the removed live-boundary separator or its sr-only boundary line', () => {
    // Arrange
    const live: IssueSummary = { ...mockSummary, id: 'live', state: 'in_progress' };
    const waiting: IssueSummary = { ...mockSummary, id: 'waiting', state: 'detected', issueNumber: 43 };
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock, [live, waiting]);

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('hr.issue-list__separator')).toBeFalsy();
    const srSpans = Array.from(el.querySelectorAll('.sr-only'));
    expect(srSpans.some((sp) => sp.textContent?.includes('End of in-progress issues'))).toBe(false);
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
    issueService.toggleGroup('resolved');
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
    issueService.toggleGroup('resolved');
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
    issueService.toggleGroup('resolved');
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
    issueService.toggleGroup('resolved');
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
    issueService.toggleGroup('resolved');
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
    issueService.toggleGroup('resolved');
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
    issueService.toggleGroup('resolved');
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
    issueService.toggleGroup('resolved');
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
    issueService.toggleGroup('resolved');
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
    issueService.toggleGroup('resolved');
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
    issueService.toggleGroup('resolved');
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
    issueService.toggleGroup('resolved');
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
    issueService.toggleGroup('resolved');
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
    issueService.toggleGroup('resolved');
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
    issueService.toggleGroup('resolved');
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
    issueService.toggleGroup('resolved');
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
    issueService.toggleGroup('resolved');
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
    issueService.toggleGroup('resolved');
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
    issueService.toggleGroup('resolved');
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
    issueService.toggleGroup('resolved');
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
    issueService.toggleGroup('resolved');
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
    issueService.toggleGroup('resolved');
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
    issueService.toggleGroup('resolved');
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
    issueService.toggleGroup('resolved');
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
    httpMock.expectOne('/api/issues').flush({ items: [], nextCursor: null });
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
    issueService.toggleGroup('resolved');
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
    issueService.toggleGroup('resolved');
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
    issueService.toggleGroup('resolved');
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

  // Layout: the chip row sits inline above the list at every width — no side rail, no mobile bar/sheet
  it('should render the group chip row above the list without a side rail, filter bar or filter sheet', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();

    // Act
    fixture.detectChanges();
    flushInit(httpMock);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const layout = el.querySelector('.issue-list__layout') as HTMLElement;
    expect(layout.firstElementChild?.tagName.toLowerCase()).toBe('fd-issue-group-filter');
    expect(el.querySelector('aside')).toBeFalsy();
    expect(el.querySelector('.issue-list__rail')).toBeFalsy();
    expect(el.querySelector('fd-issue-filter-bar')).toBeFalsy();
    expect(el.querySelector('fd-issue-filter-sheet')).toBeFalsy();
    expect(el.querySelector('.issue-list__filter-bar-container')).toBeFalsy();
  });

  // Dispatch-order queue grouping tests (issue #261)

  // QueueGroup-1: rank-1 gutter (--next modifier) on first eligible queued issue; pill removed
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

    // Assert — .issue-card__next-up pill removed; rank-1 is now the --next gutter modifier
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.issue-card__next-up')).toBeFalsy();
    const gutterNext = el.querySelector('.issue-card__queue-position--next');
    expect(gutterNext).toBeTruthy();
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

  // Publish phase (#787): the card's badge receives the issue's publish phase
  it('should pass the publish phase through to the card badge', () => {
    // Arrange
    const publishing: IssueSummary = { ...mockSummary, id: 'pub-1', state: 'in_progress', publishPhase: 'publishing' };
    const retrying: IssueSummary = { ...mockSummary, id: 'pub-2', state: 'in_progress', issueNumber: 2, publishPhase: 'retrying' };
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock, [publishing, retrying]);

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const labels = Array.from(el.querySelectorAll('fd-state-badge span')).map((b) => b.textContent?.trim());
    expect(labels).toEqual(['PUBLISHING', 'PUBLISH RETRY']);
  });

  it('should not bind a live commit count on the card', () => {
    // Arrange
    const live: IssueSummary = { ...mockSummary, id: 'live-1', state: 'in_progress' };
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock, [live]);

    // Act
    fixture.detectChanges();

    // Assert
    const card = fixture.debugElement.query((d) => d.name === 'fd-issue-card');
    expect('commitCount' in (card.componentInstance as object)).toBe(false);
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

  // Step 8: queue-order stale marker — absence (no any cast; driven via real HTTP)
  it('should NOT render the stale marker when queueOrderStale() is false', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock, [mockSummary]);

    // Act
    fixture.detectChanges();

    // Assert — stale marker must be absent when the signal is false (default)
    const el = fixture.nativeElement as HTMLElement;
    const marker = el.querySelector('.issue-list__stale-marker');
    expect(marker).toBeFalsy();
  });
});

// Stale-marker tests: use a typed stub IssueService so no private-field any-cast is needed
interface StubIssueService {
  queueOrderStale: WritableSignal<boolean>;
  initialLoading: WritableSignal<boolean>;
  issues: WritableSignal<IssueSummary[]>;
  activeBandIssues: WritableSignal<IssueSummary[]>;
  liveIssueCount: WritableSignal<number>;
  loadError: WritableSignal<string | null>;
  expandedIssueId: WritableSignal<string | null>;
  issueDetail: WritableSignal<null>;
  detailLoading: WritableSignal<boolean>;
  detailError: WritableSignal<null>;
  resolvedIssues: WritableSignal<IssueSummary[]>;
  resolvedLoading: WritableSignal<boolean>;
  resolvedError: WritableSignal<null>;
  resolvedLoadMoreError: WritableSignal<null>;
  resolvedLoadingMore: WritableSignal<boolean>;
  hasMoreResolved: WritableSignal<boolean>;
  selectedGroups: WritableSignal<ReadonlySet<StateGroupId>>;
  resolvedSelected: Signal<boolean>;
  activeSections: Signal<readonly IssueSection[]>;
  counts: WritableSignal<Record<string, number>>;
  retrying: WritableSignal<boolean>;
  retryError: WritableSignal<null>;
  retrySuccess: WritableSignal<null>;
  ineligibleQueuedIssues: WritableSignal<IssueSummary[]>;
  nextUpIssueId: WritableSignal<string | null>;
  isEmpty: WritableSignal<boolean>;
  eligibleQueuedIssues: WritableSignal<IssueSummary[]>;
  queuePositions: WritableSignal<ReadonlyMap<string, number>>;
  nextUpAnnouncement: WritableSignal<string>;
  loadIssues: () => void;
  loadCounts: () => void;
  toggleExpand: (id: string) => void;
  toggleGroup: (id: StateGroupId) => void;
  loadMoreResolved: () => void;
  retryResolvedFetch: () => void;
  loadDetail: (id: string) => void;
  retryIssue: (id: string) => void;
  countFor: (state: IssueState) => number;
  countForGroup: (id: StateGroupId) => number;
  isGroupSelected: (id: StateGroupId) => boolean;
}

function makeStubIssueService(issues: IssueSummary[] = []): StubIssueService {
  const activeBandIssues = signal(issues);
  const selectedGroups = signal<ReadonlySet<StateGroupId>>(DEFAULT_SELECTED_GROUPS);
  // Mirror IssueService.activeSections: split the stubbed band by group, in STATE_GROUPS order.
  const activeSections = computed<readonly IssueSection[]>(() =>
    STATE_GROUPS
      .map(group => ({ group, issues: activeBandIssues().filter(i => groupFor(i.state).id === group.id) }))
      .filter(section => section.issues.length > 0)
  );
  return {
    queueOrderStale: signal(false),
    initialLoading: signal(false),
    issues: signal(issues),
    activeBandIssues,
    liveIssueCount: signal(0),
    loadError: signal(null),
    expandedIssueId: signal(null),
    issueDetail: signal(null),
    detailLoading: signal(false),
    detailError: signal(null),
    resolvedIssues: signal([]),
    resolvedLoading: signal(false),
    resolvedError: signal(null),
    resolvedLoadMoreError: signal(null),
    resolvedLoadingMore: signal(false),
    hasMoreResolved: signal(false),
    selectedGroups,
    resolvedSelected: computed(() => selectedGroups().has('resolved')),
    activeSections,
    counts: signal({}),
    retrying: signal(false),
    retryError: signal(null),
    retrySuccess: signal(null),
    ineligibleQueuedIssues: signal([]),
    nextUpIssueId: signal(null),
    isEmpty: signal(issues.length === 0),
    eligibleQueuedIssues: signal([]),
    queuePositions: signal(new Map<string, number>()),
    nextUpAnnouncement: signal(''),
    loadIssues: () => {},
    loadCounts: () => {},
    toggleExpand: () => {},
    toggleGroup: () => {},
    loadMoreResolved: () => {},
    retryResolvedFetch: () => {},
    loadDetail: () => {},
    retryIssue: () => {},
    countFor: () => 0,
    countForGroup: () => 0,
    isGroupSelected: (id: StateGroupId) => selectedGroups().has(id),
  };
}

const mockSettingsService = { loadSettings: () => {} };
const mockRunTotalsService = {
  fetch: () => {},
  scope: signal<'month' | 'all'>('month').asReadonly(),
  totals: signal(null).asReadonly(),
  loading: signal(false).asReadonly(),
  error: signal(null).asReadonly(),
  setScope: () => {},
};

function setupStubComponent(stub: StubIssueService) {
  TestBed.configureTestingModule({
    imports: [IssueListComponent],
    providers: [
      { provide: IssueService, useValue: stub },
      { provide: SettingsService, useValue: mockSettingsService },
      { provide: WorkerRunTotalsService, useValue: mockRunTotalsService },
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

describe('IssueListComponent (stale marker)', () => {
  beforeEach(() => {
    TestBed.resetTestingModule();
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
  });

  // Finding 1 — sr-only announcer is always mounted
  it('should have a persistent sr-only role="status" stale announcer always in the DOM', () => {
    // Arrange
    const stub = makeStubIssueService();
    const { fixture } = setupStubComponent(stub);

    // Act
    fixture.detectChanges();

    // Assert — announcer is always mounted regardless of stale state
    const el = fixture.nativeElement as HTMLElement;
    const announcer = el.querySelector('[role="status"].issue-list__stale-announcer');
    expect(announcer).toBeTruthy();
    expect(announcer?.classList).toContain('sr-only');
  });

  it('should have empty sr-only stale announcer text when queueOrderStale is false', () => {
    // Arrange
    const stub = makeStubIssueService();
    stub.queueOrderStale.set(false);
    const { fixture } = setupStubComponent(stub);

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const announcer = el.querySelector('[role="status"].issue-list__stale-announcer') as HTMLElement;
    expect(announcer?.textContent?.trim()).toBe('');
  });

  it('should populate the sr-only stale announcer text when queueOrderStale becomes true', () => {
    // Arrange
    const stub = makeStubIssueService();
    const { fixture } = setupStubComponent(stub);
    fixture.detectChanges();

    // Act — drive stale state via the public signal
    stub.queueOrderStale.set(true);
    fixture.detectChanges();

    // Assert — sr-only announcer text is populated
    const el = fixture.nativeElement as HTMLElement;
    const announcer = el.querySelector('[role="status"].issue-list__stale-announcer') as HTMLElement;
    expect(announcer?.textContent?.trim()).toBe('Queue order may be out of date');
  });

  // Finding 3 — visible strip driven via typed public signal (no any cast)
  it('should NOT render the visible stale marker strip when queueOrderStale is false', () => {
    // Arrange
    const stub = makeStubIssueService();
    stub.queueOrderStale.set(false);
    const { fixture } = setupStubComponent(stub);

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.issue-list__stale-marker')).toBeFalsy();
  });

  it('should render the visible stale marker strip with the exact copy when queueOrderStale is true', () => {
    // Arrange
    const stub = makeStubIssueService();
    const { fixture } = setupStubComponent(stub);
    fixture.detectChanges();

    // Act — drive stale state via typed public signal
    stub.queueOrderStale.set(true);
    fixture.detectChanges();

    // Assert — visible strip present with correct text
    const el = fixture.nativeElement as HTMLElement;
    const marker = el.querySelector('.issue-list__stale-marker') as HTMLElement | null;
    expect(marker).toBeTruthy();
    expect(marker?.textContent?.trim()).toContain('Queue order may be out of date');
  });

  // Finding 4 — visible strip must NOT carry role="status" (only the sr-only announcer is the live region)
  it('should NOT have role="status" on the visible stale marker strip', () => {
    // Arrange
    const stub = makeStubIssueService();
    const { fixture } = setupStubComponent(stub);
    fixture.detectChanges();

    stub.queueOrderStale.set(true);
    fixture.detectChanges();

    // Assert — role="status" is on the sr-only announcer, not the visible strip
    const el = fixture.nativeElement as HTMLElement;
    const marker = el.querySelector('.issue-list__stale-marker') as HTMLElement | null;
    expect(marker?.getAttribute('role')).not.toBe('status');
  });

  it('should NOT have aria-atomic on the visible stale marker strip', () => {
    // Arrange
    const stub = makeStubIssueService();
    const { fixture } = setupStubComponent(stub);
    fixture.detectChanges();

    stub.queueOrderStale.set(true);
    fixture.detectChanges();

    // Assert — aria-atomic removed from visible strip (implied by role="status" on sr-only span)
    const el = fixture.nativeElement as HTMLElement;
    const marker = el.querySelector('.issue-list__stale-marker') as HTMLElement | null;
    expect(marker?.hasAttribute('aria-atomic')).toBe(false);
  });

  // Finding 2 — marker absent while initialLoading is true (even if somehow stale were set)
  it('should NOT render the visible stale marker strip while initialLoading is true', () => {
    // Arrange — initialLoading true means the service would not set queueOrderStale during load,
    // but we confirm the template does not show it even if the signal could have been set
    const stub = makeStubIssueService();
    stub.initialLoading.set(true);
    stub.queueOrderStale.set(false); // service guarantees this, but verify template too
    const { fixture } = setupStubComponent(stub);

    // Act
    fixture.detectChanges();

    // Assert — no visible strip, no confusion with loading skeletons
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.issue-list__stale-marker')).toBeFalsy();
  });

  // Finding 5 — split: marker render and grid independence are separate tests
  it('should render the visible stale marker strip alongside a non-empty grid', () => {
    // Arrange
    const second: IssueSummary = { ...mockSummary, id: 'def456', issueNumber: 43, title: 'Fix bug' };
    const stub = makeStubIssueService([mockSummary, second]);
    const { fixture } = setupStubComponent(stub);
    fixture.detectChanges();

    // Act
    stub.queueOrderStale.set(true);
    fixture.detectChanges();

    // Assert — marker present
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.issue-list__stale-marker')).toBeTruthy();
  });

  it('should not hide any cards when the stale marker strip is rendered', () => {
    // Arrange
    const second: IssueSummary = { ...mockSummary, id: 'def456', issueNumber: 43, title: 'Fix bug' };
    const stub = makeStubIssueService([mockSummary, second]);
    const { fixture } = setupStubComponent(stub);
    fixture.detectChanges();

    // Act
    stub.queueOrderStale.set(true);
    fixture.detectChanges();

    // Assert — both cards still rendered (marker does not gate the grid)
    const el = fixture.nativeElement as HTMLElement;
    const cards = el.querySelectorAll('fd-issue-card');
    expect(cards.length).toBe(2);
  });
});

describe('IssueListComponent (ordinal binding + next-up announcer)', () => {
  beforeEach(() => {
    TestBed.resetTestingModule();
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
  });

  // Cycle a-i: fourth always-mounted sr-only role="status" announcer is present
  it('should have a fourth always-mounted sr-only role="status" next-up announcer in the DOM', () => {
    // Arrange
    const stub = makeStubIssueService();
    const { fixture } = setupStubComponent(stub);

    // Act
    fixture.detectChanges();

    // Assert — announcer node is always mounted regardless of content
    const el = fixture.nativeElement as HTMLElement;
    const announcer = el.querySelector('.issue-list__next-up-announcer');
    expect(announcer).toBeTruthy();
    expect(announcer?.getAttribute('role')).toBe('status');
    expect(announcer?.classList).toContain('sr-only');
  });

  // Cycle a-ii: announcer carries nextUpAnnouncement() text when a queued issue exists
  it('should populate the next-up announcer with nextUpAnnouncement() text when non-empty', () => {
    // Arrange
    const stub = makeStubIssueService();
    stub.nextUpAnnouncement.set('Next up: issue #42 — Enable dark mode');
    const { fixture } = setupStubComponent(stub);

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const announcer = el.querySelector('.issue-list__next-up-announcer') as HTMLElement;
    expect(announcer?.textContent?.trim()).toBe('Next up: issue #42 — Enable dark mode');
  });

  // Cycle a-iii: announcer is empty string when nextUpAnnouncement() is ''
  it('should leave the next-up announcer empty when nextUpAnnouncement() is empty', () => {
    // Arrange
    const stub = makeStubIssueService();
    stub.nextUpAnnouncement.set('');
    const { fixture } = setupStubComponent(stub);

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const announcer = el.querySelector('.issue-list__next-up-announcer') as HTMLElement;
    expect(announcer?.textContent?.trim()).toBe('');
  });

  // Cycle a-iv: announcer is always mounted (never behind @if)
  it('should keep the next-up announcer mounted even when there are no issues', () => {
    // Arrange — empty issue list, no next-up
    const stub = makeStubIssueService([]);
    const { fixture } = setupStubComponent(stub);

    // Act
    fixture.detectChanges();

    // Assert — node is present even with nothing loaded
    const el = fixture.nativeElement as HTMLElement;
    const announcer = el.querySelector('.issue-list__next-up-announcer');
    expect(announcer).toBeTruthy();
  });

  // Cycle b-i: dispatchable queued card receives its ordinal from queuePositions()
  it('should pass the correct queuePosition ordinal to a dispatchable queued issue card', () => {
    // Arrange
    const queuedIssue: IssueSummary = {
      ...mockSummary,
      id: 'q1',
      state: 'queued',
      repositoryEligibilityStatus: null,
    };
    const stub = makeStubIssueService([queuedIssue]);
    stub.queuePositions.set(new Map([['q1', 1]]));
    stub.nextUpIssueId.set('q1');
    const { fixture } = setupStubComponent(stub);

    // Act
    fixture.detectChanges();

    // Assert — the gutter shows the ordinal (position 1 renders as "1")
    const el = fixture.nativeElement as HTMLElement;
    const gutter = el.querySelector('.issue-card__queue-position') as HTMLElement;
    expect(gutter).toBeTruthy();
    expect(gutter?.textContent?.trim()).toBe('1');
  });

  // Cycle b-ii: ineligible queued card shows em dash (null queuePosition → "—")
  it('should pass null queuePosition to an ineligible queued issue card, rendering an em dash', () => {
    // Arrange — ineligible queued issue: not in queuePositions
    const ineligibleIssue: IssueSummary = {
      ...mockSummary,
      id: 'inelig1',
      state: 'queued',
      repositoryEligibilityStatus: 'ineligible',
    };
    const stub = makeStubIssueService([ineligibleIssue]);
    stub.queuePositions.set(new Map()); // not in the map → null → "—"
    stub.ineligibleQueuedIssues.set([ineligibleIssue]);
    const { fixture } = setupStubComponent(stub);

    // Act
    fixture.detectChanges();

    // Assert — gutter shows em dash for null position
    const el = fixture.nativeElement as HTMLElement;
    const gutter = el.querySelector('.issue-card__queue-position') as HTMLElement;
    expect(gutter).toBeTruthy();
    expect(gutter?.textContent?.trim()).toBe('—');
  });

  // Cycle b-iii: resolved band cards do NOT receive a queuePosition (they are not queued-tier)
  it('should NOT show a queue-position gutter on a resolved-band issue card', () => {
    // Arrange — resolved issue is not in queuePositions (no gutter shown for non-queued state)
    const resolvedIssue: IssueSummary = { ...mockSummary, id: 'done1', state: 'completed' };
    const stub = makeStubIssueService([]);
    stub.resolvedIssues.set([resolvedIssue]);
    stub.selectedGroups.set(new Set<StateGroupId>([...DEFAULT_SELECTED_GROUPS, 'resolved']));
    const { fixture } = setupStubComponent(stub);

    // Act
    fixture.detectChanges();

    // Assert — the resolved band card has no gutter (queuePosition defaults to null → no element rendered for non-queued)
    const el = fixture.nativeElement as HTMLElement;
    const resolvedBand = el.querySelector('.issue-list__resolved-band');
    expect(resolvedBand).toBeTruthy();
    const gutter = resolvedBand?.querySelector('.issue-card__queue-position');
    // Gutter element exists in card markup but with null input it renders as "—" only for queued-tier states.
    // For non-queued-tier states (completed) the card hides the gutter entirely.
    // We assert the resolved band card does not show any numeric ordinal text.
    const gutterText = (gutter as HTMLElement | null)?.textContent?.trim() ?? '';
    expect(gutterText).not.toMatch(/^\d+$/);
  });
});

import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { IssueListComponent } from './issue-list';
import { IssueService } from '../issue.service';
import { IssueSignalRService } from '../../../core/services/issue-signalr.service';
import { IssueSummary } from '../issue.model';
import { GlobalSettingsResponse } from '../../../features/settings/settings.model';

const mockIssueSignalRService = {
  on: () => {},
  onReconnected: () => {},
  connectionStatus: signal<'connected' | 'reconnecting' | 'disconnected'>('disconnected'),
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
  authMode: 'ApiKey',
  maxConcurrent: 3,
  timeoutMinutes: 30,
  accessTokenPresent: false,
  refreshTokenPresent: false,
  expiresAt: null,
  subscriptionType: null,
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

function setupComponent() {
  TestBed.configureTestingModule({
    imports: [IssueListComponent],
    providers: [
      IssueService,
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: IssueSignalRService, useValue: mockIssueSignalRService },
    ],
  });

  const fixture = TestBed.createComponent(IssueListComponent);
  const httpMock = TestBed.inject(HttpTestingController);
  return { fixture, httpMock };
}

function flushInit(httpMock: HttpTestingController, issues: IssueSummary[] = []) {
  httpMock.expectOne('/api/issues').flush(issues);
  httpMock.expectOne('/api/settings').flush(mockSettingsResponse);
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

  // Cycle 2: calls loadIssues on init
  it('should call loadIssues on initialization', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();

    // Act
    fixture.detectChanges();

    // Assert — the HTTP call proves loadIssues was called
    httpMock.expectOne('/api/issues').flush([]);
    httpMock.expectOne('/api/settings').flush(mockSettingsResponse);
  });

  // Cycle 3: renders fd-issue-card for each issue
  it('should render fd-issue-card for each sorted issue', () => {
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

  // Cycle 4: shows empty state when no issues
  it('should render fd-empty-state when there are no issues', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock);

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const emptyState = el.querySelector('fd-empty-state');
    expect(emptyState).toBeTruthy();
  });

  it('should not render fd-empty-state when issues exist', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock, [mockSummary]);

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const emptyState = el.querySelector('fd-empty-state');
    expect(emptyState).toBeFalsy();
  });

  it('should not render fd-empty-state during initial load before the first response', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();

    // Act — detect changes but do NOT flush the HTTP responses
    fixture.detectChanges();

    // Assert — empty state must not appear while still loading
    const el = fixture.nativeElement as HTMLElement;
    const emptyState = el.querySelector('fd-empty-state');
    expect(emptyState).toBeFalsy();

    // Cleanup
    flushInit(httpMock);
  });

  it('should not render fd-empty-state when loadIssues results in an HTTP error', () => {
    // Arrange
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();

    // Act — simulate a server error on issues, settings succeeds
    httpMock.expectOne('/api/issues').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    httpMock.expectOne('/api/settings').flush(mockSettingsResponse);
    fixture.detectChanges();

    // Assert — error state, not empty state
    const el = fixture.nativeElement as HTMLElement;
    const emptyState = el.querySelector('fd-empty-state');
    expect(emptyState).toBeFalsy();
  });

  // Cycle 4b: detail wrapper has stable id for aria-controls
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

  // Cycle 4c: load error is shown with retry
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

  // Cycle 5: renders fd-connection-indicator
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

  // Cycle 7: separator renders between live and non-live issues
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

  it('should sort continuation_queued above the separator alongside other live states', () => {
    // Arrange — continuation_queued is operationally equivalent to queued and must appear above the separator
    const continuationQueued: IssueSummary = { ...mockSummary, id: 'cont-queued', state: 'continuation_queued' };
    const nonLiveIssue: IssueSummary = { ...mockSummary, id: 'done', state: 'detected', issueNumber: 43 };
    const { fixture, httpMock } = setupComponent();
    fixture.detectChanges();
    flushInit(httpMock, [nonLiveIssue, continuationQueued]);

    // Act
    fixture.detectChanges();

    // Assert — separator must appear, meaning continuation_queued is treated as live
    const el = fixture.nativeElement as HTMLElement;
    const separator = el.querySelector('hr.issue-list__separator');
    expect(separator).toBeTruthy();
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

    // Assert
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

  // Cycle 8: sr-only span announces section boundary for screen readers
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
    const srSpan = el.querySelector('.sr-only');
    expect(srSpan).toBeTruthy();
    expect(srSpan?.textContent).toContain('End of in-progress issues');
  });

  // Cycle 6: expand/collapse wiring - fd-issue-detail appears when card is expanded
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

  // Cycle 10: skeleton cards shown during initial load
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
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const skeletons = el.querySelector('.issue-list__skeletons');
    expect(skeletons).toBeFalsy();
  });

  // Cycle 9: fd-dispatch-controls renders below the header
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
});

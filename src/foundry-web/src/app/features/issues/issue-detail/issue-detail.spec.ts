import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { Observable, Subject, of } from 'rxjs';
import { IssueDetailComponent } from './issue-detail';
import { InputQuestion, IssueDetail, IssueStateDetails } from '../issue.model';
import { IssueService, TriageAction } from '../issue.service';
import { IssueSignalRService } from '../../../core/services/issue-signalr.service';
import { WorkerRunService } from '../../workers/worker-run.service';
import { WorkerRunDetail } from '../../workers/worker-run.model';
import { WorkerSignalRService, WORKER_HUB_FACTORY } from '../../../core/services/worker-signalr.service';
import { MAX_CHECKS_FIX_ATTEMPTS, MAX_REBASE_CONFLICT_ATTEMPTS, RETRYABLE_STATES } from '../../../shared/utils/issue-state';

const mockIssueSignalRService = {
  on: () => {},
  onReconnected: () => {},
};

const mockWorkerRunService = {
  getDetail: () => of(null),
  getLog: () => of(null),
};

const mockWorkerHub = {
  on: () => {},
  onReconnected: () => {},
  onReconnecting: () => {},
  onClose: () => {},
  stream: () => ({ subscribe: () => ({ dispose: () => {} }) }),
  start: () => Promise.resolve(),
};

const mockStateDetails: IssueStateDetails = {
  workerRunId: null,
  branchName: 'feat/dark-mode',
  feedbackCutoffAt: null,
  failureReason: null,
  failedAt: null,
  completedAt: '2026-02-01T12:00:00Z',
  blockedBy: null,
  violations: null,
  transientRetry: null,
};

const mockDetail: IssueDetail = {
  id: 'abc123',
  issueNumber: 42,
  title: 'Enable dark mode',
  state: 'completed',
  repositorySlug: 'owner/repo',
  detectedAt: '2026-01-01T00:00:00Z',
  url: 'https://github.com/owner/repo/issues/42',
  author: 'dev',
  labels: ['enhancement', 'ui'],
  stateDetails: mockStateDetails,
  kind: { source: 'labelled', kind: 'feature', conflictingTriageKind: null },
  triage: null,
};

/** The triage-action signals every IssueService stand-in needs once the detail renders them. */
function triageSignals() {
  return {
    triageActionPending: signal<TriageAction | null>(null),
    triageActionError: signal<string | null>(null),
    triageActionSuccess: signal<string | null>(null),
  };
}

function createComponent(
  detail: IssueDetail | null,
  loading = false,
  error: string | null = null,
) {
  TestBed.configureTestingModule({
    imports: [IssueDetailComponent],
    providers: [
      IssueService,
      WorkerSignalRService,
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: IssueSignalRService, useValue: mockIssueSignalRService },
      { provide: WorkerRunService, useValue: mockWorkerRunService },
      { provide: WORKER_HUB_FACTORY, useValue: () => mockWorkerHub },
    ],
  });
  const fixture = TestBed.createComponent(IssueDetailComponent);
  fixture.componentRef.setInput('detail', detail);
  fixture.componentRef.setInput('loading', loading);
  fixture.componentRef.setInput('error', error);
  fixture.detectChanges();
  return fixture;
}

describe('IssueDetailComponent', () => {
  // Cycle 1: component creates
  it('should create the component', () => {
    // Arrange
    const fixture = createComponent(null, false);

    // Act — component renders on creation

    // Assert
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should render with role="region" when detail is provided', () => {
    // Arrange
    const fixture = createComponent(mockDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Act — component renders on creation

    // Assert
    const region = el.querySelector('[role="region"]');
    expect(region).toBeTruthy();
  });

  // Cycle 2: loading skeleton
  it('should show shimmer skeleton when loading is true', () => {
    // Arrange
    const fixture = createComponent(null, true);
    const el = fixture.nativeElement as HTMLElement;

    // Act — component renders on creation

    // Assert
    const skeleton = el.querySelector('.issue-detail__skeleton');
    expect(skeleton).toBeTruthy();
  });

  it('should mark skeleton container as aria-busy when loading', () => {
    // Arrange
    const fixture = createComponent(null, true);
    const el = fixture.nativeElement as HTMLElement;

    // Act — component renders on creation

    // Assert
    const skeleton = el.querySelector('[aria-busy="true"]');
    expect(skeleton).toBeTruthy();
  });

  it('should render three shimmer bars in the skeleton', () => {
    // Arrange
    const fixture = createComponent(null, true);
    const el = fixture.nativeElement as HTMLElement;

    // Act — component renders on creation

    // Assert
    const bars = el.querySelectorAll('.issue-detail__shimmer-bar');
    expect(bars.length).toBe(3);
  });

  it('should not show skeleton when not loading', () => {
    // Arrange
    const fixture = createComponent(mockDetail, false);
    const el = fixture.nativeElement as HTMLElement;

    // Act — component renders on creation

    // Assert
    const skeleton = el.querySelector('.issue-detail__skeleton');
    expect(skeleton).toBeFalsy();
  });

  // Cycle 4: labels
  it('should render each label as a pill', () => {
    // Arrange
    const fixture = createComponent(mockDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Act — component renders on creation

    // Assert
    const pills = el.querySelectorAll('.issue-detail__label-pill');
    expect(pills.length).toBe(2);
    const texts = Array.from(pills).map((p) => p.textContent?.trim());
    expect(texts).toContain('enhancement');
    expect(texts).toContain('ui');
  });

  // The card owns the PR link (worded per provider); the detail panel no longer repeats it.
  it('should not render a PR field in the detail panel', () => {
    // Arrange — mockDetail is a completed issue whose summary carries a PR URL
    const fixture = createComponent({ ...mockDetail, pullRequestUrl: 'https://github.com/owner/repo/pull/99' });
    const el = fixture.nativeElement as HTMLElement;

    // Act — component renders on creation

    // Assert
    expect(el.querySelector('.issue-detail__pr-link')).toBeNull();
    const fieldKeys = Array.from(el.querySelectorAll('.issue-detail__field-key')).map((k) => k.textContent?.trim());
    expect(fieldKeys).not.toContain('Pull request');
    expect(fieldKeys).not.toContain('Merge request');
  });

  // Cycle 6: state detail fields - branchName
  it('should display the branch name when present', () => {
    // Arrange
    const fixture = createComponent(mockDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Act — component renders on creation

    // Assert
    const branch = el.querySelector('.issue-detail__branch');
    expect(branch?.textContent?.trim()).toBe('feat/dark-mode');
  });

  it('should not render branch row when branchName is null', () => {
    // Arrange
    const detailNoBranch: IssueDetail = {
      ...mockDetail,
      stateDetails: { ...mockStateDetails, branchName: null },
    };

    // Act
    const fixture = createComponent(detailNoBranch);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const branch = el.querySelector('.issue-detail__branch');
    expect(branch).toBeFalsy();
  });

  // Cycle 7: aria-label includes issue number
  it('should set aria-label referencing the issue number', () => {
    // Arrange
    const fixture = createComponent(mockDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Act — component renders on creation

    // Assert
    const region = el.querySelector('[role="region"]') as HTMLElement;
    expect(region?.getAttribute('aria-label')).toBe('Issue details for #42');
  });

  // Cycle 8: error state
  it('should show error message when error input is truthy', () => {
    // Arrange
    const fixture = createComponent(null, false, 'Http failure');
    const el = fixture.nativeElement as HTMLElement;

    // Act — component renders on creation

    // Assert
    const errorEl = el.querySelector('.issue-detail__error');
    expect(errorEl).toBeTruthy();
    expect(errorEl?.textContent).toContain('Failed to load details');
  });

  it('should not show detail content when error is present', () => {
    // Arrange
    const fixture = createComponent(null, false, 'Http failure');
    const el = fixture.nativeElement as HTMLElement;

    // Act — component renders on creation

    // Assert
    const content = el.querySelector('.issue-detail__content');
    expect(content).toBeFalsy();
  });

  it('should not show skeleton when error is present', () => {
    // Arrange
    const fixture = createComponent(null, true, 'Http failure');
    const el = fixture.nativeElement as HTMLElement;

    // Act — component renders on creation

    // Assert
    const skeleton = el.querySelector('.issue-detail__skeleton');
    expect(skeleton).toBeFalsy();
  });

  it('should not show error block when error is null', () => {
    // Arrange
    const fixture = createComponent(mockDetail, false, null);
    const el = fixture.nativeElement as HTMLElement;

    // Act — component renders on creation

    // Assert
    const errorEl = el.querySelector('.issue-detail__error');
    expect(errorEl).toBeFalsy();
  });

  // B10: persistent "View issue" link is always present in the detail panel
  it('should render a "View issue" link when detail is present', () => {
    // Arrange
    const fixture = createComponent(mockDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Act — component renders on creation

    // Assert
    const link = el.querySelector('.issue-detail__issue-link') as HTMLAnchorElement;
    expect(link).toBeTruthy();
    expect(link?.textContent?.trim()).toBe('View issue');
    expect(link?.getAttribute('href')).toBe('https://github.com/owner/repo/issues/42');
    expect(link?.getAttribute('target')).toBe('_blank');
    expect(link?.getAttribute('rel')).toBe('noopener noreferrer');
  });

  it('should set aria-label on issue link to include issue number', () => {
    // Arrange
    const fixture = createComponent(mockDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Act — component renders on creation

    // Assert
    const link = el.querySelector('.issue-detail__issue-link') as HTMLAnchorElement;
    expect(link?.getAttribute('aria-label')).toBe('View issue #42 on provider');
  });

  it('should render "View issue" link for early-state issues with null stateDetails', () => {
    // Arrange
    const detectedDetail: IssueDetail = {
      ...mockDetail,
      state: 'detected',
      stateDetails: null,
    };

    // Act
    const fixture = createComponent(detectedDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const link = el.querySelector('.issue-detail__issue-link') as HTMLAnchorElement;
    expect(link).toBeTruthy();
    expect(link?.getAttribute('href')).toBe('https://github.com/owner/repo/issues/42');
  });

  // B2 guard: null stateDetails must not throw
  it('should render content region without error when stateDetails is null (detected/queued states)', () => {
    // Arrange — detected state returns null stateDetails from the API
    const detectedDetail: IssueDetail = {
      ...mockDetail,
      state: 'detected',
      stateDetails: null,
    };

    // Act
    const fixture = createComponent(detectedDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert — region renders, no state-detail fields, no crash
    const region = el.querySelector('[role="region"]');
    expect(region).toBeTruthy();
    const branch = el.querySelector('.issue-detail__branch');
    expect(branch).toBeFalsy();
    const prLink = el.querySelector('.issue-detail__pr-link');
    expect(prLink).toBeFalsy();
  });

  it('should render Author field when stateDetails is null', () => {
    // Arrange
    const detectedDetail: IssueDetail = {
      ...mockDetail,
      state: 'detected',
      author: 'octocat',
      stateDetails: null,
    };

    // Act
    const fixture = createComponent(detectedDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const fields = Array.from(el.querySelectorAll('.issue-detail__field'));
    const authorField = fields.find((f) => f.querySelector('.issue-detail__field-key')?.textContent?.trim() === 'Author');
    expect(authorField).toBeTruthy();
    expect(authorField?.querySelector('.issue-detail__field-value')?.textContent?.trim()).toBe('octocat');
  });

  // Retry button — shown for all retryable states
  // Positive cases are driven by RETRYABLE_STATES so the spec stays bound to
  // the single source of truth; adding a state to the set automatically tests it.
  it('should cover the expected set of retryable states', () => {
    // Arrange + Assert — pin membership so an unreviewed addition is caught
    expect([...RETRYABLE_STATES].sort()).toEqual(
      ['branch_diverged', 'continuable_failed', 'failed', 'revision_failed', 'unchanged'],
    );
  });

  for (const state of RETRYABLE_STATES) {
    it(`should render the retry button when state is ${state}`, () => {
      // Arrange
      const retryableDetail: IssueDetail = { ...mockDetail, state };

      // Act
      const fixture = createComponent(retryableDetail);
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      const btn = el.querySelector('.issue-detail__retry-btn') as HTMLButtonElement;
      expect(btn).toBeTruthy();
    });
  }

  it('should not render the retry button for a non-retryable state (completed)', () => {
    // Arrange — completed is not in the retryable set
    const fixture = createComponent(mockDetail); // mockDetail.state = 'completed'
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const btn = el.querySelector('.issue-detail__retry-btn');
    expect(btn).toBeFalsy();
  });

  it('should not render the retry button for review state', () => {
    // Arrange — review has its own feedback flow, not a retry
    const reviewDetail: IssueDetail = { ...mockDetail, state: 'review' };

    // Act
    const fixture = createComponent(reviewDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const btn = el.querySelector('.issue-detail__retry-btn');
    expect(btn).toBeFalsy();
  });

  it('should call retryIssue on the service when retry button is clicked for a failed state', () => {
    // Arrange
    const failedDetail: IssueDetail = { ...mockDetail, state: 'failed' };
    const fixture = createComponent(failedDetail);
    const el = fixture.nativeElement as HTMLElement;
    const http = TestBed.inject(HttpTestingController);

    // Act
    const btn = el.querySelector('.issue-detail__retry-btn') as HTMLButtonElement;
    btn.click();
    const req = http.expectOne(`/api/issues/${failedDetail.id}/retry`);

    // Assert
    expect(req.request.method).toBe('POST');
    req.flush({});
    http.expectOne(`/api/issues/${failedDetail.id}`).flush(failedDetail);
    http.verify();
  });

  it('should POST to /api/issues/{id}/retry when retry button is clicked for unchanged state', () => {
    // Arrange
    const unchangedDetail: IssueDetail = { ...mockDetail, state: 'unchanged' };
    const fixture = createComponent(unchangedDetail);
    const el = fixture.nativeElement as HTMLElement;
    const http = TestBed.inject(HttpTestingController);

    // Act
    const btn = el.querySelector('.issue-detail__retry-btn') as HTMLButtonElement;
    btn.click();
    const req = http.expectOne(`/api/issues/${unchangedDetail.id}/retry`);

    // Assert
    expect(req.request.method).toBe('POST');
    req.flush({});
    http.expectOne(`/api/issues/${unchangedDetail.id}`).flush(unchangedDetail);
    http.verify();
  });

  it('should set aria-label on retry button referencing the issue number', () => {
    // Arrange
    const failedDetail: IssueDetail = { ...mockDetail, state: 'failed' };

    // Act
    const fixture = createComponent(failedDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const btn = el.querySelector('.issue-detail__retry-btn') as HTMLButtonElement;
    expect(btn?.getAttribute('aria-label')).toBe('Retry issue #42');
  });

  it('should disable the retry button while retrying is true', () => {
    // Arrange
    const failedDetail: IssueDetail = { ...mockDetail, state: 'failed' };
    const fixture = createComponent(failedDetail);
    const el = fixture.nativeElement as HTMLElement;
    const http = TestBed.inject(HttpTestingController);

    // Act — click starts the request
    const btn = el.querySelector('.issue-detail__retry-btn') as HTMLButtonElement;
    btn.click();
    fixture.detectChanges();

    // Assert — button is disabled while in flight
    expect(btn.disabled).toBe(true);
    expect(btn.textContent?.trim()).toBe('Retrying Issue...');

    // Cleanup
    http.expectOne(`/api/issues/${failedDetail.id}/retry`).flush({});
    http.expectOne(`/api/issues/${failedDetail.id}`).flush(failedDetail);
    http.verify();
  });

  it('should always render the retry error span in the DOM when state is failed', () => {
    // Arrange
    const failedDetail: IssueDetail = { ...mockDetail, state: 'failed' };

    // Act
    const fixture = createComponent(failedDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert — span is present even with no error (empty content)
    const errorEl = el.querySelector('.issue-detail__retry-error');
    expect(errorEl).toBeTruthy();
    expect(errorEl?.textContent?.trim()).toBe('');
  });

  it('should show retry error message when retryIssue fails', () => {
    // Arrange
    const failedDetail: IssueDetail = { ...mockDetail, state: 'failed' };
    const fixture = createComponent(failedDetail);
    const el = fixture.nativeElement as HTMLElement;
    const issueService = TestBed.inject(IssueService);
    const http = TestBed.inject(HttpTestingController);

    // Act — trigger a failure
    const btn = el.querySelector('.issue-detail__retry-btn') as HTMLButtonElement;
    btn.click();
    http.expectOne(`/api/issues/${failedDetail.id}/retry`).flush('Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    fixture.detectChanges();

    // Assert — error is surfaced next to the button (span always present, now contains text)
    expect(issueService.retryError()).toBe('Failed to retry issue.');
    const errorEl = el.querySelector('.issue-detail__retry-error');
    expect(errorEl).toBeTruthy();
    expect(errorEl?.textContent?.trim()).toBe('Failed to retry issue.');
    http.verify();
  });

  // UX-5: success announcement live region
  it('should render a polite live region for success announcements in the actions area', () => {
    // Arrange
    const failedDetail: IssueDetail = { ...mockDetail, state: 'failed' };

    // Act
    const fixture = createComponent(failedDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert — live region is always present
    const liveRegion = el.querySelector('.issue-detail__retry-success-announcement');
    expect(liveRegion).toBeTruthy();
    expect(liveRegion?.getAttribute('aria-live')).toBe('polite');
  });

  it('should announce success message after successful retry', () => {
    // Arrange
    const failedDetail: IssueDetail = { ...mockDetail, state: 'failed' };
    const fixture = createComponent(failedDetail);
    const el = fixture.nativeElement as HTMLElement;
    const http = TestBed.inject(HttpTestingController);

    // Act — trigger a successful retry
    const btn = el.querySelector('.issue-detail__retry-btn') as HTMLButtonElement;
    btn.click();
    http.expectOne(`/api/issues/${failedDetail.id}/retry`).flush({});
    http.expectOne(`/api/issues/${failedDetail.id}`).flush(failedDetail);
    fixture.detectChanges();

    // Assert — announcement text is set
    const liveRegion = el.querySelector('.issue-detail__retry-success-announcement');
    expect(liveRegion?.textContent?.trim()).toBe('Retry queued. Issue status is updating.');
    http.verify();
  });

  // Worker run section: when workerRunId is null, no worker run block renders
  it('should not render worker run block when workerRunId is null', () => {
    // Arrange
    const fixture = createComponent(mockDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const workerRun = el.querySelector('.issue-detail__worker-run');
    expect(workerRun).toBeFalsy();
  });

  // Step 7: run-stats row must not appear in the detail panel (rendered on the card instead)
  it('should NOT render a run-stats row in the detail panel', () => {
    // Arrange — worker run with all telemetry fields populated
    const failedDetail: IssueDetail = {
      ...mockDetail,
      state: 'failed',
      stateDetails: { ...mockStateDetails, workerRunId: 'run-123' },
    };
    const mockWorkerRunServiceWithStats = {
      getDetail: () => of({
        workerRunId: 'run-123',
        issueId: 'issue-1',
        state: 'failed',
        failureCategory: null,
        failureSummary: null,
        resultText: null,
        subtype: null,
        isError: null,
        durationMs: 5000,
        numTurns: 10,
        totalCostUsd: 1.23,
        inputTokens: 5000,
        outputTokens: 2000,
        lastActivityAt: null,
        hasStoredLog: false,
        secretFindings: [],
        revertedFiles: [],
      }),
      getLog: () => of(null),
    };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [IssueDetailComponent],
      providers: [
        { provide: IssueService, useValue: { retrying: signal(false), retryError: signal(null), retrySuccess: signal(null), ...triageSignals() } },
        WorkerSignalRService,
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: IssueSignalRService, useValue: mockIssueSignalRService },
        { provide: WorkerRunService, useValue: mockWorkerRunServiceWithStats },
        { provide: WORKER_HUB_FACTORY, useValue: () => mockWorkerHub },
      ],
    });
    const fixture = TestBed.createComponent(IssueDetailComponent);
    fixture.componentRef.setInput('detail', failedDetail);
    fixture.componentRef.setInput('loading', false);
    fixture.componentRef.setInput('error', null);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    // Assert — stat row must be absent; telemetry lives on the card
    const runStats = el.querySelector('.issue-detail__run-stats');
    expect(runStats).toBeFalsy();
  });

  // Worker run section: failure chip renders when WorkerRunDetail is returned with a failureCategory
  it('should render failure category chip when worker run service returns a failureCategory', () => {
    // Arrange
    const failedDetail: IssueDetail = {
      ...mockDetail,
      state: 'failed',
      stateDetails: { ...mockStateDetails, workerRunId: 'run-123' },
    };

    const mockWorkerRunServiceWithDetail = {
      getDetail: () => of({
        workerRunId: 'run-123',
        issueId: 'issue-1',
        state: 'failed',
        failureCategory: 'worker_bootstrap_failed',
        failureSummary: 'Worker bootstrap failed: container died',
        resultText: null,
        subtype: null,
        isError: null,
        durationMs: 5000,
        numTurns: null,
        totalCostUsd: null,
        inputTokens: null,
        outputTokens: null,
        lastActivityAt: null,
        hasStoredLog: false,
        secretFindings: [],
        revertedFiles: [],
      }),
      getLog: () => of(null),
    };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [IssueDetailComponent],
      providers: [
        IssueService,
        WorkerSignalRService,
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: IssueSignalRService, useValue: mockIssueSignalRService },
        { provide: WorkerRunService, useValue: mockWorkerRunServiceWithDetail },
        { provide: WORKER_HUB_FACTORY, useValue: () => mockWorkerHub },
      ],
    });
    const fixture = TestBed.createComponent(IssueDetailComponent);
    fixture.componentRef.setInput('detail', failedDetail);
    fixture.componentRef.setInput('loading', false);
    fixture.componentRef.setInput('error', null);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const chip = el.querySelector('.issue-detail__failure-chip');
    expect(chip).toBeTruthy();
    expect(chip?.textContent?.trim()).toBe('BOOTSTRAP FAILED');
    const summary = el.querySelector('.issue-detail__failure-summary');
    expect(summary?.textContent?.trim()).toBe('Worker bootstrap failed: container died');
  });

  // Worker run section: a turn-limited run shows its chip beside the split-the-issue summary
  it('should render the turn limit chip and its summary when the worker run failed on the turn limit', () => {
    // Arrange
    const failedDetail: IssueDetail = {
      ...mockDetail,
      state: 'failed',
      stateDetails: { ...mockStateDetails, workerRunId: 'run-turn-limit' },
    };
    const turnLimitSummary = 'Reached the turn limit before finishing. Split the issue into smaller issues, then retry.';
    const mockWorkerRunServiceTurnLimit = {
      getDetail: () => of({
        workerRunId: 'run-turn-limit',
        issueId: 'issue-1',
        state: 'failed',
        failureCategory: 'turn_limit',
        failureSummary: turnLimitSummary,
        resultText: null,
        subtype: 'error_max_turns',
        isError: true,
        durationMs: 5000,
        numTurns: 301,
        totalCostUsd: null,
        inputTokens: null,
        outputTokens: null,
        lastActivityAt: null,
        hasStoredLog: false,
        secretFindings: [],
        revertedFiles: [],
      }),
      getLog: () => of(null),
    };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [IssueDetailComponent],
      providers: [
        IssueService,
        WorkerSignalRService,
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: IssueSignalRService, useValue: mockIssueSignalRService },
        { provide: WorkerRunService, useValue: mockWorkerRunServiceTurnLimit },
        { provide: WORKER_HUB_FACTORY, useValue: () => mockWorkerHub },
      ],
    });
    const fixture = TestBed.createComponent(IssueDetailComponent);
    fixture.componentRef.setInput('detail', failedDetail);
    fixture.componentRef.setInput('loading', false);
    fixture.componentRef.setInput('error', null);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const chip = el.querySelector('.issue-detail__failure-chip');
    expect(chip?.classList).toContain('badge--failure-turn-limit');
    expect(chip?.textContent?.trim()).toBe('TURN LIMIT');
    expect(chip?.getAttribute('aria-label')).toBe('Failure category: TURN LIMIT');
    const summary = el.querySelector('.issue-detail__failure-summary');
    expect(summary?.textContent?.trim()).toBe(turnLimitSummary);
  });

  // Worker run section: auth and transient Claude API failures show their own chip
  for (const { category, label, cssClass } of [
    { category: 'auth_invalid', label: 'AUTH FAILED', cssClass: 'badge--failure-auth-invalid' },
    { category: 'transient_api_error', label: 'CLAUDE API ERROR', cssClass: 'badge--failure-transient-api-error' },
  ]) {
    it(`should render the ${label} chip when the worker run failed with ${category}`, () => {
      // Arrange
      const failedDetail: IssueDetail = {
        ...mockDetail,
        state: 'failed',
        stateDetails: { ...mockStateDetails, workerRunId: 'run-category' },
      };
      const mockWorkerRunServiceCategory = {
        getDetail: () => of({
          workerRunId: 'run-category',
          issueId: 'issue-1',
          state: 'failed',
          failureCategory: category,
          failureSummary: 'Worker failed',
          resultText: null,
          subtype: null,
          isError: true,
          durationMs: 5000,
          numTurns: null,
          totalCostUsd: null,
          inputTokens: null,
          outputTokens: null,
          lastActivityAt: null,
          hasStoredLog: false,
          secretFindings: [],
          revertedFiles: [],
        }),
        getLog: () => of(null),
      };

      TestBed.resetTestingModule();
      TestBed.configureTestingModule({
        imports: [IssueDetailComponent],
        providers: [
          IssueService,
          WorkerSignalRService,
          provideHttpClient(),
          provideHttpClientTesting(),
          { provide: IssueSignalRService, useValue: mockIssueSignalRService },
          { provide: WorkerRunService, useValue: mockWorkerRunServiceCategory },
          { provide: WORKER_HUB_FACTORY, useValue: () => mockWorkerHub },
        ],
      });
      const fixture = TestBed.createComponent(IssueDetailComponent);
      fixture.componentRef.setInput('detail', failedDetail);
      fixture.componentRef.setInput('loading', false);
      fixture.componentRef.setInput('error', null);
      fixture.detectChanges();
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      const chip = el.querySelector('.issue-detail__failure-chip');
      expect(chip?.classList).toContain(cssClass);
      expect(chip?.textContent?.trim()).toBe(label);
      expect(chip?.getAttribute('aria-label')).toBe(`Failure category: ${label}`);
    });
  }

  // Worker run section: a completed run with a stored log shows it, keeping each line's timestamp
  it('should render the stored log with timestamped lines for a completed run with hasStoredLog', () => {
    // Arrange
    const reviewDetail: IssueDetail = {
      ...mockDetail,
      state: 'review',
      stateDetails: { ...mockStateDetails, workerRunId: 'run-completed' },
    };
    const getLog = vi.fn(() => of(
      '2026-11-09T10:00:00.000000000Z PR opened\n2026-11-09T10:20:00.000000000Z exiting',
    ));
    const mockWorkerRunServiceCompleted = {
      getDetail: () => of({
        workerRunId: 'run-completed',
        issueId: 'issue-1',
        state: 'completed',
        failureCategory: null,
        failureSummary: null,
        resultText: null,
        subtype: null,
        isError: null,
        durationMs: null,
        numTurns: null,
        totalCostUsd: null,
        inputTokens: null,
        outputTokens: null,
        lastActivityAt: null,
        hasStoredLog: true,
        secretFindings: [],
        revertedFiles: [],
      }),
      getLog,
    };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [IssueDetailComponent],
      providers: [
        IssueService,
        WorkerSignalRService,
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: IssueSignalRService, useValue: mockIssueSignalRService },
        { provide: WorkerRunService, useValue: mockWorkerRunServiceCompleted },
        { provide: WORKER_HUB_FACTORY, useValue: () => mockWorkerHub },
      ],
    });
    const fixture = TestBed.createComponent(IssueDetailComponent);
    fixture.componentRef.setInput('detail', reviewDetail);
    fixture.componentRef.setInput('loading', false);
    fixture.componentRef.setInput('error', null);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(getLog).toHaveBeenCalledWith('run-completed');
    const lines = Array.from(el.querySelectorAll('fd-log-view .log-view__line'))
      .map((line) => line.textContent);
    expect(lines).toEqual([
      '2026-11-09T10:00:00.000000000Z PR opened',
      '2026-11-09T10:20:00.000000000Z exiting',
    ]);
  });

  // Transient retry block — AC1: active retry shows attempt chip and next-attempt time
  it('should render the "Attempt N of M" chip and next-attempt time when transientRetry is present and not exhausted', () => {
    // Arrange
    const retryingDetail: IssueDetail = {
      ...mockDetail,
      state: 'failed',
      stateDetails: {
        ...mockStateDetails,
        transientRetry: {
          attemptNumber: 1,
          maxAttempts: 2,
          isExhausted: false,
          nextAttemptDueAt: '2026-08-10T14:30:00Z',
        },
      },
    };

    // Act
    const fixture = createComponent(retryingDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const chip = el.querySelector('.issue-detail__retry-chip') as HTMLElement;
    expect(chip).toBeTruthy();
    expect(chip?.textContent?.trim()).toContain('Attempt 1 of 2');
    const message = el.querySelector('.issue-detail__retry-message') as HTMLElement;
    expect(message).toBeTruthy();
    expect(message?.textContent?.trim()).toContain('Automatic retry pending');
  });

  // Transient retry block — AC2: exhausted shows "Retry exhausted" chip and manual retry copy
  it('should render the "Retry exhausted" chip and manual retry copy when transientRetry is exhausted', () => {
    // Arrange
    const exhaustedDetail: IssueDetail = {
      ...mockDetail,
      state: 'failed',
      stateDetails: {
        ...mockStateDetails,
        transientRetry: {
          attemptNumber: 2,
          maxAttempts: 2,
          isExhausted: true,
          nextAttemptDueAt: null,
        },
      },
    };

    // Act
    const fixture = createComponent(exhaustedDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const chip = el.querySelector('.issue-detail__retry-chip') as HTMLElement;
    expect(chip).toBeTruthy();
    expect(chip?.textContent?.trim()).toContain('Retry exhausted');
    const message = el.querySelector('.issue-detail__retry-message') as HTMLElement;
    expect(message).toBeTruthy();
    expect(message?.textContent?.trim()).toContain('Automatic retries exhausted after 2 attempts');
    expect(message?.textContent?.trim()).toContain('Use Retry Issue to try again manually');
  });

  // Transient retry block — AC3: null transientRetry renders no retry block
  it('should not render a retry block when transientRetry is null', () => {
    // Arrange
    const noRetryDetail: IssueDetail = {
      ...mockDetail,
      state: 'failed',
      stateDetails: {
        ...mockStateDetails,
        transientRetry: null,
      },
    };

    // Act
    const fixture = createComponent(noRetryDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const retryState = el.querySelector('.issue-detail__retry-state');
    expect(retryState).toBeFalsy();
  });

  // Live region — always present within state-details, populated when retry data is present, empty otherwise
  it('should render a persistent aria-live region inside state details when transientRetry is present', () => {
    // Arrange
    const retryingDetail: IssueDetail = {
      ...mockDetail,
      state: 'failed',
      stateDetails: {
        ...mockStateDetails,
        transientRetry: {
          attemptNumber: 1,
          maxAttempts: 2,
          isExhausted: false,
          nextAttemptDueAt: '2026-08-10T14:30:00Z',
        },
      },
    };

    // Act
    const fixture = createComponent(retryingDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert — retry live region exists and contains retry content
    const liveRegion = el.querySelector('.issue-detail__retry-live-region') as HTMLElement;
    expect(liveRegion).toBeTruthy();
    expect(liveRegion?.textContent?.trim()).toContain('Attempt 1 of 2');
  });

  it('should render a persistent aria-live region that is empty when transientRetry is null', () => {
    // Arrange
    const noRetryDetail: IssueDetail = {
      ...mockDetail,
      state: 'failed',
      stateDetails: {
        ...mockStateDetails,
        transientRetry: null,
      },
    };

    // Act
    const fixture = createComponent(noRetryDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert — retry live region is present but empty; stateDetails is non-null so the region is mounted
    const liveRegion = el.querySelector('.issue-detail__retry-live-region') as HTMLElement;
    expect(liveRegion).toBeTruthy();
    expect(liveRegion?.textContent?.trim()).toBe('');
  });

  // Null guard on nextAttemptDueAt — non-exhausted retry with null nextAttemptDueAt shows fallback text
  it('should show fallback text when nextAttemptDueAt is null on a non-exhausted retry', () => {
    // Arrange
    const retryNullDue: IssueDetail = {
      ...mockDetail,
      state: 'failed',
      stateDetails: {
        ...mockStateDetails,
        transientRetry: {
          attemptNumber: 1,
          maxAttempts: 2,
          isExhausted: false,
          nextAttemptDueAt: null,
        },
      },
    };

    // Act
    const fixture = createComponent(retryNullDue);
    const el = fixture.nativeElement as HTMLElement;

    // Assert — no dangling "next attempt at ." fragment; fallback phrase is present
    const message = el.querySelector('.issue-detail__retry-message') as HTMLElement;
    expect(message).toBeTruthy();
    const text = message?.textContent?.trim() ?? '';
    expect(text).toContain('shortly');
    expect(text).not.toContain('next attempt at .');
  });

  // --- Rebase outcome row (#684): shown for every outcome ---

  it.each([
    ['up_to_date', null, 'Up to date'],
    ['rebased', null, 'Rebased'],
    ['disabled', null, 'Disabled'],
    ['skipped', 'timeout', 'Skipped (timeout)'],
    ['skipped', 'permission-denied', 'Skipped (permission denied)'],
    ['skipped', 'force-push-protected', 'Skipped (force-push protected)'],
    ['skipped', 'rebase-failed', 'Skipped (rebase failed)'],
    ['skipped', 'brand-new-reason', 'Skipped (brand-new-reason)'],
    ['conflict', null, 'Conflict'],
  ])('should render a Rebase row for %s outcome (reason %s) as "%s"', (outcome, reason, expected) => {
    // Arrange
    const detail: IssueDetail = {
      ...mockDetail,
      state: 'review',
      stateDetails: { ...mockStateDetails, rebaseOutcome: outcome, rebaseSkippedReason: reason },
    };

    // Act
    const fixture = createComponent(detail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const row = el.querySelector('.issue-detail__rebase');
    expect(row).toBeTruthy();
    expect(row?.querySelector('.issue-detail__field-key')?.textContent?.trim()).toBe('Rebase');
    expect(row?.querySelector('.issue-detail__field-value')?.textContent?.trim()).toBe(expected);
  });

  it('should NOT render a Rebase row when rebaseOutcome is not set', () => {
    // Arrange
    const detail: IssueDetail = {
      ...mockDetail,
      state: 'review',
      stateDetails: { ...mockStateDetails, rebaseOutcome: null },
    };

    // Act
    const fixture = createComponent(detail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('.issue-detail__rebase')).toBeFalsy();
  });

  // --- Awaiting input: open questions and post status ---

  it('should list the open questions of an awaiting_input issue', () => {
    // Arrange
    const awaitingDetail: IssueDetail = {
      ...mockDetail,
      state: 'awaiting_input',
      stateDetails: {
        ...mockStateDetails,
        awaitingInput: {
          questions: [{ question: 'Which database?', context: 'Two are configured.' }],
          postStatus: 'pending',
          postedAt: null,
          commentUrl: null,
          postFailureReason: null,
          nextPostAttemptAt: null,
          resumeTarget: 'revision',
        },
      },
    };

    // Act
    const fixture = createComponent(awaitingDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const section = el.querySelector('.issue-detail__awaiting-input');
    expect(section?.textContent).toContain('Open Questions');
    expect(section?.textContent).toContain('Which database?');
    expect(section?.textContent).toContain('Two are configured.');
    expect(section?.textContent).toContain('Posting the questions');
  });

  function awaitingWithQuestion(question: InputQuestion): IssueDetail {
    return {
      ...mockDetail,
      state: 'awaiting_input',
      stateDetails: {
        ...mockStateDetails,
        awaitingInput: {
          questions: [question],
          postStatus: 'pending',
          postedAt: null,
          commentUrl: null,
          postFailureReason: null,
          nextPostAttemptAt: null,
          resumeTarget: 'revision',
        },
      },
    };
  }

  it('should show the recommendation of a question after its context', () => {
    // Act
    const fixture = createComponent(
      awaitingWithQuestion({
        question: 'Which database?',
        context: 'Two are configured.',
        recommendation: 'Postgres, since production runs it.',
      }),
    );
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const recommendation = el.querySelector('.issue-detail__question-recommendation');
    expect(recommendation?.textContent).toContain('Recommended:');
    expect(recommendation?.textContent).toContain('Postgres, since production runs it.');
    expect(recommendation?.previousElementSibling?.classList).toContain(
      'issue-detail__question-context',
    );
  });

  it.each([null, undefined, ''])(
    'should render no recommendation when it is %j',
    (recommendation) => {
      // Act
      const fixture = createComponent(
        awaitingWithQuestion({
          question: 'Which database?',
          context: 'Two are configured.',
          recommendation,
        }),
      );
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      expect(el.querySelector('.issue-detail__question-recommendation')).toBeFalsy();
    },
  );

  it('should link to the posted question once posted', () => {
    // Arrange
    const awaitingDetail: IssueDetail = {
      ...mockDetail,
      state: 'awaiting_input',
      stateDetails: {
        ...mockStateDetails,
        awaitingInput: {
          questions: [{ question: 'Which database?', context: '' }],
          postStatus: 'posted',
          postedAt: '2026-11-11T10:00:00Z',
          commentUrl: 'https://github.com/owner/repo/pull/7#issuecomment-555',
          postFailureReason: null,
          nextPostAttemptAt: null,
          resumeTarget: 'revision',
        },
      },
    };

    // Act
    const fixture = createComponent(awaitingDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const link = el.querySelector<HTMLAnchorElement>('.issue-detail__question-link');
    expect(link?.href).toBe('https://github.com/owner/repo/pull/7#issuecomment-555');
  });

  // --- Surface 3: still_failing failing-checks list ---

  it('should render failing-checks section when checksOutcome is still_failing with non-empty failingChecks', () => {
    // Arrange
    const reviewDetail: IssueDetail = {
      ...mockDetail,
      state: 'review',
      stateDetails: {
        ...mockStateDetails,
        checksOutcome: 'still_failing',
        failingChecks: [
          { name: 'build / dotnet-test', conclusion: 'failure', detailsUrl: 'https://github.com/owner/repo/runs/123' },
        ],
      },
    };

    // Act
    const fixture = createComponent(reviewDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const section = el.querySelector('.issue-detail__failing-checks');
    expect(section).toBeTruthy();
  });

  it('should render "Failing Checks" field-key heading in the section', () => {
    // Arrange
    const reviewDetail: IssueDetail = {
      ...mockDetail,
      state: 'review',
      stateDetails: {
        ...mockStateDetails,
        checksOutcome: 'still_failing',
        failingChecks: [
          { name: 'build / dotnet-test', conclusion: 'failure', detailsUrl: 'https://github.com/owner/repo/runs/123' },
        ],
      },
    };

    // Act
    const fixture = createComponent(reviewDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const section = el.querySelector('.issue-detail__failing-checks');
    const heading = section?.querySelector('.issue-detail__field-key');
    expect(heading?.textContent?.trim()).toBe('Failing Checks');
  });

  it('should render each failing check as a list item with name and conclusion', () => {
    // Arrange
    const reviewDetail: IssueDetail = {
      ...mockDetail,
      state: 'review',
      stateDetails: {
        ...mockStateDetails,
        checksOutcome: 'still_failing',
        failingChecks: [
          { name: 'build / dotnet-test', conclusion: 'failure', detailsUrl: 'https://github.com/owner/repo/runs/123' },
          { name: 'lint', conclusion: 'timed_out', detailsUrl: null },
        ],
      },
    };

    // Act
    const fixture = createComponent(reviewDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const items = el.querySelectorAll('.issue-detail__failing-item');
    expect(items.length).toBe(2);

    const firstName = items[0].querySelector('.issue-detail__failing-name');
    expect(firstName?.textContent?.trim()).toBe('build / dotnet-test');

    const firstConclusion = items[0].querySelector('.issue-detail__failing-conclusion');
    expect(firstConclusion?.textContent?.trim()).toBe('failure');
  });

  it('should render conclusion chip with role="img" and aria-label', () => {
    // Arrange
    const reviewDetail: IssueDetail = {
      ...mockDetail,
      state: 'review',
      stateDetails: {
        ...mockStateDetails,
        checksOutcome: 'still_failing',
        failingChecks: [
          { name: 'build / dotnet-test', conclusion: 'failure', detailsUrl: null },
        ],
      },
    };

    // Act
    const fixture = createComponent(reviewDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const chip = el.querySelector('.issue-detail__failing-conclusion') as HTMLElement;
    expect(chip?.getAttribute('role')).toBe('img');
    expect(chip?.getAttribute('aria-label')).toBe('Conclusion: failure');
  });

  it('should render a "View details" link when detailsUrl is present', () => {
    // Arrange
    const reviewDetail: IssueDetail = {
      ...mockDetail,
      state: 'review',
      stateDetails: {
        ...mockStateDetails,
        checksOutcome: 'still_failing',
        failingChecks: [
          { name: 'build / dotnet-test', conclusion: 'failure', detailsUrl: 'https://github.com/owner/repo/runs/123' },
        ],
      },
    };

    // Act
    const fixture = createComponent(reviewDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const link = el.querySelector('.issue-detail__failing-link') as HTMLAnchorElement;
    expect(link).toBeTruthy();
    expect(link?.textContent?.trim()).toBe('View details');
    expect(link?.getAttribute('href')).toBe('https://github.com/owner/repo/runs/123');
    expect(link?.getAttribute('target')).toBe('_blank');
    expect(link?.getAttribute('rel')).toBe('noopener noreferrer');
  });

  it('should set descriptive aria-label on the View details link', () => {
    // Arrange
    const reviewDetail: IssueDetail = {
      ...mockDetail,
      state: 'review',
      stateDetails: {
        ...mockStateDetails,
        checksOutcome: 'still_failing',
        failingChecks: [
          { name: 'build / dotnet-test', conclusion: 'failure', detailsUrl: 'https://github.com/owner/repo/runs/123' },
        ],
      },
    };

    // Act
    const fixture = createComponent(reviewDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const link = el.querySelector('.issue-detail__failing-link') as HTMLAnchorElement;
    expect(link?.getAttribute('aria-label')).toBe('View details for failing check build / dotnet-test');
  });

  it('should NOT render a "View details" link when detailsUrl is null', () => {
    // Arrange
    const reviewDetail: IssueDetail = {
      ...mockDetail,
      state: 'review',
      stateDetails: {
        ...mockStateDetails,
        checksOutcome: 'still_failing',
        failingChecks: [
          { name: 'lint', conclusion: 'timed_out', detailsUrl: null },
        ],
      },
    };

    // Act
    const fixture = createComponent(reviewDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const link = el.querySelector('.issue-detail__failing-link');
    expect(link).toBeFalsy();
  });

  it('should NOT render failing-checks section when checksOutcome is not still_failing', () => {
    // Arrange
    const reviewDetail: IssueDetail = {
      ...mockDetail,
      state: 'review',
      stateDetails: {
        ...mockStateDetails,
        checksOutcome: 'passed',
        failingChecks: null,
      },
    };

    // Act
    const fixture = createComponent(reviewDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const section = el.querySelector('.issue-detail__failing-checks');
    expect(section).toBeFalsy();
  });

  it('should NOT render failing-checks section when checksOutcome is still_failing but failingChecks is empty', () => {
    // Arrange
    const reviewDetail: IssueDetail = {
      ...mockDetail,
      state: 'review',
      stateDetails: {
        ...mockStateDetails,
        checksOutcome: 'still_failing',
        failingChecks: [],
      },
    };

    // Act
    const fixture = createComponent(reviewDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const section = el.querySelector('.issue-detail__failing-checks');
    expect(section).toBeFalsy();
  });

  it('should NOT render failing-checks section when checksOutcome is still_failing but failingChecks is null', () => {
    // Arrange
    const reviewDetail: IssueDetail = {
      ...mockDetail,
      state: 'review',
      stateDetails: {
        ...mockStateDetails,
        checksOutcome: 'still_failing',
        failingChecks: null,
      },
    };

    // Act
    const fixture = createComponent(reviewDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const section = el.querySelector('.issue-detail__failing-checks');
    expect(section).toBeFalsy();
  });

  // Fix 3d: failing-checks <ul> labelled by its heading
  it('should label the failing-checks <ul> with its heading id (Fix 3d)', () => {
    // Arrange
    const reviewDetail: IssueDetail = {
      ...mockDetail,
      state: 'review',
      stateDetails: {
        ...mockStateDetails,
        checksOutcome: 'still_failing',
        failingChecks: [
          { name: 'build / dotnet-test', conclusion: 'failure', detailsUrl: null },
        ],
      },
    };

    // Act
    const fixture = createComponent(reviewDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert — heading has an id; <ul> references it via aria-labelledby
    const section = el.querySelector('.issue-detail__failing-checks') as HTMLElement;
    const heading = section?.querySelector('.issue-detail__field-key') as HTMLElement;
    const list = section?.querySelector('.issue-detail__failing-list') as HTMLElement;
    const headingId = heading?.getAttribute('id');
    expect(headingId).toBeTruthy();
    expect(list?.getAttribute('aria-labelledby')).toBe(headingId);
  });

  // Fix 4: "View issue" link guarded by safeHref
  it('should NOT render the View issue link when issue url is not https (Fix 4)', () => {
    // Arrange — http:// url should be blocked by safeHref
    const httpDetail: IssueDetail = {
      ...mockDetail,
      url: 'http://github.com/owner/repo/issues/42',
    };

    // Act
    const fixture = createComponent(httpDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const link = el.querySelector('.issue-detail__issue-link');
    expect(link).toBeFalsy();
  });

  it('should render the View issue link when issue url is https (Fix 4 guard-pass)', () => {
    // Arrange — https:// url must still render
    const fixture = createComponent(mockDetail); // mockDetail has https url

    // Act
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const link = el.querySelector('.issue-detail__issue-link') as HTMLAnchorElement;
    expect(link).toBeTruthy();
    expect(link?.getAttribute('href')).toBe('https://github.com/owner/repo/issues/42');
  });

  // Revision callout — Cycle R1: failed_checks shows "Fixing checks (attempt n of 2)" chip
  it('should render "Fixing checks (attempt 1 of 2)" chip when revisionTriggerKind is failed_checks and state is revision_in_progress', () => {
    // Arrange
    const detail: IssueDetail = {
      ...mockDetail,
      state: 'revision_in_progress',
      stateDetails: {
        ...mockStateDetails,
        revisionTriggerKind: 'failed_checks',
        checksFixAttempts: 1,
      },
    };

    // Act
    const fixture = createComponent(detail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const chip = el.querySelector('.issue-detail__revision-chip') as HTMLElement;
    expect(chip).toBeTruthy();
    expect(chip?.textContent?.trim()).toBe(`Fixing checks (attempt 1 of ${MAX_CHECKS_FIX_ATTEMPTS})`);
  });

  // Revision callout — Cycle R2: failed_checks attempt 2 renders correctly
  it(`should render "Fixing checks (attempt 2 of ${MAX_CHECKS_FIX_ATTEMPTS})" chip when checksFixAttempts is 2`, () => {
    // Arrange
    const detail: IssueDetail = {
      ...mockDetail,
      state: 'revision_in_progress',
      stateDetails: {
        ...mockStateDetails,
        revisionTriggerKind: 'failed_checks',
        checksFixAttempts: 2,
      },
    };

    // Act
    const fixture = createComponent(detail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const chip = el.querySelector('.issue-detail__revision-chip') as HTMLElement;
    expect(chip?.textContent?.trim()).toBe(`Fixing checks (attempt 2 of ${MAX_CHECKS_FIX_ATTEMPTS})`);
  });

  // Revision callout — Cycle R3: failed_checks defaults attempt to 1 when checksFixAttempts is null
  it('should default to attempt 1 when checksFixAttempts is null for failed_checks', () => {
    // Arrange
    const detail: IssueDetail = {
      ...mockDetail,
      state: 'revision_in_progress',
      stateDetails: {
        ...mockStateDetails,
        revisionTriggerKind: 'failed_checks',
        checksFixAttempts: null,
      },
    };

    // Act
    const fixture = createComponent(detail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const chip = el.querySelector('.issue-detail__revision-chip') as HTMLElement;
    expect(chip?.textContent?.trim()).toBe(`Fixing checks (attempt 1 of ${MAX_CHECKS_FIX_ATTEMPTS})`);
  });

  // Revision callout — Cycle R4: revision_queued + failed_checks shows queued message
  it('should show queued message for failed_checks when state is revision_queued', () => {
    // Arrange
    const detail: IssueDetail = {
      ...mockDetail,
      state: 'revision_queued',
      stateDetails: {
        ...mockStateDetails,
        revisionTriggerKind: 'failed_checks',
        checksFixAttempts: 1,
      },
    };

    // Act
    const fixture = createComponent(detail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const message = el.querySelector('.issue-detail__revision-message') as HTMLElement;
    expect(message?.textContent?.trim()).toContain('Fix run queued');
  });

  it(`should render "Resolving conflicts (attempt 2 of ${MAX_REBASE_CONFLICT_ATTEMPTS})" chip when revisionTriggerKind is rebase_conflict`, () => {
    // Arrange
    const detail: IssueDetail = {
      ...mockDetail,
      state: 'revision_in_progress',
      stateDetails: {
        ...mockStateDetails,
        revisionTriggerKind: 'rebase_conflict',
        rebaseConflictAttempts: 2,
      },
    };

    // Act
    const fixture = createComponent(detail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const chip = el.querySelector('.issue-detail__revision-chip') as HTMLElement;
    const message = el.querySelector('.issue-detail__revision-message') as HTMLElement;
    expect(chip?.textContent?.trim()).toBe(`Resolving conflicts (attempt 2 of ${MAX_REBASE_CONFLICT_ATTEMPTS})`);
    expect(message?.textContent?.trim()).toContain('a worker is running');
  });

  it('should show queued conflict-resolution message for rebase_conflict when state is revision_queued', () => {
    // Arrange
    const detail: IssueDetail = {
      ...mockDetail,
      state: 'revision_queued',
      stateDetails: {
        ...mockStateDetails,
        revisionTriggerKind: 'rebase_conflict',
        rebaseConflictAttempts: null,
      },
    };

    // Act
    const fixture = createComponent(detail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const chip = el.querySelector('.issue-detail__revision-chip') as HTMLElement;
    const message = el.querySelector('.issue-detail__revision-message') as HTMLElement;
    expect(chip?.textContent?.trim()).toBe(`Resolving conflicts (attempt 1 of ${MAX_REBASE_CONFLICT_ATTEMPTS})`);
    expect(message?.textContent?.trim()).toContain('Conflict-resolution run queued');
  });

  // Revision callout — Cycle R5: review_feedback shows "Addressing review" chip
  it('should render "Addressing review" chip when revisionTriggerKind is review_feedback', () => {
    // Arrange
    const detail: IssueDetail = {
      ...mockDetail,
      state: 'revision_in_progress',
      stateDetails: {
        ...mockStateDetails,
        revisionTriggerKind: 'review_feedback',
      },
    };

    // Act
    const fixture = createComponent(detail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const chip = el.querySelector('.issue-detail__revision-chip') as HTMLElement;
    expect(chip).toBeTruthy();
    expect(chip?.textContent?.trim()).toBe('Addressing review');
  });

  // Revision callout — Cycle R6: revision callout not shown when revisionTriggerKind is absent
  it('should not render revision callout content when revisionTriggerKind is absent', () => {
    // Arrange — state is revision_in_progress but no trigger kind
    const detail: IssueDetail = {
      ...mockDetail,
      state: 'revision_in_progress',
      stateDetails: {
        ...mockStateDetails,
        revisionTriggerKind: null,
      },
    };

    // Act
    const fixture = createComponent(detail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert — chip is absent; live region collapses
    const chip = el.querySelector('.issue-detail__revision-chip');
    expect(chip).toBeFalsy();
  });

  // Revision callout — Cycle R7: revision callout not shown when state is not a revision state
  it('should not render revision callout when state is in_progress even if revisionTriggerKind is set', () => {
    // Arrange — state is in_progress (not a revision state)
    const detail: IssueDetail = {
      ...mockDetail,
      state: 'in_progress',
      stateDetails: {
        ...mockStateDetails,
        revisionTriggerKind: 'failed_checks',
        checksFixAttempts: 1,
      },
    };

    // Act
    const fixture = createComponent(detail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert — chip is absent
    const chip = el.querySelector('.issue-detail__revision-chip');
    expect(chip).toBeFalsy();
  });

  // Revision callout — revision_failed exclusion: failed state has its own failure callout,
  // not the in-progress revision chip.
  it('should NOT render revision chip when state is revision_failed with revisionTriggerKind failed_checks', () => {
    // Arrange — revision_failed is intentionally excluded from REVISION_STATES;
    // it surfaces via the retryable failure path, not the in-progress revision callout.
    const detail: IssueDetail = {
      ...mockDetail,
      state: 'revision_failed',
      stateDetails: {
        ...mockStateDetails,
        revisionTriggerKind: 'failed_checks',
        checksFixAttempts: 1,
      },
    };

    // Act
    const fixture = createComponent(detail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert — chip is absent; revision_failed renders no revision callout
    const chip = el.querySelector('.issue-detail__revision-chip');
    expect(chip).toBeFalsy();
  });

  // Revision callout — Cycle R8: live region is permanently mounted with role="status"
  it('should permanently mount the revision live region with role="status" and aria-live="polite"', () => {
    // Arrange — state is not a revision state (empty live region)
    const fixture = createComponent(mockDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert — region present even when empty
    const liveRegion = el.querySelector('.issue-detail__revision-live-region');
    expect(liveRegion).toBeTruthy();
    expect(liveRegion?.getAttribute('role')).toBe('status');
    expect(liveRegion?.getAttribute('aria-live')).toBe('polite');
    expect(liveRegion?.getAttribute('aria-atomic')).toBe('true');
  });

  // Revision callout — Cycle R9: failed_checks in_progress shows fixing message
  it('should show fixing message for failed_checks when state is revision_in_progress', () => {
    // Arrange
    const detail: IssueDetail = {
      ...mockDetail,
      state: 'revision_in_progress',
      stateDetails: {
        ...mockStateDetails,
        revisionTriggerKind: 'failed_checks',
        checksFixAttempts: 1,
      },
    };

    // Act
    const fixture = createComponent(detail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const message = el.querySelector('.issue-detail__revision-message') as HTMLElement;
    expect(message?.textContent?.trim()).toContain('Fixing checks — a worker is running');
  });

  // Revision callout — Cycle R10: review_feedback in_progress shows reviewer feedback message
  it('should show addressing feedback message for review_feedback when state is revision_in_progress', () => {
    // Arrange
    const detail: IssueDetail = {
      ...mockDetail,
      state: 'revision_in_progress',
      stateDetails: {
        ...mockStateDetails,
        revisionTriggerKind: 'review_feedback',
      },
    };

    // Act
    const fixture = createComponent(detail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const message = el.querySelector('.issue-detail__revision-message') as HTMLElement;
    expect(message?.textContent?.trim()).toContain('addressing reviewer feedback');
  });
});

describe('IssueDetailComponent — cancel run', () => {
  function runDetail(overrides: Record<string, unknown> = {}) {
    return {
      workerRunId: 'run-cancel',
      role: 'implement',
      issueId: 'abc123',
      state: 'running',
      failureCategory: null,
      failureSummary: null,
      resultText: null,
      subtype: null,
      isError: null,
      durationMs: null,
      numTurns: null,
      totalCostUsd: null,
      inputTokens: null,
      outputTokens: null,
      lastActivityAt: null,
      hasStoredLog: false,
      cancelRequestedAt: null,
      publish: null,
      publishRetry: null,
      discardsAt: null,
      secretFindings: [],
      revertedFiles: [],
      ...overrides,
    };
  }

  function issueInState(state: string): IssueDetail {
    return {
      ...mockDetail,
      state: state as IssueDetail['state'],
      stateDetails: { ...mockStateDetails, workerRunId: 'run-cancel', completedAt: null },
    };
  }

  function setup(
    detail: IssueDetail,
    run: ReturnType<typeof runDetail> | null,
    requestCancelResult: string | null = null,
  ) {
    const requestCancel = vi.fn(() => of(requestCancelResult));
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [IssueDetailComponent],
      providers: [
        { provide: IssueService, useValue: { retrying: signal(false), retryError: signal(null), retrySuccess: signal(null), ...triageSignals() } },
        WorkerSignalRService,
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: IssueSignalRService, useValue: mockIssueSignalRService },
        {
          provide: WorkerRunService,
          useValue: { getDetail: () => of(run), getLog: () => of(null), requestCancel },
        },
        { provide: WORKER_HUB_FACTORY, useValue: () => mockWorkerHub },
      ],
    });
    const fixture = TestBed.createComponent(IssueDetailComponent);
    fixture.componentRef.setInput('detail', detail);
    fixture.componentRef.setInput('loading', false);
    fixture.componentRef.setInput('error', null);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const cancelButton = () => el.querySelector('.issue-detail__cancel-btn') as HTMLButtonElement | null;
    const dialog = () => el.querySelector('dialog.confirm-dialog') as HTMLDialogElement | null;
    return { fixture, el, requestCancel, cancelButton, dialog };
  }

  it.each(['in_progress', 'revision_in_progress'])('should show an enabled Cancel run button when the issue is %s', (state) => {
    // Arrange / Act
    const { cancelButton } = setup(issueInState(state), runDetail());

    // Assert
    expect(cancelButton()).not.toBeNull();
    expect(cancelButton()?.disabled).toBe(false);
    expect(cancelButton()?.textContent?.trim()).toBe('Cancel run');
  });

  it.each(['queued', 'failed', 'review', 'completed'])('should not show the Cancel run button when the issue is %s', (state) => {
    // Arrange / Act
    const { cancelButton } = setup(issueInState(state), runDetail());

    // Assert
    expect(cancelButton()).toBeNull();
  });

  it('should disable the button with a Starting… hint while the run is starting', () => {
    // Arrange / Act
    const { el, cancelButton } = setup(issueInState('in_progress'), runDetail({ state: 'starting' }));

    // Assert
    expect(cancelButton()?.disabled).toBe(true);
    expect(el.querySelector('.issue-detail__cancel-hint')?.textContent?.trim()).toBe('Starting…');
  });

  it('should open the confirm dialog instead of cancelling immediately', () => {
    // Arrange
    const { fixture, requestCancel, cancelButton, dialog } = setup(issueInState('in_progress'), runDetail());

    // Act
    cancelButton()!.click();
    fixture.detectChanges();

    // Assert
    expect(dialog()?.open).toBe(true);
    expect(dialog()?.textContent).toContain('Stop the worker?');
    expect(dialog()?.textContent).toContain("Foundry publishes the run's commits to the branch; Retry continues from them.");
    expect(requestCancel).not.toHaveBeenCalled();
  });

  it('should request cancellation and show Cancelling… once confirmed', () => {
    // Arrange
    const { fixture, requestCancel, cancelButton, dialog } = setup(issueInState('in_progress'), runDetail());
    cancelButton()!.click();
    fixture.detectChanges();

    // Act
    (dialog()!.querySelector('.confirm-dialog__confirm') as HTMLButtonElement).click();
    fixture.detectChanges();

    // Assert
    expect(requestCancel).toHaveBeenCalledWith('run-cancel');
    expect(cancelButton()?.textContent?.trim()).toBe('Cancelling…');
    expect(cancelButton()?.disabled).toBe(true);
    expect(dialog()?.open).toBe(false);
  });

  it('should not request cancellation when the dialog is dismissed', () => {
    // Arrange
    const { fixture, requestCancel, cancelButton, dialog } = setup(issueInState('in_progress'), runDetail());
    cancelButton()!.click();
    fixture.detectChanges();

    // Act
    (dialog()!.querySelector('.confirm-dialog__cancel') as HTMLButtonElement).click();
    fixture.detectChanges();

    // Assert
    expect(requestCancel).not.toHaveBeenCalled();
    expect(cancelButton()?.textContent?.trim()).toBe('Cancel run');
  });

  it('should show Cancelling… on load when the run already carries a cancel request', () => {
    // Arrange / Act
    const { cancelButton } = setup(
      issueInState('in_progress'),
      runDetail({ cancelRequestedAt: '2026-11-04T10:00:00Z' }),
    );

    // Assert
    expect(cancelButton()?.textContent?.trim()).toBe('Cancelling…');
    expect(cancelButton()?.disabled).toBe(true);
  });

  it('should restore the button and show the error when the cancel request fails', () => {
    // Arrange
    const { fixture, el, cancelButton, dialog } = setup(
      issueInState('in_progress'),
      runDetail(),
      'The run is no longer running.',
    );
    cancelButton()!.click();
    fixture.detectChanges();

    // Act
    (dialog()!.querySelector('.confirm-dialog__confirm') as HTMLButtonElement).click();
    fixture.detectChanges();

    // Assert
    expect(cancelButton()?.textContent?.trim()).toBe('Cancel run');
    expect(el.querySelector('.issue-detail__actions')?.textContent).toContain('The run is no longer running.');
  });

  it('should explain that the run finished before the cancel took effect', () => {
    // Arrange / Act
    const { el } = setup(
      issueInState('review'),
      runDetail({ state: 'completed', cancelRequestedAt: '2026-11-04T10:00:00Z' }),
    );

    // Assert
    expect(el.querySelector('.issue-detail__cancel-note')?.textContent).toContain(
      'The run finished before the cancel took effect.',
    );
  });

  it('should not show the finished-before-cancel note for a completed run without a cancel request', () => {
    // Arrange / Act
    const { el } = setup(issueInState('review'), runDetail({ state: 'completed' }));

    // Assert
    expect(el.querySelector('.issue-detail__cancel-note')).toBeNull();
  });
});

describe('IssueDetailComponent — retry fresh', () => {
  function setup(state: string) {
    const retryIssueFresh = vi.fn();
    const retryIssue = vi.fn();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [IssueDetailComponent],
      providers: [
        {
          provide: IssueService,
          useValue: {
            retrying: signal(false),
            retryError: signal(null),
            retrySuccess: signal(null),
            ...triageSignals(),
            retryIssue,
            retryIssueFresh,
          },
        },
        WorkerSignalRService,
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: IssueSignalRService, useValue: mockIssueSignalRService },
        {
          provide: WorkerRunService,
          useValue: { getDetail: () => of(null), getLog: () => of(null), requestCancel: vi.fn() },
        },
        { provide: WORKER_HUB_FACTORY, useValue: () => mockWorkerHub },
      ],
    });
    const fixture = TestBed.createComponent(IssueDetailComponent);
    fixture.componentRef.setInput('detail', {
      ...mockDetail,
      state: state as IssueDetail['state'],
      stateDetails: null,
    });
    fixture.componentRef.setInput('loading', false);
    fixture.componentRef.setInput('error', null);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const freshButton = () => el.querySelector('.issue-detail__retry-fresh-btn') as HTMLButtonElement | null;
    const dialog = () => el.querySelector('dialog.confirm-dialog') as HTMLDialogElement | null;
    return { fixture, el, retryIssue, retryIssueFresh, freshButton, dialog };
  }

  it('should show Retry fresh alongside Retry Issue for a continuable failure', () => {
    // Arrange / Act
    const { el, freshButton } = setup('continuable_failed');

    // Assert
    expect(el.querySelector('.issue-detail__retry-btn')).not.toBeNull();
    expect(freshButton()?.textContent?.trim()).toBe('Retry fresh');
  });

  it.each(['failed', 'revision_failed', 'unchanged', 'completed'])(
    'should not show Retry fresh when the issue is %s',
    (state) => {
      // Arrange / Act
      const { freshButton } = setup(state);

      // Assert
      expect(freshButton()).toBeNull();
    },
  );

  it('should open a confirmation stating the branch work is discarded instead of retrying immediately', () => {
    // Arrange
    const { fixture, retryIssueFresh, freshButton, dialog } = setup('continuable_failed');

    // Act
    freshButton()!.click();
    fixture.detectChanges();

    // Assert
    expect(dialog()?.open).toBe(true);
    expect(dialog()?.textContent).toContain('Retry from a fresh branch?');
    expect(dialog()?.textContent).toContain('The branch and all of its commits will be deleted');
    expect(retryIssueFresh).not.toHaveBeenCalled();
  });

  it('should retry fresh once confirmed', () => {
    // Arrange
    const { fixture, retryIssue, retryIssueFresh, freshButton, dialog } = setup('continuable_failed');
    freshButton()!.click();
    fixture.detectChanges();

    // Act
    (dialog()!.querySelector('.confirm-dialog__confirm') as HTMLButtonElement).click();
    fixture.detectChanges();

    // Assert
    expect(retryIssueFresh).toHaveBeenCalledWith(mockDetail.id);
    expect(retryIssue).not.toHaveBeenCalled();
    expect(dialog()?.open).toBe(false);
  });

  it('should not retry when the confirmation is dismissed', () => {
    // Arrange
    const { fixture, retryIssueFresh, freshButton, dialog } = setup('continuable_failed');
    freshButton()!.click();
    fixture.detectChanges();

    // Act
    (dialog()!.querySelector('.confirm-dialog__cancel') as HTMLButtonElement).click();
    fixture.detectChanges();

    // Assert
    expect(retryIssueFresh).not.toHaveBeenCalled();
    expect(dialog()?.open).toBe(false);
  });
});

describe('IssueDetailComponent — run role tag', () => {
  function runDetail(overrides: Record<string, unknown> = {}) {
    return {
      workerRunId: 'run-role',
      issueId: 'abc123',
      role: 'triage',
      state: 'failed',
      failureCategory: 'timeout',
      failureSummary: 'The run exceeded the 60 minute timeout.',
      resultText: 'Partial work done.',
      subtype: null,
      isError: null,
      durationMs: null,
      numTurns: null,
      totalCostUsd: null,
      inputTokens: null,
      outputTokens: null,
      lastActivityAt: null,
      hasStoredLog: false,
      cancelRequestedAt: null,
      publish: null,
      publishRetry: null,
      discardsAt: null,
      secretFindings: [],
      revertedFiles: [],
      ...overrides,
    };
  }

  function setup(run: ReturnType<typeof runDetail> | null) {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [IssueDetailComponent],
      providers: [
        { provide: IssueService, useValue: { retrying: signal(false), retryError: signal(null), retrySuccess: signal(null), ...triageSignals() } },
        WorkerSignalRService,
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: IssueSignalRService, useValue: mockIssueSignalRService },
        { provide: WorkerRunService, useValue: { getDetail: () => of(run), getLog: () => of(null) } },
        { provide: WORKER_HUB_FACTORY, useValue: () => mockWorkerHub },
      ],
    });
    const fixture = TestBed.createComponent(IssueDetailComponent);
    fixture.componentRef.setInput('detail', {
      ...mockDetail,
      state: 'failed',
      stateDetails: { ...mockStateDetails, workerRunId: 'run-role' },
    });
    fixture.componentRef.setInput('loading', false);
    fixture.componentRef.setInput('error', null);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  afterEach(() => TestBed.resetTestingModule());

  it('should show a Run header with the run role tag', () => {
    // Act
    const el = setup(runDetail());

    // Assert
    const header = el.querySelector('.issue-detail__worker-run .issue-detail__run-header');
    expect(header?.querySelector('.issue-detail__field-key')?.textContent?.trim()).toBe('Run');
    expect(header?.querySelector('fd-role-tag')?.textContent).toBe('Role: triage');
  });

  it('should place the Run header above the failure category and result', () => {
    // Act
    const el = setup(runDetail());

    // Assert
    const block = el.querySelector('.issue-detail__worker-run');
    expect(block?.firstElementChild?.classList.contains('issue-detail__run-header')).toBe(true);
    expect(block?.querySelector('.issue-detail__failure-category')).not.toBeNull();
    expect(block?.querySelector('.issue-detail__result-text')).not.toBeNull();
  });

  it('should show no role tag when the run detail is unavailable', () => {
    // Act
    const el = setup(null);

    // Assert
    expect(el.querySelector('.issue-detail__run-header')).toBeNull();
    expect(el.querySelector('fd-role-tag')).toBeNull();
  });
});

describe('IssueDetailComponent — triage (#772)', () => {
  const noKind: IssueDetail['kind'] = { source: 'unclassified', kind: null, conflictingTriageKind: null };

  function verdict(overrides: Partial<NonNullable<IssueDetail['triage']>> = {}): NonNullable<IssueDetail['triage']> {
    return {
      outcome: 'ready',
      kind: 'feature',
      questions: [],
      decidedAt: '2026-11-06T14:14:03Z',
      ...overrides,
    };
  }

  function issue(state: string, overrides: Partial<IssueDetail> = {}): IssueDetail {
    return {
      ...mockDetail,
      state: state as IssueDetail['state'],
      stateDetails: null,
      kind: noKind,
      triage: null,
      ...overrides,
    };
  }

  function failedDetails(overrides: Partial<IssueStateDetails> = {}): IssueStateDetails {
    return {
      ...mockStateDetails,
      branchName: null,
      completedAt: null,
      workerRunId: 'run-triage',
      failureReason: 'Verdict missing from output',
      failedAt: '2026-11-06T14:20:11Z',
      ...overrides,
    };
  }

  function setup(detail: IssueDetail) {
    const signals = triageSignals();
    const service = {
      retrying: signal(false),
      retryError: signal(null),
      retrySuccess: signal(null),
      ...signals,
      retryIssue: vi.fn(),
      retryIssueFresh: vi.fn(),
      retryTriage: vi.fn(),
      skipTriage: vi.fn(),
      retriage: vi.fn(),
    };
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [IssueDetailComponent],
      providers: [
        { provide: IssueService, useValue: service },
        WorkerSignalRService,
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: IssueSignalRService, useValue: mockIssueSignalRService },
        {
          provide: WorkerRunService,
          useValue: { getDetail: () => of(null), getLog: () => of(null), requestCancel: vi.fn() },
        },
        { provide: WORKER_HUB_FACTORY, useValue: () => mockWorkerHub },
      ],
    });
    const fixture = TestBed.createComponent(IssueDetailComponent);
    fixture.componentRef.setInput('detail', detail);
    fixture.componentRef.setInput('loading', false);
    fixture.componentRef.setInput('error', null);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const region = () => el.querySelector('.issue-detail__triage-live-region') as HTMLElement;
    const kindField = () => el.querySelector('.issue-detail__field--kind') as HTMLElement | null;
    const button = (label: string) =>
      Array.from(el.querySelectorAll<HTMLButtonElement>('.issue-detail__actions button')).find(
        (b) => b.getAttribute('aria-label')?.startsWith(label),
      ) ?? null;
    const dialogs = () => Array.from(el.querySelectorAll<HTMLDialogElement>('dialog.confirm-dialog'));
    const openDialog = () => dialogs().find((d) => d.open) ?? null;
    return { fixture, el, service, region, kindField, button, dialogs, openDialog };
  }

  function text(node: Element | null): string {
    return (node?.textContent ?? '').replace(/\s+/g, ' ').trim();
  }

  // --- 2a. Kind field ---

  it('should render the Kind field first in the fields grid', () => {
    // Arrange / Act
    const { el } = setup(
      issue('blocked', {
        kind: { source: 'labelled', kind: 'bug', conflictingTriageKind: null },
        stateDetails: { ...mockStateDetails, branchName: null, completedAt: null, blockedBy: [{ issueNumber: 12, reason: null }, { issueNumber: 14, reason: 'waits on the schema' }] },
      }),
    );

    // Assert
    const firstField = el.querySelector('.issue-detail__fields > .issue-detail__field');
    expect(firstField?.classList.contains('issue-detail__field--kind')).toBe(true);
    expect(text(firstField?.querySelector('.issue-detail__field-key') ?? null)).toBe('Kind');
  });

  it('should render the Kind field even when there are no state details', () => {
    // Arrange / Act
    const { kindField } = setup(issue('queued', { kind: { source: 'triaged', kind: 'feature', conflictingTriageKind: null } }));

    // Assert
    expect(kindField()).not.toBeNull();
  });

  it.each([
    ['labelled', 'bug', 'Bug', 'From label'],
    ['triaged', 'feature', 'Feature', 'From triage'],
    ['triaged', 'refactor', 'Refactor', 'From triage'],
    ['labelled', 'documentation', 'Documentation', 'From label'],
    ['unclassified', null, 'Unclassified', 'No kind label, and triage has not classified it'],
  ] as const)('should show a %s %s kind as "%s" with the note "%s"', (source, kind, label, note) => {
    // Arrange / Act
    const { kindField } = setup(issue('queued', { kind: { source, kind, conflictingTriageKind: null } }));

    // Assert
    expect(text(kindField()!.querySelector('.issue-detail__kind-value'))).toBe(label);
    expect(text(kindField()!.querySelector('.issue-detail__kind-source'))).toBe(note);
  });

  it('should mark the Unclassified value as secondary text', () => {
    // Arrange / Act
    const { kindField } = setup(issue('queued'));

    // Assert
    expect(kindField()!.querySelector('.issue-detail__kind-value--unclassified')).not.toBeNull();
  });

  it('should show an unknown kind value as is', () => {
    // Arrange / Act
    const { kindField } = setup(
      issue('queued', {
        kind: { source: 'labelled', kind: 'chore' as IssueDetail['kind']['kind'], conflictingTriageKind: null },
      }),
    );

    // Assert
    expect(text(kindField()!.querySelector('.issue-detail__kind-value'))).toBe('chore');
  });

  it('should show the Triage differs line only when conflictingTriageKind is set', () => {
    // Arrange / Act
    const plain = setup(issue('queued', { kind: { source: 'labelled', kind: 'bug', conflictingTriageKind: null } }));
    const plainDiffers = plain.el.querySelector('.issue-detail__kind-differs');
    const differs = setup(issue('queued', { kind: { source: 'labelled', kind: 'bug', conflictingTriageKind: 'refactor' } }));
    const line = differs.el.querySelector('.issue-detail__kind-differs');

    // Assert
    expect(plainDiffers).toBeNull();
    expect(text(line?.querySelector('.issue-detail__chip') ?? null)).toBe('Triage differs');
    expect(text(line)).toContain('Triage read it as a refactor. The label decides; nothing changes.');
  });

  // --- 2b. Triage callout ---

  it('should keep the triage callout permanently mounted as a polite, focusable live region', () => {
    // Arrange / Act
    const { region } = setup(issue('completed'));

    // Assert
    expect(region()).not.toBeNull();
    expect(region().getAttribute('role')).toBe('status');
    expect(region().getAttribute('aria-live')).toBe('polite');
    expect(region().getAttribute('aria-atomic')).toBe('true');
    expect(region().getAttribute('tabindex')).toBe('-1');
    expect(region().classList.contains('issue-detail__triage-live-region--empty')).toBe(true);
    expect(text(region())).toBe('');
  });

  it.each([
    ['detected', 'Waiting for a worker to triage this issue.', 'neutral'],
    ['triage_queued', 'Waiting for a worker to triage this issue.', 'neutral'],
    ['triage_blocked', 'Waiting for its blockers to close. Triage runs once they do.', 'neutral'],
    ['triage_in_progress', 'A triage worker is reading the issue and the repository.', 'neutral'],
    [
      'triage_failed',
      'Triage did not finish. Retry triage to run it again, or skip triage to queue the issue without a readiness check.',
      'failed',
    ],
    ['queued', 'Not triaged. This issue was queued without a readiness check.', 'neutral'],
    ['blocked', 'Not triaged. This issue was queued without a readiness check.', 'neutral'],
  ])('should show the %s callout', (state, message, tint) => {
    // Arrange / Act
    const { region } = setup(issue(state));

    // Assert
    expect(text(region().querySelector('.issue-detail__field-key'))).toBe('Triage');
    expect(text(region())).toContain(message);
    expect(region().classList.contains(`issue-detail__triage-state--${tint}`)).toBe(true);
    expect(region().classList.contains('issue-detail__triage-live-region--empty')).toBe(false);
  });

  it('should render each blocker as its own list item with its number', () => {
    // Arrange / Act
    const { el } = setup(
      issue('triage_blocked', {
        stateDetails: { ...mockStateDetails, branchName: null, completedAt: null, blockedBy: [{ issueNumber: 12, reason: null }, { issueNumber: 14, reason: null }] },
      }),
    );

    // Assert
    const numbers = Array.from(el.querySelectorAll('ul.issue-detail__blockers > li')).map((li) =>
      text(li.querySelector('.issue-detail__blocker-number')),
    );
    expect(numbers).toEqual(['#12', '#14']);
  });

  it('should show the triage reason only on the blockers that have one', () => {
    // Arrange / Act
    const { el } = setup(
      issue('triage_blocked', {
        stateDetails: { ...mockStateDetails, branchName: null, completedAt: null, blockedBy: [{ issueNumber: 12, reason: null }, { issueNumber: 14, reason: 'waits on the schema' }] },
      }),
    );

    // Assert
    const reasons = Array.from(el.querySelectorAll('ul.issue-detail__blockers > li')).map((li) =>
      li.querySelector('.issue-detail__blocker-reason') === null ? null : text(li.querySelector('.issue-detail__blocker-reason')),
    );
    expect(reasons).toEqual([null, 'Triage: waits on the schema']);
  });

  it('should show no triage reason for a blocker whose reason is empty', () => {
    // Arrange / Act
    const { el } = setup(
      issue('triage_blocked', {
        stateDetails: { ...mockStateDetails, branchName: null, completedAt: null, blockedBy: [{ issueNumber: 12, reason: '' }] },
      }),
    );

    // Assert
    expect(el.querySelector('.issue-detail__blocker-reason')).toBeNull();
  });

  it('should expose the blockers as a list', () => {
    // Arrange / Act
    const { el } = setup(
      issue('triage_blocked', {
        stateDetails: { ...mockStateDetails, branchName: null, completedAt: null, blockedBy: [{ issueNumber: 12, reason: null }] },
      }),
    );

    // Assert
    expect(el.querySelector('ul.issue-detail__blockers')?.getAttribute('role')).toBe('list');
  });

  it('should name the blockers list by the visible Blocked By key', () => {
    // Arrange / Act
    const { el } = setup(
      issue('triage_blocked', {
        stateDetails: { ...mockStateDetails, branchName: null, completedAt: null, blockedBy: [{ issueNumber: 12, reason: null }] },
      }),
    );

    // Assert
    const labelId = el.querySelector('ul.issue-detail__blockers')?.getAttribute('aria-labelledby');
    expect(text(labelId ? el.querySelector(`[id="${labelId}"]`) : null)).toBe('Blocked By');
  });

  it('should give each component instance its own blockers label id', () => {
    // Arrange / Act
    const blocked = issue('triage_blocked', {
      stateDetails: { ...mockStateDetails, branchName: null, completedAt: null, blockedBy: [{ issueNumber: 12, reason: null }] },
    });
    const idOf = (el: HTMLElement): string | null | undefined =>
      el.querySelector('ul.issue-detail__blockers')?.getAttribute('aria-labelledby');
    const firstId = idOf(setup(blocked).el);
    const secondId = idOf(setup(blocked).el);

    // Assert
    expect([firstId, secondId].every((id) => id != null && id !== '')).toBe(true);
    expect(firstId).not.toBe(secondId);
  });

  it('should span the Blocked By field across the full width', () => {
    // Arrange / Act
    const { el } = setup(
      issue('triage_blocked', {
        stateDetails: { ...mockStateDetails, branchName: null, completedAt: null, blockedBy: [{ issueNumber: 12, reason: null }] },
      }),
    );

    // Assert
    const field = el.querySelector('ul.issue-detail__blockers')?.closest('.issue-detail__field');
    expect(field?.classList.contains('issue-detail__field--full-width')).toBe(true);
  });

  it.each([
    ['null', null],
    ['empty', []],
  ])('should hide the Blocked By field when blockedBy is %s', (_label, blockedBy) => {
    // Arrange / Act
    const { el } = setup(
      issue('triage_blocked', {
        stateDetails: { ...mockStateDetails, branchName: null, completedAt: null, blockedBy },
      }),
    );

    // Assert
    expect(el.querySelector('ul.issue-detail__blockers')).toBeNull();
  });

  it('should offer no operator actions for a triage_blocked issue', () => {
    // Arrange / Act
    const { el } = setup(
      issue('triage_blocked', {
        stateDetails: { ...mockStateDetails, branchName: null, completedAt: null, blockedBy: [{ issueNumber: 12, reason: null }, { issueNumber: 14, reason: 'waits on the schema' }] },
      }),
    );

    // Assert
    expect(el.querySelector('.issue-detail__actions')).toBeNull();
  });

  it('should show the triage_blocked callout ahead of a recorded verdict', () => {
    // Arrange / Act
    const { region } = setup(issue('triage_blocked', { triage: verdict() }));

    // Assert
    expect(text(region())).toContain('Waiting for its blockers to close. Triage runs once they do.');
  });

  it('should lead the triage_failed callout with a bold "Triage did not finish."', () => {
    // Arrange / Act
    const { region } = setup(issue('triage_failed', { stateDetails: failedDetails() }));

    // Assert
    expect(text(region().querySelector('strong'))).toBe('Triage did not finish.');
  });

  it('should show the needs-more-detail callout with a warning tint for awaiting_input with a retriage resume target', () => {
    // Arrange / Act
    const { region } = setup(
      issue('awaiting_input', {
        triage: verdict({ outcome: 'needs_input', questions: ['Which API?'] }),
        stateDetails: {
          ...mockStateDetails,
          branchName: null,
          completedAt: null,
          awaitingInput: {
            questions: [{ question: 'Which API?', context: '' }],
            postStatus: 'posted',
            postedAt: '2026-11-06T14:15:00Z',
            commentUrl: null,
            postFailureReason: null,
            nextPostAttemptAt: null,
            resumeTarget: 'retriage',
          },
        },
      }),
    );

    // Assert
    expect(text(region())).toContain(
      'Triage needs more detail. The questions are posted on the issue; once a reply settles, Foundry triages again.',
    );
    expect(region().classList.contains('issue-detail__triage-state--warning')).toBe(true);
  });

  it('should head the awaiting-input questions "Questions from triage" for a retriage resume target', () => {
    // Arrange / Act
    const awaiting = (resumeTarget: 'retriage' | 'revision') =>
      setup(
        issue('awaiting_input', {
          stateDetails: {
            ...mockStateDetails,
            branchName: null,
            completedAt: null,
            awaitingInput: {
              questions: [{ question: 'Which API?', context: '' }],
              postStatus: 'pending',
              postedAt: null,
              commentUrl: null,
              postFailureReason: null,
              nextPostAttemptAt: null,
              resumeTarget,
            },
          },
        }),
      ).el.querySelector('#awaiting-input-heading');
    const triageHeading = text(awaiting('retriage'));
    const revisionHeading = text(awaiting('revision'));

    // Assert
    expect(triageHeading).toBe('Questions from triage');
    expect(revisionHeading).toBe('Open Questions');
  });

  it('should leave the callout empty for awaiting_input with a non-triage resume target and no verdict', () => {
    // Arrange / Act
    const { region } = setup(
      issue('awaiting_input', {
        stateDetails: {
          ...mockStateDetails,
          branchName: null,
          completedAt: null,
          awaitingInput: {
            questions: [],
            postStatus: 'pending',
            postedAt: null,
            commentUrl: null,
            postFailureReason: null,
            nextPostAttemptAt: null,
            resumeTarget: 'continuation',
          },
        },
      }),
    );

    // Assert
    expect(text(region())).toBe('');
    expect(region().classList.contains('issue-detail__triage-live-region--empty')).toBe(true);
  });

  it('should show a READY verdict with its kind and decision time', () => {
    // Arrange / Act
    const { region } = setup(issue('queued', { triage: verdict() }));

    // Assert
    const chip = region().querySelector('.issue-detail__chip');
    expect(text(chip)).toBe('Ready');
    expect(chip?.classList.contains('issue-detail__chip--success')).toBe(true);
    expect(text(region())).toContain('Feature');
    expect(text(region())).toMatch(/Triaged Nov 6, 2026/);
    expect(text(region())).not.toContain('Not triaged');
    expect(text(region())).not.toContain('question');
    expect(region().classList.contains('issue-detail__triage-state--neutral')).toBe(true);
  });

  it.each([
    [1, '1 question asked'],
    [3, '3 questions asked'],
  ])('should show a NEEDS INPUT verdict with %i question(s) asked', (count, phrase) => {
    // Arrange / Act
    const { region } = setup(
      issue('in_progress', {
        triage: verdict({ outcome: 'needs_input', kind: 'bug', questions: Array.from({ length: count }, (_, i) => `Q${i}`) }),
        stateDetails: { ...mockStateDetails, completedAt: null, workerRunId: 'run-1' },
      }),
    );

    // Assert
    const chip = region().querySelector('.issue-detail__chip');
    expect(text(chip)).toBe('Needs input');
    expect(chip?.classList.contains('issue-detail__chip--warning')).toBe(true);
    expect(text(region())).toContain('Bug');
    expect(text(region())).toContain(phrase);
  });

  it('should label a BLOCKED verdict chip "Blocked"', () => {
    // Arrange / Act
    const { region } = setup(issue('queued', { triage: verdict({ outcome: 'blocked' }) }));

    // Assert
    expect(text(region().querySelector('.issue-detail__chip'))).toBe('Blocked');
  });

  it('should tint a BLOCKED verdict chip neutral', () => {
    // Arrange / Act
    const { region } = setup(issue('queued', { triage: verdict({ outcome: 'blocked' }) }));

    // Assert
    expect(region().querySelector('.issue-detail__chip')?.classList.contains('issue-detail__chip--neutral')).toBe(true);
  });

  it('should show the kind beside a BLOCKED verdict chip', () => {
    // Arrange / Act
    const { region } = setup(issue('queued', { triage: verdict({ outcome: 'blocked', kind: 'refactor' }) }));

    // Assert
    expect(text(region())).toContain('Refactor');
  });

  it('should show when a BLOCKED verdict was decided', () => {
    // Arrange / Act
    const { region } = setup(issue('queued', { triage: verdict({ outcome: 'blocked', decidedAt: '2026-11-06T14:14:03Z' }) }));

    // Assert
    expect(text(region())).toMatch(/Triaged Nov 6, 2026/);
  });

  it('should fall back to the raw outcome for a verdict outcome it has no label for', () => {
    // Arrange / Act
    const { region } = setup(issue('queued', { triage: verdict({ outcome: 'future_outcome' as 'ready' }) }));

    // Assert
    expect(text(region().querySelector('.issue-detail__chip'))).toBe('future_outcome');
  });

  it('should explain a BLOCKED verdict as depending on open issues', () => {
    // Arrange / Act
    const { region } = setup(issue('queued', { triage: verdict({ outcome: 'blocked' }) }));

    // Assert
    expect(text(region())).toContain('Triage found open issues this depends on.');
  });

  it.each(['ready', 'needs_input'] as const)('should not explain open issues for a %s verdict', (outcome) => {
    // Arrange / Act
    const { region } = setup(issue('queued', { triage: verdict({ outcome }) }));

    // Assert
    expect(text(region())).not.toContain('Triage found open issues');
    expect(region().querySelector('.issue-detail__chip--neutral')).toBeNull();
  });

  it('should keep the waiting-on-blockers callout over a stored BLOCKED verdict while triage_blocked', () => {
    // Arrange / Act
    const { region } = setup(issue('triage_blocked', { triage: verdict({ outcome: 'blocked' }) }));

    // Assert
    expect(text(region())).toContain('Waiting for its blockers to close. Triage runs once they do.');
    expect(region().querySelector('.issue-detail__chip')).toBeNull();
  });

  it.each(['triage_queued', 'triage_in_progress', 'triage_failed'])(
    'should hide a stale verdict while the issue is %s',
    (state) => {
      // Arrange / Act
      const { region } = setup(issue(state, { triage: verdict() }));

      // Assert
      expect(region().querySelector('.issue-detail__chip')).toBeNull();
      expect(text(region())).not.toContain('Triaged');
    },
  );

  it.each(['completed', 'review', 'failed'])('should leave the callout empty for %s with no verdict', (state) => {
    // Arrange / Act
    const { region } = setup(issue(state));

    // Assert
    expect(text(region())).toBe('');
  });

  // --- 2c. triage_failed detail ---

  it('should show the failure reason and date for triage_failed, and no branch', () => {
    // Arrange / Act
    const { el } = setup(issue('triage_failed', { stateDetails: failedDetails() }));

    // Assert
    const keys = Array.from(el.querySelectorAll('.issue-detail__field-key')).map((k) => text(k));
    expect(keys).toContain('Failure Reason');
    expect(keys).toContain('Failed At');
    expect(keys).not.toContain('Branch');
    expect(text(el)).toContain('Verdict missing from output');
  });

  it('should tell the user to use Retry triage once automatic retries are exhausted', () => {
    // Arrange / Act
    const { el } = setup(
      issue('triage_failed', {
        stateDetails: failedDetails({
          transientRetry: { attemptNumber: 3, maxAttempts: 3, isExhausted: true, nextAttemptDueAt: null },
        }),
      }),
    );

    // Assert
    const retry = text(el.querySelector('.issue-detail__retry-live-region'));
    expect(retry).toContain('Use Retry triage to run it again.');
    expect(retry).not.toContain('Retry Issue');
  });

  it('should keep the Retry Issue hint for a plain failed issue', () => {
    // Arrange / Act
    const { el } = setup(
      issue('failed', {
        stateDetails: failedDetails({
          transientRetry: { attemptNumber: 3, maxAttempts: 3, isExhausted: true, nextAttemptDueAt: null },
        }),
      }),
    );

    // Assert
    expect(text(el.querySelector('.issue-detail__retry-live-region'))).toContain('Use Retry Issue to try again manually.');
  });

  // --- 3. Actions ---

  it('should show Retry triage and Skip triage for triage_failed, and never Retry Issue', () => {
    // Arrange / Act
    const { el, button } = setup(issue('triage_failed', { stateDetails: failedDetails() }));

    // Assert
    const retry = button('Retry triage');
    const skip = button('Skip triage');
    expect(text(retry)).toBe('Retry triage');
    expect(retry?.getAttribute('aria-label')).toBe('Retry triage for issue #42');
    expect(retry?.classList.contains('issue-detail__retry-btn')).toBe(true);
    expect(text(skip)).toBe('Skip triage');
    expect(skip?.getAttribute('aria-label')).toBe('Skip triage for issue #42');
    expect(skip?.classList.contains('issue-detail__secondary-btn')).toBe(true);
    expect(button('Retry issue')).toBeNull();
    expect(button('Re-triage')).toBeNull();
    expect(Array.from(el.querySelectorAll('button')).some((b) => text(b) === 'Retry Issue')).toBe(false);
  });

  it('should retry triage straight away, with no confirmation', () => {
    // Arrange
    const { button, service, openDialog } = setup(issue('triage_failed', { stateDetails: failedDetails() }));

    // Act
    button('Retry triage')!.click();

    // Assert
    expect(service.retryTriage).toHaveBeenCalledWith('abc123');
    expect(openDialog()).toBeNull();
  });

  it('should confirm Skip triage with the §3 copy before skipping', () => {
    // Arrange
    const { fixture, button, service, openDialog } = setup(issue('triage_failed', { stateDetails: failedDetails() }));

    // Act
    button('Skip triage')!.click();
    fixture.detectChanges();

    // Assert
    const dialog = openDialog();
    expect(service.skipTriage).not.toHaveBeenCalled();
    expect(text(dialog!.querySelector('.confirm-dialog__title'))).toBe('Skip triage for #42?');
    expect(text(dialog!.querySelector('.confirm-dialog__message'))).toBe(
      'The issue is queued without a readiness check. If it has open blockers, it waits as Blocked first. Its kind stays as it is now.',
    );
    expect(text(dialog!.querySelector('.confirm-dialog__confirm'))).toBe('Skip triage');
    expect(text(dialog!.querySelector('.confirm-dialog__cancel'))).toBe('Cancel');
    expect(dialog!.querySelector('.confirm-dialog__confirm--destructive')).toBeNull();

    // Act — confirm
    (dialog!.querySelector('.confirm-dialog__confirm') as HTMLButtonElement).click();
    fixture.detectChanges();

    // Assert
    expect(service.skipTriage).toHaveBeenCalledWith('abc123');
  });

  it('should not skip triage when the dialog is cancelled', () => {
    // Arrange
    const { fixture, button, service, openDialog } = setup(issue('triage_failed', { stateDetails: failedDetails() }));
    button('Skip triage')!.click();
    fixture.detectChanges();

    // Act
    (openDialog()!.querySelector('.confirm-dialog__cancel') as HTMLButtonElement).click();
    fixture.detectChanges();

    // Assert
    expect(service.skipTriage).not.toHaveBeenCalled();
    expect(openDialog()).toBeNull();
  });

  it.each(['queued', 'blocked'])('should show Re-triage for %s', (state) => {
    // Arrange / Act
    const { button } = setup(issue(state));

    // Assert
    const retriage = button('Re-triage');
    expect(text(retriage)).toBe('Re-triage');
    expect(retriage?.getAttribute('aria-label')).toBe('Re-triage issue #42');
    expect(retriage?.classList.contains('issue-detail__retry-btn')).toBe(true);
  });

  it.each(['detected', 'triage_queued', 'triage_in_progress', 'triage_failed', 'failed', 'review', 'completed', 'awaiting_input'])(
    'should not show Re-triage for %s',
    (state) => {
      // Arrange / Act
      const { button } = setup(issue(state));

      // Assert
      expect(button('Re-triage')).toBeNull();
    },
  );

  it('should confirm Re-triage from queued with the §3 copy', () => {
    // Arrange
    const { fixture, button, service, openDialog } = setup(issue('queued'));

    // Act
    button('Re-triage')!.click();
    fixture.detectChanges();

    // Assert
    const dialog = openDialog()!;
    expect(text(dialog.querySelector('.confirm-dialog__title'))).toBe('Triage #42 again?');
    expect(text(dialog.querySelector('.confirm-dialog__message'))).toBe(
      'The issue leaves the queue and a triage worker reads it again. If it needs more detail, the questions are posted on the issue and it waits for a reply.',
    );
    expect(text(dialog.querySelector('.confirm-dialog__confirm'))).toBe('Re-triage');
    expect(text(dialog.querySelector('.confirm-dialog__cancel'))).toBe('Cancel');
    expect(dialog.querySelector('.confirm-dialog__confirm--destructive')).toBeNull();
    expect(service.retriage).not.toHaveBeenCalled();

    // Act — confirm
    (dialog.querySelector('.confirm-dialog__confirm') as HTMLButtonElement).click();

    // Assert
    expect(service.retriage).toHaveBeenCalledWith('abc123');
  });

  it('should tell the Re-triage dialog from blocked that triage waits for open blockers', () => {
    // Arrange
    const { fixture, button, openDialog } = setup(issue('blocked'));

    // Act
    button('Re-triage')!.click();
    fixture.detectChanges();

    // Assert
    expect(text(openDialog()!.querySelector('.confirm-dialog__message'))).toBe(
      'The issue leaves the queue and a triage worker reads it again. If it needs more detail, the questions are posted on the issue and it waits for a reply. While its blockers are open it waits untriaged, and triage runs once they close.',
    );
  });

  it.each([
    ['retry_triage', 'Retrying triage…', 'Skip triage'],
    ['skip_triage', 'Skipping triage…', 'Retry triage'],
  ] as const)('should disable both triage buttons while %s is pending, labelling the one in progress', (action, pending, other) => {
    // Arrange
    const { fixture, el, service } = setup(issue('triage_failed', { stateDetails: failedDetails() }));

    // Act
    service.triageActionPending.set(action);
    fixture.detectChanges();

    // Assert
    const buttons = Array.from(el.querySelectorAll<HTMLButtonElement>('.issue-detail__actions button'));
    expect(buttons.map((b) => text(b))).toEqual(
      action === 'retry_triage' ? [pending, other] : [other, pending],
    );
    expect(buttons.every((b) => b.disabled)).toBe(true);
  });

  it('should show "Re-triaging…" and disable Re-triage while it is pending', () => {
    // Arrange
    const { fixture, button, service } = setup(issue('queued'));

    // Act
    service.triageActionPending.set('retriage');
    fixture.detectChanges();

    // Assert
    expect(text(button('Re-triage'))).toBe('Re-triaging…');
    expect(button('Re-triage')?.disabled).toBe(true);
  });

  it('should show a triage action error in the assertive error span', () => {
    // Arrange
    const { fixture, el, service } = setup(issue('triage_failed', { stateDetails: failedDetails() }));

    // Act
    service.triageActionError.set('This issue is no longer in Triage failed.');
    fixture.detectChanges();

    // Assert
    const error = el.querySelector('.issue-detail__actions .issue-detail__retry-error');
    expect(error?.getAttribute('aria-live')).toBe('assertive');
    expect(error?.getAttribute('aria-atomic')).toBe('true');
    expect(text(error)).toBe('This issue is no longer in Triage failed.');
  });

  it('should announce a triage action success in a polite sr-only span that survives the reload', () => {
    // Arrange
    const { fixture, el, service } = setup(issue('queued'));

    // Act — success, then the detail reloads into the new state (the Re-triage row is removed)
    service.triageActionSuccess.set('Re-triage queued. Issue status is updating.');
    fixture.componentRef.setInput('detail', issue('triage_queued'));
    fixture.detectChanges();

    // Assert
    const announcement = el.querySelector('.issue-detail__triage-success-announcement');
    expect(announcement?.classList.contains('sr-only')).toBe(true);
    expect(announcement?.getAttribute('aria-live')).toBe('polite');
    expect(text(announcement)).toBe('Re-triage queued. Issue status is updating.');
  });

  it('should move focus to the triage callout once the detail reloads after a successful action', async () => {
    // Arrange
    const { fixture, el, service, region } = setup(issue('queued'));
    (el.querySelector('.issue-detail__retry-btn') as HTMLButtonElement).focus();

    // Act — the action succeeds, then the reloaded detail arrives
    service.triageActionSuccess.set('Re-triage queued. Issue status is updating.');
    fixture.detectChanges();
    fixture.componentRef.setInput('detail', issue('triage_queued'));
    fixture.detectChanges();
    await fixture.whenStable();

    // Assert
    expect(document.activeElement).toBe(region());
    expect(el.querySelector('.issue-detail__retry-btn')).toBeNull();
  });

  it('should not move focus to the triage callout on an ordinary detail reload', async () => {
    // Arrange
    const { fixture, region } = setup(issue('queued'));

    // Act
    fixture.componentRef.setInput('detail', issue('triage_queued'));
    fixture.detectChanges();
    await fixture.whenStable();

    // Assert
    expect(document.activeElement).not.toBe(region());
  });

  // --- 2d. Cancel run on a triage run ---

  it('should word the cancel dialog for a triage run', () => {
    // Arrange
    const { fixture, el } = setup(
      issue('triage_in_progress', { stateDetails: { ...mockStateDetails, branchName: null, completedAt: null } }),
    );

    // Act
    fixture.detectChanges();

    // Assert — the dialog is rendered (closed) with the triage copy
    const dialog = el.querySelector('dialog.confirm-dialog')!;
    expect(el.querySelector('.issue-detail__cancel-btn')).not.toBeNull();
    expect(text(dialog.querySelector('.confirm-dialog__title'))).toBe('Stop the triage worker?');
    expect(text(dialog.querySelector('.confirm-dialog__message'))).toBe(
      'The issue moves to Triage failed, where you can retry or skip triage.',
    );
    expect(text(dialog.querySelector('.confirm-dialog__confirm'))).toBe('Stop worker');
    expect(text(dialog.querySelector('.confirm-dialog__cancel'))).toBe('Keep running');
    expect(dialog.querySelector('.confirm-dialog__confirm--destructive')).not.toBeNull();
  });

  it('should keep the implement-run cancel copy for in_progress', () => {
    // Arrange / Act
    const { el } = setup(issue('in_progress', { stateDetails: { ...mockStateDetails, completedAt: null } }));

    // Assert
    expect(text(el.querySelector('dialog.confirm-dialog .confirm-dialog__title'))).toBe('Stop the worker?');
  });
});

describe('IssueDetailComponent — publishing (#787)', () => {
  function runDetail(overrides: Partial<WorkerRunDetail> = {}): WorkerRunDetail {
    return {
      workerRunId: 'run-pub',
      role: 'implement',
      issueId: 'abc123',
      state: 'publishing',
      failureCategory: null,
      failureSummary: null,
      resultText: null,
      subtype: null,
      isError: null,
      durationMs: null,
      numTurns: null,
      totalCostUsd: null,
      inputTokens: null,
      outputTokens: null,
      lastActivityAt: null,
      hasStoredLog: true,
      cancelRequestedAt: null,
      publish: null,
      publishRetry: null,
      discardsAt: null,
      secretFindings: [],
      revertedFiles: [],
      ...overrides,
    };
  }

  function issueInState(state: string, overrides: Partial<IssueDetail> = {}): IssueDetail {
    return {
      ...mockDetail,
      state: state as IssueDetail['state'],
      providerType: 'github',
      stateDetails: { ...mockStateDetails, workerRunId: 'run-pub', branchName: 'feat/42-add-retry', completedAt: null },
      ...overrides,
    };
  }

  const exhaustedRun = (): WorkerRunDetail => runDetail({
    state: 'publish_failed',
    publishRetry: { attemptNumber: 5, maxAttempts: 5, nextAttemptAt: null, reason: 'the provider rejected the push' },
    discardsAt: '2026-11-20T10:00:00Z',
  });

  function setup(detail: IssueDetail, run: WorkerRunDetail | null, publishRetryResult: string | null = null) {
    const requestPublishRetry = vi.fn((): Observable<string | null> => of(publishRetryResult));
    const getLog = vi.fn(() => of('line'));
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [IssueDetailComponent],
      providers: [
        { provide: IssueService, useValue: { retrying: signal(false), retryError: signal(null), retrySuccess: signal(null), ...triageSignals() } },
        WorkerSignalRService,
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: IssueSignalRService, useValue: mockIssueSignalRService },
        {
          provide: WorkerRunService,
          useValue: { getDetail: () => of(run), getLog, requestCancel: vi.fn(() => of(null)), requestPublishRetry },
        },
        { provide: WORKER_HUB_FACTORY, useValue: () => mockWorkerHub },
      ],
    });
    const fixture = TestBed.createComponent(IssueDetailComponent);
    fixture.componentRef.setInput('detail', detail);
    fixture.componentRef.setInput('loading', false);
    fixture.componentRef.setInput('error', null);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const publishButton = () =>
      Array.from(el.querySelectorAll<HTMLButtonElement>('.issue-detail__retry-btn'))
        .find((b) => b.getAttribute('aria-label')?.startsWith('Retry publishing')) ?? null;
    const publishDialog = () =>
      Array.from(el.querySelectorAll<HTMLDialogElement>('dialog.confirm-dialog'))
        .find((d) => d.querySelector('.confirm-dialog__title')?.textContent?.trim() === 'Retry publishing?') ?? null;
    return { fixture, el, requestPublishRetry, getLog, publishButton, publishDialog };
  }

  function confirmPublishRetry(ctx: ReturnType<typeof setup>): void {
    ctx.publishButton()!.click();
    ctx.fixture.detectChanges();
    (ctx.publishDialog()!.querySelector('.confirm-dialog__confirm') as HTMLButtonElement).click();
    ctx.fixture.detectChanges();
  }

  // Cancel run
  it.each(['publishing', 'publish_failed'] as const)('should hide Cancel run while the run is %s', (runState) => {
    // Arrange / Act
    const phase = runState === 'publishing' ? 'publishing' : 'retrying';
    const { el } = setup(issueInState('in_progress', { publishPhase: phase }), runDetail({ state: runState }));

    // Assert
    expect(el.querySelector('.issue-detail__cancel-btn')).toBeNull();
  });

  it('should show Cancel run before the run detail has loaded', () => {
    // Arrange / Act
    const { el } = setup(issueInState('in_progress'), null);

    // Assert
    expect(el.querySelector('.issue-detail__cancel-btn')).not.toBeNull();
  });

  // Log view
  it.each(['publishing', 'publish_failed'] as const)('should show no log view while the run is %s', (runState) => {
    // Arrange / Act
    const { el, getLog } = setup(issueInState('in_progress'), runDetail({ state: runState }));

    // Assert
    expect(el.querySelector('fd-log-view')).toBeNull();
    expect(getLog).not.toHaveBeenCalled();
  });

  it('should show no log view for an issue whose publish is exhausted', () => {
    // Arrange / Act
    const { el } = setup(issueInState('publish_failed'), exhaustedRun());

    // Assert
    expect(el.querySelector('fd-log-view')).toBeNull();
  });

  // Placement
  it('should render the publish callout in the state-details block', () => {
    // Arrange / Act
    const { el } = setup(issueInState('in_progress', { publishPhase: 'publishing' }), runDetail());

    // Assert
    const status = el.querySelector('fd-publish-status');
    expect(status).not.toBeNull();
    expect(status?.textContent).toContain("Foundry is publishing the run's commits to feat/42-add-retry.");
  });

  it('should render the published facts first in the run block when the run published', () => {
    // Arrange / Act
    const { el } = setup(issueInState('review'), runDetail({
      state: 'completed',
      publish: { headSha: 'abcdef1234567890', headCommitUrl: null, pullRequestUrl: null, branchCommitCount: 2 },
    }));

    // Assert
    const runBlock = el.querySelector('.issue-detail__worker-run');
    expect(runBlock?.firstElementChild?.tagName.toLowerCase()).toBe('fd-publish-facts');
  });

  it('should not render published facts when the run has not published', () => {
    // Arrange / Act
    const { el } = setup(issueInState('in_progress'), runDetail({ state: 'running' }));

    // Assert
    expect(el.querySelector('fd-publish-facts')).toBeNull();
  });

  // Retry publish
  it('should show Retry publish only for publish_failed, with its aria-label', () => {
    // Arrange / Act
    const { publishButton } = setup(issueInState('publish_failed'), exhaustedRun());

    // Assert
    expect(publishButton()?.textContent?.trim()).toBe('Retry publish');
    expect(publishButton()?.getAttribute('aria-label')).toBe('Retry publishing for issue #42');
  });

  it('should not offer Retry Issue for publish_failed', () => {
    // Arrange / Act
    const { el } = setup(issueInState('publish_failed'), exhaustedRun());

    // Assert
    expect(el.querySelector('[aria-label="Retry issue #42"]')).toBeNull();
  });

  it('should offer Retry Issue for branch_diverged', () => {
    // Arrange / Act
    const { el, publishButton } = setup(issueInState('branch_diverged'), runDetail({ state: 'completed' }));

    // Assert
    expect(el.querySelector('[aria-label="Retry issue #42"]')).not.toBeNull();
    expect(publishButton()).toBeNull();
  });

  it('should open a non-destructive confirm dialog instead of retrying immediately', () => {
    // Arrange
    const { fixture, requestPublishRetry, publishButton, publishDialog } = setup(issueInState('publish_failed'), exhaustedRun());

    // Act
    publishButton()!.click();
    fixture.detectChanges();

    // Assert
    const dialog = publishDialog();
    expect(dialog?.open).toBe(true);
    expect(dialog?.querySelector('.confirm-dialog__message')?.textContent?.trim()).toBe(
      "Foundry pushes the run's commits to feat/42-add-retry and opens or updates the pull request. CI runs on the push.",
    );
    expect(dialog?.querySelector('.confirm-dialog__confirm')?.textContent?.trim()).toBe('Retry publish');
    expect(dialog?.querySelector('.confirm-dialog__cancel')?.textContent?.trim()).toBe('Not now');
    expect(dialog?.querySelector('.confirm-dialog__confirm--destructive')).toBeNull();
    expect(requestPublishRetry).not.toHaveBeenCalled();
  });

  it('should call the service once confirmed', () => {
    // Arrange
    const ctx = setup(issueInState('publish_failed'), exhaustedRun());

    // Act
    confirmPublishRetry(ctx);

    // Assert
    expect(ctx.requestPublishRetry).toHaveBeenCalledWith('run-pub');
    expect(ctx.publishDialog()?.open).toBe(false);
  });

  it('should disable the button and read "Retrying publish…" while pending', () => {
    // Arrange
    const ctx = setup(issueInState('publish_failed'), exhaustedRun());
    ctx.requestPublishRetry.mockReturnValue(new Subject<string | null>());

    // Act
    confirmPublishRetry(ctx);

    // Assert
    expect(ctx.publishButton()?.disabled).toBe(true);
    expect(ctx.publishButton()?.textContent?.trim()).toBe('Retrying publish…');
  });

  it.each([
    'This run is no longer waiting to publish.',
    'The run was not found.',
    'Failed to retry publish.',
  ])('should show the error "%s" and restore the button', (message) => {
    // Arrange
    const ctx = setup(issueInState('publish_failed'), exhaustedRun(), message);

    // Act
    confirmPublishRetry(ctx);

    // Assert
    expect(ctx.publishButton()?.disabled).toBe(false);
    expect(ctx.publishButton()?.textContent?.trim()).toBe('Retry publish');
    const error = ctx.publishButton()?.parentElement?.querySelector('.issue-detail__retry-error');
    expect(error?.textContent).toContain(message);
    expect(error?.getAttribute('aria-live')).toBe('assertive');
  });

  it('should announce success and keep the announcement across a detail reload', () => {
    // Arrange
    const ctx = setup(issueInState('publish_failed'), exhaustedRun());

    // Act
    confirmPublishRetry(ctx);
    ctx.fixture.componentRef.setInput('detail', issueInState('in_progress', { publishPhase: 'publishing' }));
    ctx.fixture.detectChanges();

    // Assert
    const announcement = ctx.el.querySelector('.issue-detail__publish-retry-success-announcement');
    expect(announcement?.textContent?.trim()).toBe('Publish retry started. Issue status is updating.');
    expect(announcement?.getAttribute('aria-live')).toBe('polite');
    expect(ctx.publishButton()).toBeNull();
  });

  it('should move focus to the content region once the button unmounts after success', async () => {
    // Arrange
    const ctx = setup(issueInState('publish_failed'), exhaustedRun());
    document.body.appendChild(ctx.el);

    // Act
    confirmPublishRetry(ctx);
    ctx.fixture.componentRef.setInput('detail', issueInState('in_progress', { publishPhase: 'publishing' }));
    ctx.fixture.detectChanges();
    await ctx.fixture.whenStable();

    // Assert
    const content = ctx.el.querySelector('.issue-detail__content');
    expect(content?.getAttribute('tabindex')).toBe('-1');
    expect(document.activeElement).toBe(content);
    ctx.el.remove();
  });
});

describe('IssueDetailComponent — diff gate (#788)', () => {
  const findings = [
    { path: 'src/Api/appsettings.json', line: 14, commitSha: 'abc1234', ruleId: 'aws-access-key-id' },
    { path: 'scripts/deploy.sh', line: 3, commitSha: 'abc1234', ruleId: 'generic-api-key' },
  ];

  function runDetail(overrides: Partial<WorkerRunDetail> = {}): WorkerRunDetail {
    return {
      workerRunId: 'run-gate',
      role: 'implement',
      issueId: 'abc123',
      state: 'publish_blocked',
      failureCategory: null,
      failureSummary: null,
      resultText: null,
      subtype: null,
      isError: null,
      durationMs: null,
      numTurns: null,
      totalCostUsd: null,
      inputTokens: null,
      outputTokens: null,
      lastActivityAt: null,
      hasStoredLog: true,
      cancelRequestedAt: null,
      publish: null,
      publishRetry: null,
      discardsAt: null,
      secretFindings: [],
      revertedFiles: [],
      ...overrides,
    };
  }

  const blockedRun = (): WorkerRunDetail => runDetail({
    secretFindings: findings,
    discardsAt: '2026-11-16T15:04:12Z',
  });

  const exhaustedRun = (): WorkerRunDetail => runDetail({
    state: 'publish_failed',
    publishRetry: { attemptNumber: 5, maxAttempts: 5, nextAttemptAt: null, reason: 'the provider rejected the push' },
    discardsAt: '2026-11-20T10:00:00Z',
  });

  function issueInState(state: string, overrides: Partial<IssueDetail> = {}): IssueDetail {
    return {
      ...mockDetail,
      state: state as IssueDetail['state'],
      providerType: 'github',
      stateDetails: { ...mockStateDetails, workerRunId: 'run-gate', branchName: 'feat/42-add-retry', completedAt: null },
      ...overrides,
    };
  }

  function setup(detail: IssueDetail, run: WorkerRunDetail | null, discardResult: string | null = null) {
    const requestDiscard = vi.fn((): Observable<string | null> => of(discardResult));
    const requestPublishRetry = vi.fn((): Observable<string | null> => of(null));
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [IssueDetailComponent],
      providers: [
        { provide: IssueService, useValue: { retrying: signal(false), retryError: signal(null), retrySuccess: signal(null), ...triageSignals() } },
        WorkerSignalRService,
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: IssueSignalRService, useValue: mockIssueSignalRService },
        {
          provide: WorkerRunService,
          useValue: {
            getDetail: () => of(run),
            getLog: vi.fn(() => of('line')),
            requestCancel: vi.fn(() => of(null)),
            requestPublishRetry,
            requestDiscard,
          },
        },
        { provide: WORKER_HUB_FACTORY, useValue: () => mockWorkerHub },
      ],
    });
    const fixture = TestBed.createComponent(IssueDetailComponent);
    fixture.componentRef.setInput('detail', detail);
    fixture.componentRef.setInput('loading', false);
    fixture.componentRef.setInput('error', null);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const actionButtons = () =>
      Array.from(el.querySelectorAll<HTMLButtonElement>('.issue-detail__actions button'));
    const discardButton = () => actionButtons().find((b) => b.classList.contains('issue-detail__discard-btn')) ?? null;
    const publishButton = () =>
      actionButtons().find((b) => b.getAttribute('aria-label')?.startsWith('Retry publishing')) ?? null;
    const discardDialog = () =>
      Array.from(el.querySelectorAll<HTMLDialogElement>('dialog.confirm-dialog'))
        .find((d) => d.querySelector('.confirm-dialog__title')?.textContent?.trim() === 'Discard this run?') ?? null;
    return { fixture, el, requestDiscard, requestPublishRetry, actionButtons, discardButton, publishButton, discardDialog };
  }

  function confirmDiscard(ctx: ReturnType<typeof setup>): void {
    ctx.discardButton()!.click();
    ctx.fixture.detectChanges();
    (ctx.discardDialog()!.querySelector('.confirm-dialog__confirm') as HTMLButtonElement).click();
    ctx.fixture.detectChanges();
  }

  // Blocked state
  it('should show the blocked callout with the findings in the publish region', () => {
    // Arrange / Act
    const { el } = setup(issueInState('publish_blocked'), blockedRun());

    // Assert
    const status = el.querySelector('fd-publish-status');
    expect(status?.textContent).toContain('Push blocked');
    expect(status?.querySelectorAll('fd-secret-findings li').length).toBe(2);
  });

  it('should offer exactly one action, Discard run, for a secret-blocked issue', () => {
    // Arrange / Act
    const { el, actionButtons, discardButton } = setup(issueInState('publish_blocked'), blockedRun());

    // Assert
    expect(actionButtons().map((b) => b.textContent?.trim())).toEqual(['Discard run']);
    expect(discardButton()?.getAttribute('aria-label')).toBe('Discard run for issue #42');
    expect(el.querySelector('[aria-label="Retry issue #42"]')).toBeNull();
  });

  it('should render no control to publish anyway or reveal a secret', () => {
    // Arrange / Act
    const { el } = setup(issueInState('publish_blocked'), blockedRun());

    // Assert
    const labels = Array.from(el.querySelectorAll('button, a, summary'))
      .map((b) => `${b.textContent ?? ''} ${b.getAttribute('aria-label') ?? ''}`);
    for (const label of labels) {
      expect(label).not.toMatch(/publish anyway|reveal|show secret|false positive|copy/i);
    }
  });

  it('should show no log view for a secret-blocked run', () => {
    // Arrange / Act
    const { el } = setup(issueInState('publish_blocked'), blockedRun());

    // Assert
    expect(el.querySelector('fd-log-view')).toBeNull();
  });

  it('should show Retry publish then Discard run for an exhausted publish', () => {
    // Arrange / Act
    const { actionButtons } = setup(issueInState('publish_failed'), exhaustedRun());

    // Assert
    expect(actionButtons().map((b) => b.textContent?.trim())).toEqual(['Retry publish', 'Discard run']);
  });

  it.each(['in_progress', 'review', 'failed', 'branch_diverged'])('should not offer Discard run for %s', (state) => {
    // Arrange / Act
    const { discardButton } = setup(issueInState(state), runDetail({ state: 'completed' }));

    // Assert
    expect(discardButton()).toBeNull();
  });

  // Confirm dialog
  it('should open a destructive confirm dialog instead of discarding immediately', () => {
    // Arrange
    const { fixture, requestDiscard, discardButton, discardDialog } = setup(issueInState('publish_blocked'), blockedRun());

    // Act
    discardButton()!.click();
    fixture.detectChanges();

    // Assert
    const dialog = discardDialog();
    expect(dialog?.open).toBe(true);
    expect(dialog?.querySelector('.confirm-dialog__message')?.textContent?.trim()).toBe(
      "The run's commits are deleted from this host and nothing is pushed. The issue moves to Failed, where Retry "
      + 'Issue starts a new worker from the branch on GitHub. This cannot be undone.',
    );
    expect(dialog?.querySelector('.confirm-dialog__confirm')?.textContent?.trim()).toBe('Discard run');
    expect(dialog?.querySelector('.confirm-dialog__cancel')?.textContent?.trim()).toBe('Keep run');
    expect(dialog?.querySelector('.confirm-dialog__confirm--destructive')).not.toBeNull();
    expect(requestDiscard).not.toHaveBeenCalled();
  });

  it('should name GitLab in the dialog for a GitLab issue', () => {
    // Arrange
    const { fixture, discardButton, discardDialog } = setup(
      issueInState('publish_blocked', { providerType: 'gitlab' }), blockedRun());

    // Act
    discardButton()!.click();
    fixture.detectChanges();

    // Assert
    expect(discardDialog()?.querySelector('.confirm-dialog__message')?.textContent).toContain('from the branch on GitLab.');
  });

  it('should call the service once confirmed', () => {
    // Arrange
    const ctx = setup(issueInState('publish_blocked'), blockedRun());

    // Act
    confirmDiscard(ctx);

    // Assert
    expect(ctx.requestDiscard).toHaveBeenCalledWith('run-gate');
    expect(ctx.discardDialog()?.open).toBe(false);
  });

  it('should not call the service when the dialog is dismissed', () => {
    // Arrange
    const ctx = setup(issueInState('publish_blocked'), blockedRun());
    ctx.discardButton()!.click();
    ctx.fixture.detectChanges();

    // Act
    (ctx.discardDialog()!.querySelector('.confirm-dialog__cancel') as HTMLButtonElement).click();
    ctx.fixture.detectChanges();

    // Assert
    expect(ctx.requestDiscard).not.toHaveBeenCalled();
    expect(ctx.discardDialog()?.open).toBe(false);
  });

  // Pending
  it('should disable the button and read "Discarding…" while pending, disabling Retry publish too', () => {
    // Arrange
    const ctx = setup(issueInState('publish_failed'), exhaustedRun());
    ctx.requestDiscard.mockReturnValue(new Subject<string | null>());

    // Act
    confirmDiscard(ctx);

    // Assert
    expect(ctx.discardButton()?.disabled).toBe(true);
    expect(ctx.discardButton()?.textContent?.trim()).toBe('Discarding…');
    expect(ctx.publishButton()?.disabled).toBe(true);
  });

  // Errors
  it.each([
    'This run is no longer waiting to publish.',
    'The run was not found.',
    'Failed to discard the run.',
  ])('should show the error "%s" in the assertive region and restore the button', (message) => {
    // Arrange
    const ctx = setup(issueInState('publish_blocked'), blockedRun(), message);

    // Act
    confirmDiscard(ctx);

    // Assert
    expect(ctx.discardButton()?.disabled).toBe(false);
    expect(ctx.discardButton()?.textContent?.trim()).toBe('Discard run');
    const error = ctx.discardButton()?.parentElement?.querySelector('.issue-detail__retry-error');
    expect(error?.textContent).toContain(message);
    expect(error?.getAttribute('aria-live')).toBe('assertive');
  });

  // Success
  it('should announce success and keep the announcement across a detail reload', () => {
    // Arrange
    const ctx = setup(issueInState('publish_blocked'), blockedRun());

    // Act
    confirmDiscard(ctx);
    ctx.fixture.componentRef.setInput('detail', issueInState('failed'));
    ctx.fixture.detectChanges();

    // Assert
    const announcement = ctx.el.querySelector('.issue-detail__discard-success-announcement');
    expect(announcement?.textContent?.trim()).toBe('Run discarded. Issue status is updating.');
    expect(announcement?.getAttribute('aria-live')).toBe('polite');
    expect(announcement?.classList.contains('sr-only')).toBe(true);
    expect(ctx.discardButton()).toBeNull();
  });

  it('should move focus to the content region once the button unmounts after success', async () => {
    // Arrange
    const ctx = setup(issueInState('publish_blocked'), blockedRun());
    document.body.appendChild(ctx.el);

    // Act
    confirmDiscard(ctx);
    ctx.fixture.componentRef.setInput('detail', issueInState('failed'));
    ctx.fixture.detectChanges();
    await ctx.fixture.whenStable();

    // Assert
    const content = ctx.el.querySelector('.issue-detail__content');
    expect(document.activeElement).toBe(content);
    ctx.el.remove();
  });

  // Reverted files
  it('should render the reverted files after the published facts in the run block', () => {
    // Arrange / Act
    const { el } = setup(issueInState('review'), runDetail({
      state: 'completed',
      publish: { headSha: 'abcdef1234567890', headCommitUrl: null, pullRequestUrl: null, branchCommitCount: 2 },
      revertedFiles: [
        { path: 'assets/demo.mp4', reason: 'oversized', sizeBytes: 50_541_363 },
        { path: 'tools/protoc.exe', reason: 'binary', sizeBytes: 3_250_586 },
      ],
    }));

    // Assert
    const runBlock = el.querySelector('.issue-detail__worker-run');
    const reverted = runBlock?.querySelector('fd-reverted-files');
    expect(reverted).not.toBeNull();
    expect(reverted?.previousElementSibling?.tagName.toLowerCase()).toBe('fd-publish-facts');
    expect(reverted?.querySelectorAll('li').length).toBe(2);
  });

  it('should not render reverted files when the list is empty', () => {
    // Arrange / Act
    const { el } = setup(issueInState('review'), runDetail({
      state: 'completed',
      publish: { headSha: 'abcdef1234567890', headCommitUrl: null, pullRequestUrl: null, branchCommitCount: 2 },
    }));

    // Assert
    expect(el.querySelector('fd-reverted-files')).toBeNull();
  });
});

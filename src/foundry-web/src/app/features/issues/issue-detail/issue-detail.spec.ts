import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { of } from 'rxjs';
import { IssueDetailComponent } from './issue-detail';
import { IssueDetail, IssueStateDetails } from '../issue.model';
import { IssueService } from '../issue.service';
import { IssueSignalRService } from '../../../core/services/issue-signalr.service';
import { WorkerRunService } from '../../workers/worker-run.service';
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
};

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
      ['continuable_failed', 'failed', 'revision_failed', 'unchanged'],
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
      }),
      getLog: () => of(null),
    };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [IssueDetailComponent],
      providers: [
        { provide: IssueService, useValue: { retrying: signal(false), retryError: signal(null), retrySuccess: signal(null) } },
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
        { provide: IssueService, useValue: { retrying: signal(false), retryError: signal(null), retrySuccess: signal(null) } },
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
    expect(dialog()?.textContent).toContain('Commits on the branch are kept; Retry continues from them.');
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

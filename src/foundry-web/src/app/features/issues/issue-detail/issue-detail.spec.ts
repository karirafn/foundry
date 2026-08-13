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
import { RETRYABLE_STATES } from '../../../shared/utils/issue-state';

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
  stream: () => ({ subscribe: () => ({ dispose: () => {} }) }),
  start: () => Promise.resolve(),
};

const mockStateDetails: IssueStateDetails = {
  workerRunId: null,
  branchName: 'feat/dark-mode',
  pullRequestUrl: 'https://github.com/owner/repo/pull/99',
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
  providerType: 'GitHub',
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

  // Cycle 5: state detail fields - PR link
  it('should render a PR link when pullRequestUrl is present', () => {
    // Arrange
    const fixture = createComponent(mockDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Act — component renders on creation

    // Assert
    const link = el.querySelector('.issue-detail__pr-link') as HTMLAnchorElement;
    expect(link).toBeTruthy();
    expect(link?.getAttribute('href')).toBe('https://github.com/owner/repo/pull/99');
    expect(link?.getAttribute('target')).toBe('_blank');
  });

  it('should set aria-label on PR link to include issue number and provider-correct term', () => {
    // Arrange — GitHub provider uses "pull request"
    const fixture = createComponent(mockDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Act — component renders on creation

    // Assert
    const link = el.querySelector('.issue-detail__pr-link') as HTMLAnchorElement;
    expect(link?.getAttribute('aria-label')).toBe('Open pull request for issue #42');
  });

  it('should not render a PR link when pullRequestUrl is not https', () => {
    // Arrange
    const detailHttpPr: IssueDetail = {
      ...mockDetail,
      stateDetails: { ...mockStateDetails, pullRequestUrl: 'http://github.com/owner/repo/pull/99' },
    };

    // Act
    const fixture = createComponent(detailHttpPr);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const link = el.querySelector('.issue-detail__pr-link');
    expect(link).toBeFalsy();
  });

  it('should not render a PR link when pullRequestUrl is null', () => {
    // Arrange
    const detailNoPr: IssueDetail = {
      ...mockDetail,
      stateDetails: { ...mockStateDetails, pullRequestUrl: null },
    };

    // Act
    const fixture = createComponent(detailNoPr);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const link = el.querySelector('.issue-detail__pr-link');
    expect(link).toBeFalsy();
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

  // MR/PR terminology: GitHub providerType shows "Pull request" and "View PR"
  it('should show "Pull request" field key and "View PR" link text for GitHub providerType', () => {
    // Arrange — mockDetail has providerType: 'GitHub'
    const fixture = createComponent(mockDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Act — component renders on creation

    // Assert
    const fields = el.querySelectorAll('.issue-detail__field');
    const prField = Array.from(fields).find((f) =>
      f.querySelector('.issue-detail__field-key')?.textContent?.trim().toLowerCase().includes('request'),
    );
    expect(prField?.querySelector('.issue-detail__field-key')?.textContent?.trim()).toBe('Pull request');
    const link = el.querySelector('.issue-detail__pr-link') as HTMLAnchorElement;
    expect(link?.textContent?.trim()).toBe('View PR');
  });

  // MR/PR terminology: GitLab providerType shows "Merge request" and "View MR"
  it('should show "Merge request" field key and "View MR" link text for GitLab providerType', () => {
    // Arrange
    const gitlabDetail: IssueDetail = {
      ...mockDetail,
      providerType: 'GitLab',
      stateDetails: {
        ...mockStateDetails,
        pullRequestUrl: 'https://git.acme.com/owner/repo/-/merge_requests/99',
      },
    };

    // Act
    const fixture = createComponent(gitlabDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const fields = el.querySelectorAll('.issue-detail__field');
    const prField = Array.from(fields).find((f) =>
      f.querySelector('.issue-detail__field-key')?.textContent?.trim().toLowerCase().includes('request'),
    );
    expect(prField?.querySelector('.issue-detail__field-key')?.textContent?.trim()).toBe('Merge request');
    const link = el.querySelector('.issue-detail__pr-link') as HTMLAnchorElement;
    expect(link?.textContent?.trim()).toBe('View MR');
  });

  // Aria-label: GitLab providerType uses "merge request" in aria-label
  it('should set aria-label on MR link to use "merge request" for GitLab providerType', () => {
    // Arrange
    const gitlabDetail: IssueDetail = {
      ...mockDetail,
      providerType: 'GitLab',
      stateDetails: {
        ...mockStateDetails,
        pullRequestUrl: 'https://git.acme.com/owner/repo/-/merge_requests/99',
      },
    };

    // Act
    const fixture = createComponent(gitlabDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const link = el.querySelector('.issue-detail__pr-link') as HTMLAnchorElement;
    expect(link?.getAttribute('aria-label')).toBe('Open merge request for issue #42');
  });

  // Aria-label: self-hosted GitLab (non-gitlab.com domain) correctly uses "merge request"
  it('should use "merge request" terminology for self-hosted GitLab with custom domain', () => {
    // Arrange — custom domain that would not match URL sniffing for "gitlab"
    const selfHostedGitLabDetail: IssueDetail = {
      ...mockDetail,
      providerType: 'GitLab',
      stateDetails: {
        ...mockStateDetails,
        pullRequestUrl: 'https://git.acme.com/owner/repo/-/merge_requests/99',
      },
    };

    // Act
    const fixture = createComponent(selfHostedGitLabDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const link = el.querySelector('.issue-detail__pr-link') as HTMLAnchorElement;
    expect(link?.textContent?.trim()).toBe('View MR');
    expect(link?.getAttribute('aria-label')).toBe('Open merge request for issue #42');
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

    // Assert — live region exists and contains retry content
    const liveRegion = el.querySelector('[role="status"][aria-live="polite"]') as HTMLElement;
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

    // Assert — live region is present but empty; stateDetails is non-null so the region is mounted
    const liveRegion = el.querySelector('[role="status"][aria-live="polite"]') as HTMLElement;
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
});

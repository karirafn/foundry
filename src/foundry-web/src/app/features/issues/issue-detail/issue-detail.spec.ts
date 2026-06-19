import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { IssueDetailComponent } from './issue-detail';
import { IssueDetail, IssueStateDetails } from '../issue.model';
import { IssueService } from '../issue.service';
import { IssueSignalRService } from '../../../core/services/issue-signalr.service';

const mockIssueSignalRService = {
  on: () => {},
  onReconnected: () => {},
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
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: IssueSignalRService, useValue: mockIssueSignalRService },
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

  it('should set aria-label on PR link to include issue number', () => {
    // Arrange
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

  // Cycle 16: ineligible state — violations list
  it('should render eligibility violations when state is ineligible and violations are present', () => {
    // Arrange
    const ineligibleDetail: IssueDetail = {
      ...mockDetail,
      state: 'ineligible',
      stateDetails: {
        ...mockStateDetails,
        violations: [
          { rule: 'no-open-pr', description: 'Issue already has an open pull request' },
          { rule: 'label-removed', description: 'Trigger label was removed' },
        ],
      },
    };

    // Act
    const fixture = createComponent(ineligibleDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const items = el.querySelectorAll('.issue-detail__violation');
    expect(items.length).toBe(2);
    const texts = Array.from(items).map((i) => i.textContent?.trim());
    expect(texts).toContain('Issue already has an open pull request');
    expect(texts).toContain('Trigger label was removed');
  });

  it('should not render violations section when state is not ineligible', () => {
    // Arrange
    const fixture = createComponent(mockDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Act — component renders on creation

    // Assert
    const violations = el.querySelector('.issue-detail__violations');
    expect(violations).toBeFalsy();
  });

  it('should show fallback message when state is ineligible but violations list is empty', () => {
    // Arrange
    const ineligibleNoViolations: IssueDetail = {
      ...mockDetail,
      state: 'ineligible',
      stateDetails: { ...mockStateDetails, violations: [] },
    };

    // Act
    const fixture = createComponent(ineligibleNoViolations);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const violations = el.querySelector('.issue-detail__violations');
    expect(violations).toBeFalsy();
    const label = el.querySelector('#eligibility-violations-label') as HTMLElement;
    const fallback = label?.parentElement?.querySelector('.issue-detail__field-value');
    expect(fallback?.textContent?.trim()).toBe('Eligibility details are unavailable');
  });

  it('should show fallback message when state is ineligible and violations is null', () => {
    // Arrange
    const ineligibleNullViolations: IssueDetail = {
      ...mockDetail,
      state: 'ineligible',
      stateDetails: { ...mockStateDetails, violations: null },
    };

    // Act
    const fixture = createComponent(ineligibleNullViolations);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const violations = el.querySelector('.issue-detail__violations');
    expect(violations).toBeFalsy();
    const label = el.querySelector('#eligibility-violations-label') as HTMLElement;
    const fallback = label?.parentElement?.querySelector('.issue-detail__field-value');
    expect(fallback?.textContent?.trim()).toBe('Eligibility details are unavailable');
  });

  // Cycle 17: ineligible state — retry button
  it('should render a retry button when state is ineligible', () => {
    // Arrange
    const ineligibleDetail: IssueDetail = {
      ...mockDetail,
      state: 'ineligible',
      stateDetails: { ...mockStateDetails, violations: null },
    };

    // Act
    const fixture = createComponent(ineligibleDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const btn = el.querySelector('.issue-detail__retry-eligibility-btn') as HTMLButtonElement;
    expect(btn).toBeTruthy();
    expect(btn?.textContent?.trim()).toBe('Retry Eligibility Check');
  });

  it('should not render retry eligibility button when state is not ineligible', () => {
    // Arrange
    const fixture = createComponent(mockDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Act — component renders on creation

    // Assert
    const btn = el.querySelector('.issue-detail__retry-eligibility-btn');
    expect(btn).toBeFalsy();
  });

  it('should call retryEligibility on the service when retry button is clicked', () => {
    // Arrange
    const ineligibleDetail: IssueDetail = {
      ...mockDetail,
      state: 'ineligible',
      stateDetails: { ...mockStateDetails, violations: null },
    };
    const fixture = createComponent(ineligibleDetail);
    const el = fixture.nativeElement as HTMLElement;
    const http = TestBed.inject(HttpTestingController);

    // Act
    const btn = el.querySelector('.issue-detail__retry-eligibility-btn') as HTMLButtonElement;
    btn.click();
    const req = http.expectOne(`/api/issues/${ineligibleDetail.id}/retry-eligibility`);

    // Assert
    expect(req.request.method).toBe('POST');
    req.flush(null);
    // Service calls loadDetail after success; flush that request too
    http.expectOne(`/api/issues/${ineligibleDetail.id}`).flush(ineligibleDetail);
    http.verify();
  });

  it('should set aria-label on retry button referencing the issue number', () => {
    // Arrange
    const ineligibleDetail: IssueDetail = {
      ...mockDetail,
      state: 'ineligible',
      stateDetails: { ...mockStateDetails, violations: null },
    };

    // Act
    const fixture = createComponent(ineligibleDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const btn = el.querySelector('.issue-detail__retry-eligibility-btn') as HTMLButtonElement;
    expect(btn?.getAttribute('aria-label')).toBe('Retry eligibility check for issue #42');
  });

  it('should disable retry button while retryingEligibility is true', () => {
    // Arrange
    const ineligibleDetail: IssueDetail = {
      ...mockDetail,
      state: 'ineligible',
      stateDetails: { ...mockStateDetails, violations: null },
    };
    const fixture = createComponent(ineligibleDetail);
    const el = fixture.nativeElement as HTMLElement;
    const issueService = TestBed.inject(IssueService);
    const http = TestBed.inject(HttpTestingController);

    // Act — click starts the request, signal becomes true
    const btn = el.querySelector('.issue-detail__retry-eligibility-btn') as HTMLButtonElement;
    btn.click();
    fixture.detectChanges();

    // Assert — button is disabled while request is in flight
    expect(issueService.retryingEligibility()).toBe(true);
    expect(btn.disabled).toBe(true);
    expect(btn.textContent?.trim()).toBe('Retrying...');

    // Cleanup
    http.expectOne(`/api/issues/${ineligibleDetail.id}/retry-eligibility`).flush(null);
    http.expectOne(`/api/issues/${ineligibleDetail.id}`).flush(ineligibleDetail);
    http.verify();
  });

  it('should associate violations list with its label via aria-labelledby', () => {
    // Arrange
    const ineligibleDetail: IssueDetail = {
      ...mockDetail,
      state: 'ineligible',
      stateDetails: {
        ...mockStateDetails,
        violations: [{ rule: 'no-open-pr', description: 'Issue already has an open pull request' }],
      },
    };

    // Act
    const fixture = createComponent(ineligibleDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const label = el.querySelector('#eligibility-violations-label');
    expect(label).toBeTruthy();
    const list = el.querySelector('.issue-detail__violations') as HTMLUListElement;
    expect(list?.getAttribute('aria-labelledby')).toBe('eligibility-violations-label');
  });

  // Transition behavior: stale ineligible detail does not show content while loading
  it('should not render content block when loading is true even if stale detail is present', () => {
    // Arrange — simulate stale ineligible detail still in memory during transition
    const ineligibleDetail: IssueDetail = {
      ...mockDetail,
      state: 'ineligible',
      stateDetails: { ...mockStateDetails, violations: null },
    };

    // Act — loading=true with stale detail (transition state)
    const fixture = createComponent(ineligibleDetail, true);
    const el = fixture.nativeElement as HTMLElement;

    // Assert — content is not rendered, only the skeleton is shown
    const content = el.querySelector('.issue-detail__content');
    expect(content).toBeFalsy();
    const skeleton = el.querySelector('.issue-detail__skeleton');
    expect(skeleton).toBeTruthy();
  });

  it('should not render retry eligibility button when loading is true even if stale ineligible detail is present', () => {
    // Arrange — simulate stale ineligible detail during loading transition
    const ineligibleDetail: IssueDetail = {
      ...mockDetail,
      state: 'ineligible',
      stateDetails: { ...mockStateDetails, violations: null },
    };

    // Act
    const fixture = createComponent(ineligibleDetail, true);
    const el = fixture.nativeElement as HTMLElement;

    // Assert — retry button is absent while skeleton is shown
    const retryBtn = el.querySelector('.issue-detail__retry-eligibility-btn');
    expect(retryBtn).toBeFalsy();
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

  // MR/PR terminology: GitHub URL shows "Pull request" and "View PR"
  it('should show "Pull request" field key and "View PR" link text for GitHub PR URL', () => {
    // Arrange
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

  // MR/PR terminology: GitLab URL shows "Merge request" and "View MR"
  it('should show "Merge request" field key and "View MR" link text for GitLab MR URL', () => {
    // Arrange
    const gitlabDetail: IssueDetail = {
      ...mockDetail,
      stateDetails: {
        ...mockStateDetails,
        pullRequestUrl: 'https://gitlab.com/owner/repo/-/merge_requests/99',
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
});

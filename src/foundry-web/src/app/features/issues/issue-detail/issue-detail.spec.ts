import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { IssueDetailComponent, MEDIA_QUERY_FACTORY } from './issue-detail';
import { IssueDetail } from '../issue.model';
import { WorkerLogService, WORKER_LOG_HUB_FACTORY } from '../worker-log.service';
import { IssueService } from '../issue.service';
import { SignalRService } from '../../../core/services/signalr.service';

const mockSignalRService = {
  on: () => {},
  onReconnected: () => {},
};

const mockDetail: IssueDetail = {
  id: 'abc123',
  issueNumber: 42,
  title: 'Enable dark mode',
  state: 'completed',
  repositorySlug: 'owner/repo',
  detectedAt: '2026-01-01T00:00:00Z',
  url: 'https://github.com/owner/repo/issues/42',
  body: 'We need a dark mode for the dashboard.',
  author: 'dev',
  labels: ['enhancement', 'ui'],
  stateDetails: {
    workerRunId: null,
    branchName: 'feat/dark-mode',
    pullRequestUrl: 'https://github.com/owner/repo/pull/99',
    feedbackCutoffAt: null,
    failureReason: null,
    failedAt: null,
    completedAt: '2026-02-01T12:00:00Z',
    blockedBy: null,
  },
};

const mockDetailWithWorkerRun: IssueDetail = {
  ...mockDetail,
  stateDetails: {
    ...mockDetail.stateDetails,
    workerRunId: 'run-42',
  },
};

const mockHubFactory = () => ({
  on: () => {},
  off: () => {},
  start: () => Promise.resolve(),
  stop: () => Promise.resolve(),
  invoke: () => Promise.resolve(),
});

function createComponent(
  detail: IssueDetail | null,
  loading = false,
  error: string | null = null,
  mqFactory = desktopMqFactory
) {
  TestBed.configureTestingModule({
    imports: [IssueDetailComponent],
    providers: [
      WorkerLogService,
      IssueService,
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: WORKER_LOG_HUB_FACTORY, useValue: mockHubFactory },
      { provide: MEDIA_QUERY_FACTORY, useValue: mqFactory },
      { provide: SignalRService, useValue: mockSignalRService },
    ],
  });
  const fixture = TestBed.createComponent(IssueDetailComponent);
  fixture.componentRef.setInput('detail', detail);
  fixture.componentRef.setInput('loading', loading);
  fixture.componentRef.setInput('error', error);
  fixture.detectChanges();
  return fixture;
}

const desktopMqFactory = (_query: string): MediaQueryList => ({
  matches: false,
  media: _query,
  onchange: null,
  addEventListener: () => {},
  removeEventListener: () => {},
  addListener: () => {},
  removeListener: () => {},
  dispatchEvent: () => false,
} as unknown as MediaQueryList);

const mobileMqFactory = (query: string): MediaQueryList => ({
  matches: query === '(max-width: 767px)',
  media: query,
  onchange: null,
  addEventListener: () => {},
  removeEventListener: () => {},
  addListener: () => {},
  removeListener: () => {},
  dispatchEvent: () => false,
} as unknown as MediaQueryList);

describe('IssueDetailComponent', () => {
  // Cycle 1: component creates
  it('should create the component', () => {
    // Arrange / Act
    const fixture = createComponent(null, false);

    // Assert
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should render with role="region" when detail is provided', () => {
    // Arrange / Act
    const fixture = createComponent(mockDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const region = el.querySelector('[role="region"]');
    expect(region).toBeTruthy();
  });

  // Cycle 2: loading skeleton
  it('should show shimmer skeleton when loading is true', () => {
    // Arrange / Act
    const fixture = createComponent(null, true);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const skeleton = el.querySelector('.issue-detail__skeleton');
    expect(skeleton).toBeTruthy();
  });

  it('should mark skeleton container as aria-busy when loading', () => {
    // Arrange / Act
    const fixture = createComponent(null, true);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const skeleton = el.querySelector('[aria-busy="true"]');
    expect(skeleton).toBeTruthy();
  });

  it('should render three shimmer bars in the skeleton', () => {
    // Arrange / Act
    const fixture = createComponent(null, true);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const bars = el.querySelectorAll('.issue-detail__shimmer-bar');
    expect(bars.length).toBe(3);
  });

  it('should not show skeleton when not loading', () => {
    // Arrange / Act
    const fixture = createComponent(mockDetail, false);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const skeleton = el.querySelector('.issue-detail__skeleton');
    expect(skeleton).toBeFalsy();
  });

  // Cycle 3: body content
  it('should display the issue body text', () => {
    // Arrange / Act
    const fixture = createComponent(mockDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const body = el.querySelector('.issue-detail__body');
    expect(body?.textContent?.trim()).toContain('We need a dark mode');
  });

  // Cycle 4: labels
  it('should render each label as a pill', () => {
    // Arrange / Act
    const fixture = createComponent(mockDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const pills = el.querySelectorAll('.issue-detail__label-pill');
    expect(pills.length).toBe(2);
    const texts = Array.from(pills).map((p) => p.textContent?.trim());
    expect(texts).toContain('enhancement');
    expect(texts).toContain('ui');
  });

  // Cycle 5: state detail fields - PR link
  it('should render a PR link when pullRequestUrl is present', () => {
    // Arrange / Act
    const fixture = createComponent(mockDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const link = el.querySelector('.issue-detail__pr-link') as HTMLAnchorElement;
    expect(link).toBeTruthy();
    expect(link?.getAttribute('href')).toBe('https://github.com/owner/repo/pull/99');
    expect(link?.getAttribute('target')).toBe('_blank');
  });

  it('should set aria-label on PR link to include issue number', () => {
    // Arrange / Act
    const fixture = createComponent(mockDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const link = el.querySelector('.issue-detail__pr-link') as HTMLAnchorElement;
    expect(link?.getAttribute('aria-label')).toBe('Open pull request for issue #42');
  });

  it('should not render a PR link when pullRequestUrl is not https', () => {
    // Arrange
    const detailHttpPr: IssueDetail = {
      ...mockDetail,
      stateDetails: { ...mockDetail.stateDetails, pullRequestUrl: 'http://github.com/owner/repo/pull/99' },
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
      stateDetails: { ...mockDetail.stateDetails, pullRequestUrl: null },
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
    // Arrange / Act
    const fixture = createComponent(mockDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const branch = el.querySelector('.issue-detail__branch');
    expect(branch?.textContent?.trim()).toBe('feat/dark-mode');
  });

  it('should not render branch row when branchName is null', () => {
    // Arrange
    const detailNoBranch: IssueDetail = {
      ...mockDetail,
      stateDetails: { ...mockDetail.stateDetails, branchName: null },
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
    // Arrange / Act
    const fixture = createComponent(mockDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const region = el.querySelector('[role="region"]') as HTMLElement;
    expect(region?.getAttribute('aria-label')).toBe('Issue details for #42');
  });

  // Cycle 8: error state
  it('should show error message when error input is truthy', () => {
    // Arrange / Act
    const fixture = createComponent(null, false, 'Http failure');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const errorEl = el.querySelector('.issue-detail__error');
    expect(errorEl).toBeTruthy();
    expect(errorEl?.textContent).toContain('Failed to load details');
  });

  it('should not show detail content when error is present', () => {
    // Arrange / Act
    const fixture = createComponent(null, false, 'Http failure');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const content = el.querySelector('.issue-detail__content');
    expect(content).toBeFalsy();
  });

  it('should not show skeleton when error is present', () => {
    // Arrange / Act
    const fixture = createComponent(null, true, 'Http failure');
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const skeleton = el.querySelector('.issue-detail__skeleton');
    expect(skeleton).toBeFalsy();
  });

  it('should not show error block when error is null', () => {
    // Arrange / Act
    const fixture = createComponent(mockDetail, false, null);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const errorEl = el.querySelector('.issue-detail__error');
    expect(errorEl).toBeFalsy();
  });

  // Cycle 9: View Logs button — appears when workerRunId is non-null
  it('should show "View Logs" button when workerRunId is non-null', () => {
    // Arrange / Act
    const fixture = createComponent(mockDetailWithWorkerRun);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const btn = el.querySelector('.issue-detail__view-logs-btn') as HTMLButtonElement;
    expect(btn).toBeTruthy();
    expect(btn?.textContent?.trim()).toContain('View Logs');
  });

  it('should not show "View Logs" button when workerRunId is null', () => {
    // Arrange / Act
    const fixture = createComponent(mockDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const btn = el.querySelector('.issue-detail__view-logs-btn');
    expect(btn).toBeFalsy();
  });

  // Cycle 10: aria-expanded is false initially
  it('should set aria-expanded="false" on "View Logs" button initially', () => {
    // Arrange / Act
    const fixture = createComponent(mockDetailWithWorkerRun);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const btn = el.querySelector('.issue-detail__view-logs-btn') as HTMLButtonElement;
    expect(btn?.getAttribute('aria-expanded')).toBe('false');
  });

  it('should not set aria-controls when log panel is closed', () => {
    // Arrange / Act
    const fixture = createComponent(mockDetailWithWorkerRun);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const btn = el.querySelector('.issue-detail__view-logs-btn') as HTMLButtonElement;
    expect(btn?.getAttribute('aria-controls')).toBeNull();
  });

  it('should set aria-controls pointing to the log panel id when panel is open on desktop', () => {
    // Arrange
    const fixture = createComponent(mockDetailWithWorkerRun);
    const el = fixture.nativeElement as HTMLElement;
    const btn = el.querySelector('.issue-detail__view-logs-btn') as HTMLButtonElement;

    // Act
    btn.click();
    fixture.detectChanges();

    // Assert
    expect(btn?.getAttribute('aria-controls')).toBe('issue-detail-log-panel');
  });

  // Cycle 11: toggle changes text and aria-expanded
  it('should show "Hide Logs" text and aria-expanded="true" after clicking "View Logs"', () => {
    // Arrange
    const fixture = createComponent(mockDetailWithWorkerRun);
    const el = fixture.nativeElement as HTMLElement;
    const btn = el.querySelector('.issue-detail__view-logs-btn') as HTMLButtonElement;

    // Act
    btn.click();
    fixture.detectChanges();

    // Assert
    expect(btn?.textContent?.trim()).toContain('Hide Logs');
    expect(btn?.getAttribute('aria-expanded')).toBe('true');
  });

  it('should revert to "View Logs" text after clicking "Hide Logs"', () => {
    // Arrange
    const fixture = createComponent(mockDetailWithWorkerRun);
    const el = fixture.nativeElement as HTMLElement;
    const btn = el.querySelector('.issue-detail__view-logs-btn') as HTMLButtonElement;
    btn.click();
    fixture.detectChanges();

    // Act
    btn.click();
    fixture.detectChanges();

    // Assert
    expect(btn?.textContent?.trim()).toContain('View Logs');
  });

  // Cycle 12: inline panel renders on non-mobile when panel open
  it('should render inline log panel after clicking "View Logs" on desktop', () => {
    // Arrange
    const fixture = createComponent(mockDetailWithWorkerRun);
    const el = fixture.nativeElement as HTMLElement;
    const btn = el.querySelector('.issue-detail__view-logs-btn') as HTMLButtonElement;

    // Act
    btn.click();
    fixture.detectChanges();

    // Assert
    const panel = el.querySelector('.issue-detail__log-panel-inline');
    expect(panel).toBeTruthy();
  });

  it('should not render inline log panel when "View Logs" is not clicked', () => {
    // Arrange / Act
    const fixture = createComponent(mockDetailWithWorkerRun);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const panel = el.querySelector('.issue-detail__log-panel-inline');
    expect(panel).toBeFalsy();
  });

  // Cycle 13: overlay renders on mobile
  it('should render mobile overlay instead of inline panel when viewport is mobile', () => {
    // Arrange
    const fixture = createComponent(mockDetailWithWorkerRun, false, null, mobileMqFactory);
    const el = fixture.nativeElement as HTMLElement;
    const btn = el.querySelector('.issue-detail__view-logs-btn') as HTMLButtonElement;

    // Act
    btn.click();
    fixture.detectChanges();

    // Assert
    const overlay = el.querySelector('.issue-detail__overlay');
    expect(overlay).toBeTruthy();
    const inlinePanel = el.querySelector('.issue-detail__log-panel-inline');
    expect(inlinePanel).toBeFalsy();
  });

  it('should render overlay with role="dialog" and aria-modal="true" on mobile', () => {
    // Arrange
    const fixture = createComponent(mockDetailWithWorkerRun, false, null, mobileMqFactory);
    const el = fixture.nativeElement as HTMLElement;
    const btn = el.querySelector('.issue-detail__view-logs-btn') as HTMLButtonElement;

    // Act
    btn.click();
    fixture.detectChanges();

    // Assert
    const overlay = el.querySelector('[role="dialog"]') as HTMLElement;
    expect(overlay).toBeTruthy();
    expect(overlay?.getAttribute('aria-modal')).toBe('true');
    expect(overlay?.getAttribute('aria-label')).toBe('Worker log output');
  });

  // Cycle 14: overlay close button
  it('should close overlay when close button is clicked', () => {
    // Arrange
    const fixture = createComponent(mockDetailWithWorkerRun, false, null, mobileMqFactory);
    const el = fixture.nativeElement as HTMLElement;
    const btn = el.querySelector('.issue-detail__view-logs-btn') as HTMLButtonElement;
    btn.click();
    fixture.detectChanges();

    // Act
    const closeBtn = el.querySelector('.issue-detail__overlay-close') as HTMLButtonElement;
    expect(closeBtn).toBeTruthy();
    expect(closeBtn?.getAttribute('aria-label')).toBe('Close worker logs');
    closeBtn.click();
    fixture.detectChanges();

    // Assert
    const overlay = el.querySelector('.issue-detail__overlay');
    expect(overlay).toBeFalsy();
  });

  // Cycle 15: terminal icon is present in "View Logs" button
  it('should include an SVG icon inside the "View Logs" button', () => {
    // Arrange / Act
    const fixture = createComponent(mockDetailWithWorkerRun);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const btn = el.querySelector('.issue-detail__view-logs-btn') as HTMLButtonElement;
    const icon = btn?.querySelector('svg');
    expect(icon).toBeTruthy();
    expect(icon?.getAttribute('aria-hidden')).toBe('true');
  });

  // Cycle 16: ineligible state — violations list
  it('should render eligibility violations when state is ineligible and violations are present', () => {
    // Arrange
    const ineligibleDetail: IssueDetail = {
      ...mockDetail,
      state: 'ineligible',
      stateDetails: {
        ...mockDetail.stateDetails,
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
    // Arrange / Act
    const fixture = createComponent(mockDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const violations = el.querySelector('.issue-detail__violations');
    expect(violations).toBeFalsy();
  });

  it('should not render violations section when violations list is empty', () => {
    // Arrange
    const ineligibleNoViolations: IssueDetail = {
      ...mockDetail,
      state: 'ineligible',
      stateDetails: { ...mockDetail.stateDetails, violations: [] },
    };

    // Act
    const fixture = createComponent(ineligibleNoViolations);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const violations = el.querySelector('.issue-detail__violations');
    expect(violations).toBeFalsy();
  });

  // Cycle 17: ineligible state — retry button
  it('should render a retry button when state is ineligible', () => {
    // Arrange
    const ineligibleDetail: IssueDetail = {
      ...mockDetail,
      state: 'ineligible',
      stateDetails: { ...mockDetail.stateDetails, violations: null },
    };

    // Act
    const fixture = createComponent(ineligibleDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const btn = el.querySelector('.issue-detail__retry-eligibility-btn') as HTMLButtonElement;
    expect(btn).toBeTruthy();
    expect(btn?.textContent?.trim()).toBe('Retry');
  });

  it('should not render retry eligibility button when state is not ineligible', () => {
    // Arrange / Act
    const fixture = createComponent(mockDetail);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const btn = el.querySelector('.issue-detail__retry-eligibility-btn');
    expect(btn).toBeFalsy();
  });

  it('should call retryEligibility on the service when retry button is clicked', () => {
    // Arrange
    const ineligibleDetail: IssueDetail = {
      ...mockDetail,
      state: 'ineligible',
      stateDetails: { ...mockDetail.stateDetails, violations: null },
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
});

import { TestBed } from '@angular/core/testing';
import { WorkerLogPanelComponent } from './worker-log-panel';
import { BranchCreatedContent, MilestoneContent, WorkerReportSummary } from '../worker-report.model';

const mockBranchCreatedContent: BranchCreatedContent = {
  type: 'branch-created',
  branchName: 'foundry/issue-42/main',
  summary: 'Created branch and pushed initial commit',
};

const mockBranchCreatedReport: WorkerReportSummary = {
  id: 'report-10',
  workerRunId: 'run-1',
  sequenceNumber: 10,
  reportType: 'branch-created',
  content: JSON.stringify(mockBranchCreatedContent),
  ingestedAt: '2026-06-01T14:35:00Z',
};

const mockMilestoneContent: MilestoneContent = {
  type: 'milestone',
  summary: 'Tests passing, ready to commit',
};

const mockMilestoneReport: WorkerReportSummary = {
  id: 'report-11',
  workerRunId: 'run-1',
  sequenceNumber: 11,
  reportType: 'milestone',
  content: JSON.stringify(mockMilestoneContent),
  ingestedAt: '2026-06-01T14:36:00Z',
};

const mockProgressReport: WorkerReportSummary = {
  id: 'report-1',
  workerRunId: 'run-1',
  sequenceNumber: 1,
  reportType: 'progress',
  content: 'Running tests...',
  ingestedAt: '2026-06-01T14:30:00Z',
};

const mockErrorReport: WorkerReportSummary = {
  id: 'report-2',
  workerRunId: 'run-1',
  sequenceNumber: 2,
  reportType: 'error',
  content: 'Build failed: compilation error',
  ingestedAt: '2026-06-01T14:31:00Z',
};

const mockFinalContent = JSON.stringify({
  type: 'final',
  status: 'success',
  summary: 'All tests passed',
  prUrl: 'https://github.com/owner/repo/pull/42',
  branchName: 'foundry/issue-10',
  metrics: { testsRun: 10, testsPassed: 10 },
});

const mockFinalReport: WorkerReportSummary = {
  id: 'report-3',
  workerRunId: 'run-1',
  sequenceNumber: 3,
  reportType: 'final',
  content: mockFinalContent,
  ingestedAt: '2026-06-01T14:32:00Z',
};

function setup(overrides: {
  reports?: WorkerReportSummary[];
  loading?: boolean;
  error?: string | null;
  isLive?: boolean;
  hideHeader?: boolean;
  issueUrl?: string | null;
  containerOutput?: string | null;
} = {}) {
  const retryEmitted: boolean[] = [];

  TestBed.configureTestingModule({
    imports: [WorkerLogPanelComponent],
  });

  const fixture = TestBed.createComponent(WorkerLogPanelComponent);
  fixture.componentRef.setInput('reports', overrides.reports ?? []);
  fixture.componentRef.setInput('loading', overrides.loading ?? false);
  fixture.componentRef.setInput('error', overrides.error ?? null);
  fixture.componentRef.setInput('isLive', overrides.isLive ?? false);
  fixture.componentRef.setInput('hideHeader', overrides.hideHeader ?? false);
  if (overrides.issueUrl !== undefined) {
    fixture.componentRef.setInput('issueUrl', overrides.issueUrl);
  }
  if (overrides.containerOutput !== undefined) {
    fixture.componentRef.setInput('containerOutput', overrides.containerOutput);
  }
  fixture.componentInstance.retry.subscribe(() => retryEmitted.push(true));

  return { fixture, retryEmitted };
}

describe('WorkerLogPanelComponent', () => {
  // Cycle 1: tracer bullet — component creates with role="log"
  it('should create and render the panel container with role="log"', () => {
    // Arrange / Act
    const { fixture } = setup();
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const panel = el.querySelector('[role="log"]');
    expect(panel).toBeTruthy();
  });

  // Cycle 2: loading state shows shimmer bars
  it('should show shimmer bars when loading is true', () => {
    // Arrange
    const { fixture } = setup({ loading: true });

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const shimmer = el.querySelector('.worker-log-panel__shimmer');
    expect(shimmer).toBeTruthy();
  });

  it('should hide shimmer bars when loading is false', () => {
    // Arrange
    const { fixture } = setup({ loading: false });

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const shimmer = el.querySelector('.worker-log-panel__shimmer');
    expect(shimmer).toBeFalsy();
  });

  // Cycle 3: empty state
  it('should show "No reports yet" when reports is empty and not loading', () => {
    // Arrange
    const { fixture } = setup({ reports: [], loading: false });

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const emptyState = el.querySelector('.worker-log-panel__empty');
    expect(emptyState?.textContent?.trim()).toBe('No reports yet');
  });

  it('should not show empty state when reports exist', () => {
    // Arrange
    const { fixture } = setup({ reports: [mockProgressReport] });

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const emptyState = el.querySelector('.worker-log-panel__empty');
    expect(emptyState).toBeFalsy();
  });

  // Cycle 4: error state
  it('should show error message when error is set', () => {
    // Arrange
    const { fixture } = setup({ error: 'Failed to load logs' });

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const errorEl = el.querySelector('.worker-log-panel__error-state');
    expect(errorEl?.textContent).toContain('Failed to load logs');
  });

  it('should show Retry button when error is set', () => {
    // Arrange
    const { fixture } = setup({ error: 'Network error' });

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const retryBtn = el.querySelector('.worker-log-panel__retry');
    expect(retryBtn).toBeTruthy();
    expect(retryBtn?.textContent?.trim()).toBe('Retry');
  });

  it('should emit retry event when Retry button is clicked', () => {
    // Arrange
    const { fixture, retryEmitted } = setup({ error: 'Network error' });
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const retryBtn = el.querySelector('.worker-log-panel__retry') as HTMLElement;
    retryBtn.click();

    // Assert
    expect(retryEmitted.length).toBe(1);
  });

  it('should not show error when error is null', () => {
    // Arrange
    const { fixture } = setup({ error: null });

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const errorEl = el.querySelector('.worker-log-panel__error-state');
    expect(errorEl).toBeFalsy();
  });

  // Cycle 5: progress report rendering
  it('should render progress report entry', () => {
    // Arrange
    const { fixture } = setup({ reports: [mockProgressReport] });

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const entries = el.querySelectorAll('.worker-log-panel__entry');
    expect(entries.length).toBe(1);
  });

  it('should render progress report timestamp in [HH:mm:ss] format', () => {
    // Arrange
    const { fixture } = setup({ reports: [mockProgressReport] });

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const timestamp = el.querySelector('.worker-log-panel__timestamp');
    expect(timestamp?.textContent?.trim()).toMatch(/\[\d{2}:\d{2}:\d{2}\]/);
  });

  it('should render progress report content', () => {
    // Arrange
    const { fixture } = setup({ reports: [mockProgressReport] });

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const content = el.querySelector('.worker-log-panel__content');
    expect(content?.textContent?.trim()).toContain('Running tests...');
  });

  // Cycle 6: error report rendering
  it('should apply error entry class to error reports', () => {
    // Arrange
    const { fixture } = setup({ reports: [mockErrorReport] });

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const errorEntry = el.querySelector('.worker-log-panel__entry--error');
    expect(errorEntry).toBeTruthy();
  });

  it('should not have role="alert" on individual error entries to avoid screen-reader spam during live streaming', () => {
    // Arrange
    const { fixture } = setup({ reports: [mockErrorReport] });

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const entry = el.querySelector('.worker-log-panel__entry--error') as HTMLElement;
    expect(entry).toBeTruthy();
    expect(entry?.getAttribute('role')).toBeNull();
  });

  // Cycle 7: final report rendering
  it('should render final report as a card', () => {
    // Arrange
    const { fixture } = setup({ reports: [mockFinalReport] });

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const card = el.querySelector('.worker-log-panel__final-card');
    expect(card).toBeTruthy();
  });

  it('should render final report summary text', () => {
    // Arrange
    const { fixture } = setup({ reports: [mockFinalReport] });

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const summary = el.querySelector('.worker-log-panel__final-summary');
    expect(summary?.textContent?.trim()).toBe('All tests passed');
  });

  it('should render final report branch name in mono font element', () => {
    // Arrange
    const { fixture } = setup({ reports: [mockFinalReport] });

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const branch = el.querySelector('.worker-log-panel__final-branch');
    expect(branch?.textContent?.trim()).toContain('foundry/issue-10');
  });

  it('should render final report PR URL as a link', () => {
    // Arrange
    const { fixture } = setup({ reports: [mockFinalReport] });

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const prLink = el.querySelector('.worker-log-panel__pr-link') as HTMLAnchorElement;
    expect(prLink).toBeTruthy();
    expect(prLink?.getAttribute('href')).toBe('https://github.com/owner/repo/pull/42');
    expect(prLink?.getAttribute('target')).toBe('_blank');
  });

  it('should render final report test metrics', () => {
    // Arrange
    const { fixture } = setup({ reports: [mockFinalReport] });

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const metrics = el.querySelector('.worker-log-panel__final-metrics');
    expect(metrics?.textContent?.trim()).toContain('10/10');
  });

  it('should render final report status badge', () => {
    // Arrange
    const { fixture } = setup({ reports: [mockFinalReport] });

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const badge = el.querySelector('.worker-log-panel__final-status');
    expect(badge).toBeTruthy();
    expect(badge?.textContent?.toLowerCase()).toContain('success');
  });

  // Cycle 8: live indicator
  it('should show live indicator when isLive is true', () => {
    // Arrange
    const { fixture } = setup({ isLive: true });

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const liveIndicator = el.querySelector('.worker-log-panel__live-indicator');
    expect(liveIndicator).toBeTruthy();
  });

  it('should hide live indicator when isLive is false', () => {
    // Arrange
    const { fixture } = setup({ isLive: false });

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const liveIndicator = el.querySelector('.worker-log-panel__live-indicator');
    expect(liveIndicator).toBeFalsy();
  });

  it('should show LIVE text in the live indicator', () => {
    // Arrange
    const { fixture } = setup({ isLive: true });

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const liveLabel = el.querySelector('.worker-log-panel__live-label');
    expect(liveLabel?.textContent?.trim()).toBe('LIVE');
  });

  // Cycle 9: aria attributes
  it('should set aria-busy="true" on panel when loading', () => {
    // Arrange
    const { fixture } = setup({ loading: true });

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const panel = el.querySelector('[role="log"]');
    expect(panel?.getAttribute('aria-busy')).toBe('true');
  });

  it('should render the panel body with role="log" which carries implicit aria-live="polite" semantics', () => {
    // Arrange
    const { fixture } = setup({ isLive: true });

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const panel = el.querySelector('[role="log"]');
    expect(panel).toBeTruthy();
  });

  // Cycle 10: final report with null prUrl doesn't show link
  it('should not render PR link when prUrl is null', () => {
    // Arrange
    const noUrlFinalReport: WorkerReportSummary = {
      ...mockFinalReport,
      content: JSON.stringify({
        type: 'final',
        status: 'failed',
        summary: 'Build failed',
        prUrl: null,
        branchName: null,
        metrics: null,
      }),
    };
    const { fixture } = setup({ reports: [noUrlFinalReport] });

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const prLink = el.querySelector('.worker-log-panel__pr-link');
    expect(prLink).toBeFalsy();
  });

  it('should not render metrics when metrics is null', () => {
    // Arrange
    const noMetricsFinalReport: WorkerReportSummary = {
      ...mockFinalReport,
      content: JSON.stringify({
        type: 'final',
        status: 'failed',
        summary: 'Build failed',
        prUrl: null,
        branchName: null,
        metrics: null,
      }),
    };
    const { fixture } = setup({ reports: [noMetricsFinalReport] });

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const metrics = el.querySelector('.worker-log-panel__final-metrics');
    expect(metrics).toBeFalsy();
  });

  // Cycle 12: branch-created report rendering
  it('should render branch-created report as a card with the branch name visible', () => {
    // Arrange
    const { fixture } = setup({ reports: [mockBranchCreatedReport] });

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const card = el.querySelector('.worker-log-panel__branch-created-card');
    expect(card).toBeTruthy();
    const branchName = el.querySelector('.worker-log-panel__branch-name');
    expect(branchName?.textContent?.trim()).toContain('foundry/issue-42/main');
  });

  it('should render branch-created report with a View branch link when issueUrl is a GitHub URL', () => {
    // Arrange
    const { fixture } = setup({
      reports: [mockBranchCreatedReport],
      issueUrl: 'https://github.com/owner/repo/issues/42',
    });

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const link = el.querySelector('.worker-log-panel__branch-link') as HTMLAnchorElement;
    expect(link).toBeTruthy();
    expect(link?.getAttribute('href')).toBe('https://github.com/owner/repo/tree/foundry/issue-42/main');
    expect(link?.getAttribute('target')).toBe('_blank');
  });

  it('should not render branch link when issueUrl is null', () => {
    // Arrange
    const { fixture } = setup({
      reports: [mockBranchCreatedReport],
      issueUrl: null,
    });

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const link = el.querySelector('.worker-log-panel__branch-link');
    expect(link).toBeFalsy();
    const branchName = el.querySelector('.worker-log-panel__branch-name');
    expect(branchName?.textContent?.trim()).toContain('foundry/issue-42/main');
  });

  // Cycle 13: milestone report rendering
  it('should render milestone report entry with its summary text', () => {
    // Arrange
    const { fixture } = setup({ reports: [mockMilestoneReport] });

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const entry = el.querySelector('.worker-log-panel__entry--milestone');
    expect(entry).toBeTruthy();
    const content = el.querySelector('.worker-log-panel__content');
    expect(content?.textContent?.trim()).toContain('Tests passing, ready to commit');
  });

  it('should render a visually hidden "Milestone:" label in milestone entries for screen readers', () => {
    // Arrange
    const { fixture } = setup({ reports: [mockMilestoneReport] });

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const entry = el.querySelector('.worker-log-panel__entry--milestone');
    expect(entry).toBeTruthy();
    const srLabel = entry?.querySelector('.sr-only');
    expect(srLabel?.textContent?.trim()).toBe('Milestone:');
  });

  it('should return null from buildBranchUrl when URL does not contain /issues/ segment', () => {
    // Arrange
    const { fixture } = setup({
      reports: [mockBranchCreatedReport],
      issueUrl: 'https://example.com/some-other-path/42',
    });

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const link = el.querySelector('.worker-log-panel__branch-link');
    expect(link).toBeFalsy();
  });

  // Cycle 11: hideHeader input
  it('should show panel header by default', () => {
    // Arrange
    const { fixture } = setup();

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const header = el.querySelector('.worker-log-panel__header');
    expect(header).toBeTruthy();
  });

  it('should hide panel header when hideHeader input is true', () => {
    // Arrange
    const { fixture } = setup({ hideHeader: true });

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const header = el.querySelector('.worker-log-panel__header');
    expect(header).toBeFalsy();
  });

  // Container output section — Cycle 14
  it('should render container output section when containerOutput is non-null', () => {
    // Arrange
    const { fixture } = setup({ containerOutput: 'Error: container crashed' });

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const section = el.querySelector('.worker-log-panel__container-output');
    expect(section).toBeTruthy();
  });

  // Cycle 15: no section when containerOutput is null
  it('should not render container output section when containerOutput is null', () => {
    // Arrange
    const { fixture } = setup({ containerOutput: null });

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const section = el.querySelector('.worker-log-panel__container-output');
    expect(section).toBeFalsy();
  });

  // Cycle 16: auto-expand when reports empty — pre stays in DOM, hidden attribute controls visibility
  it('should auto-expand container output when reports are empty', () => {
    // Arrange
    const { fixture } = setup({ reports: [], containerOutput: 'crash log' });

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const pre = el.querySelector('.worker-log-panel__container-output-pre') as HTMLElement;
    expect(pre).toBeTruthy();
    expect(pre.hidden).toBe(false);
  });

  // Cycle 17: collapsed when reports exist alongside container output
  it('should collapse container output by default when reports also exist', () => {
    // Arrange
    const { fixture } = setup({ reports: [mockProgressReport], containerOutput: 'crash log' });

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const pre = el.querySelector('.worker-log-panel__container-output-pre') as HTMLElement;
    expect(pre).toBeTruthy();
    expect(pre.hidden).toBe(true);
  });

  // Cycle 18: toggle button expands the section
  it('should expand container output when toggle button is clicked while collapsed', () => {
    // Arrange
    const { fixture } = setup({ reports: [mockProgressReport], containerOutput: 'crash log' });
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const toggle = el.querySelector('.worker-log-panel__container-output-toggle') as HTMLButtonElement;
    toggle.click();
    fixture.detectChanges();

    // Assert
    const pre = el.querySelector('.worker-log-panel__container-output-pre') as HTMLElement;
    expect(pre).toBeTruthy();
    expect(pre.hidden).toBe(false);
  });

  // Cycle 19: explicit toggle collapses auto-expanded section
  it('should collapse auto-expanded container output when toggle is clicked', () => {
    // Arrange — no reports, so auto-expands
    const { fixture } = setup({ reports: [], containerOutput: 'crash log' });
    fixture.detectChanges();

    // Act — click to collapse
    const el = fixture.nativeElement as HTMLElement;
    const toggle = el.querySelector('.worker-log-panel__container-output-toggle') as HTMLButtonElement;
    toggle.click();
    fixture.detectChanges();

    // Assert
    const pre = el.querySelector('.worker-log-panel__container-output-pre') as HTMLElement;
    expect(pre).toBeTruthy();
    expect(pre.hidden).toBe(true);
  });

  // Finding 1: unique per-instance panelId — two fixtures from the same TestBed config
  it('should generate unique panelId values for separate component instances', () => {
    // Arrange — configure once, create two fixtures
    TestBed.configureTestingModule({ imports: [WorkerLogPanelComponent] });
    const fixture1 = TestBed.createComponent(WorkerLogPanelComponent);
    fixture1.componentRef.setInput('reports', []);
    fixture1.componentRef.setInput('loading', false);
    fixture1.componentRef.setInput('error', null);
    fixture1.componentRef.setInput('isLive', false);
    const fixture2 = TestBed.createComponent(WorkerLogPanelComponent);
    fixture2.componentRef.setInput('reports', []);
    fixture2.componentRef.setInput('loading', false);
    fixture2.componentRef.setInput('error', null);
    fixture2.componentRef.setInput('isLive', false);

    // Act
    fixture1.detectChanges();
    fixture2.detectChanges();

    // Assert — access via index signature since panelId is protected
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const id1 = (fixture1.componentInstance as any).panelId as string;
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const id2 = (fixture2.componentInstance as any).panelId as string;
    expect(id1).not.toEqual(id2);
  });

  // Finding 2: aria-controls always points to present element
  it('should keep pre element in DOM when collapsed so aria-controls always references a valid element', () => {
    // Arrange
    const { fixture } = setup({ reports: [mockProgressReport], containerOutput: 'crash log' });
    fixture.detectChanges();

    // Assert — pre must exist regardless of collapsed state
    const el = fixture.nativeElement as HTMLElement;
    const pre = el.querySelector('.worker-log-panel__container-output-pre');
    expect(pre).toBeTruthy();
  });

  // Finding 5: initial expanded state is set once and does not flip when reports arrive
  it('should not re-collapse expanded container output when reports arrive after initial set', () => {
    // Arrange — start with containerOutput but no reports
    const { fixture } = setup({ reports: [], containerOutput: 'crash log' });
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const preInitial = el.querySelector('.worker-log-panel__container-output-pre') as HTMLElement;
    expect(preInitial.hidden).toBe(false);

    // Act — reports arrive (simulate by setting input)
    fixture.componentRef.setInput('reports', [mockProgressReport]);
    fixture.detectChanges();

    // Assert — still expanded, user did not interact
    const pre = el.querySelector('.worker-log-panel__container-output-pre') as HTMLElement;
    expect(pre.hidden).toBe(false);
  });
});

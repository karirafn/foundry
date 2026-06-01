import { TestBed } from '@angular/core/testing';
import { WorkerLogPanelComponent } from './worker-log-panel';
import { WorkerReportSummary } from '../worker-report.model';

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
});

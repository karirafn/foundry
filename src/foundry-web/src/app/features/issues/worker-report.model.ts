export type ReportType = 'progress' | 'final' | 'error' | 'branch-created' | 'milestone';

export interface ReportMetrics {
  readonly testsRun: number;
  readonly testsPassed: number;
}

export interface FinalReportContent {
  readonly type: string;
  readonly status: string;
  readonly summary: string;
  readonly prUrl: string | null;
  readonly branchName: string | null;
  readonly metrics: ReportMetrics | null;
}

export interface BranchCreatedContent {
  readonly type: 'branch-created';
  readonly branchName: string;
  readonly summary: string;
}

export interface MilestoneContent {
  readonly type: 'milestone';
  readonly summary: string;
  readonly branchName?: string;
}

export interface WorkerReportSummary {
  readonly id: string;
  readonly workerRunId: string;
  readonly sequenceNumber: number;
  readonly reportType: ReportType;
  readonly content: string;
  readonly ingestedAt: string;
}

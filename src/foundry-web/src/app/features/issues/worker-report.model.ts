export type ReportType = 'progress' | 'final' | 'error';

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

export interface WorkerReportSummary {
  readonly id: string;
  readonly workerRunId: string;
  readonly sequenceNumber: number;
  readonly reportType: ReportType;
  readonly content: string;
  readonly ingestedAt: string;
}

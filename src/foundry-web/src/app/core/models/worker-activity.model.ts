export interface WorkerActivity {
  readonly workerRunId: string;
  readonly issueId: string;
  readonly lastActivityAt: string;
  readonly commitCount: number;
}

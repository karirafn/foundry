export type IssueState =
  | 'detected'
  | 'queued'
  | 'blocked'
  | 'in_progress'
  | 'review'
  | 'unchanged'
  | 'failed'
  | 'completed'
  | 'dismissed'
  | 'revision_queued'
  | 'revision_in_progress'
  | 'revision_failed';

export interface IssueSummary {
  id: string;
  issueNumber: number;
  title: string;
  state: IssueState;
  repositorySlug: string;
  detectedAt: string;
  url: string;
}

export interface IssueStateDetails {
  workerRunId: string | null;
  branchName: string | null;
  pullRequestUrl: string | null;
  feedbackCutoffAt: string | null;
  failureReason: string | null;
  failedAt: string | null;
  completedAt: string | null;
  blockedBy: string | null;
}

export interface IssueDetail extends IssueSummary {
  body: string;
  author: string;
  labels: string[];
  stateDetails: IssueStateDetails;
}

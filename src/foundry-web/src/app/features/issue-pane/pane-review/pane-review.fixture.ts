import { RecordedReviewVerdict, WorkerRunDetail } from '../../workers/worker-run.model';

// Enum values as the API serializes them, copied from src/Foundry.WebApi/openapi/v1.json
// (ReviewVerdictOutcome, ReviewSeverity, ReviewCategory, ReviewCriterionStatus, ReviewDimensionStatus,
// ReviewTestsAssessment), which the server generates from its contracts.

export const REVIEWED_SHA = '0123456789abcdef0123456789abcdef01234567';
export const CARRIED_SHA = 'fedcba9876543210fedcba9876543210fedcba98';

export function verdict(overrides: Partial<RecordedReviewVerdict> = {}): RecordedReviewVerdict {
  return {
    summary: 'The change adds the retry endpoint.',
    findings: [],
    dimensions: [
      { category: 'Correctness', status: 'Clear', note: 'Logic holds.' },
      { category: 'Criteria', status: 'Clear', note: 'All met.' },
      { category: 'Tests', status: 'Clear', note: 'Covered.' },
      { category: 'Security', status: 'NotApplicable', note: 'No input handling.' },
      { category: 'Quality', status: 'Clear', note: '' },
      { category: 'Design', status: 'Clear', note: '' },
      { category: 'Conventions', status: 'Clear', note: '' },
    ],
    criteria: [{ id: 'AC1', status: 'Met', evidence: 'RetryReviewTests covers it.' }],
    tests: { assessment: 'Adequate', reason: 'Handler tests cover both paths.', coveringTests: ['tests/RetryReviewTests.cs'] },
    outcome: 'Approved',
    reviewedCommit: REVIEWED_SHA,
    patchId: 'abc',
    carriedFrom: null,
    ...overrides,
  };
}

export function reviewRun(overrides: Partial<WorkerRunDetail> = {}): WorkerRunDetail {
  return {
    workerRunId: 'review-run-1',
    issueId: 'abc123',
    role: 'review',
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
    hasStoredLog: false,
    cancelRequestedAt: null,
    publish: null,
    publishRetry: null,
    discardsAt: null,
    secretFindings: [],
    revertedFiles: [],
    publishBlockedAt: null,
    falsePositivesConfirmedAt: null,
    confirmedFindings: [],
    toolchain: null,
    reviewVerdict: verdict(),
    productionChangedWithoutTests: false,
    reviewCriteria: [{ id: 'AC1', text: 'Retry review queues the review again.' }],
    ...overrides,
  };
}

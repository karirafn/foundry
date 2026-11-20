import {
  RecordedReviewVerdict,
  ReviewCategory,
  ReviewFinding,
  ReviewVerdictOutcome,
  WorkerRunDetail,
} from '../../workers/worker-run.model';

const SHORT_SHA_LENGTH = 7;

// A summary longer than this clamps to 4 lines behind Show more, as the pane's run result does.
const SUMMARY_CLAMP_LINES = 4;
const SUMMARY_CLAMP_CHARS = 320;

export type ChipTone = 'success' | 'warning' | 'neutral' | 'error';

export interface ReviewChip {
  readonly label: string;
  readonly tone: ChipTone;
}

export interface ReviewCommit {
  readonly shortSha: string;
  readonly fullSha: string;
}

export interface ReviewFindingRow {
  readonly severity: ReviewChip;
  /** `path:line`, or the path alone for a finding without a line. */
  readonly location: string;
  readonly category: string;
  /** The principle id, or a convention file with its quote; null when the finding cites no rule. */
  readonly rule: string | null;
  readonly body: string;
}

export interface ReviewCriterionRow {
  readonly id: string;
  readonly status: ReviewChip;
  /** The criterion's text from the issue; null when the run did not report the criteria it was given. */
  readonly text: string | null;
  readonly evidence: string;
}

export interface ReviewDimensionRow {
  readonly label: string;
  readonly status: { readonly label: string; readonly tone: 'success' | 'warning' | 'secondary' };
  readonly note: string;
}

/** The pane's Review section: the latest review verdict, ready to render as text. */
export interface PaneReview {
  readonly outcome: ReviewChip;
  readonly reviewed: ReviewCommit;
  /** The commit a carried approval was first given on; null for a verdict a run returned. */
  readonly carriedFrom: ReviewCommit | null;
  readonly summary: string;
  readonly summaryLong: boolean;
  /** Null when the review found nothing. */
  readonly findings: { readonly title: string; readonly open: boolean; readonly items: readonly ReviewFindingRow[] } | null;
  /** Empty items when the issue had no acceptance criteria. */
  readonly criteria: { readonly title: string; readonly items: readonly ReviewCriterionRow[] };
  readonly tests: {
    readonly title: string;
    readonly reason: string;
    readonly coveringTests: readonly string[];
    /** Foundry's own check: production code changed while no test did. */
    readonly productionChangedWithoutTests: boolean;
  };
  readonly dimensions: { readonly title: string; readonly rows: readonly ReviewDimensionRow[] };
}

const OUTCOMES: Readonly<Record<ReviewVerdictOutcome, ReviewChip>> = {
  Approved: { label: 'Approved', tone: 'success' },
  ApprovedForPerson: { label: 'Approved for your review', tone: 'neutral' },
  ChangesRequested: { label: 'Changes requested', tone: 'warning' },
};

const CRITERION_STATUSES: Readonly<Record<RecordedReviewVerdict['criteria'][number]['status'], ReviewChip>> = {
  Met: { label: 'Met', tone: 'success' },
  Unmet: { label: 'Unmet', tone: 'warning' },
  NotVerifiable: { label: "Can't verify", tone: 'neutral' },
  CoveredByChecks: { label: 'Covered by checks', tone: 'success' },
};

const TESTS_ASSESSMENTS: Readonly<Record<RecordedReviewVerdict['tests']['assessment'], string>> = {
  Adequate: 'Adequate',
  Missing: 'Missing',
  NotNeeded: 'Not needed',
};

const DIMENSION_STATUSES: Readonly<Record<RecordedReviewVerdict['dimensions'][number]['status'], ReviewDimensionRow['status']>> = {
  Clear: { label: 'Clear', tone: 'success' },
  Findings: { label: 'Findings', tone: 'warning' },
  NotApplicable: { label: 'Not applicable', tone: 'secondary' },
};

const DIMENSION_ORDER: readonly ReviewCategory[] = [
  'Correctness',
  'Criteria',
  'Tests',
  'Security',
  'Quality',
  'Design',
  'Conventions',
];

function commit(sha: string): ReviewCommit {
  return { shortSha: sha.slice(0, SHORT_SHA_LENGTH), fullSha: sha };
}

function isLong(text: string): boolean {
  return text.length > SUMMARY_CLAMP_CHARS || text.split('\n').length > SUMMARY_CLAMP_LINES;
}

function ruleOf(finding: ReviewFinding): string | null {
  const rule = finding.rule;
  if (rule === null) {
    return null;
  }
  if (rule.principle) {
    return rule.principle;
  }
  if (rule.file) {
    return rule.quote ? `${rule.file}: “${rule.quote}”` : rule.file;
  }
  return null;
}

function findingRow(finding: ReviewFinding): ReviewFindingRow {
  return {
    severity: finding.severity === 'Blocking' ? { label: 'Blocking', tone: 'error' } : { label: 'Advisory', tone: 'neutral' },
    location: finding.line === null ? finding.path : `${finding.path}:${finding.line}`,
    category: finding.category,
    rule: ruleOf(finding),
    body: finding.body,
  };
}

function findingsOf(verdict: RecordedReviewVerdict): PaneReview['findings'] {
  if (verdict.findings.length === 0) {
    return null;
  }
  const blocking = verdict.findings.filter((f) => f.severity === 'Blocking');
  const advisory = verdict.findings.filter((f) => f.severity !== 'Blocking');
  return {
    title: `Findings — ${blocking.length} blocking, ${advisory.length} advisory`,
    open: verdict.outcome === 'ChangesRequested',
    items: [...blocking, ...advisory].map(findingRow),
  };
}

function criteriaOf(verdict: RecordedReviewVerdict, run: WorkerRunDetail): PaneReview['criteria'] {
  const texts = new Map((run.reviewCriteria ?? []).map((c) => [c.id, c.text]));
  const met = verdict.criteria.filter((c) => c.status === 'Met').length;
  const byChecks = verdict.criteria.filter((c) => c.status === 'CoveredByChecks').length;
  const total = verdict.criteria.length;
  return {
    title:
      byChecks === 0
        ? `Acceptance criteria — ${met} of ${total} met`
        : `Acceptance criteria — ${met + byChecks} of ${total} satisfied, ${byChecks} by checks`,
    items: verdict.criteria.map((c) => ({
      id: c.id,
      status: CRITERION_STATUSES[c.status],
      text: texts.get(c.id) ?? null,
      evidence: c.evidence,
    })),
  };
}

function dimensionsOf(verdict: RecordedReviewVerdict): PaneReview['dimensions'] {
  const rows = [...verdict.dimensions]
    .sort((a, b) => DIMENSION_ORDER.indexOf(a.category) - DIMENSION_ORDER.indexOf(b.category))
    .map((d) => ({ label: d.category, status: DIMENSION_STATUSES[d.status], note: d.note }));
  const withFindings = verdict.dimensions.filter((d) => d.status === 'Findings').length;
  return { title: `Dimensions — ${withFindings} with findings`, rows };
}

/** The Review section of a review run, or null when the run recorded no verdict. */
export function buildReview(run: WorkerRunDetail): PaneReview | null {
  const verdict = run.reviewVerdict;
  if (!verdict) {
    return null;
  }

  return {
    outcome: OUTCOMES[verdict.outcome],
    reviewed: commit(verdict.reviewedCommit),
    carriedFrom: verdict.carriedFrom ? commit(verdict.carriedFrom.reviewedCommit) : null,
    summary: verdict.summary,
    summaryLong: isLong(verdict.summary),
    findings: findingsOf(verdict),
    criteria: criteriaOf(verdict, run),
    tests: {
      title: `Tests — ${TESTS_ASSESSMENTS[verdict.tests.assessment]}`,
      reason: verdict.tests.reason,
      coveringTests: verdict.tests.coveringTests,
      productionChangedWithoutTests: run.productionChangedWithoutTests === true,
    },
    dimensions: dimensionsOf(verdict),
  };
}

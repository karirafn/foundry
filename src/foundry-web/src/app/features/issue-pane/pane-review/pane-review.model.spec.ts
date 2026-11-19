import { CARRIED_SHA, REVIEWED_SHA, reviewRun, verdict } from './pane-review.fixture';
import { buildReview } from './pane-review.model';
import { ReviewFinding } from '../../workers/worker-run.model';

function finding(overrides: Partial<ReviewFinding> = {}): ReviewFinding {
  return {
    path: 'src/a.cs',
    line: 12,
    severity: 'Advisory',
    category: 'Quality',
    rule: null,
    body: 'Rename it.',
    ...overrides,
  };
}

describe('buildReview', () => {
  it('builds nothing for a run without a verdict', () => {
    // Act / Assert
    expect(buildReview(reviewRun({ reviewVerdict: null }))).toBeNull();
    expect(buildReview(reviewRun({ reviewVerdict: undefined }))).toBeNull();
  });

  it.each([
    ['Approved', 'Approved', 'success'],
    ['ApprovedForPerson', 'Approved for your review', 'neutral'],
    ['ChangesRequested', 'Changes requested', 'warning'],
  ] as const)('labels the %s outcome as "%s" in the %s tone', (outcome, label, tone) => {
    // Act
    const review = buildReview(reviewRun({ reviewVerdict: verdict({ outcome }) }))!;

    // Assert
    expect(review.outcome).toEqual({ label, tone });
  });

  it('names the reviewed commit short with its full SHA', () => {
    // Act
    const review = buildReview(reviewRun())!;

    // Assert
    expect(review.reviewed).toEqual({ shortSha: '0123456', fullSha: REVIEWED_SHA });
    expect(review.carriedFrom).toBeNull();
  });

  it('marks a carried approval Approved with the commit it was carried from', () => {
    // Act
    const review = buildReview(
      reviewRun({ reviewVerdict: verdict({ outcome: 'Approved', carriedFrom: { runId: 'run-0', reviewedCommit: CARRIED_SHA } }) }),
    )!;

    // Assert
    expect(review.outcome).toEqual({ label: 'Approved', tone: 'success' });
    expect(review.carriedFrom).toEqual({ shortSha: 'fedcba9', fullSha: CARRIED_SHA });
  });

  it.each([
    ['short', 'One line.', false],
    ['many lines', 'a\nb\nc\nd\ne', true],
    ['very long', 'x'.repeat(321), true],
  ])('clamps a %s summary: %s', (_name, summary, long) => {
    // Act / Assert
    expect(buildReview(reviewRun({ reviewVerdict: verdict({ summary }) }))!.summaryLong).toBe(long);
  });

  it('leaves the findings out when there are none', () => {
    // Act / Assert
    expect(buildReview(reviewRun())!.findings).toBeNull();
  });

  it('lists blocking findings first and counts both kinds in the title', () => {
    // Arrange
    const findings = [
      finding({ path: 'src/advisory.cs' }),
      finding({ path: 'src/blocking.cs', severity: 'Blocking', category: 'Correctness', line: null }),
      finding({ path: 'src/advisory2.cs', rule: { principle: 'code.5', file: null, quote: null } }),
    ];

    // Act
    const review = buildReview(reviewRun({ reviewVerdict: verdict({ findings }) }))!;

    // Assert
    expect(review.findings!.title).toBe('Findings — 1 blocking, 2 advisory');
    expect(review.findings!.items.map((f) => f.location)).toEqual(['src/blocking.cs', 'src/advisory.cs:12', 'src/advisory2.cs:12']);
    expect(review.findings!.items[0]).toMatchObject({ severity: { label: 'Blocking', tone: 'error' }, category: 'Correctness', rule: null });
    expect(review.findings!.items[1].severity).toEqual({ label: 'Advisory', tone: 'neutral' });
    expect(review.findings!.items[2].rule).toBe('code.5');
  });

  it('names a convention rule by its file and quote', () => {
    // Arrange
    const findings = [finding({ rule: { principle: null, file: 'CLAUDE.md', quote: 'No var' } })];

    // Act
    const review = buildReview(reviewRun({ reviewVerdict: verdict({ findings }) }))!;

    // Assert
    expect(review.findings!.items[0].rule).toBe('CLAUDE.md: “No var”');
  });

  it.each([
    ['ChangesRequested', true],
    ['Approved', false],
    ['ApprovedForPerson', false],
  ] as const)('opens the findings for %s: %s', (outcome, open) => {
    // Act
    const review = buildReview(reviewRun({ reviewVerdict: verdict({ outcome, findings: [finding()] }) }))!;

    // Assert
    expect(review.findings!.open).toBe(open);
  });

  it('pairs each criterion answer with its supplied text and counts the met ones', () => {
    // Arrange
    const run = reviewRun({
      reviewVerdict: verdict({
        criteria: [
          { id: 'AC1', status: 'Met', evidence: 'Tested.' },
          { id: 'AC2', status: 'Unmet', evidence: 'Missing.' },
          { id: 'AC3', status: 'NotVerifiable', evidence: 'UI only.' },
        ],
      }),
      reviewCriteria: [
        { id: 'AC1', text: 'One.' },
        { id: 'AC2', text: 'Two.' },
        { id: 'AC3', text: 'Three.' },
      ],
    });

    // Act
    const criteria = buildReview(run)!.criteria;

    // Assert
    expect(criteria.title).toBe('Acceptance criteria — 1 of 3 met');
    expect(criteria.items).toEqual([
      { id: 'AC1', status: { label: 'Met', tone: 'success' }, text: 'One.', evidence: 'Tested.' },
      { id: 'AC2', status: { label: 'Unmet', tone: 'warning' }, text: 'Two.', evidence: 'Missing.' },
      { id: 'AC3', status: { label: "Can't verify", tone: 'neutral' }, text: 'Three.', evidence: 'UI only.' },
    ]);
  });

  it('shows a criterion without its supplied text when the run has none', () => {
    // Act
    const criteria = buildReview(reviewRun({ reviewCriteria: null }))!.criteria;

    // Assert
    expect(criteria.items[0].text).toBeNull();
  });

  it('has no criterion items when none were supplied', () => {
    // Act
    const criteria = buildReview(reviewRun({ reviewVerdict: verdict({ criteria: [] }), reviewCriteria: [] }))!.criteria;

    // Assert
    expect(criteria.items).toEqual([]);
  });

  it.each([
    ['Adequate', 'Tests — Adequate'],
    ['Missing', 'Tests — Missing'],
    ['NotNeeded', 'Tests — Not needed'],
  ] as const)('titles the %s tests assessment "%s"', (assessment, title) => {
    // Act
    const tests = buildReview(reviewRun({ reviewVerdict: verdict({ tests: { assessment, reason: 'Why.', coveringTests: [] } }) }))!.tests;

    // Assert
    expect(tests.title).toBe(title);
    expect(tests.reason).toBe('Why.');
  });

  it.each([
    [true, true],
    [false, false],
    [null, false],
  ])('carries Foundry’s test check %s as %s', (flag, shown) => {
    // Act / Assert
    expect(buildReview(reviewRun({ productionChangedWithoutTests: flag }))!.tests.productionChangedWithoutTests).toBe(shown);
  });

  it('lists the dimensions in their fixed order with a status word and counts those with findings', () => {
    // Arrange
    const dimensions = [
      { category: 'Conventions', status: 'Findings', note: 'Naming.' },
      { category: 'Correctness', status: 'Clear', note: 'Holds.' },
      { category: 'Security', status: 'NotApplicable', note: '' },
      { category: 'Tests', status: 'Findings', note: 'Missing.' },
    ] as const;

    // Act
    const result = buildReview(reviewRun({ reviewVerdict: verdict({ dimensions: [...dimensions] }) }))!.dimensions;

    // Assert
    expect(result.title).toBe('Dimensions — 2 with findings');
    expect(result.rows).toEqual([
      { label: 'Correctness', status: { label: 'Clear', tone: 'success' }, note: 'Holds.' },
      { label: 'Tests', status: { label: 'Findings', tone: 'warning' }, note: 'Missing.' },
      { label: 'Security', status: { label: 'Not applicable', tone: 'secondary' }, note: '' },
      { label: 'Conventions', status: { label: 'Findings', tone: 'warning' }, note: 'Naming.' },
    ]);
  });
});

import { IssueDetail, IssueStateDetails, IssueSummary, RunStats } from '../../issues/issue.model';
import { WorkerRunDetail } from '../../workers/worker-run.model';
import { PaneFact, buildFacts } from './pane-facts.model';

const stateDetails: IssueStateDetails = {
  workerRunId: null,
  branchName: null,
  feedbackCutoffAt: null,
  failureReason: null,
  failedAt: null,
  completedAt: null,
  blockedBy: null,
  violations: null,
  transientRetry: null,
};

const baseDetail: IssueDetail = {
  id: 'abc123',
  issueNumber: 42,
  title: 'Enable dark mode',
  state: 'queued',
  repositorySlug: 'owner/repo',
  detectedAt: '2026-01-01T00:00:00Z',
  url: 'https://github.com/owner/repo/issues/42',
  stage: 'implement',
  needsAction: false,
  awaitsDispatch: true,
  waitsOnBlockers: false,
  codeStep: null,
  repositoryId: 'repo-1',
  enteredStateAt: '2026-01-01T00:00:00Z',
  author: 'dev',
  labels: [],
  stateDetails: null,
  kind: { source: 'labelled', kind: 'feature', conflictingTriageKind: null },
  triage: null,
};

function issue(state: string, overrides: Partial<IssueDetail> = {}): IssueDetail {
  return { ...baseDetail, state: state as IssueDetail['state'], ...overrides };
}

function details(overrides: Partial<IssueStateDetails>): IssueStateDetails {
  return { ...stateDetails, ...overrides };
}

function runDetail(overrides: Partial<WorkerRunDetail> = {}): WorkerRunDetail {
  return {
    workerRunId: 'run-0123456789abcdef',
    issueId: 'abc123',
    role: 'implement',
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
    ...overrides,
  };
}

function summaryWith(runStats: RunStats | null): IssueSummary {
  const { author: _a, labels: _l, stateDetails: _s, kind: _k, triage: _t, ...summary } = baseDetail;
  return { ...summary, runStats };
}

const verdict = (overrides: Partial<NonNullable<IssueDetail['triage']>> = {}): NonNullable<IssueDetail['triage']> => ({
  outcome: 'ready',
  kind: 'feature',
  questions: [],
  decidedAt: '2026-11-06T14:14:03Z',
  ...overrides,
});

function fact<T extends PaneFact['type']>(facts: readonly PaneFact[], type: T): Extract<PaneFact, { type: T }> | undefined {
  return facts.find((f): f is Extract<PaneFact, { type: T }> => f.type === type);
}

function keyed(facts: readonly PaneFact[], key: string): PaneFact | undefined {
  return facts.find((f) => f.key === key);
}

describe('buildFacts', () => {
  it('lists the rows in the Facts table order and omits the empty ones', () => {
    // Arrange
    const detail = issue('failed', {
      labels: ['ui'],
      triage: verdict(),
      stateDetails: details({
        workerRunId: 'run-0123456789abcdef',
        branchName: 'feat/42-dark-mode',
        feedbackCutoffAt: '2026-02-01T10:00:00Z',
        failedAt: '2026-02-01T11:00:00Z',
        completedAt: '2026-02-01T12:00:00Z',
        blockedBy: [{ issueNumber: 12, reason: null }],
        checksFixAttempts: 1,
        rebaseConflictAttempts: 2,
        rebaseOutcome: 'rebased',
      }),
    });
    const run = runDetail({
      resultText: 'Done.',
      hasStoredLog: true,
      publish: { headSha: 'abcdef1234567890', headCommitUrl: null, pullRequestUrl: null, branchCommitCount: 2 },
      revertedFiles: [{ path: 'a.bin', reason: 'binary', sizeBytes: 10 }],
    });
    const stats: RunStats = {
      runCount: 2,
      durationMs: 60_000,
      numTurns: 5,
      totalCostUsd: 1,
      inputTokens: { total: 1, knownRunCount: 1, unknownRunCount: 0 },
      outputTokens: { total: 1, knownRunCount: 1, unknownRunCount: 0 },
    };

    // Act
    const keys = buildFacts(detail, run, summaryWith(stats)).map((f) => f.key);

    // Assert
    expect(keys).toEqual([
      'Kind',
      'Triage',
      'Branch',
      'Published',
      'Reverted files',
      'Runs',
      'Run',
      'Checks fixes',
      'Conflict fixes',
      'Rebase',
      'Blocked by',
      'Labels',
      'Author',
      'Feedback cutoff',
      'Failed at',
      'Completed',
      'Run result',
      'Log',
    ]);
  });

  it('shows only Kind and Author for an issue with nothing else to tell', () => {
    // Act
    const keys = buildFacts(issue('detected'), null, null).map((f) => f.key);

    // Assert
    expect(keys).toEqual(['Kind', 'Author']);
  });

  describe('Kind', () => {
    it.each([
      ['labelled', 'bug', 'Bug', true, 'From label'],
      ['triaged', 'feature', 'Feature', true, 'From triage'],
      ['unclassified', null, 'Unclassified', false, 'No kind label, and triage has not classified it'],
    ] as const)('reads a %s %s kind as "%s" with the note "%s"', (source, kind, label, known, note) => {
      // Act
      const k = fact(buildFacts(issue('queued', { kind: { source, kind, conflictingTriageKind: null } }), null, null), 'kind');

      // Assert
      expect(k).toMatchObject({ label, known, sourceNote: note, triageDiffers: null });
    });

    it('carries the Triage differs sentence when triage judged another kind', () => {
      // Act
      const k = fact(
        buildFacts(issue('queued', { kind: { source: 'labelled', kind: 'bug', conflictingTriageKind: 'refactor' } }), null, null),
        'kind',
      );

      // Assert
      expect(k?.triageDiffers).toBe('Triage read it as a refactor. The label decides; nothing changes.');
    });

    it('reads documentation without an article in the Triage differs sentence', () => {
      // Act
      const k = fact(
        buildFacts(issue('queued', { kind: { source: 'labelled', kind: 'bug', conflictingTriageKind: 'documentation' } }), null, null),
        'kind',
      );

      // Assert
      expect(k?.triageDiffers).toBe('Triage read it as documentation. The label decides; nothing changes.');
    });
  });

  describe('Triage', () => {
    it('shows a Ready verdict with its kind and decision time', () => {
      // Act
      const t = fact(buildFacts(issue('queued', { triage: verdict() }), null, null), 'triage');

      // Assert
      expect(t?.verdict).toEqual({
        outcomeLabel: 'Ready',
        tone: 'success',
        kindLabel: 'Feature',
        decidedAt: '2026-11-06T14:14:03Z',
        note: null,
      });
    });

    it.each([
      [1, '1 question asked'],
      [3, '3 questions asked'],
    ])('shows a Needs input verdict with %i question(s) asked', (count, phrase) => {
      // Act
      const t = fact(
        buildFacts(
          issue('in_progress', { triage: verdict({ outcome: 'needs_input', questions: Array.from({ length: count }, (_, i) => `Q${i}`) }) }),
          null,
          null,
        ),
        'triage',
      );

      // Assert
      expect(t?.verdict).toMatchObject({ outcomeLabel: 'Needs input', tone: 'warning', note: phrase });
    });

    it('explains a Blocked verdict as depending on open issues, in a neutral tone', () => {
      // Act
      const t = fact(buildFacts(issue('queued', { triage: verdict({ outcome: 'blocked' }) }), null, null), 'triage');

      // Assert
      expect(t?.verdict).toMatchObject({ outcomeLabel: 'Blocked', tone: 'neutral', note: 'Triage found open issues this depends on.' });
    });

    it('falls back to the raw outcome for an outcome it has no label for', () => {
      // Act
      const t = fact(buildFacts(issue('queued', { triage: verdict({ outcome: 'future_outcome' as 'ready' }) }), null, null), 'triage');

      // Assert
      expect(t?.verdict?.outcomeLabel).toBe('future_outcome');
    });

    it.each(['queued', 'blocked'])('says a %s issue without a verdict was not triaged', (state) => {
      // Act
      const t = fact(buildFacts(issue(state), null, null), 'triage');

      // Assert
      expect(t).toBeDefined();
      expect(t?.verdict).toBeNull();
    });

    it.each(['detected', 'triage_queued', 'triage_blocked', 'triage_in_progress', 'triage_failed'])(
      'hides a stale verdict while the issue is %s',
      (state) => {
        // Act
        const facts = buildFacts(issue(state, { triage: verdict() }), null, null);

        // Assert
        expect(fact(facts, 'triage')).toBeUndefined();
      },
    );

    it('hides a stale verdict while triage waits for answers', () => {
      // Arrange
      const detail = issue('awaiting_input', {
        triage: verdict(),
        stateDetails: details({
          awaitingInput: {
            questions: [],
            postStatus: 'pending',
            postedAt: null,
            commentUrl: null,
            postFailureReason: null,
            nextPostAttemptAt: null,
            resumeTarget: 'retriage',
          },
        }),
      });

      // Act
      const facts = buildFacts(detail, null, null);

      // Assert
      expect(fact(facts, 'triage')).toBeUndefined();
    });

    it.each(['completed', 'review', 'failed'])('shows no Triage row for %s with no verdict', (state) => {
      // Act
      const facts = buildFacts(issue(state), null, null);

      // Assert
      expect(fact(facts, 'triage')).toBeUndefined();
    });
  });

  it('shows the branch in mono, and no Branch row without one', () => {
    // Act
    const withBranch = keyed(buildFacts(issue('review', { stateDetails: details({ branchName: 'feat/x' }) }), null, null), 'Branch');
    const without = keyed(buildFacts(issue('review', { stateDetails: details({}) }), null, null), 'Branch');

    // Assert
    expect(withBranch).toMatchObject({ type: 'text', value: 'feat/x', mono: true });
    expect(without).toBeUndefined();
  });

  describe('Published', () => {
    const publish = { headSha: '0123456789abcdef', headCommitUrl: 'https://github.com/o/r/commit/0123456', pullRequestUrl: null, branchCommitCount: 4 };

    it('shows the 7-character head SHA, its link and full SHA, and the commit count', () => {
      // Act
      const p = fact(buildFacts(issue('review'), runDetail({ publish }), null), 'published');

      // Assert
      expect(p).toMatchObject({
        shortSha: '0123456',
        fullSha: '0123456789abcdef',
        href: 'https://github.com/o/r/commit/0123456',
        commits: '4 commits',
      });
    });

    it.each([
      [0, 'No commits'],
      [1, '1 commit'],
    ])('reads %i commits as "%s"', (count, expected) => {
      // Act
      const p = fact(buildFacts(issue('review'), runDetail({ publish: { ...publish, branchCommitCount: count } }), null), 'published');

      // Assert
      expect(p?.commits).toBe(expected);
    });

    it('shows no Published row for a run that published nothing', () => {
      // Act
      const facts = buildFacts(issue('review'), runDetail(), null);

      // Assert
      expect(fact(facts, 'published')).toBeUndefined();
    });
  });

  it('lists the reverted files, and no row without any', () => {
    // Arrange
    const files = [{ path: 'assets/demo.mp4', reason: 'oversized' as const, sizeBytes: 50_541_363 }];

    // Act
    const withFiles = fact(buildFacts(issue('review'), runDetail({ revertedFiles: files }), null), 'reverted_files');
    const without = fact(buildFacts(issue('review'), runDetail(), null), 'reverted_files');

    // Assert
    expect(withFiles?.files).toEqual(files);
    expect(without).toBeUndefined();
  });

  describe('Runs', () => {
    const stats = (overrides: Partial<RunStats> = {}): RunStats => ({
      runCount: 3,
      durationMs: 3_840_000,
      numTurns: 42,
      totalCostUsd: 1.5,
      inputTokens: { total: 12_345, knownRunCount: 3, unknownRunCount: 0 },
      outputTokens: { total: 678, knownRunCount: 2, unknownRunCount: 1 },
      ...overrides,
    });

    it('summarises the run stats with the run-stats helpers', () => {
      // Act
      const r = keyed(buildFacts(issue('review'), null, summaryWith(stats())), 'Runs');

      // Assert
      expect(r).toMatchObject({ type: 'text', value: '3 runs · 1h 4m · 42 turns · $1.50 · 12,345 in / ≥ 678 out' });
    });

    it('reads one run in the singular and leaves out unknown stats', () => {
      // Act
      const r = keyed(
        buildFacts(issue('review'), null, summaryWith(stats({ runCount: 1, durationMs: null, numTurns: null, totalCostUsd: null }))),
        'Runs',
      );

      // Assert
      expect(r).toMatchObject({ value: '1 run · 12,345 in / ≥ 678 out' });
    });

    it.each([
      ['no stats', null],
      ['no runs', stats({ runCount: 0 })],
    ])('shows no Runs row with %s', (_, runStats) => {
      // Act
      const r = keyed(buildFacts(issue('review'), null, summaryWith(runStats)), 'Runs');

      // Assert
      expect(r).toBeUndefined();
    });
  });

  describe('Run', () => {
    it('shows the run role and its 8-character ID, with the full ID', () => {
      // Act
      const r = fact(buildFacts(issue('failed'), runDetail({ role: 'triage' }), null), 'run');

      // Assert
      expect(r).toMatchObject({ role: 'triage', shortId: 'run-0123', id: 'run-0123456789abcdef' });
    });

    it('shows the run ID without a role while the run has not loaded', () => {
      // Act
      const r = fact(buildFacts(issue('failed', { stateDetails: details({ workerRunId: 'feedface-0000' }) }), null, null), 'run');

      // Assert
      expect(r).toMatchObject({ role: null, shortId: 'feedface', id: 'feedface-0000' });
    });

    it('shows no Run row without a run', () => {
      // Act
      const facts = buildFacts(issue('queued'), null, null);

      // Assert
      expect(fact(facts, 'run')).toBeUndefined();
    });
  });

  it.each([
    ['Checks fixes', { checksFixAttempts: 1 }, '1 of 2'],
    ['Conflict fixes', { rebaseConflictAttempts: 2 }, '2 of 2'],
  ])('shows %s against the cap', (key, overrides, expected) => {
    // Act
    const row = keyed(buildFacts(issue('review', { stateDetails: details(overrides) }), null, null), key);

    // Assert
    expect(row).toMatchObject({ type: 'text', value: expected });
  });

  it.each([null, 0])('shows no fix-attempt rows when the count is %s', (count) => {
    // Act
    const facts = buildFacts(
      issue('review', { stateDetails: details({ checksFixAttempts: count, rebaseConflictAttempts: count }) }),
      null,
      null,
    );

    // Assert
    expect(keyed(facts, 'Checks fixes')).toBeUndefined();
    expect(keyed(facts, 'Conflict fixes')).toBeUndefined();
  });

  it.each([
    ['up_to_date', null, 'Up to date'],
    ['skipped', 'timeout', 'Skipped (timeout)'],
    ['conflict', null, 'Conflict'],
  ])('shows a Rebase row for %s (reason %s) as "%s"', (outcome, reason, expected) => {
    // Act
    const row = keyed(
      buildFacts(issue('review', { stateDetails: details({ rebaseOutcome: outcome, rebaseSkippedReason: reason }) }), null, null),
      'Rebase',
    );

    // Assert
    expect(row).toMatchObject({ type: 'text', value: expected });
  });

  it('lists each blocker with its number and triage reason', () => {
    // Act
    const b = fact(
      buildFacts(
        issue('blocked', { stateDetails: details({ blockedBy: [{ issueNumber: 12, reason: null }, { issueNumber: 14, reason: 'waits on the schema' }] }) }),
        null,
        null,
      ),
      'blockers',
    );

    // Assert
    expect(b?.blockers).toEqual([
      { number: '#12', reason: null },
      { number: '#14', reason: 'Triage: waits on the schema' },
    ]);
  });

  it('shows no Blocked by row for an empty blocker list', () => {
    // Act
    const facts = buildFacts(issue('blocked', { stateDetails: details({ blockedBy: [] }) }), null, null);

    // Assert
    expect(fact(facts, 'blockers')).toBeUndefined();
  });

  it('shows each label, and no Labels row without labels', () => {
    // Act
    const withLabels = fact(buildFacts(issue('queued', { labels: ['enhancement', 'ui'] }), null, null), 'labels');
    const without = fact(buildFacts(issue('queued'), null, null), 'labels');

    // Assert
    expect(withLabels?.labels).toEqual(['enhancement', 'ui']);
    expect(without).toBeUndefined();
  });

  it('shows the author when the issue has no state details', () => {
    // Act
    const row = keyed(buildFacts(issue('queued', { stateDetails: null }), null, null), 'Author');

    // Assert
    expect(row).toMatchObject({ type: 'text', value: 'dev', mono: false });
  });

  it.each([
    ['Feedback cutoff', { feedbackCutoffAt: '2026-02-01T10:00:00Z' }],
    ['Failed at', { failedAt: '2026-02-01T11:00:00Z' }],
    ['Completed', { completedAt: '2026-02-01T12:00:00Z' }],
  ])('shows %s as a date', (key, overrides) => {
    // Act
    const row = keyed(buildFacts(issue('completed', { stateDetails: details(overrides) }), null, null), key);

    // Assert
    expect(row).toEqual({ type: 'date', key, iso: Object.values(overrides)[0] });
  });

  describe('Run result', () => {
    it('shows a short result unclamped', () => {
      // Act
      const r = fact(buildFacts(issue('failed'), runDetail({ resultText: 'Partial work done.' }), null), 'result');

      // Assert
      expect(r).toMatchObject({ text: 'Partial work done.', long: false });
    });

    it.each([
      ['more than 4 lines', 'a\nb\nc\nd\ne'],
      ['a long paragraph', 'x'.repeat(400)],
    ])('marks a result of %s as long, so it clamps', (_, text) => {
      // Act
      const r = fact(buildFacts(issue('failed'), runDetail({ resultText: text }), null), 'result');

      // Assert
      expect(r?.long).toBe(true);
    });
  });

  describe('Log', () => {
    it('shows the stored log of a finished run', () => {
      // Act
      const l = fact(buildFacts(issue('review'), runDetail({ workerRunId: 'run-done', hasStoredLog: true }), null), 'log');

      // Assert
      expect(l).toEqual({ type: 'log', key: 'Log', workerRunId: 'run-done' });
    });

    it('shows no Log row for a run without a stored log', () => {
      // Act
      const facts = buildFacts(issue('in_progress'), runDetail({ state: 'running' }), null);

      // Assert
      expect(fact(facts, 'log')).toBeUndefined();
    });

    it.each(['publishing', 'publish_failed', 'publish_blocked'] as const)('shows no Log row for a run in %s', (state) => {
      // Act
      const facts = buildFacts(issue('publish_failed'), runDetail({ state, hasStoredLog: true }), null);

      // Assert
      expect(fact(facts, 'log')).toBeUndefined();
    });
  });
});

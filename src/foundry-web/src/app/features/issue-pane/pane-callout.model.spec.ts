import { IssueDetail, IssueStateDetails, IssueSummary } from '../issues/issue.model';
import { WorkerRunDetail } from '../workers/worker-run.model';
import { buildCallout } from './pane-callout.model';

// A medium date as DatePipe renders it, in whatever time zone the test runs in.
const MEDIUM_DATE = /[A-Z][a-z]{2} \d{1,2}, 2026, \d{1,2}:\d{2}:\d{2}\s[AP]M/;

function stateDetails(overrides: Partial<IssueStateDetails> = {}): IssueStateDetails {
  return {
    workerRunId: null,
    branchName: 'feat/42-dark-mode',
    feedbackCutoffAt: null,
    failureReason: null,
    failedAt: null,
    completedAt: null,
    blockedBy: null,
    violations: null,
    transientRetry: null,
    ...overrides,
  };
}

function detail(state: IssueDetail['state'], details: Partial<IssueStateDetails> | null = {}): IssueDetail {
  return {
    id: 'abc',
    issueNumber: 42,
    title: 'Enable dark mode',
    state,
    repositorySlug: 'acme/api',
    detectedAt: '2026-01-01T00:00:00Z',
    url: 'https://github.com/acme/api/issues/42',
    stage: null,
    needsAction: false,
    awaitsDispatch: false,
    codeStep: null,
    author: 'dev',
    labels: [],
    stateDetails: details === null ? null : stateDetails(details),
    kind: { source: 'labelled', kind: 'feature', conflictingTriageKind: null },
    triage: null,
  };
}

function summary(overrides: Partial<IssueSummary> = {}): IssueSummary {
  return {
    id: 'abc',
    issueNumber: 42,
    title: 'Enable dark mode',
    state: 'in_progress',
    repositorySlug: 'acme/api',
    detectedAt: '2026-01-01T00:00:00Z',
    url: 'https://github.com/acme/api/issues/42',
    providerType: 'github',
    stage: 'implement',
    needsAction: false,
    awaitsDispatch: false,
    codeStep: null,
    ...overrides,
  };
}

function run(overrides: Partial<WorkerRunDetail> = {}): WorkerRunDetail {
  return {
    workerRunId: 'run-1',
    issueId: 'abc',
    role: 'implement',
    state: 'running',
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

describe('buildCallout', () => {
  describe('one callout per row of the Callout model table', () => {
    it('shows a completed issue as a success with its date and pull request link', () => {
      // Act
      const callout = buildCallout(
        detail('completed', { completedAt: '2026-02-01T12:00:00Z' }),
        null,
        summary({ state: 'completed', pullRequestUrl: 'https://github.com/acme/api/pull/7' }),
        null,
      );

      // Assert
      expect(callout.tone).toBe('success');
      expect(callout.title).toBe('Completed');
      expect(callout.lines[0]).toMatch(new RegExp(`^Completed ${MEDIUM_DATE.source}\\.$`));
      expect(callout.links).toEqual([
        { label: 'View PR', href: 'https://github.com/acme/api/pull/7', ariaLabel: 'View pull request for issue #42' },
      ]);
      expect(callout.actions).toEqual([]);
    });

    it('adds the cancel note when the run finished before the cancel took effect', () => {
      // Act
      const callout = buildCallout(
        detail('completed'),
        run({ state: 'completed', cancelRequestedAt: '2026-02-01T11:00:00Z' }),
        null,
        null,
      );

      // Assert
      expect(callout.lines).toContain('The run finished before the cancel took effect.');
    });

    it('shows a secret-blocked push with its findings, guidance and only Discard run', () => {
      // Arrange
      const findings = [{ path: 'a.env', line: 3, commitSha: 'abc', ruleId: 'aws-key' }];

      // Act
      const callout = buildCallout(
        detail('publish_blocked'),
        run({ state: 'publish_blocked', secretFindings: findings, discardsAt: '2026-03-01T12:00:00Z' }),
        null,
        null,
      );

      // Assert
      expect(callout.tone).toBe('error');
      expect(callout.title).toBe('Push blocked');
      expect(callout.lines).toEqual([
        "The diff gate found 1 possible secret in the run's changes, so nothing was pushed to feat/42-dark-mode.",
      ]);
      expect(callout.secretFindings).toEqual(findings);
      expect(callout.notes[0]).toContain('Foundry never publishes a run with a detected secret. Discard the run, then retry the issue');
      expect(callout.notes[0]).toMatch(new RegExp(`the run is discarded automatically on ${MEDIUM_DATE.source}\\.$`));
      expect(callout.actions).toEqual(['discard_run']);
    });

    it('shows an exhausted publish with its attempts and reason, offering Retry publish then Discard run', () => {
      // Act
      const callout = buildCallout(
        detail('publish_failed'),
        run({
          state: 'publish_failed',
          publishRetry: { attemptNumber: 5, maxAttempts: 5, nextAttemptAt: null, reason: 'remote rejected' },
          discardsAt: '2026-03-01T12:00:00Z',
        }),
        null,
        null,
      );

      // Assert
      expect(callout.tone).toBe('error');
      expect(callout.title).toBe('Publish failed');
      expect(callout.lines.slice(0, 2)).toEqual([
        'Publishing failed after 5 attempts: remote rejected.',
        'Fix the cause, then use Retry publish.',
      ]);
      expect(callout.lines[2]).toMatch(new RegExp(`^Unpublished work is discarded on ${MEDIUM_DATE.source}\\.$`));
      expect(callout.actions).toEqual(['retry_publish', 'discard_run']);
    });

    it('shows a diverged branch with both branch links and guidance, offering Retry issue', () => {
      // Act
      const callout = buildCallout(
        detail('branch_diverged', {
          branchUrl: 'https://github.com/acme/api/tree/feat/42-dark-mode',
          sideBranchName: 'feat/42-dark-mode-run',
          sideBranchUrl: 'https://github.com/acme/api/tree/feat/42-dark-mode-run',
          divergenceReason: 'branch-moved',
        }),
        null,
        summary({ state: 'branch_diverged' }),
        null,
      );

      // Assert
      expect(callout.tone).toBe('warning');
      expect(callout.title).toBe('Branch diverged');
      expect(callout.lines[0]).toContain('Someone pushed to the issue branch while the worker ran');
      expect(callout.rows).toEqual([
        {
          key: 'Issue branch',
          value: 'feat/42-dark-mode',
          href: 'https://github.com/acme/api/tree/feat/42-dark-mode',
          ariaLabel: 'View branch feat/42-dark-mode on GitHub',
          mono: true,
        },
        {
          key: "Run's work",
          value: 'feat/42-dark-mode-run',
          href: 'https://github.com/acme/api/tree/feat/42-dark-mode-run',
          ariaLabel: "View the run's branch feat/42-dark-mode-run on GitHub",
          mono: true,
        },
      ]);
      expect(callout.notes).toEqual([
        "Merge the run's branch into the issue branch on GitHub. Foundry continues automatically once the merge lands, or use Retry issue.",
      ]);
      expect(callout.actions).toEqual(['retry_issue']);
    });

    it('words a force-push-protected divergence as a replace', () => {
      // Act
      const callout = buildCallout(
        detail('branch_diverged', { divergenceReason: 'force-push-protected' }),
        null,
        summary({ providerType: 'gitlab' }),
        null,
      );

      // Assert
      expect(callout.lines[0]).toContain('GitLab protects the issue branch from force pushes');
      expect(callout.notes[0]).toContain("Replace the issue branch with the run's branch on GitLab");
    });

    it('shows a working issue that publishes as Publishing, with no Stop worker', () => {
      // Act
      const callout = buildCallout(
        detail('in_progress', { workerRunId: 'run-1' }),
        run({ state: 'publishing' }),
        summary({ publishPhase: 'publishing' }),
        null,
      );

      // Assert
      expect(callout.tone).toBe('working');
      expect(callout.title).toBe('Publishing');
      expect(callout.lines).toEqual(["Foundry is publishing the run's commits to feat/42-dark-mode."]);
      expect(callout.actions).toEqual([]);
    });

    it('shows a working issue retrying its publish with the attempt chip and next attempt', () => {
      // Act
      const callout = buildCallout(
        detail('in_progress'),
        run({
          state: 'publish_failed',
          publishRetry: { attemptNumber: 2, maxAttempts: 5, nextAttemptAt: '2026-02-01T12:00:00Z', reason: 'timeout' },
        }),
        summary({ publishPhase: 'retrying' }),
        null,
      );

      // Assert
      expect(callout.tone).toBe('warning');
      expect(callout.title).toBe('Retrying publish');
      expect(callout.chip).toEqual({ label: 'Publish attempt 2 of 5', cssClass: 'badge--retrying', ariaLabel: null });
      expect(callout.lines[0]).toMatch(new RegExp(`^Publishing failed: timeout\\. Next attempt at ${MEDIUM_DATE.source}\\.$`));
      expect(callout.actions).toEqual([]);
    });

    it('asks for more triage detail with the questions, post status and question link', () => {
      // Act
      const callout = buildCallout(
        detail('awaiting_input', {
          awaitingInput: {
            questions: [{ question: 'Which API?', context: 'Two exist.', recommendation: 'The v2 one.' }],
            postStatus: 'posted',
            postedAt: '2026-02-01T12:00:00Z',
            commentUrl: 'https://github.com/acme/api/issues/42#c1',
            postFailureReason: null,
            nextPostAttemptAt: null,
            resumeTarget: 'retriage',
          },
        }),
        null,
        null,
        null,
      );

      // Assert
      expect(callout.tone).toBe('warning');
      expect(callout.title).toBe('Triage needs more detail');
      expect(callout.lines).toEqual([
        'The questions are posted on the issue; once a reply settles, Foundry triages again.',
      ]);
      expect(callout.list).toEqual({
        heading: 'Questions from triage',
        items: [{ text: 'Which API?', detail: 'Two exist.', recommendation: 'The v2 one.', badge: null, link: null }],
      });
      expect(callout.notes[0]).toMatch(new RegExp(`^Posted ${MEDIUM_DATE.source} — waiting for a reply\\.$`));
      expect(callout.links).toEqual([
        {
          label: 'View question',
          href: 'https://github.com/acme/api/issues/42#c1',
          ariaLabel: 'View the questions on issue #42',
        },
      ]);
      expect(callout.actions).toEqual([]);
    });

    it('asks for answers to open questions, saying when posting them failed', () => {
      // Act
      const callout = buildCallout(
        detail('awaiting_input', {
          awaitingInput: {
            questions: [{ question: 'Which API?', context: '' }],
            postStatus: 'failed',
            postedAt: null,
            commentUrl: null,
            postFailureReason: 'rate limited',
            nextPostAttemptAt: null,
            resumeTarget: 'revision',
          },
        }),
        null,
        null,
        null,
      );

      // Assert
      expect(callout.title).toBe('Needs answers');
      expect(callout.lines).toEqual([
        'Answer the questions on the issue. Once a reply settles, Foundry resumes the work.',
      ]);
      expect(callout.list?.heading).toBe('Open questions');
      expect(callout.list?.items[0].detail).toBeNull();
      expect(callout.notes).toEqual(['Posting the questions failed: rate limited.']);
      expect(callout.links).toEqual([]);
    });

    it('shows a failed triage with its failure chip and reason, offering Retry triage and Skip triage', () => {
      // Act
      const callout = buildCallout(
        detail('triage_failed', { failureReason: 'Verdict did not parse' }),
        run({ state: 'failed', failureCategory: 'verdict_invalid', failureSummary: 'Bad JSON' }),
        null,
        null,
      );

      // Assert
      expect(callout.tone).toBe('error');
      expect(callout.title).toBe('Triage did not finish');
      expect(callout.chip).toEqual({
        label: 'INVALID VERDICT',
        cssClass: 'badge--failure-verdict-invalid',
        ariaLabel: 'Failure category: INVALID VERDICT',
      });
      expect(callout.lines).toEqual([
        'Retry triage to run it again, or skip triage to queue the issue without a readiness check.',
        'Bad JSON',
        'Verdict did not parse',
      ]);
      expect(callout.actions).toEqual(['retry_triage', 'skip_triage']);
    });

    it('shows a failed run with its failure reason, offering Retry issue', () => {
      // Act
      const callout = buildCallout(detail('failed', { failureReason: 'Exit code 1' }), null, null, null);

      // Assert
      expect(callout.tone).toBe('error');
      expect(callout.title).toBe('Run failed');
      expect(callout.lines).toEqual(['Exit code 1']);
      expect(callout.rows).toEqual([]);
      expect(callout.actions).toEqual(['retry_issue']);
    });

    it('titles a failed run whose checks still fail, listing the failing checks', () => {
      // Act
      const callout = buildCallout(
        detail('failed', {
          checksOutcome: 'still_failing',
          failingChecks: [{ name: 'build', conclusion: 'failure', detailsUrl: 'https://ci/1' }],
        }),
        null,
        null,
        null,
      );

      // Assert
      expect(callout.title).toBe('Checks still failing');
      expect(callout.list).toEqual({
        heading: 'Failing checks',
        items: [
          {
            text: 'build',
            detail: null,
            recommendation: null,
            badge: 'failure',
            link: { label: 'View details', href: 'https://ci/1', ariaLabel: 'View details for failing check build' },
          },
        ],
      });
    });

    it('shows a continuable failure as work kept, offering Retry issue and Retry fresh', () => {
      // Act
      const callout = buildCallout(
        detail('continuable_failed'),
        run({ state: 'failed', failureCategory: 'cancelled' }),
        null,
        null,
      );

      // Assert
      expect(callout.tone).toBe('error');
      expect(callout.title).toBe('Run stopped with work kept');
      expect(callout.chip?.label).toBe('CANCELLED');
      expect(callout.lines).toEqual([
        "The branch keeps the run's commits. Retry continues from them; Retry fresh starts over from the default branch.",
      ]);
      expect(callout.actions).toEqual(['retry_issue', 'retry_fresh']);
    });

    it.each([
      ['failed_checks', 'Checks fix failed'],
      ['rebase_conflict', 'Conflict resolution failed'],
      ['input_provided', 'Resume failed'],
      ['review_feedback', 'Revision failed'],
    ] as const)('titles a revision failure triggered by %s "%s", offering Retry issue', (trigger, title) => {
      // Act
      const callout = buildCallout(
        detail('revision_failed', { revisionTriggerKind: trigger, failureReason: 'Exit code 1' }),
        null,
        null,
        null,
      );

      // Assert
      expect(callout.tone).toBe('error');
      expect(callout.title).toBe(title);
      expect(callout.lines).toEqual(['Exit code 1']);
      expect(callout.actions).toEqual(['retry_issue']);
    });

    it('shows an unchanged run as No changes with its reason, offering Retry issue', () => {
      // Act
      const callout = buildCallout(detail('unchanged', { failureReason: 'Nothing to do' }), null, null, null);

      // Assert
      expect(callout.tone).toBe('neutral');
      expect(callout.title).toBe('No changes');
      expect(callout.lines).toEqual(['The run finished without changing any files.', 'Nothing to do']);
      expect(callout.rows).toEqual([]);
      expect(callout.actions).toEqual(['retry_issue']);
    });

    it.each([
      ['failed_checks', { checksFixAttempts: 2 }, 'Fixing checks (attempt 2 of 2)', 'Fixing checks — a worker is running.'],
      ['input_provided', {}, 'Resuming after input', 'A worker is applying the answers to the open questions.'],
      ['rebase_conflict', {}, 'Resolving conflicts (attempt 1 of 2)', 'Resolving conflicts with the default branch — a worker is running.'],
      ['review_feedback', {}, 'Addressing review', 'A worker is addressing reviewer feedback.'],
    ] as const)('shows a revision in progress for %s with its chip label as title, offering Stop worker', (trigger, extra, title, line) => {
      // Act
      const callout = buildCallout(
        detail('revision_in_progress', { revisionTriggerKind: trigger, ...extra }),
        run(),
        null,
        null,
      );

      // Assert
      expect(callout.tone).toBe('working');
      expect(callout.title).toBe(title);
      expect(callout.lines).toEqual([line]);
      expect(callout.actions).toEqual(['stop_worker']);
    });

    it('shows a queued revision with its queued message and no actions', () => {
      // Act
      const callout = buildCallout(
        detail('revision_queued', { revisionTriggerKind: 'failed_checks' }),
        null,
        null,
        null,
      );

      // Assert
      expect(callout.tone).toBe('neutral');
      expect(callout.title).toBe('Fixing checks (attempt 1 of 2)');
      expect(callout.lines).toEqual(['Fix run queued — checks failed on the PR.']);
      expect(callout.actions).toEqual([]);
    });

    it('shows an implementing issue, offering Stop worker', () => {
      // Act
      const callout = buildCallout(detail('in_progress'), run(), summary(), null);

      // Assert
      expect(callout.tone).toBe('working');
      expect(callout.title).toBe('Implementing');
      expect(callout.lines).toEqual(['A worker is implementing this issue.']);
      expect(callout.actions).toEqual(['stop_worker']);
    });

    it('offers Stop worker before the run detail has loaded', () => {
      // Act
      const callout = buildCallout(detail('in_progress'), null, null, null);

      // Assert
      expect(callout.actions).toEqual(['stop_worker']);
    });

    it('shows a running triage, offering Stop worker', () => {
      // Act
      const callout = buildCallout(detail('triage_in_progress', null), run({ role: 'triage' }), null, null);

      // Assert
      expect(callout.tone).toBe('working');
      expect(callout.title).toBe('Triaging');
      expect(callout.lines).toEqual(['A triage worker is reading the issue and the repository.']);
      expect(callout.actions).toEqual(['stop_worker']);
    });

    it('shows a pending rebase with no actions', () => {
      // Act
      const callout = buildCallout(detail('rebase_pending'), null, null, null);

      // Assert
      expect(callout.tone).toBe('working');
      expect(callout.title).toBe('Rebasing');
      expect(callout.lines).toEqual(['Foundry is rebasing the branch onto the default branch.']);
      expect(callout.actions).toEqual([]);
    });

    it('shows pending checks in the provider’s term for a pull request', () => {
      // Act
      const callout = buildCallout(detail('checks_pending'), null, summary({ providerType: 'gitlab' }), null);

      // Assert
      expect(callout.tone).toBe('working');
      expect(callout.title).toBe('Checks running');
      expect(callout.lines).toEqual(['Waiting for CI checks on the merge request.']);
    });

    it('shows a review whose checks failed with no failing check identified', () => {
      // Act
      const callout = buildCallout(
        detail('review', { checksOutcome: 'still_failing', failingChecks: [] }),
        null,
        summary({ state: 'review', checksOutcome: 'still_failing', pullRequestUrl: 'https://github.com/acme/api/pull/7' }),
        null,
      );

      // Assert
      expect(callout.tone).toBe('error');
      expect(callout.title).toBe('Checks failing');
      expect(callout.lines).toEqual([
        "Checks failed, but no failing check could be identified. Open the pull request's checks on GitHub.",
      ]);
      expect(callout.links.map((l) => l.label)).toEqual(['View PR']);
    });

    it('shows a review whose checks still fail after the fix cap, listing them', () => {
      // Act
      const callout = buildCallout(
        detail('review', { checksOutcome: 'still_failing', failingChecks: [{ name: 'lint', conclusion: 'failure' }] }),
        null,
        summary({ state: 'review', pullRequestUrl: 'https://github.com/acme/api/pull/7' }),
        null,
      );

      // Assert
      expect(callout.tone).toBe('error');
      expect(callout.title).toBe('Checks still failing');
      expect(callout.lines).toEqual(['Foundry stopped fixing checks after 2 attempts. Review the pull request on GitHub.']);
      expect(callout.list?.heading).toBe('Failing checks');
      expect(callout.list?.items.map((i) => i.text)).toEqual(['lint']);
      expect(callout.links.map((l) => l.label)).toEqual(['View PR']);
    });

    it('shows a merge request ready for review on GitLab', () => {
      // Act
      const callout = buildCallout(
        detail('review'),
        null,
        summary({ state: 'review', providerType: 'gitlab', pullRequestUrl: 'https://gitlab.com/acme/api/-/merge_requests/7' }),
        null,
      );

      // Assert
      expect(callout.tone).toBe('success');
      expect(callout.title).toBe('MR ready for review');
      expect(callout.lines).toEqual([
        'Review the merge request on GitLab. Foundry addresses review comments in a revision run.',
      ]);
      expect(callout.links).toEqual([
        {
          label: 'View MR',
          href: 'https://gitlab.com/acme/api/-/merge_requests/7',
          ariaLabel: 'View merge request for issue #42',
        },
      ]);
      expect(callout.actions).toEqual([]);
    });

    it.each(['detected', 'triage_queued'] as const)('shows %s as waiting for triage with its queue position', (state) => {
      // Act
      const callout = buildCallout(detail(state, null), null, null, 3);

      // Assert
      expect(callout.tone).toBe('neutral');
      expect(callout.title).toBe('Waiting for triage');
      expect(callout.lines).toEqual(['Waiting for a worker to triage this issue.', '#3 in the dispatch queue.']);
      expect(callout.actions).toEqual([]);
    });

    it('shows a queued issue first in line as Next up, offering Re-triage', () => {
      // Act
      const callout = buildCallout(detail('queued', null), null, null, 1);

      // Assert
      expect(callout.tone).toBe('neutral');
      expect(callout.title).toBe('Queued');
      expect(callout.lines).toEqual(['Next up.']);
      expect(callout.actions).toEqual(['retriage']);
    });

    it('shows a queued continuation continuing from the issue branch, with no actions', () => {
      // Act
      const callout = buildCallout(detail('continuation_queued'), null, null, 2);

      // Assert
      expect(callout.title).toBe('Queued');
      expect(callout.lines).toEqual(['#2 in the dispatch queue.', 'Continues from the issue branch.']);
      expect(callout.actions).toEqual([]);
    });

    it('shows a blocked issue naming its blockers, offering Re-triage', () => {
      // Act
      const callout = buildCallout(
        detail('blocked', { blockedBy: [{ issueNumber: 12, reason: null }, { issueNumber: 14, reason: 'API first' }] }),
        null,
        null,
        null,
      );

      // Assert
      expect(callout.tone).toBe('neutral');
      expect(callout.title).toBe('Blocked');
      expect(callout.lines).toEqual(['Waiting on #12, #14. It queues once they close.']);
      expect(callout.actions).toEqual(['retriage']);
    });

    it('shows a triage-blocked issue waiting for its blockers, listing them with no actions', () => {
      // Act
      const callout = buildCallout(
        detail('triage_blocked', { blockedBy: [{ issueNumber: 12, reason: 'Needs the schema' }, { issueNumber: 14, reason: null }] }),
        null,
        null,
        null,
      );

      // Assert
      expect(callout.tone).toBe('neutral');
      expect(callout.title).toBe('Waiting for blockers');
      expect(callout.lines).toEqual(['Waiting for its blockers to close. Triage runs once they do.']);
      expect(callout.list).toEqual({
        heading: 'Blocked by',
        items: [
          { text: '#12', detail: 'Triage: Needs the schema', recommendation: null, badge: null, link: null },
          { text: '#14', detail: null, recommendation: null, badge: null, link: null },
        ],
      });
      expect(callout.actions).toEqual([]);
    });
  });

  describe('priority', () => {
    it('lets publishing win over a revision in progress and keeps the revision as a row', () => {
      // Act
      const callout = buildCallout(
        detail('revision_in_progress', { revisionTriggerKind: 'review_feedback' }),
        run({ state: 'publishing' }),
        summary({ publishPhase: 'publishing' }),
        null,
      );

      // Assert
      expect(callout.title).toBe('Publishing');
      expect(callout.rows).toEqual([
        { key: 'Revision', value: 'Addressing review', href: null, ariaLabel: null, mono: false },
      ]);
    });

    it('keeps a failure reason the winning callout does not show as a row', () => {
      // Act
      const callout = buildCallout(detail('queued', { failureReason: 'Previous run timed out' }), null, null, null);

      // Assert
      expect(callout.title).toBe('Queued');
      expect(callout.rows).toEqual([
        { key: 'Failure reason', value: 'Previous run timed out', href: null, ariaLabel: null, mono: false },
      ]);
    });

    it('keeps the failing checks of a revision fixing them as the callout list', () => {
      // Act
      const callout = buildCallout(
        detail('revision_in_progress', {
          revisionTriggerKind: 'failed_checks',
          checksOutcome: 'still_failing',
          failingChecks: [{ name: 'build', conclusion: 'failure' }],
        }),
        run(),
        null,
        null,
      );

      // Assert
      expect(callout.title).toBe('Fixing checks (attempt 1 of 2)');
      expect(callout.list?.heading).toBe('Failing checks');
    });
  });

  describe('appends', () => {
    it.each(['queued', 'triage_queued', 'continuation_queued', 'revision_queued', 'detected'] as const)(
      'tells a %s issue of an ineligible repository that it is not dispatchable',
      (state) => {
        // Act
        const callout = buildCallout(detail(state), null, summary({ state, repositoryEligibilityStatus: 'ineligible' }), null);

        // Assert
        expect(callout.lines.at(-1)).toBe(
          'This repository is not dispatchable right now. Check repository settings to make it eligible.',
        );
      },
    );

    it('does not add the ineligible line outside the queue', () => {
      // Act
      const callout = buildCallout(detail('failed'), null, summary({ repositoryEligibilityStatus: 'ineligible' }), null);

      // Assert
      expect(callout.lines.join(' ')).not.toContain('not dispatchable');
    });

    it('appends a pending automatic retry with its attempt and next time to any callout', () => {
      // Act
      const callout = buildCallout(
        detail('in_progress', {
          transientRetry: { attemptNumber: 2, maxAttempts: 3, isExhausted: false, nextAttemptDueAt: '2026-02-01T12:00:00Z' },
        }),
        run(),
        null,
        null,
      );

      // Assert
      expect(callout.title).toBe('Implementing');
      expect(callout.lines.at(-1)).toMatch(
        new RegExp(`^Attempt 2 of 3\\. Automatic retry pending — next attempt at ${MEDIUM_DATE.source}\\.$`),
      );
    });

    it('says the next automatic retry comes shortly when it has no time', () => {
      // Act
      const callout = buildCallout(
        detail('queued', { transientRetry: { attemptNumber: 1, maxAttempts: 3, isExhausted: false, nextAttemptDueAt: null } }),
        null,
        null,
        null,
      );

      // Assert
      expect(callout.lines.at(-1)).toBe('Attempt 1 of 3. Automatic retry pending — next attempt shortly.');
    });

    it.each([
      ['failed', 'Automatic retries exhausted after 3 attempts. Use Retry issue to try again manually.'],
      ['triage_failed', 'Automatic retries exhausted after 3 attempts. Use Retry triage to run it again.'],
      ['unchanged', 'Automatic retries exhausted after 3 attempts.'],
    ] as const)('tells a %s issue what to do once automatic retries are exhausted', (state, line) => {
      // Act
      const callout = buildCallout(
        detail(state, { transientRetry: { attemptNumber: 3, maxAttempts: 3, isExhausted: true, nextAttemptDueAt: null } }),
        null,
        null,
        null,
      );

      // Assert
      expect(callout.lines.at(-1)).toBe(line);
    });
  });
});

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
    waitsOnBlockers: false,
    codeStep: null,
    runKind: null,
    repositoryId: 'repo-1',
    enteredStateAt: '2026-01-01T00:00:00Z',
    author: 'dev',
    labels: [],
    stateDetails: details === null ? null : stateDetails(details),
    kind: { source: 'labelled', kind: 'feature', conflictingTriageKind: null },
    triage: null,
    clarifications: [],
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
    waitsOnBlockers: false,
    codeStep: null,
    runKind: null,
    repositoryId: 'repo-1',
    enteredStateAt: '2026-01-01T00:00:00Z',
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
    publishBlockedAt: null,
    falsePositivesConfirmedAt: null,
    confirmedFindings: [],
    toolchain: null,
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

    it('shows a secret-blocked push with its findings and guidance, offering Discard run then the confirmation', () => {
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
      expect(callout.notes).toHaveLength(1);
      expect(callout.notes[0]).toMatch(new RegExp(
        '^Foundry publishes this run only if you confirm that every finding is a false positive\\. If any finding '
          + 'is a real credential, discard the run and rotate the credential: the worker had it in its workspace\\. '
          + 'Then retry the issue to start a new worker from the branch\\. If no one acts, the run is discarded '
          + `automatically on ${MEDIUM_DATE.source}\\.$`,
      ));
      expect(callout.actions).toEqual(['discard_run', 'confirm_false_positives']);
    });

    it('leaves the auto-discard sentence out of the blocked guidance when the run has no discard date', () => {
      // Act
      const callout = buildCallout(detail('publish_blocked'), run({ state: 'publish_blocked', discardsAt: null }), null, null);

      // Assert
      expect(callout.notes[0]).toMatch(/Then retry the issue to start a new worker from the branch\.$/);
      expect(callout.notes[0]).not.toContain('discarded automatically');
    });

    it('tells a blocked run that was confirmed before that its findings are new, above the findings', () => {
      // Act
      const callout = buildCallout(
        detail('publish_blocked'),
        run({
          state: 'publish_blocked',
          secretFindings: [{ path: 'b.env', line: 9, commitSha: 'def', ruleId: 'generic-api-key' }],
          falsePositivesConfirmedAt: '2026-03-01T10:00:00Z',
        }),
        null,
        null,
      );

      // Assert
      expect(callout.lines).toEqual([
        "The diff gate found 1 possible secret in the run's changes, so nothing was pushed to feat/42-dark-mode.",
        'Earlier findings on this run were confirmed as false positives. These findings are new.',
      ]);
    });

    it('has no re-block line for a run blocked for the first time', () => {
      // Act
      const callout = buildCallout(detail('publish_blocked'), run({ state: 'publish_blocked' }), null, null);

      // Assert
      expect(callout.lines.join(' ')).not.toContain('confirmed as false positives');
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

    it('asks for more triage detail with a link to the posted question, leaving the questions to Clarifications', () => {
      // Act
      const callout = buildCallout(
        detail('awaiting_input', {
          awaitingInput: {
            questions: [{ $type: 'legacy', question: 'Which API?', context: 'Two exist.', recommendation: 'The v2 one.' }],
            postStatus: 'posted',
            postedAt: '2026-02-01T12:00:00Z',
            commentUrl: 'https://github.com/acme/api/issues/42#c1',
            postFailureReason: null,
            nextPostAttemptAt: null,
            reminderSentAt: '2026-02-04T12:00:00Z',
            reminderDueAt: null,
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
      expect(callout.list).toBeNull();
      expect(callout.notes).toEqual([]);
      expect(callout.links).toEqual([
        {
          label: 'View question',
          href: 'https://github.com/acme/api/issues/42#c1',
          ariaLabel: 'View the questions on issue #42',
        },
      ]);
      expect(callout.actions).toEqual(['continue_without_answer']);
    });

    it('asks for answers to open questions, offering Retry post when posting them failed', () => {
      // Act
      const callout = buildCallout(
        detail('awaiting_input', {
          awaitingInput: {
            questions: [{ $type: 'legacy', question: 'Which API?', context: '' }],
            postStatus: 'failed',
            postedAt: null,
            commentUrl: null,
            postFailureReason: 'rate limited',
            nextPostAttemptAt: null,
            reminderSentAt: null,
            reminderDueAt: null,
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
      expect(callout.list).toBeNull();
      expect(callout.notes).toEqual([]);
      expect(callout.links).toEqual([]);
      expect(callout.actions).toEqual(['retry_post', 'continue_without_answer']);
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

    it('explains that new commits on GitHub sent the issue back from review, linking the pull request', () => {
      // Act
      const callout = buildCallout(
        detail('checks_pending', { checksPendingReason: 'head_moved' }),
        null,
        summary({ state: 'checks_pending', pullRequestUrl: 'https://github.com/acme/api/pull/7' }),
        null,
      );

      // Assert
      expect(callout.tone).toBe('working');
      expect(callout.title).toBe('Checks re-running');
      expect(callout.lines).toEqual([
        'The pull request\'s branch changed on GitHub after its checks finished, so it left your review.',
        'Waiting for CI checks on the new commits.',
      ]);
      expect(callout.links).toEqual([
        { label: 'View PR', href: 'https://github.com/acme/api/pull/7', ariaLabel: 'View pull request for issue #42' },
      ]);
    });

    it('explains new commits on a merge request in GitLab terms', () => {
      // Act
      const callout = buildCallout(
        detail('checks_pending', { checksPendingReason: 'head_moved' }),
        null,
        summary({ state: 'checks_pending', providerType: 'gitlab' }),
        null,
      );

      // Assert
      expect(callout.title).toBe('Checks re-running');
      expect(callout.lines[0]).toBe(
        'The merge request\'s branch changed on GitLab after its checks finished, so it left your review.',
      );
    });

    it('explains that the pull request\'s checks passed on a rerun of the same commits, linking it', () => {
      // Act
      const callout = buildCallout(
        detail('checks_pending', { checksPendingReason: 'checks_rerun' }),
        null,
        summary({ state: 'checks_pending', pullRequestUrl: 'https://github.com/acme/api/pull/7' }),
        null,
      );

      // Assert
      expect(callout.tone).toBe('working');
      expect(callout.title).toBe('Checks passed on a rerun');
      expect(callout.lines).toEqual([
        'The pull request\'s checks ran again on the same commits and passed. Foundry is confirming the result.',
      ]);
      expect(callout.links).toEqual([
        { label: 'View PR', href: 'https://github.com/acme/api/pull/7', ariaLabel: 'View pull request for issue #42' },
      ]);
    });

    it('explains a checks rerun on a merge request without a link when it has none', () => {
      // Act
      const callout = buildCallout(
        detail('checks_pending', { checksPendingReason: 'checks_rerun' }),
        null,
        summary({ state: 'checks_pending', providerType: 'gitlab' }),
        null,
      );

      // Assert
      expect(callout.title).toBe('Checks passed on a rerun');
      expect(callout.lines).toEqual([
        'The merge request\'s checks ran again on the same commits and passed. Foundry is confirming the result.',
      ]);
      expect(callout.links).toEqual([]);
    });

    for (const [name, reason] of [
      ['after_run', 'after_run'],
      ['null', null],
      ['an unknown value', 'from_the_future'],
    ] as const) {
      it(`keeps today's pending-checks copy when the reason is ${name}`, () => {
        // Act
        const callout = buildCallout(
          detail('checks_pending', { checksPendingReason: reason as IssueStateDetails['checksPendingReason'] }),
          null,
          summary({ state: 'checks_pending', pullRequestUrl: 'https://github.com/acme/api/pull/7' }),
          null,
        );

        // Assert
        expect(callout.title).toBe('Checks running');
        expect(callout.lines).toEqual(['Waiting for CI checks on the pull request.']);
        expect(callout.links).toEqual([]);
      });
    }

    it('keeps today\'s pending-checks copy when the reason is missing', () => {
      // Act
      const callout = buildCallout(
        detail('checks_pending'),
        null,
        summary({ state: 'checks_pending', pullRequestUrl: 'https://github.com/acme/api/pull/7' }),
        null,
      );

      // Assert
      expect(callout.title).toBe('Checks running');
      expect(callout.lines).toEqual(['Waiting for CI checks on the pull request.']);
      expect(callout.links).toEqual([]);
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

    it("shows a review whose checks failed before running any code as CI couldn't run, listing the checks", () => {
      // Act
      const callout = buildCallout(
        detail('review', {
          checksOutcome: 'infrastructure_failed',
          failingChecks: [
            {
              name: 'build',
              conclusion: 'ci_quota_exceeded',
              detailsUrl: 'https://gitlab.com/acme/api/-/jobs/1',
            },
          ],
        }),
        null,
        summary({
          state: 'review',
          checksOutcome: 'infrastructure_failed',
          providerType: 'gitlab',
          pullRequestUrl: 'https://gitlab.com/acme/api/-/merge_requests/7',
        }),
        null,
      );

      // Assert
      expect(callout.tone).toBe('warning');
      expect(callout.title).toBe("CI couldn't run");
      expect(callout.lines).toEqual([
        "The merge request's checks failed before running any code, so Foundry didn't start a fix run.",
        'Retry the failed jobs on GitLab. Foundry picks up the result once they pass.',
      ]);
      expect(callout.list?.heading).toBe("Checks that couldn't run");
      expect(callout.list?.items.map((i) => [i.text, i.badge, i.link?.href, i.link?.ariaLabel])).toEqual([
        ['build', 'ci_quota_exceeded', 'https://gitlab.com/acme/api/-/jobs/1', 'View details for failing check build'],
      ]);
      expect(callout.links.map((l) => l.label)).toEqual(['View MR']);
    });

    it("keeps the CI couldn't run title when no check that couldn't run was identified", () => {
      // Act
      const callout = buildCallout(
        detail('review', { checksOutcome: 'infrastructure_failed', failingChecks: [] }),
        null,
        summary({ state: 'review', checksOutcome: 'infrastructure_failed', pullRequestUrl: 'https://github.com/acme/api/pull/7' }),
        null,
      );

      // Assert
      expect(callout.tone).toBe('warning');
      expect(callout.title).toBe("CI couldn't run");
      expect(callout.lines).toEqual([
        "CI failed before running any code, but no job could be identified. Open the pull request's checks on GitHub.",
        'Retry the failed jobs on GitHub. Foundry picks up the result once they pass.',
      ]);
      expect(callout.list).toBeNull();
      expect(callout.links.map((l) => l.label)).toEqual(['View PR']);
    });

    it("shows CI couldn't run rather than a skipped merge", () => {
      // Act
      const callout = buildCallout(
        detail('review', {
          checksOutcome: 'infrastructure_failed',
          failingChecks: [{ name: 'build', conclusion: 'ci_quota_exceeded' }],
        }),
        null,
        summary({ state: 'review', checksOutcome: 'infrastructure_failed', mergeOutcome: 'skipped' }),
        null,
      );

      // Assert
      expect(callout.title).toBe("CI couldn't run");
    });

    it('shows Foundry merging a pull request whose checks passed', () => {
      // Act
      const callout = buildCallout(detail('merge_pending'), null, summary({ state: 'merge_pending' }), null);

      // Assert
      expect(callout.tone).toBe('working');
      expect(callout.title).toBe('Merging');
      expect(callout.lines).toEqual(['Checks passed. Foundry is merging the pull request on GitHub.']);
    });

    it('shows Foundry merging a merge request in GitLab terms', () => {
      // Act
      const callout = buildCallout(
        detail('merge_pending'),
        null,
        summary({ state: 'merge_pending', providerType: 'gitlab' }),
        null,
      );

      // Assert
      expect(callout.lines).toEqual(['Checks passed. Foundry is merging the merge request on GitLab.']);
    });

    for (const [reason, line] of [
      [
        'not_mergeable',
        'Checks passed, but GitHub refused the merge: conflicts, a branch rule or missing approvals. '
          + 'Resolve it on GitHub and merge the pull request yourself.',
      ],
      [
        'unauthorized',
        "Checks passed, but the account's token isn't allowed to merge on GitHub. "
          + 'Grant it merge access, or merge the pull request yourself.',
      ],
      [
        'head_moved',
        "Checks passed, but new commits reached the pull request after the checks ran, so Foundry didn't merge "
          + 'unchecked work. Review the pull request on GitHub.',
      ],
      [
        'provider_error',
        'Checks passed, but GitHub kept failing when Foundry tried to merge. Merge the pull request yourself on GitHub.',
      ],
      ['from_the_future', "Checks passed, but Foundry didn't merge the pull request. Review it on GitHub."],
      [null, "Checks passed, but Foundry didn't merge the pull request. Review it on GitHub."],
    ] as const) {
      it(`explains a skipped merge (${reason ?? 'no reason'}) and links the pull request`, () => {
        // Act
        const callout = buildCallout(
          detail('review', { checksOutcome: 'passed', mergeOutcome: 'skipped', mergeSkippedReason: reason }),
          null,
          summary({
            state: 'review',
            checksOutcome: 'passed',
            mergeOutcome: 'skipped',
            mergeSkippedReason: reason,
            pullRequestUrl: 'https://github.com/acme/api/pull/7',
          }),
          null,
        );

        // Assert
        expect(callout.tone).toBe('warning');
        expect(callout.title).toBe('Merge skipped');
        expect(callout.lines).toEqual([line, 'Foundry addresses review comments in a revision run.']);
        expect(callout.links.map((l) => l.label)).toEqual(['View PR']);
      });
    }

    it('explains a skipped merge on GitLab in merge-request terms', () => {
      // Act
      const callout = buildCallout(
        detail('review', { mergeOutcome: 'skipped', mergeSkippedReason: 'head_moved' }),
        null,
        summary({ state: 'review', providerType: 'gitlab', mergeOutcome: 'skipped', mergeSkippedReason: 'head_moved' }),
        null,
      );

      // Assert
      expect(callout.lines[0]).toBe(
        "Checks passed, but new commits reached the merge request after the checks ran, so Foundry didn't merge "
          + 'unchecked work. Review the merge request on GitLab.',
      );
    });

    it('shows a review whose merge setting was off as ready for review', () => {
      // Act
      const callout = buildCallout(
        detail('review', { mergeOutcome: 'disabled' }),
        null,
        summary({ state: 'review', mergeOutcome: 'disabled' }),
        null,
      );

      // Assert
      expect(callout.title).toBe('PR ready for review');
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

  describe('catch-up queue (ADR 0111)', () => {
    const PR_URL = 'https://github.com/acme/api/pull/7';
    const PR_LINK = { label: 'View PR', href: PR_URL, ariaLabel: 'View pull request for issue #42' };

    function catchUp(
      state: IssueDetail['state'],
      details: Partial<IssueStateDetails>,
      providerType = 'github',
      pullRequestUrl: string | null = PR_URL,
    ) {
      return buildCallout(detail(state, details), null, summary({ state, providerType, pullRequestUrl }), null);
    }

    describe('rebase_pending', () => {
      it('explains a catch-up rebase with its round, linking the pull request', () => {
        // Act
        const callout = catchUp('rebase_pending', {
          rebasePendingReason: 'catch_up',
          catchUpRound: 1,
          catchUpRoundLimit: 3,
        });

        // Assert
        expect(callout.tone).toBe('working');
        expect(callout.title).toBe('Catching up (round 1 of 3)');
        expect(callout.lines).toEqual([
          'The pull request fell behind the default branch, so GitHub refused the merge.',
          'Foundry is rebasing it onto the default branch; checks run again before it merges.',
        ]);
        expect(callout.links).toEqual([PR_LINK]);
        expect(callout.chip).toBeNull();
        expect(callout.rows).toEqual([]);
      });

      it('titles a catch-up rebase without a round as catching up', () => {
        // Act
        const callout = catchUp('rebase_pending', { rebasePendingReason: 'catch_up', catchUpRound: null });

        // Assert
        expect(callout.title).toBe('Catching up');
      });

      for (const [name, reason] of [
        ['after_run', 'after_run'],
        ['null', null],
        ['an unknown value', 'from_the_future'],
      ] as const) {
        it(`keeps today's rebasing copy when the reason is ${name}`, () => {
          // Act
          const callout = catchUp('rebase_pending', {
            rebasePendingReason: reason as IssueStateDetails['rebasePendingReason'],
          });

          // Assert
          expect(callout.tone).toBe('working');
          expect(callout.title).toBe('Rebasing');
          expect(callout.lines).toEqual(['Foundry is rebasing the branch onto the default branch.']);
          expect(callout.links).toEqual([]);
        });
      }
    });

    describe('checks_pending', () => {
      it('explains checks on the rebased commits of a catch-up, linking the pull request', () => {
        // Act
        const callout = catchUp('checks_pending', {
          checksPendingReason: 'catch_up',
          catchUpRound: 2,
          catchUpRoundLimit: 3,
        });

        // Assert
        expect(callout.tone).toBe('working');
        expect(callout.title).toBe('Catching up (round 2 of 3)');
        expect(callout.lines).toEqual([
          'Foundry rebased the pull request onto the default branch after it fell behind.',
          'Waiting for CI checks on the rebased commits before merging.',
        ]);
        expect(callout.links).toEqual([PR_LINK]);
      });

      it('explains a catch-up on GitLab in merge-request terms', () => {
        // Act
        const callout = catchUp('checks_pending', { checksPendingReason: 'catch_up', catchUpRound: 1 }, 'gitlab', null);

        // Assert
        expect(callout.title).toBe('Catching up (round 1)');
        expect(callout.lines[0]).toBe('Foundry rebased the merge request onto the default branch after it fell behind.');
        expect(callout.links).toEqual([]);
      });
    });

    describe('merge_pending', () => {
      it('explains a pull request next in the catch-up queue', () => {
        // Act
        const callout = catchUp('merge_pending', { mergeWait: { position: 1, waitingForIssueNumber: null } });

        // Assert
        expect(callout.tone).toBe('neutral');
        expect(callout.title).toBe('Waiting to catch up');
        expect(callout.lines).toEqual([
          'Checks passed, but the pull request fell behind the default branch, so GitHub refused the merge.',
          'Next to catch up.',
          'Foundry rebases one pull request at a time in this repository and reruns its checks before merging.',
        ]);
        expect(callout.links).toEqual([PR_LINK]);
        expect(callout.rows).toEqual([]);
      });

      it('names a later place in the catch-up queue', () => {
        // Act
        const callout = catchUp('merge_pending', { mergeWait: { position: 3, waitingForIssueNumber: null } });

        // Assert
        expect(callout.lines[1]).toBe('#3 in the catch-up queue.');
      });

      it('names the up-to-date issue the queue head waits for', () => {
        // Act
        const callout = catchUp('merge_pending', { mergeWait: { position: 1, waitingForIssueNumber: 57 } });

        // Assert
        expect(callout.lines).toEqual([
          'Checks passed, but the pull request fell behind the default branch, so GitHub refused the merge.',
          'Next to catch up.',
          'It waits for #57, whose pull request is already up to date, to finish its checks and merge first.',
          'Foundry rebases one pull request at a time in this repository and reruns its checks before merging.',
        ]);
      });

      it('shows the rounds a waiting pull request has used', () => {
        // Act
        const callout = catchUp('merge_pending', {
          mergeWait: { position: 2, waitingForIssueNumber: null },
          catchUpRound: 1,
          catchUpRoundLimit: 3,
        });

        // Assert
        expect(callout.rows).toEqual([
          { key: 'Catch-up rounds', value: '1 of 3', href: null, ariaLabel: null, mono: false },
        ]);
      });

      it('shows no rounds for a waiting pull request with no round', () => {
        // Act
        const callout = catchUp('merge_pending', {
          mergeWait: { position: 2, waitingForIssueNumber: null },
          catchUpRound: null,
          catchUpRoundLimit: 3,
        });

        // Assert
        expect(callout.rows).toEqual([]);
      });

      it('explains a merge after a catch-up round', () => {
        // Act
        const callout = catchUp('merge_pending', { mergeWait: null, catchUpRound: 1, catchUpRoundLimit: 3 });

        // Assert
        expect(callout.tone).toBe('working');
        expect(callout.title).toBe('Merging');
        expect(callout.lines).toEqual([
          'Checks passed after catch-up round 1 of 3. Foundry is merging the pull request on GitHub.',
        ]);
        expect(callout.links).toEqual([PR_LINK]);
      });

      it('names the round alone when the limit is missing', () => {
        // Act
        const callout = catchUp('merge_pending', { catchUpRound: 1, catchUpRoundLimit: null });

        // Assert
        expect(callout.lines).toEqual([
          'Checks passed after catch-up round 1. Foundry is merging the pull request on GitHub.',
        ]);
      });

      it("keeps today's merging copy with no wait and no round", () => {
        // Act
        const callout = catchUp('merge_pending', { mergeWait: null, catchUpRound: null });

        // Assert
        expect(callout.tone).toBe('working');
        expect(callout.title).toBe('Merging');
        expect(callout.lines).toEqual(['Checks passed. Foundry is merging the pull request on GitHub.']);
        expect(callout.links).toEqual([]);
      });
    });

    describe('review, merge skipped', () => {
      function skipped(reason: string, rebaseSkippedReason: string | null) {
        return buildCallout(
          detail('review', { mergeOutcome: 'skipped', mergeSkippedReason: reason, rebaseSkippedReason }),
          null,
          summary({
            state: 'review',
            mergeOutcome: 'skipped',
            mergeSkippedReason: reason,
            rebaseSkippedReason,
            pullRequestUrl: PR_URL,
          }),
          null,
        );
      }

      for (const [reason, rebaseSkippedReason, line] of [
        [
          'fell_behind',
          null,
          'Checks passed, but the pull request kept falling behind the default branch as other merges landed, so '
            + 'Foundry stopped catching it up. Update the branch on GitHub and merge the pull request yourself.',
        ],
        [
          'catch_up_failed',
          'force-push-protected',
          "Checks passed, but the pull request fell behind the default branch and Foundry couldn't rebase it "
            + '(force-push protected). Update the branch on GitHub and merge the pull request yourself.',
        ],
        [
          'catch_up_failed',
          null,
          "Checks passed, but the pull request fell behind the default branch and Foundry couldn't rebase it. "
            + 'Update the branch on GitHub and merge the pull request yourself.',
        ],
        [
          'catch_up_failed',
          'from_the_future',
          "Checks passed, but the pull request fell behind the default branch and Foundry couldn't rebase it "
            + '(from_the_future). Update the branch on GitHub and merge the pull request yourself.',
        ],
      ] as const) {
        it(`explains a skipped merge (${reason}, rebase ${rebaseSkippedReason ?? 'no reason'}) and links the pull request`, () => {
          // Act
          const callout = skipped(reason, rebaseSkippedReason);

          // Assert
          expect(callout.tone).toBe('warning');
          expect(callout.title).toBe('Merge skipped');
          expect(callout.lines).toEqual([line, 'Foundry addresses review comments in a revision run.']);
          expect(callout.links.map((l) => l.label)).toEqual(['View PR']);
        });
      }
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

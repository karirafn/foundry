import { actionConfirmation, actionSuccessMessage, confirmFalsePositivesCopy } from './issue-action-copy';

describe('issue action copy', () => {
  it.each([
    { kind: 'retry_issue' },
    { kind: 'retry_triage' },
    { kind: 'retry_post' },
    { kind: 'retry_review' },
    { kind: 'confirm_false_positives', workerRunId: 'run-1', publishBlockedAt: '2026-11-14T09:00:00+00:00' },
  ] as const)('runs %o without a confirmation', (action) => {
    // Act
    const confirmation = actionConfirmation(action);

    // Assert
    expect(confirmation).toBeNull();
  });

  it('confirms Retry publish as non-destructive, naming the branch', () => {
    // Act
    const confirmation = actionConfirmation({ kind: 'retry_publish', workerRunId: 'run-1', branchName: 'feat/42-dark-mode' });

    // Assert
    expect(confirmation).toEqual({
      title: 'Retry publishing?',
      message: "Foundry pushes the run's commits to feat/42-dark-mode and opens or updates the pull request. CI runs on the push.",
      confirmLabel: 'Retry publish',
      cancelLabel: 'Not now',
      destructive: false,
    });
  });

  it('names the issue branch when the run has no branch', () => {
    // Act
    const confirmation = actionConfirmation({ kind: 'retry_publish', workerRunId: 'run-1', branchName: null });

    // Assert
    expect(confirmation?.message).toContain("Foundry pushes the run's commits to the issue branch and");
  });

  it('names the provider in the Discard run confirmation, falling back when it is unknown', () => {
    // Act
    const gitlab = actionConfirmation({ kind: 'discard_run', workerRunId: 'run-1', providerType: 'gitlab' });
    const unknown = actionConfirmation({ kind: 'discard_run', workerRunId: 'run-1', providerType: '' });

    // Assert
    expect(gitlab?.message).toContain('Retry issue starts a new worker from the branch on GitLab.');
    expect(gitlab?.destructive).toBe(true);
    expect(unknown?.message).toContain('from the branch on the provider.');
  });

  it('words the Stop worker confirmation for a triage run', () => {
    // Act
    const confirmation = actionConfirmation({ kind: 'stop_worker', workerRunId: 'run-1', triageRun: true });

    // Assert
    expect(confirmation?.title).toBe('Stop the triage worker?');
    expect(confirmation?.message).toBe('The issue moves to Triage failed, where you can retry or skip triage.');
  });

  it('tells the Re-triage confirmation of a blocked issue that triage waits for its blockers', () => {
    // Act
    const queued = actionConfirmation({ kind: 'retriage', issueNumber: 42, blocked: false });
    const blocked = actionConfirmation({ kind: 'retriage', issueNumber: 42, blocked: true });

    // Assert
    expect(queued?.title).toBe('Triage #42 again?');
    expect(queued?.message).not.toContain('While its blockers are open');
    expect(blocked?.message).toContain('While its blockers are open it waits untriaged, and triage runs once they close.');
  });

  it('tells the Continue confirmation of a triage question that the issue skips triage', () => {
    // Act
    const confirmation = actionConfirmation({ kind: 'continue_without_answer', issueNumber: 42, resumesTriage: true });

    // Assert
    expect(confirmation?.message).toContain('as Skip triage does');
  });

  it('announces nothing once a stop succeeds', () => {
    // Act
    const message = actionSuccessMessage('stop_worker');

    // Assert
    expect(message).toBeNull();
  });

  it.each([
    [0, 'Publish with possible secrets?'],
    [1, 'Publish with 1 possible secret?'],
    [3, 'Publish with 3 possible secrets?'],
  ])('titles the false-positive confirmation for %i findings', (count, title) => {
    // Act
    const copy = confirmFalsePositivesCopy(count, 'feat/42-dark-mode', 'github');

    // Assert
    expect(copy.title).toBe(title);
  });
});

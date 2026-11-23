import { TestBed } from '@angular/core/testing';
import { Subject, of } from 'rxjs';
import { vi } from 'vitest';
import { WorkerRunService } from '../workers/worker-run.service';
import { IssueActionRequest, IssueActionsService } from './issue-actions.service';
import { IssueService } from './issue.service';

function fakeIssueService() {
  return {
    retryIssue: vi.fn((_id: string) => of<string | null>(null)),
    retryIssueFresh: vi.fn((_id: string) => of<string | null>(null)),
    retryTriage: vi.fn((_id: string) => of<string | null>(null)),
    skipTriage: vi.fn((_id: string) => of<string | null>(null)),
    retriage: vi.fn((_id: string) => of<string | null>(null)),
    continueWithoutAnswer: vi.fn((_id: string) => of<string | null>(null)),
    retryQuestionPost: vi.fn((_id: string) => of<string | null>(null)),
    retryReview: vi.fn((_id: string) => of<string | null>(null)),
  };
}

function fakeWorkerRunService() {
  return {
    requestCancel: vi.fn((_runId: string) => of<string | null>(null)),
    requestPublishRetry: vi.fn((_runId: string) => of<string | null>(null)),
    requestDiscard: vi.fn((_runId: string) => of<string | null>(null)),
    requestConfirmFalsePositives: vi.fn((_runId: string, _publishBlockedAt: string) => of<string | null>(null)),
  };
}

function setup() {
  const issues = fakeIssueService();
  const runs = fakeWorkerRunService();
  TestBed.configureTestingModule({
    providers: [
      { provide: IssueService, useValue: issues },
      { provide: WorkerRunService, useValue: runs },
    ],
  });
  return { service: TestBed.inject(IssueActionsService), issues, runs };
}

function openDialog(): HTMLDialogElement | null {
  return Array.from(document.querySelectorAll<HTMLDialogElement>('dialog.confirm-dialog')).find((d) => d.open) ?? null;
}

async function answerDialog(button: '.confirm-dialog__confirm' | '.confirm-dialog__cancel'): Promise<void> {
  (openDialog()!.querySelector(button) as HTMLButtonElement).click();
  await Promise.resolve();
}

describe('IssueActionsService', () => {
  afterEach(() => {
    document.querySelectorAll('body > fd-confirm-dialog').forEach((dialog) => dialog.remove());
  });

  it('reads an issue as not pending while an action on another issue is', () => {
    // Arrange
    const { service, issues } = setup();
    issues.retryIssue.mockReturnValue(new Subject<string | null>());

    // Act
    service.perform('issue-a', { kind: 'retry_issue' });

    // Assert
    expect(service.pending('issue-a')()).toBe(true);
    expect(service.pendingAction('issue-a')()).toBe('retry_issue');
    expect(service.pending('issue-b')()).toBe(false);
    expect(service.pendingAction('issue-b')()).toBeNull();
  });

  it('clears pending and records the success with its announcement once the call succeeds', () => {
    // Arrange
    const { service, issues } = setup();
    const response = new Subject<string | null>();
    issues.retryTriage.mockReturnValue(response);
    const action: IssueActionRequest = { kind: 'retry_triage' };
    service.perform('issue-a', action);

    // Act
    response.next(null);

    // Assert
    expect(service.pending('issue-a')()).toBe(false);
    expect(service.success('issue-a')()).toEqual({ action, message: 'Triage queued again. Issue status is updating.' });
    expect(service.error('issue-a')()).toBeNull();
    expect(service.success('issue-b')()).toBeNull();
  });

  it('carries the failure message and the failed action on the issue only', () => {
    // Arrange
    const { service, issues } = setup();
    issues.retryReview.mockReturnValue(of("This issue's review is no longer skipped."));

    // Act
    service.perform('issue-a', { kind: 'retry_review' });

    // Assert
    expect(service.pending('issue-a')()).toBe(false);
    expect(service.error('issue-a')()).toBe("This issue's review is no longer skipped.");
    expect(service.failure('issue-a')()).toEqual({ action: 'retry_review', message: "This issue's review is no longer skipped." });
    expect(service.success('issue-a')()).toBeNull();
    expect(service.error('issue-b')()).toBeNull();
  });

  it('ignores another action on an issue while one is pending', () => {
    // Arrange
    const { service, issues } = setup();
    issues.retryIssue.mockReturnValue(new Subject<string | null>());
    service.perform('issue-a', { kind: 'retry_issue' });

    // Act
    service.perform('issue-a', { kind: 'retry_issue' });

    // Assert
    expect(issues.retryIssue).toHaveBeenCalledTimes(1);
  });

  it('clears an earlier error and success when the issue starts another action', () => {
    // Arrange
    const { service, issues } = setup();
    service.perform('issue-a', { kind: 'retry_post' });
    issues.retryQuestionPost.mockReturnValue(of('Failed to retry posting the questions.'));
    service.perform('issue-a', { kind: 'retry_post' });
    const errorBefore = service.error('issue-a')();
    issues.retryQuestionPost.mockReturnValue(new Subject<string | null>());

    // Act
    service.perform('issue-a', { kind: 'retry_post' });

    // Assert
    expect(errorBefore).toBe('Failed to retry posting the questions.');
    expect(service.error('issue-a')()).toBeNull();
    expect(service.success('issue-a')()).toBeNull();
  });

  it('clears the error of an issue on request', () => {
    // Arrange
    const { service, issues } = setup();
    issues.retryIssue.mockReturnValue(of('Failed to retry issue.'));
    service.perform('issue-a', { kind: 'retry_issue' });

    // Act
    service.clearError('issue-a');

    // Assert
    expect(service.error('issue-a')()).toBeNull();
  });

  it('runs an action without a confirmation straight away', () => {
    // Arrange
    const { service, issues } = setup();

    // Act
    service.perform('issue-a', { kind: 'retry_issue' });

    // Assert
    expect(issues.retryIssue).toHaveBeenCalledWith('issue-a');
    expect(openDialog()).toBeNull();
  });

  it('confirms with the action copy before running it', async () => {
    // Arrange
    const { service, runs } = setup();
    service.perform('issue-a', { kind: 'retry_publish', workerRunId: 'run-1', branchName: 'feat/42-dark-mode' });
    const title = openDialog()?.querySelector('.confirm-dialog__title')?.textContent;
    const called = runs.requestPublishRetry.mock.calls.length;

    // Act
    await answerDialog('.confirm-dialog__confirm');

    // Assert
    expect(title).toBe('Retry publishing?');
    expect(called).toBe(0);
    expect(runs.requestPublishRetry).toHaveBeenCalledWith('run-1');
    expect(service.success('issue-a')()?.message).toBe('Publish retry started. Issue status is updating.');
  });

  it('does not run the action when its confirmation is dismissed', async () => {
    // Arrange
    const { service, runs } = setup();
    service.perform('issue-a', { kind: 'discard_run', workerRunId: 'run-1', providerType: 'github' });

    // Act
    await answerDialog('.confirm-dialog__cancel');

    // Assert
    expect(runs.requestDiscard).not.toHaveBeenCalled();
    expect(service.pending('issue-a')()).toBe(false);
  });

  it.each([
    [{ kind: 'retry_fresh' }, 'retryIssueFresh', ['issue-a']],
    [{ kind: 'skip_triage', issueNumber: 42 }, 'skipTriage', ['issue-a']],
    [{ kind: 'retriage', issueNumber: 42, blocked: false }, 'retriage', ['issue-a']],
    [{ kind: 'continue_without_answer', issueNumber: 42, resumesTriage: false }, 'continueWithoutAnswer', ['issue-a']],
    [{ kind: 'stop_worker', workerRunId: 'run-1', triageRun: false }, 'requestCancel', ['run-1']],
  ] as const)('calls the endpoint of %o once confirmed', async (action, method, args) => {
    // Arrange
    const { service, issues, runs } = setup();
    const calls: Record<string, ReturnType<typeof vi.fn>> = { ...issues, ...runs };
    service.perform('issue-a', action as IssueActionRequest);

    // Act
    await answerDialog('.confirm-dialog__confirm');

    // Assert
    expect(calls[method]).toHaveBeenCalledWith(...args);
  });

  it.each([
    [{ kind: 'retry_triage' }, 'retryTriage', ['issue-a']],
    [{ kind: 'retry_post' }, 'retryQuestionPost', ['issue-a']],
    [{ kind: 'retry_review' }, 'retryReview', ['issue-a']],
    [
      { kind: 'confirm_false_positives', workerRunId: 'run-1', publishBlockedAt: '2026-11-14T09:00:00+00:00' },
      'requestConfirmFalsePositives',
      ['run-1', '2026-11-14T09:00:00+00:00'],
    ],
  ] as const)('calls the endpoint of %o straight away', (action, method, args) => {
    // Arrange
    const { service, issues, runs } = setup();
    const calls: Record<string, ReturnType<typeof vi.fn>> = { ...issues, ...runs };

    // Act
    service.perform('issue-a', action as IssueActionRequest);

    // Assert
    expect(calls[method]).toHaveBeenCalledWith(...args);
    expect(openDialog()).toBeNull();
  });
});

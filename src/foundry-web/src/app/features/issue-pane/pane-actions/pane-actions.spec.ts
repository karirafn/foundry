import { TestBed } from '@angular/core/testing';
import { Subject, of } from 'rxjs';
import { IssueDetail, IssueStateDetails } from '../../issues/issue.model';
import { IssueActionsService } from '../../issues/issue-actions.service';
import { IssueService } from '../../issues/issue.service';
import { WorkerRunDetail } from '../../workers/worker-run.model';
import { WorkerRunService } from '../../workers/worker-run.service';
import { PaneAction } from '../pane-callout.model';
import { PaneActionsComponent } from './pane-actions';

const stateDetails: IssueStateDetails = {
  workerRunId: 'run-1',
  branchName: 'feat/42-dark-mode',
  feedbackCutoffAt: null,
  failureReason: null,
  failedAt: null,
  completedAt: null,
  blockedBy: null,
  violations: null,
  transientRetry: null,
};

function issue(state: IssueDetail['state']): IssueDetail {
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
    stateDetails,
    kind: { source: 'labelled', kind: 'feature', conflictingTriageKind: null },
    triage: null,
    clarifications: [],
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
    requestCancel: vi.fn(() => of<string | null>(null)),
    requestPublishRetry: vi.fn(() => of<string | null>(null)),
    requestDiscard: vi.fn(() => of<string | null>(null)),
    requestConfirmFalsePositives: vi.fn((_runId: string, _publishBlockedAt: string) => of<string | null>(null)),
  };
}

interface SetupOptions {
  run?: WorkerRunDetail | null;
  providerType?: string;
  workerRuns?: ReturnType<typeof fakeWorkerRunService>;
  service?: ReturnType<typeof fakeIssueService>;
}

function setup(state: IssueDetail['state'], actions: PaneAction[], options: SetupOptions = {}) {
  const service = options.service ?? fakeIssueService();
  const workerRuns = options.workerRuns ?? fakeWorkerRunService();
  TestBed.configureTestingModule({
    imports: [PaneActionsComponent],
    providers: [
      { provide: IssueService, useValue: service },
      { provide: WorkerRunService, useValue: workerRuns },
    ],
  });
  const fixture = TestBed.createComponent(PaneActionsComponent);
  fixture.componentRef.setInput('detail', issue(state));
  fixture.componentRef.setInput('run', options.run === undefined ? run() : options.run);
  fixture.componentRef.setInput('actions', actions);
  fixture.componentRef.setInput('providerType', options.providerType ?? 'github');
  const succeeded: PaneAction[] = [];
  fixture.componentInstance.succeeded.subscribe((a) => succeeded.push(a));
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  const render = () => fixture.detectChanges();
  const buttons = () => Array.from(el.querySelectorAll<HTMLButtonElement>('.pane-actions button'));
  const button = (label: string) => buttons().find((b) => b.textContent?.trim() === label) ?? null;
  const openDialog = () => Array.from(document.querySelectorAll<HTMLDialogElement>('dialog.confirm-dialog')).find((d) => d.open) ?? null;
  const confirm = async () => {
    (openDialog()!.querySelector('.confirm-dialog__confirm') as HTMLButtonElement).click();
    await fixture.whenStable();
    render();
  };
  const dismiss = async () => {
    (openDialog()!.querySelector('.confirm-dialog__cancel') as HTMLButtonElement).click();
    await fixture.whenStable();
    render();
  };
  const error = () => el.querySelector('.pane-actions__error[role="alert"]')?.textContent?.trim() ?? '';
  return { fixture, el, service, workerRuns, succeeded, render, buttons, button, openDialog, confirm, dismiss, error };
}

describe('PaneActionsComponent', () => {
  afterEach(() => {
    document.querySelectorAll('body > fd-confirm-dialog').forEach((dialog) => dialog.remove());
  });

  it('renders the actions in order, the first filled and the rest outlined', async () => {
    // Act
    const { buttons } = setup('continuable_failed', ['retry_issue', 'retry_fresh']);

    // Assert
    expect(buttons().map((b) => b.textContent?.trim())).toEqual(['Retry issue', 'Retry fresh']);
    expect(buttons()[0].classList.contains('pane-actions__btn--primary')).toBe(true);
    expect(buttons()[1].classList.contains('pane-actions__btn--primary')).toBe(false);
  });

  it('renders no button row and no dialog without actions, keeping the announcements mounted', async () => {
    // Act
    const { el } = setup('completed', []);

    // Assert
    expect(el.querySelector('.pane-actions')).toBeNull();
    expect(el.querySelector('fd-confirm-dialog')).toBeNull();
    expect(el.querySelectorAll('.pane-actions__success[aria-live="polite"]').length).toBe(5);
  });

  describe('Stop worker', () => {
    it('opens a confirm dialog worded for an implement run instead of stopping straight away', async () => {
      // Arrange
      const { button, openDialog, render, workerRuns } = setup('in_progress', ['stop_worker']);

      // Act
      button('Stop worker')!.click();
      render();

      // Assert
      expect(button('Stop worker')?.getAttribute('aria-label')).toBe('Stop worker for issue #42');
      expect(openDialog()?.textContent).toContain('Stop the worker?');
      expect(openDialog()?.textContent).toContain("Foundry publishes the run's commits to the branch; Retry continues from them.");
      expect(openDialog()?.querySelector('.confirm-dialog__cancel')?.textContent?.trim()).toBe('Keep running');
      expect(workerRuns.requestCancel).not.toHaveBeenCalled();
    });

    it('words the dialog for a triage run', async () => {
      // Arrange
      const { button, openDialog, render } = setup('triage_in_progress', ['stop_worker']);

      // Act
      button('Stop worker')!.click();
      render();

      // Assert
      expect(openDialog()?.textContent).toContain('Stop the triage worker?');
      expect(openDialog()?.textContent).toContain('The issue moves to Triage failed, where you can retry or skip triage.');
    });

    it('requests the stop and shows Stopping… once confirmed', async () => {
      // Arrange
      const { button, render, confirm, workerRuns } = setup('in_progress', ['stop_worker']);
      button('Stop worker')!.click();
      render();

      // Act
      await confirm();

      // Assert
      expect(workerRuns.requestCancel).toHaveBeenCalledWith('run-1');
      expect(button('Stopping…')?.disabled).toBe(true);
    });

    it('does not stop the worker when the dialog is dismissed', async () => {
      // Arrange
      const { button, render, dismiss, workerRuns } = setup('in_progress', ['stop_worker']);
      button('Stop worker')!.click();
      render();

      // Act
      await dismiss();

      // Assert
      expect(workerRuns.requestCancel).not.toHaveBeenCalled();
      expect(button('Stop worker')).not.toBeNull();
    });

    it('shows Stopping… on load when the run already carries a stop request', async () => {
      // Act
      const { button } = setup('in_progress', ['stop_worker'], { run: run({ cancelRequestedAt: '2026-11-04T10:00:00Z' }) });

      // Assert
      expect(button('Stopping…')?.disabled).toBe(true);
    });

    it.each([
      ['starting', run({ state: 'starting' })],
      ['not loaded', null],
    ])('disables the button with a Starting… hint while the run is %s', async (_label, current) => {
      // Act
      const { el, button } = setup('in_progress', ['stop_worker'], { run: current });

      // Assert
      const stop = button('Stop worker')!;
      expect(stop.disabled).toBe(true);
      const hintId = stop.getAttribute('aria-describedby');
      expect(el.querySelector(`[id="${hintId}"]`)?.textContent?.trim()).toBe('Starting…');
    });

    it('restores the button and shows the error in the alert when the stop fails', async () => {
      // Arrange
      const workerRuns = fakeWorkerRunService();
      workerRuns.requestCancel.mockReturnValue(of('The run is no longer running.'));
      const { button, render, confirm, error } = setup('in_progress', ['stop_worker'], { workerRuns });
      button('Stop worker')!.click();
      render();

      // Act
      await confirm();

      // Assert
      expect(button('Stop worker')).not.toBeNull();
      expect(error()).toBe('The run is no longer running.');
    });
  });

  describe('Retry issue and Retry fresh', () => {
    it('retries straight away and reads Retrying issue… while pending', async () => {
      // Arrange
      const service = fakeIssueService();
      service.retryIssue.mockReturnValue(new Subject<string | null>());
      const { button, render } = setup('failed', ['retry_issue'], { service });

      // Act
      button('Retry issue')!.click();
      render();

      // Assert
      expect(service.retryIssue).toHaveBeenCalledWith('abc');
      expect(button('Retrying issue…')?.disabled).toBe(true);
    });

    it('names the issue in the Retry issue label', async () => {
      // Act
      const { button } = setup('failed', ['retry_issue']);

      // Assert
      expect(button('Retry issue')?.getAttribute('aria-label')).toBe('Retry issue #42');
    });

    it('shows the retry error in the alert', async () => {
      // Arrange
      const service = fakeIssueService();
      service.retryIssue.mockReturnValue(of('Failed to retry issue.'));
      const { button, render, error } = setup('failed', ['retry_issue'], { service });

      // Act
      button('Retry issue')!.click();
      render();

      // Assert
      expect(error()).toBe('Failed to retry issue.');
    });

    it('confirms Retry fresh as destructive before retrying from a fresh branch', async () => {
      // Arrange
      const { button, render, openDialog, confirm, service } = setup('continuable_failed', ['retry_issue', 'retry_fresh']);

      // Act
      button('Retry fresh')!.click();
      render();
      const dialog = openDialog();
      const text = dialog?.textContent ?? '';
      await confirm();

      // Assert
      expect(text).toContain('Retry from a fresh branch?');
      expect(dialog?.querySelector('.confirm-dialog__confirm')?.textContent?.trim()).toBe('Discard branch and retry');
      expect(service.retryIssueFresh).toHaveBeenCalledWith('abc');
    });

    it('does not retry fresh when the confirmation is dismissed', async () => {
      // Arrange
      const { button, render, dismiss, service } = setup('continuable_failed', ['retry_issue', 'retry_fresh']);
      button('Retry fresh')!.click();
      render();

      // Act
      await dismiss();

      // Assert
      expect(service.retryIssueFresh).not.toHaveBeenCalled();
    });

    it('announces the retry success politely and reports the action as succeeded', async () => {
      // Arrange
      const response = new Subject<string | null>();
      const service = fakeIssueService();
      service.retryIssue.mockReturnValue(response);
      const { el, button, render, succeeded } = setup('failed', ['retry_issue'], { service });
      button('Retry issue')!.click();

      // Act
      response.next(null);
      render();

      // Assert
      expect(el.querySelector('.pane-actions__success--retry')?.textContent?.trim()).toBe(
        'Retry queued. Issue status is updating.',
      );
      expect(succeeded).toEqual(['retry_issue']);
    });
  });

  describe('triage actions', () => {
    it('retries triage straight away, with no confirmation', async () => {
      // Arrange
      const { button, openDialog, render, service } = setup('triage_failed', ['retry_triage', 'skip_triage']);

      // Act
      button('Retry triage')!.click();
      render();

      // Assert
      expect(service.retryTriage).toHaveBeenCalledWith('abc');
      expect(openDialog()).toBeNull();
    });

    it('confirms Skip triage with its copy before skipping', async () => {
      // Arrange
      const { button, render, openDialog, confirm, service } = setup('triage_failed', ['retry_triage', 'skip_triage']);

      // Act
      button('Skip triage')!.click();
      render();
      const text = openDialog()?.textContent ?? '';
      await confirm();

      // Assert
      expect(text).toContain('Skip triage for #42?');
      expect(text).toContain('The issue is queued without a readiness check.');
      expect(service.skipTriage).toHaveBeenCalledWith('abc');
    });

    it('confirms Re-triage from queued with its copy', async () => {
      // Arrange
      const { button, render, openDialog, confirm, service } = setup('queued', ['retriage']);

      // Act
      button('Re-triage')!.click();
      render();
      const text = openDialog()?.textContent ?? '';
      await confirm();

      // Assert
      expect(text).toContain('Triage #42 again?');
      expect(text).toContain('The issue leaves the queue and a triage worker reads it again.');
      expect(text).not.toContain('While its blockers are open');
      expect(service.retriage).toHaveBeenCalledWith('abc');
    });

    it('tells the Re-triage dialog from blocked that triage waits for open blockers', async () => {
      // Arrange
      const { button, render, openDialog } = setup('blocked', ['retriage']);

      // Act
      button('Re-triage')!.click();
      render();

      // Assert
      expect(openDialog()?.textContent).toContain('While its blockers are open it waits untriaged, and triage runs once they close.');
    });

    it('shows Re-triaging… and disables Re-triage while it is pending', async () => {
      // Arrange
      const service = fakeIssueService();
      service.retriage.mockReturnValue(new Subject<string | null>());
      const { button, render, confirm } = setup('queued', ['retriage'], { service });
      button('Re-triage')!.click();
      render();

      // Act
      await confirm();

      // Assert
      expect(button('Re-triaging…')?.disabled).toBe(true);
    });

    it('shows a triage action error in the alert', async () => {
      // Arrange
      const service = fakeIssueService();
      service.retryTriage.mockReturnValue(of('Failed to retry triage.'));
      const { button, render, error } = setup('triage_failed', ['retry_triage', 'skip_triage'], { service });

      // Act
      button('Retry triage')!.click();
      render();

      // Assert
      expect(error()).toBe('Failed to retry triage.');
    });

    it('reports a triage action as succeeded while an earlier retry success is still shown', async () => {
      // Arrange
      const response = new Subject<string | null>();
      const service = fakeIssueService();
      service.retriage.mockReturnValue(response);
      const { button, render, confirm, succeeded } = setup('queued', ['retriage'], { service });
      TestBed.inject(IssueActionsService).perform('abc', { kind: 'retry_issue' });
      render();
      button('Re-triage')!.click();
      render();
      await confirm();

      // Act
      response.next(null);
      render();

      // Assert
      expect(succeeded).toEqual(['retriage']);
    });

    it('keeps the triage success announcement once the actions are gone', async () => {
      // Arrange
      const response = new Subject<string | null>();
      const service = fakeIssueService();
      service.retryTriage.mockReturnValue(response);
      const { fixture, el, button, render, succeeded } = setup('triage_failed', ['retry_triage', 'skip_triage'], { service });
      button('Retry triage')!.click();

      // Act
      response.next(null);
      fixture.componentRef.setInput('detail', issue('triage_queued'));
      fixture.componentRef.setInput('actions', []);
      render();

      // Assert
      expect(el.querySelector('.pane-actions__success--triage')?.textContent?.trim()).toBe(
        'Triage queued again. Issue status is updating.',
      );
      expect(succeeded).toEqual(['retry_triage']);
    });
  });

  describe('review actions', () => {
    it('retries a skipped review straight away, with no confirmation', async () => {
      // Arrange
      const { button, openDialog, render, service } = setup('review', ['retry_review']);

      // Act
      button('Retry review')!.click();
      render();

      // Assert
      expect(service.retryReview).toHaveBeenCalledWith('abc');
      expect(openDialog()).toBeNull();
    });

    it('names the issue in the Retry review button for screen readers', async () => {
      // Arrange
      const { button } = setup('review', ['retry_review']);

      // Act
      const label = button('Retry review')!.getAttribute('aria-label');

      // Assert
      expect(label).toBe('Retry review for issue #42');
    });

    it('reads Retrying review… and is disabled while the retry is pending', async () => {
      // Arrange
      const service = fakeIssueService();
      service.retryReview.mockReturnValue(new Subject<string | null>());
      const { el, button, render } = setup('review', ['retry_review'], { service });

      // Act
      button('Retry review')!.click();
      render();

      // Assert
      const retry = el.querySelector<HTMLButtonElement>('button[aria-label="Retry review for issue #42"]')!;
      expect(retry.textContent?.trim()).toBe('Retrying review…');
      expect(retry.disabled).toBe(true);
    });

    it('announces the retry once the action is gone', async () => {
      // Arrange
      const response = new Subject<string | null>();
      const service = fakeIssueService();
      service.retryReview.mockReturnValue(response);
      const { fixture, el, button, render, succeeded } = setup('review', ['retry_review'], { service });
      button('Retry review')!.click();

      // Act
      response.next(null);
      fixture.componentRef.setInput('detail', issue('review_queued'));
      fixture.componentRef.setInput('actions', []);
      render();

      // Assert
      expect(el.querySelector('.pane-actions__success--triage')?.textContent?.trim()).toBe(
        'Review queued again. Issue status is updating.',
      );
      expect(succeeded).toEqual(['retry_review']);
    });

    it('shows the conflict message when the review is no longer skipped', async () => {
      // Arrange
      const service = fakeIssueService();
      service.retryReview.mockReturnValue(of("This issue's review is no longer skipped."));
      const { button, render, error } = setup('review', ['retry_review'], { service });

      // Act
      button('Retry review')!.click();
      render();

      // Assert
      expect(error()).toBe("This issue's review is no longer skipped.");
    });
  });

  describe('awaiting-input actions', () => {
    it('retries the question post straight away, with no confirmation', async () => {
      // Arrange
      const { button, openDialog, render, service } = setup('awaiting_input', ['retry_post', 'continue_without_answer']);

      // Act
      button('Retry post')!.click();
      render();

      // Assert
      expect(service.retryQuestionPost).toHaveBeenCalledWith('abc');
      expect(openDialog()).toBeNull();
    });

    it('confirms Continue without answer, saying the run cannot ask again, before continuing', async () => {
      // Arrange
      const { button, render, openDialog, confirm, service } = setup('awaiting_input', ['continue_without_answer']);

      // Act
      button('Continue without answer')!.click();
      render();
      const text = openDialog()?.textContent ?? '';
      await confirm();

      // Assert
      expect(text).toContain('Continue #42 without an answer?');
      expect(text).toContain('cannot ask again in that run');
      expect(service.continueWithoutAnswer).toHaveBeenCalledWith('abc');
    });

    it('tells the Continue dialog of a triage question that the issue skips triage', async () => {
      // Arrange
      const { fixture, button, render, openDialog } = setup('awaiting_input', ['continue_without_answer']);
      fixture.componentRef.setInput('detail', {
        ...issue('awaiting_input'),
        stateDetails: {
          ...stateDetails,
          awaitingInput: {
            questions: [{ $type: 'legacy', question: 'Which API?', context: '' }],
            postStatus: 'posted',
            postedAt: '2026-01-01T00:00:00Z',
            commentUrl: null,
            postFailureReason: null,
            nextPostAttemptAt: null,
            resumeTarget: 'retriage',
            reminderSentAt: null,
            reminderDueAt: null,
          },
        },
      });
      render();

      // Act
      button('Continue without answer')!.click();
      render();

      // Assert
      expect(openDialog()?.textContent).toContain('as Skip triage does');
    });

    it('does not continue when the dialog is dismissed', async () => {
      // Arrange
      const { button, render, dismiss, service } = setup('awaiting_input', ['continue_without_answer']);

      // Act
      button('Continue without answer')!.click();
      render();
      await dismiss();

      // Assert
      expect(service.continueWithoutAnswer).not.toHaveBeenCalled();
    });

    it('shows Continuing… and disables both buttons while Continue is pending', async () => {
      // Arrange
      const service = fakeIssueService();
      service.continueWithoutAnswer.mockReturnValue(new Subject<string | null>());
      const { button, render, confirm } = setup('awaiting_input', ['retry_post', 'continue_without_answer'], { service });
      button('Continue without answer')!.click();
      render();

      // Act
      await confirm();

      // Assert
      expect(button('Continuing…')?.disabled).toBe(true);
      expect(button('Retry post')?.disabled).toBe(true);
    });

    it('shows an awaiting-input action error in the alert', async () => {
      // Arrange
      const service = fakeIssueService();
      service.continueWithoutAnswer.mockReturnValue(of('This issue is no longer awaiting input.'));
      const { button, render, confirm, error } = setup('awaiting_input', ['continue_without_answer'], { service });
      button('Continue without answer')!.click();
      render();

      // Act
      await confirm();

      // Assert
      expect(error()).toBe('This issue is no longer awaiting input.');
    });
  });

  describe('Retry publish and Discard run', () => {
    it('opens a non-destructive confirm naming the branch instead of retrying straight away', async () => {
      // Arrange
      const { button, render, openDialog, workerRuns } = setup('publish_failed', ['retry_publish', 'discard_run']);

      // Act
      button('Retry publish')!.click();
      render();

      // Assert
      expect(button('Retry publish')?.getAttribute('aria-label')).toBe('Retry publishing for issue #42');
      expect(openDialog()?.textContent).toContain('Retry publishing?');
      expect(openDialog()?.textContent).toContain("Foundry pushes the run's commits to feat/42-dark-mode");
      expect(openDialog()?.querySelector('.confirm-dialog__confirm--destructive')).toBeNull();
      expect(workerRuns.requestPublishRetry).not.toHaveBeenCalled();
    });

    it('disables both buttons and reads Retrying publish… while pending', async () => {
      // Arrange
      const workerRuns = fakeWorkerRunService();
      const response = new Subject<string | null>();
      workerRuns.requestPublishRetry.mockReturnValue(response);
      const { button, render, confirm } = setup('publish_failed', ['retry_publish', 'discard_run'], { workerRuns });
      button('Retry publish')!.click();
      render();

      // Act
      await confirm();

      // Assert
      expect(workerRuns.requestPublishRetry).toHaveBeenCalledWith('run-1');
      expect(button('Retrying publish…')?.disabled).toBe(true);
      expect(button('Discard run')?.disabled).toBe(true);
    });

    it('announces the publish retry success across the reload that removes the button', async () => {
      // Arrange
      const { fixture, el, button, render, confirm, succeeded } = setup('publish_failed', ['retry_publish', 'discard_run']);
      button('Retry publish')!.click();
      render();
      await confirm();

      // Act
      fixture.componentRef.setInput('detail', issue('in_progress'));
      fixture.componentRef.setInput('actions', []);
      render();

      // Assert
      expect(el.querySelector('.pane-actions__success--publish-retry')?.textContent?.trim()).toBe(
        'Publish retry started. Issue status is updating.',
      );
      expect(succeeded).toEqual(['retry_publish']);
    });

    it('opens a destructive confirm naming the provider before discarding', async () => {
      // Arrange
      const { button, render, openDialog, workerRuns } = setup('publish_blocked', ['discard_run'], { providerType: 'gitlab' });

      // Act
      button('Discard run')!.click();
      render();

      // Assert
      const dialog = openDialog();
      expect(dialog?.textContent).toContain('Discard this run?');
      expect(dialog?.textContent).toContain('Retry issue starts a new worker from the branch on GitLab.');
      expect(dialog?.querySelector('.confirm-dialog__cancel')?.textContent?.trim()).toBe('Keep run');
      expect(dialog?.querySelector('.confirm-dialog__confirm--destructive')).not.toBeNull();
      expect(workerRuns.requestDiscard).not.toHaveBeenCalled();
    });

    it('discards once confirmed and announces it across the reload', async () => {
      // Arrange
      const { fixture, el, button, render, confirm, workerRuns, succeeded } = setup('publish_blocked', ['discard_run']);
      button('Discard run')!.click();
      render();

      // Act
      await confirm();
      fixture.componentRef.setInput('detail', issue('failed'));
      fixture.componentRef.setInput('actions', ['retry_issue']);
      render();

      // Assert
      expect(workerRuns.requestDiscard).toHaveBeenCalledWith('run-1');
      expect(el.querySelector('.pane-actions__success--discard')?.textContent?.trim()).toBe(
        'Run discarded. Issue status is updating.',
      );
      expect(succeeded).toEqual(['discard_run']);
    });

    it('does not discard when the dialog is dismissed', async () => {
      // Arrange
      const { button, render, dismiss, workerRuns } = setup('publish_blocked', ['discard_run']);
      button('Discard run')!.click();
      render();

      // Act
      await dismiss();

      // Assert
      expect(workerRuns.requestDiscard).not.toHaveBeenCalled();
    });

    it('shows the discard error in the alert', async () => {
      // Arrange
      const workerRuns = fakeWorkerRunService();
      workerRuns.requestDiscard.mockReturnValue(of('The run holds no unpublished work.'));
      const { button, render, confirm, error } = setup('publish_blocked', ['discard_run'], { workerRuns });
      button('Discard run')!.click();
      render();

      // Act
      await confirm();

      // Assert
      expect(error()).toBe('The run holds no unpublished work.');
      expect(button('Discard run')?.disabled).toBe(false);
    });
  });

  describe('Confirm all as false positives', () => {
    const BLOCKED_AT = '2026-11-14T09:00:00+00:00';
    const FINDINGS = [
      { path: 'src/appsettings.json', line: 14, commitSha: 'abc1234', ruleId: 'aws-access-key-id' },
      { path: 'tests/fixtures/user.json', line: 3, commitSha: 'abc1234', ruleId: 'generic-api-key' },
      { path: 'scripts/deploy.sh', line: 27, commitSha: 'def5678', ruleId: 'generic-api-key' },
    ];
    const BLOCKED_ACTIONS: PaneAction[] = ['discard_run', 'confirm_false_positives'];
    const CONFIRM_LABEL = 'Confirm all as false positives and publish';

    function blockedRun(overrides: Partial<WorkerRunDetail> = {}): WorkerRunDetail {
      return run({ state: 'publish_blocked', secretFindings: FINDINGS, publishBlockedAt: BLOCKED_AT, ...overrides });
    }

    function setupBlocked(options: SetupOptions = {}) {
      return setup('publish_blocked', BLOCKED_ACTIONS, { run: blockedRun(), ...options });
    }

    function openConfirm(ctx: ReturnType<typeof setupBlocked>) {
      ctx.button(CONFIRM_LABEL)!.click();
      ctx.render();
    }

    function acknowledgement(ctx: ReturnType<typeof setupBlocked>): HTMLInputElement {
      return ctx.openDialog()!.querySelector('input[type="checkbox"]') as HTMLInputElement;
    }

    function acknowledge(ctx: ReturnType<typeof setupBlocked>) {
      const box = acknowledgement(ctx);
      box.click();
      ctx.render();
    }

    function confirmButton(ctx: ReturnType<typeof setupBlocked>): HTMLButtonElement {
      return ctx.openDialog()!.querySelector('.confirm-dialog__confirm') as HTMLButtonElement;
    }

    it('offers Discard run then the confirmation, neither filled, each named for the issue', async () => {
      // Act
      const { buttons, button } = setupBlocked();

      // Assert
      expect(buttons().map((b) => b.textContent?.trim())).toEqual(['Discard run', CONFIRM_LABEL]);
      expect(buttons().some((b) => b.classList.contains('pane-actions__btn--primary'))).toBe(false);
      expect(button('Discard run')?.classList.contains('pane-actions__btn--destructive')).toBe(true);
      expect(button(CONFIRM_LABEL)?.classList.contains('pane-actions__btn--neutral')).toBe(true);
      expect(button('Discard run')?.getAttribute('aria-label')).toBe('Discard run for issue #42');
      expect(button(CONFIRM_LABEL)?.getAttribute('aria-label')).toBe(
        'Confirm all findings as false positives and publish issue #42',
      );
    });

    it('opens a destructive dialog naming the count, branch and provider, without confirming straight away', async () => {
      // Arrange
      const ctx = setupBlocked({ providerType: 'gitlab' });

      // Act
      openConfirm(ctx);

      // Assert
      const dialog = ctx.openDialog();
      expect(dialog?.querySelector('.confirm-dialog__title')?.textContent).toBe('Publish with 3 possible secrets?');
      expect(dialog?.querySelector('.confirm-dialog__message')?.textContent).toBe(
        "Foundry pushes the run's commits to feat/42-dark-mode, including the content these findings flagged. Once "
          + "pushed, it stays in the repository's history on GitLab, and CI runs on the push. This cannot be undone.",
      );
      expect(dialog?.querySelector('.confirm-dialog__confirm--destructive')?.textContent?.trim()).toBe('Confirm and publish');
      expect(dialog?.querySelector('.confirm-dialog__cancel')?.textContent?.trim()).toBe('Keep blocked');
      expect(ctx.workerRuns.requestConfirmFalsePositives).not.toHaveBeenCalled();
    });

    it('words the dialog title for a single finding', async () => {
      // Arrange
      const ctx = setup('publish_blocked', BLOCKED_ACTIONS, { run: blockedRun({ secretFindings: [FINDINGS[0]] }) });

      // Act
      openConfirm(ctx);

      // Assert
      expect(ctx.openDialog()?.querySelector('.confirm-dialog__title')?.textContent).toBe('Publish with 1 possible secret?');
    });

    it('lists every finding in a focusable scroll region named by the findings heading', async () => {
      // Arrange
      const ctx = setupBlocked();

      // Act
      openConfirm(ctx);

      // Assert
      const region = ctx.openDialog()!.querySelector('.pane-actions__findings-scroll') as HTMLElement;
      expect(region.getAttribute('tabindex')).toBe('0');
      expect(region.getAttribute('aria-labelledby')).toBe('confirm-findings-42');
      expect(region.querySelector('#confirm-findings-42')?.textContent?.trim()).toBe('Findings');
      const rows = Array.from(region.querySelectorAll('li')).map((li) => [
        li.querySelector('.secret-findings__path')?.textContent,
        li.querySelector('.secret-findings__line')?.textContent,
        li.querySelector('.secret-findings__rule code')?.textContent,
      ]);
      expect(rows).toEqual([
        ['src/appsettings.json', 'line 14', 'aws-access-key-id'],
        ['tests/fixtures/user.json', 'line 3', 'generic-api-key'],
        ['scripts/deploy.sh', 'line 27', 'generic-api-key'],
      ]);
    });

    it('says the confirmation covers only these findings, and labels the acknowledgement', async () => {
      // Arrange
      const ctx = setupBlocked();

      // Act
      openConfirm(ctx);

      // Assert
      const dialog = ctx.openDialog()!;
      expect(dialog.textContent).toContain(
        'This confirms only these findings. If the push turns up others, it is blocked again.',
      );
      const box = acknowledgement(ctx);
      expect(box.id).toBe('confirm-false-positives-ack-42');
      expect(dialog.querySelector(`label[for="${box.id}"]`)?.textContent?.trim()).toBe(
        'I checked every finding, and none is a real credential.',
      );
    });

    it('keeps Confirm and publish disabled until the acknowledgement is checked', async () => {
      // Arrange
      const ctx = setupBlocked();
      openConfirm(ctx);
      const disabledBefore = confirmButton(ctx).disabled;

      // Act
      acknowledge(ctx);

      // Assert
      expect(disabledBefore).toBe(true);
      expect(confirmButton(ctx).disabled).toBe(false);
    });

    it('unchecks the acknowledgement each time the dialog opens', async () => {
      // Arrange
      const ctx = setupBlocked();
      openConfirm(ctx);
      acknowledge(ctx);
      await ctx.dismiss();

      // Act
      openConfirm(ctx);

      // Assert
      expect(acknowledgement(ctx).checked).toBe(false);
      expect(confirmButton(ctx).disabled).toBe(true);
    });

    it('does not confirm when the dialog is dismissed', async () => {
      // Arrange
      const ctx = setupBlocked();
      openConfirm(ctx);
      acknowledge(ctx);

      // Act
      await ctx.dismiss();

      // Assert
      expect(ctx.workerRuns.requestConfirmFalsePositives).not.toHaveBeenCalled();
    });

    it('sends the block time of the run it rendered once confirmed', async () => {
      // Arrange
      const ctx = setupBlocked();
      openConfirm(ctx);
      acknowledge(ctx);

      // Act
      await ctx.confirm();

      // Assert
      expect(ctx.workerRuns.requestConfirmFalsePositives).toHaveBeenCalledWith('run-1', BLOCKED_AT);
    });

    it('reads Confirming… and disables Discard run while the confirmation is pending', async () => {
      // Arrange
      const workerRuns = fakeWorkerRunService();
      workerRuns.requestConfirmFalsePositives.mockReturnValue(new Subject<string | null>());
      const ctx = setupBlocked({ workerRuns });
      openConfirm(ctx);
      acknowledge(ctx);

      // Act
      await ctx.confirm();

      // Assert
      expect(ctx.button('Confirming…')?.disabled).toBe(true);
      expect(ctx.button('Discard run')?.disabled).toBe(true);
    });

    it('disables the confirmation while a discard is pending', async () => {
      // Arrange
      const workerRuns = fakeWorkerRunService();
      workerRuns.requestDiscard.mockReturnValue(new Subject<string | null>());
      const ctx = setupBlocked({ workerRuns });
      ctx.button('Discard run')!.click();
      ctx.render();

      // Act
      await ctx.confirm();

      // Assert
      expect(ctx.button('Discarding…')?.disabled).toBe(true);
      expect(ctx.button(CONFIRM_LABEL)?.disabled).toBe(true);
    });

    it('disables the confirmation until the run with its findings has loaded', async () => {
      // Act
      const { button } = setup('publish_blocked', BLOCKED_ACTIONS, { run: null });

      // Assert
      expect(button(CONFIRM_LABEL)?.disabled).toBe(true);
    });

    it.each([
      ['a 409', 'The findings changed or this run is no longer blocked. Review the current state.'],
      ['a 404', 'The run was not found.'],
      ['any other failure', 'Failed to confirm the findings.'],
    ])('shows the error for %s in the alert and restores both buttons', async (_case, message) => {
      // Arrange
      const workerRuns = fakeWorkerRunService();
      workerRuns.requestConfirmFalsePositives.mockReturnValue(of(message));
      const ctx = setupBlocked({ workerRuns });
      openConfirm(ctx);
      acknowledge(ctx);

      // Act
      await ctx.confirm();

      // Assert
      expect(ctx.error()).toBe(message);
      expect(ctx.button(CONFIRM_LABEL)?.disabled).toBe(false);
      expect(ctx.button('Discard run')?.disabled).toBe(false);
    });

    it('clears the confirmation and discard errors when the dialog opens', async () => {
      // Arrange
      const workerRuns = fakeWorkerRunService();
      workerRuns.requestConfirmFalsePositives.mockReturnValue(of('Failed to confirm the findings.'));
      const ctx = setupBlocked({ workerRuns });
      openConfirm(ctx);
      acknowledge(ctx);
      await ctx.confirm();
      const errorBefore = ctx.error();

      // Act
      openConfirm(ctx);

      // Assert
      expect(errorBefore).toBe('Failed to confirm the findings.');
      expect(ctx.error()).toBe('');
    });

    it('announces the success across the reload and reports the action as succeeded', async () => {
      // Arrange
      const ctx = setupBlocked();
      openConfirm(ctx);
      acknowledge(ctx);

      // Act
      await ctx.confirm();
      ctx.fixture.componentRef.setInput('detail', issue('in_progress'));
      ctx.fixture.componentRef.setInput('actions', []);
      ctx.render();

      // Assert
      expect(ctx.el.querySelector('.pane-actions__success--confirm-false-positives')?.textContent?.trim()).toBe(
        'Findings confirmed. Publishing restarted; issue status is updating.',
      );
      expect(ctx.succeeded).toEqual(['confirm_false_positives']);
    });

    it('closes the dialog when the detail reloads out of publish_blocked', async () => {
      // Arrange
      const ctx = setupBlocked();
      openConfirm(ctx);

      // Act
      ctx.fixture.componentRef.setInput('detail', issue('failed'));
      ctx.fixture.componentRef.setInput('actions', ['retry_issue']);
      ctx.render();

      // Assert
      expect(ctx.openDialog()).toBeNull();
    });

    it('closes the dialog when the run is blocked again on a new finding set', async () => {
      // Arrange
      const ctx = setupBlocked();
      openConfirm(ctx);

      // Act
      ctx.fixture.componentRef.setInput('run', blockedRun({ publishBlockedAt: '2026-11-14T10:00:00+00:00' }));
      ctx.render();

      // Assert
      expect(ctx.openDialog()).toBeNull();
    });

    it('offers no control to reveal, show or copy a secret anywhere', async () => {
      // Arrange
      const ctx = setupBlocked();

      // Act
      openConfirm(ctx);

      // Assert
      expect(ctx.el.textContent).not.toMatch(/reveal|show secret|copy/i);
      const labels = Array.from(ctx.el.querySelectorAll('[aria-label], [title]'))
        .map((e) => `${e.getAttribute('aria-label') ?? ''} ${e.getAttribute('title') ?? ''}`);
      expect(labels.join(' ')).not.toMatch(/reveal|show secret|copy/i);
    });
  });
});

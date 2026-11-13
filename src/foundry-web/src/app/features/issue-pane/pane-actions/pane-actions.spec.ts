import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { Subject, of } from 'rxjs';
import { IssueDetail, IssueStateDetails } from '../../issues/issue.model';
import { IssueService, TriageAction } from '../../issues/issue.service';
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
    repositoryId: 'repo-1',
    enteredStateAt: '2026-01-01T00:00:00Z',
    author: 'dev',
    labels: [],
    stateDetails,
    kind: { source: 'labelled', kind: 'feature', conflictingTriageKind: null },
    triage: null,
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

function fakeIssueService() {
  return {
    retrying: signal(false),
    retryError: signal<string | null>(null),
    retrySuccess: signal<string | null>(null),
    triageActionPending: signal<TriageAction | null>(null),
    triageActionError: signal<string | null>(null),
    triageActionSuccess: signal<string | null>(null),
    retryIssue: vi.fn(),
    retryIssueFresh: vi.fn(),
    retryTriage: vi.fn(),
    skipTriage: vi.fn(),
    retriage: vi.fn(),
  };
}

function fakeWorkerRunService() {
  return {
    requestCancel: vi.fn(() => of<string | null>(null)),
    requestPublishRetry: vi.fn(() => of<string | null>(null)),
    requestDiscard: vi.fn(() => of<string | null>(null)),
  };
}

interface SetupOptions {
  run?: WorkerRunDetail | null;
  providerType?: string;
  workerRuns?: ReturnType<typeof fakeWorkerRunService>;
}

function setup(state: IssueDetail['state'], actions: PaneAction[], options: SetupOptions = {}) {
  const service = fakeIssueService();
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
  const openDialog = () => Array.from(el.querySelectorAll<HTMLDialogElement>('dialog.confirm-dialog')).find((d) => d.open) ?? null;
  const confirm = () => {
    (openDialog()!.querySelector('.confirm-dialog__confirm') as HTMLButtonElement).click();
    render();
  };
  const dismiss = () => {
    (openDialog()!.querySelector('.confirm-dialog__cancel') as HTMLButtonElement).click();
    render();
  };
  const error = () => el.querySelector('.pane-actions__error[role="alert"]')?.textContent?.trim() ?? '';
  return { fixture, el, service, workerRuns, succeeded, render, buttons, button, openDialog, confirm, dismiss, error };
}

describe('PaneActionsComponent', () => {
  it('renders the actions in order, the first filled and the rest outlined', () => {
    // Act
    const { buttons } = setup('continuable_failed', ['retry_issue', 'retry_fresh']);

    // Assert
    expect(buttons().map((b) => b.textContent?.trim())).toEqual(['Retry issue', 'Retry fresh']);
    expect(buttons()[0].classList.contains('pane-actions__btn--primary')).toBe(true);
    expect(buttons()[1].classList.contains('pane-actions__btn--primary')).toBe(false);
  });

  it('renders no button row and no dialog without actions, keeping the announcements mounted', () => {
    // Act
    const { el } = setup('completed', []);

    // Assert
    expect(el.querySelector('.pane-actions')).toBeNull();
    expect(el.querySelector('fd-confirm-dialog')).toBeNull();
    expect(el.querySelectorAll('.pane-actions__success[aria-live="polite"]').length).toBe(4);
  });

  describe('Stop worker', () => {
    it('opens a confirm dialog worded for an implement run instead of stopping straight away', () => {
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

    it('words the dialog for a triage run', () => {
      // Arrange
      const { button, openDialog, render } = setup('triage_in_progress', ['stop_worker']);

      // Act
      button('Stop worker')!.click();
      render();

      // Assert
      expect(openDialog()?.textContent).toContain('Stop the triage worker?');
      expect(openDialog()?.textContent).toContain('The issue moves to Triage failed, where you can retry or skip triage.');
    });

    it('requests the stop and shows Stopping… once confirmed', () => {
      // Arrange
      const { button, render, confirm, workerRuns } = setup('in_progress', ['stop_worker']);
      button('Stop worker')!.click();
      render();

      // Act
      confirm();

      // Assert
      expect(workerRuns.requestCancel).toHaveBeenCalledWith('run-1');
      expect(button('Stopping…')?.disabled).toBe(true);
    });

    it('does not stop the worker when the dialog is dismissed', () => {
      // Arrange
      const { button, render, dismiss, workerRuns } = setup('in_progress', ['stop_worker']);
      button('Stop worker')!.click();
      render();

      // Act
      dismiss();

      // Assert
      expect(workerRuns.requestCancel).not.toHaveBeenCalled();
      expect(button('Stop worker')).not.toBeNull();
    });

    it('shows Stopping… on load when the run already carries a stop request', () => {
      // Act
      const { button } = setup('in_progress', ['stop_worker'], { run: run({ cancelRequestedAt: '2026-11-04T10:00:00Z' }) });

      // Assert
      expect(button('Stopping…')?.disabled).toBe(true);
    });

    it.each([
      ['starting', run({ state: 'starting' })],
      ['not loaded', null],
    ])('disables the button with a Starting… hint while the run is %s', (_label, current) => {
      // Act
      const { el, button } = setup('in_progress', ['stop_worker'], { run: current });

      // Assert
      const stop = button('Stop worker')!;
      expect(stop.disabled).toBe(true);
      const hintId = stop.getAttribute('aria-describedby');
      expect(el.querySelector(`[id="${hintId}"]`)?.textContent?.trim()).toBe('Starting…');
    });

    it('restores the button and shows the error in the alert when the stop fails', () => {
      // Arrange
      const workerRuns = fakeWorkerRunService();
      workerRuns.requestCancel.mockReturnValue(of('The run is no longer running.'));
      const { button, render, confirm, error } = setup('in_progress', ['stop_worker'], { workerRuns });
      button('Stop worker')!.click();
      render();

      // Act
      confirm();

      // Assert
      expect(button('Stop worker')).not.toBeNull();
      expect(error()).toBe('The run is no longer running.');
    });
  });

  describe('Retry issue and Retry fresh', () => {
    it('retries straight away and reads Retrying issue… while pending', () => {
      // Arrange
      const { button, render, service } = setup('failed', ['retry_issue']);

      // Act
      button('Retry issue')!.click();
      service.retrying.set(true);
      render();

      // Assert
      expect(service.retryIssue).toHaveBeenCalledWith('abc');
      expect(button('Retrying issue…')?.disabled).toBe(true);
    });

    it('names the issue in the Retry issue label', () => {
      // Act
      const { button } = setup('failed', ['retry_issue']);

      // Assert
      expect(button('Retry issue')?.getAttribute('aria-label')).toBe('Retry issue #42');
    });

    it('shows the retry error in the alert', () => {
      // Arrange
      const { render, service, error } = setup('failed', ['retry_issue']);

      // Act
      service.retryError.set('Failed to retry issue.');
      render();

      // Assert
      expect(error()).toBe('Failed to retry issue.');
    });

    it('confirms Retry fresh as destructive before retrying from a fresh branch', () => {
      // Arrange
      const { button, render, openDialog, confirm, service } = setup('continuable_failed', ['retry_issue', 'retry_fresh']);

      // Act
      button('Retry fresh')!.click();
      render();
      const dialog = openDialog();
      const text = dialog?.textContent ?? '';
      confirm();

      // Assert
      expect(text).toContain('Retry from a fresh branch?');
      expect(dialog?.querySelector('.confirm-dialog__confirm')?.textContent?.trim()).toBe('Discard branch and retry');
      expect(service.retryIssueFresh).toHaveBeenCalledWith('abc');
    });

    it('does not retry fresh when the confirmation is dismissed', () => {
      // Arrange
      const { button, render, dismiss, service } = setup('continuable_failed', ['retry_issue', 'retry_fresh']);
      button('Retry fresh')!.click();
      render();

      // Act
      dismiss();

      // Assert
      expect(service.retryIssueFresh).not.toHaveBeenCalled();
    });

    it('announces the retry success politely and reports the action as succeeded', () => {
      // Arrange
      const { el, button, render, service, succeeded } = setup('failed', ['retry_issue']);
      button('Retry issue')!.click();

      // Act
      service.retrySuccess.set('Retry queued. Issue status is updating.');
      render();

      // Assert
      expect(el.querySelector('.pane-actions__success--retry')?.textContent?.trim()).toBe(
        'Retry queued. Issue status is updating.',
      );
      expect(succeeded).toEqual(['retry_issue']);
    });
  });

  describe('triage actions', () => {
    it('retries triage straight away, with no confirmation', () => {
      // Arrange
      const { button, openDialog, render, service } = setup('triage_failed', ['retry_triage', 'skip_triage']);

      // Act
      button('Retry triage')!.click();
      render();

      // Assert
      expect(service.retryTriage).toHaveBeenCalledWith('abc');
      expect(openDialog()).toBeNull();
    });

    it('confirms Skip triage with its copy before skipping', () => {
      // Arrange
      const { button, render, openDialog, confirm, service } = setup('triage_failed', ['retry_triage', 'skip_triage']);

      // Act
      button('Skip triage')!.click();
      render();
      const text = openDialog()?.textContent ?? '';
      confirm();

      // Assert
      expect(text).toContain('Skip triage for #42?');
      expect(text).toContain('The issue is queued without a readiness check.');
      expect(service.skipTriage).toHaveBeenCalledWith('abc');
    });

    it('confirms Re-triage from queued with its copy', () => {
      // Arrange
      const { button, render, openDialog, confirm, service } = setup('queued', ['retriage']);

      // Act
      button('Re-triage')!.click();
      render();
      const text = openDialog()?.textContent ?? '';
      confirm();

      // Assert
      expect(text).toContain('Triage #42 again?');
      expect(text).toContain('The issue leaves the queue and a triage worker reads it again.');
      expect(text).not.toContain('While its blockers are open');
      expect(service.retriage).toHaveBeenCalledWith('abc');
    });

    it('tells the Re-triage dialog from blocked that triage waits for open blockers', () => {
      // Arrange
      const { button, render, openDialog } = setup('blocked', ['retriage']);

      // Act
      button('Re-triage')!.click();
      render();

      // Assert
      expect(openDialog()?.textContent).toContain('While its blockers are open it waits untriaged, and triage runs once they close.');
    });

    it('shows Re-triaging… and disables Re-triage while it is pending', () => {
      // Arrange
      const { button, render, service } = setup('queued', ['retriage']);

      // Act
      service.triageActionPending.set('retriage');
      render();

      // Assert
      expect(button('Re-triaging…')?.disabled).toBe(true);
    });

    it('shows a triage action error in the alert', () => {
      // Arrange
      const { render, service, error } = setup('triage_failed', ['retry_triage', 'skip_triage']);

      // Act
      service.triageActionError.set('Failed to retry triage.');
      render();

      // Assert
      expect(error()).toBe('Failed to retry triage.');
    });

    it('reports a triage action as succeeded while an earlier retry success is still shown', () => {
      // Arrange
      const { button, render, confirm, service, succeeded } = setup('queued', ['retriage']);
      service.retrySuccess.set('Retry queued. Issue status is updating.');
      render();
      button('Re-triage')!.click();
      render();
      confirm();

      // Act
      service.triageActionSuccess.set('Re-triage queued. Issue status is updating.');
      render();

      // Assert
      expect(succeeded).toEqual(['retriage']);
    });

    it('keeps the triage success announcement once the actions are gone', () => {
      // Arrange
      const { fixture, el, button, render, service, succeeded } = setup('triage_failed', ['retry_triage', 'skip_triage']);
      button('Retry triage')!.click();

      // Act
      service.triageActionSuccess.set('Triage queued again. Issue status is updating.');
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

  describe('Retry publish and Discard run', () => {
    it('opens a non-destructive confirm naming the branch instead of retrying straight away', () => {
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

    it('disables both buttons and reads Retrying publish… while pending', () => {
      // Arrange
      const workerRuns = fakeWorkerRunService();
      const response = new Subject<string | null>();
      workerRuns.requestPublishRetry.mockReturnValue(response);
      const { button, render, confirm } = setup('publish_failed', ['retry_publish', 'discard_run'], { workerRuns });
      button('Retry publish')!.click();
      render();

      // Act
      confirm();

      // Assert
      expect(workerRuns.requestPublishRetry).toHaveBeenCalledWith('run-1');
      expect(button('Retrying publish…')?.disabled).toBe(true);
      expect(button('Discard run')?.disabled).toBe(true);
    });

    it('announces the publish retry success across the reload that removes the button', () => {
      // Arrange
      const { fixture, el, button, render, confirm, succeeded } = setup('publish_failed', ['retry_publish', 'discard_run']);
      button('Retry publish')!.click();
      render();
      confirm();

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

    it('opens a destructive confirm naming the provider before discarding', () => {
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

    it('discards once confirmed and announces it across the reload', () => {
      // Arrange
      const { fixture, el, button, render, confirm, workerRuns, succeeded } = setup('publish_blocked', ['discard_run']);
      button('Discard run')!.click();
      render();

      // Act
      confirm();
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

    it('does not discard when the dialog is dismissed', () => {
      // Arrange
      const { button, render, dismiss, workerRuns } = setup('publish_blocked', ['discard_run']);
      button('Discard run')!.click();
      render();

      // Act
      dismiss();

      // Assert
      expect(workerRuns.requestDiscard).not.toHaveBeenCalled();
    });

    it('shows the discard error in the alert', () => {
      // Arrange
      const workerRuns = fakeWorkerRunService();
      workerRuns.requestDiscard.mockReturnValue(of('The run holds no unpublished work.'));
      const { button, render, confirm, error } = setup('publish_blocked', ['discard_run'], { workerRuns });
      button('Discard run')!.click();
      render();

      // Act
      confirm();

      // Assert
      expect(error()).toBe('The run holds no unpublished work.');
      expect(button('Discard run')?.disabled).toBe(false);
    });
  });
});

import { TestBed } from '@angular/core/testing';
import { formatDate } from '@angular/common';
import { PublishStatusComponent } from './publish-status';
import { IssueState, IssueStateDetails } from '../issue.model';
import { WorkerRunDetail, WorkerRunState } from '../../workers/worker-run.model';

const stateDetails: IssueStateDetails = {
  workerRunId: 'run-1',
  branchName: 'feat/42-add-retry',
  feedbackCutoffAt: null,
  failureReason: null,
  failedAt: null,
  completedAt: null,
  blockedBy: null,
  violations: null,
  transientRetry: null,
};

function run(state: WorkerRunState, overrides: Partial<WorkerRunDetail> = {}): WorkerRunDetail {
  return {
    workerRunId: 'run-1',
    issueId: 'issue-1',
    role: 'implement',
    state,
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
    ...overrides,
  };
}

function render(
  issueState: IssueState,
  workerRun: WorkerRunDetail | null,
  details: IssueStateDetails = stateDetails,
  providerType = 'github',
) {
  TestBed.configureTestingModule({ imports: [PublishStatusComponent] });
  const fixture = TestBed.createComponent(PublishStatusComponent);
  fixture.componentRef.setInput('issueState', issueState);
  fixture.componentRef.setInput('run', workerRun);
  fixture.componentRef.setInput('stateDetails', details);
  fixture.componentRef.setInput('providerType', providerType);
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  const region = el.querySelector('.publish-status') as HTMLElement;
  const text = (): string => (region.textContent ?? '').replace(/\s+/g, ' ').trim();
  return { fixture, el, region, text };
}

// Normalised like text(): the medium format can carry a narrow no-break space before AM/PM.
const medium = (iso: string): string => formatDate(iso, 'medium', 'en-US').replace(/\s+/g, ' ');

describe('PublishStatusComponent', () => {
  it('should keep an empty, mounted polite live region when no variant applies', () => {
    // Arrange / Act
    const { region, text } = render('in_progress', run('running'));

    // Assert
    expect(region).not.toBeNull();
    expect(region.getAttribute('role')).toBe('status');
    expect(region.getAttribute('aria-live')).toBe('polite');
    expect(region.getAttribute('aria-atomic')).toBe('true');
    expect(region.classList.contains('publish-status--empty')).toBe(true);
    expect(text()).toBe('');
  });

  it('should stay mounted while moving from one variant to the next', () => {
    // Arrange
    const { fixture, el, region } = render('in_progress', run('publishing'));

    // Act
    fixture.componentRef.setInput('run', run('publish_failed', {
      publishRetry: { attemptNumber: 2, maxAttempts: 5, nextAttemptAt: null, reason: 'network down' },
    }));
    fixture.detectChanges();

    // Assert
    expect(el.querySelector('.publish-status')).toBe(region);
    expect(region.textContent).toContain('Publish attempt 2 of 5');
  });

  describe('publishing', () => {
    it('should show the Publishing chip and the branch it publishes to, with neutral styling', () => {
      // Arrange / Act
      const { el, region, text } = render('in_progress', run('publishing'));

      // Assert
      expect(text()).toContain('Publish');
      const chip = el.querySelector('.publish-status__chip');
      expect(chip?.textContent?.trim()).toBe('Publishing');
      expect(chip?.classList.contains('badge--publishing')).toBe(true);
      expect(text()).toContain("Foundry is publishing the run's commits to feat/42-add-retry.");
      expect(region.classList.contains('publish-status--neutral')).toBe(true);
      expect(region.classList.contains('publish-status--empty')).toBe(false);
    });

    it('should show the publishing variant for a revision run', () => {
      // Arrange / Act
      const { el } = render('revision_in_progress', run('publishing'));

      // Assert
      expect(el.querySelector('.publish-status__chip')?.textContent?.trim()).toBe('Publishing');
    });
  });

  describe('retrying', () => {
    it('should show the attempt chip, the reason and the next attempt time, with warning styling', () => {
      // Arrange / Act
      const nextAttemptAt = '2026-11-11T10:15:00Z';
      const { el, region, text } = render('in_progress', run('publish_failed', {
        publishRetry: { attemptNumber: 2, maxAttempts: 5, nextAttemptAt, reason: 'the provider timed out' },
      }));

      // Assert
      const chip = el.querySelector('.publish-status__chip');
      expect(chip?.textContent?.trim()).toBe('Publish attempt 2 of 5');
      expect(chip?.classList.contains('badge--retrying')).toBe(true);
      expect(text()).toContain(`Publishing failed: the provider timed out. Next attempt at ${medium(nextAttemptAt)}.`);
      expect(region.classList.contains('publish-status--warning')).toBe(true);
    });

    it('should read "Next attempt shortly." when no next attempt time is known', () => {
      // Arrange / Act
      const { text } = render('in_progress', run('publish_failed', {
        publishRetry: { attemptNumber: 3, maxAttempts: 5, nextAttemptAt: null, reason: 'the provider timed out' },
      }));

      // Assert
      expect(text()).toContain('Publishing failed: the provider timed out. Next attempt shortly.');
    });
  });

  describe('exhausted', () => {
    it('should show the Publish failed chip, the reason, the guidance and the discard date, with error styling', () => {
      // Arrange / Act
      const discardsAt = '2026-11-20T08:00:00Z';
      const { el, region, text } = render('publish_failed', run('publish_failed', {
        publishRetry: { attemptNumber: 5, maxAttempts: 5, nextAttemptAt: null, reason: 'branch protection rejected the push' },
        discardsAt,
      }));

      // Assert
      const chip = el.querySelector('.publish-status__chip');
      expect(chip?.textContent?.trim()).toBe('Publish failed');
      expect(chip?.classList.contains('badge--retry-exhausted')).toBe(true);
      expect(text()).toContain(
        'Publishing failed after 5 attempts: branch protection rejected the push. Fix the cause, then use Retry publish. '
        + `Unpublished work is discarded on ${medium(discardsAt)}.`,
      );
      expect(region.classList.contains('publish-status--error')).toBe(true);
    });

    it('should leave out the discard sentence when no discard time is known', () => {
      // Arrange / Act
      const { text } = render('publish_failed', run('publish_failed', {
        publishRetry: { attemptNumber: 5, maxAttempts: 5, nextAttemptAt: null, reason: 'rejected' },
      }));

      // Assert
      expect(text()).toContain('Fix the cause, then use Retry publish.');
      expect(text()).not.toContain('discarded');
    });

    it('should show the exhausted variant before the run detail loads', () => {
      // Arrange / Act
      const { el } = render('publish_failed', null);

      // Assert
      expect(el.querySelector('.publish-status__chip')?.textContent?.trim()).toBe('Publish failed');
    });
  });

  describe('branch diverged', () => {
    const divergedDetails: IssueStateDetails = {
      ...stateDetails,
      branchUrl: 'https://github.com/owner/repo/tree/feat/42-add-retry',
      sideBranchName: 'feat/42-add-retry-run-abc123',
      sideBranchUrl: 'https://github.com/owner/repo/tree/feat/42-add-retry-run-abc123',
    };

    it('should explain the divergence with the Branch diverged chip and warning styling', () => {
      // Arrange / Act
      const { el, region, text } = render('branch_diverged', run('completed'), divergedDetails);

      // Assert
      const chip = el.querySelector('.publish-status__chip');
      expect(chip?.textContent?.trim()).toBe('Branch diverged');
      expect(chip?.classList.contains('badge--branch-diverged')).toBe(true);
      expect(text()).toContain(
        "Someone pushed to the issue branch while the worker ran, and merging the run's work conflicted. "
        + "Foundry pushed the run's work to a separate branch instead.",
      );
      expect(region.classList.contains('publish-status--warning')).toBe(true);
    });

    it('should link both branches with destination-specific labels', () => {
      // Arrange / Act
      const { el, text } = render('branch_diverged', run('completed'), divergedDetails);

      // Assert
      expect(text()).toContain('Issue branch');
      expect(text()).toContain("Run's work");
      const links = Array.from(el.querySelectorAll<HTMLAnchorElement>('.publish-status__branch-link'));
      expect(links.map((a) => a.textContent?.trim())).toEqual(['feat/42-add-retry', 'feat/42-add-retry-run-abc123']);
      expect(links[0].getAttribute('href')).toBe(divergedDetails.branchUrl);
      expect(links[0].getAttribute('aria-label')).toBe('View branch feat/42-add-retry on GitHub');
      expect(links[1].getAttribute('href')).toBe(divergedDetails.sideBranchUrl);
      expect(links[1].getAttribute('aria-label')).toBe("View the run's branch feat/42-add-retry-run-abc123 on GitHub");
    });

    it('should pass both branch links through safeHref, showing plain names for unsafe URLs', () => {
      // Arrange / Act
      const { el, text } = render('branch_diverged', run('completed'), {
        ...divergedDetails,
        branchUrl: 'javascript:alert(1)',
        sideBranchUrl: 'http://example.com/branch',
      });

      // Assert
      expect(el.querySelectorAll('.publish-status__branch-link').length).toBe(0);
      expect(text()).toContain('feat/42-add-retry');
      expect(text()).toContain('feat/42-add-retry-run-abc123');
    });

    it('should name the provider in the guidance', () => {
      // Arrange / Act
      const gitlab = render('branch_diverged', run('completed'), divergedDetails, 'gitlab');

      // Assert
      expect(gitlab.text()).toContain(
        "Merge the run's branch into the issue branch on GitLab. Foundry continues automatically once the merge lands, or use Retry Issue.",
      );
    });
  });
});

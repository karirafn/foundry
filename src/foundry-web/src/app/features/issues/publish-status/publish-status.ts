import { ChangeDetectionStrategy, Component, InputSignal, Signal, computed, input } from '@angular/core';
import { DatePipe } from '@angular/common';
import { IssueState, IssueStateDetails } from '../issue.model';
import { WorkerRunDetail } from '../../workers/worker-run.model';
import { WORKING_STATES } from '../../../shared/utils/issue-state';
import { providerDisplayName } from '../../../shared/utils/provider.util';
import { SafeHrefPipe } from '../../../shared/pipes/safe-href.pipe';
import { SecretFindingsComponent } from '../secret-findings/secret-findings';

type PublishVariant = 'publishing' | 'retrying' | 'exhausted' | 'blocked' | 'diverged';

const VARIANT_TINT: Readonly<Record<PublishVariant, 'neutral' | 'warning' | 'error'>> = {
  publishing: 'neutral',
  retrying: 'warning',
  exhausted: 'error',
  blocked: 'error',
  diverged: 'warning',
};

let nextId = 0;

/**
 * The issue's publish callout: Foundry publishing the run's commits, retrying a failed publish,
 * publishing exhausted, the push blocked by a detected secret, or the branch diverged. Display only. The region stays mounted (collapsed
 * when no variant applies) so screen readers announce each change of variant once.
 */
@Component({
  selector: 'fd-publish-status',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, SafeHrefPipe, SecretFindingsComponent],
  template: `
    @let v = variant();
    <div
      class="publish-status"
      [class.publish-status--empty]="v === null"
      [class.publish-status--neutral]="v !== null && tint() === 'neutral'"
      [class.publish-status--warning]="tint() === 'warning'"
      [class.publish-status--error]="tint() === 'error'"
      role="status"
      aria-live="polite"
      aria-atomic="true"
    >
      @if (v !== null) {
        <span class="publish-status__key">Publish</span>
        <div class="publish-status__callout">
          @switch (v) {
            @case ('publishing') {
              <span class="badge publish-status__chip badge--publishing">Publishing</span>
              <span class="publish-status__message">
                Foundry is publishing the run's commits to
                @if (branchName(); as branch) {
                  <span class="publish-status__branch">{{ branch }}</span>.
                } @else {
                  the issue branch.
                }
              </span>
            }
            @case ('retrying') {
              @if (run()?.publishRetry; as retry) {
                <span class="badge publish-status__chip badge--retrying"
                  >Publish attempt {{ retry.attemptNumber }} of {{ retry.maxAttempts }}</span>
                <span class="publish-status__message">
                  Publishing failed: {{ retry.reason }}.
                  @if (retry.nextAttemptAt) {
                    Next attempt at {{ retry.nextAttemptAt | date: 'medium' }}.
                  } @else {
                    Next attempt shortly.
                  }
                </span>
              } @else {
                <span class="badge publish-status__chip badge--retrying">Publish retry</span>
                <span class="publish-status__message">Publishing failed. Next attempt shortly.</span>
              }
            }
            @case ('exhausted') {
              <span class="badge publish-status__chip badge--retry-exhausted">Publish failed</span>
              <span class="publish-status__message">
                @if (run()?.publishRetry; as retry) {
                  Publishing failed after {{ retry.maxAttempts }} attempts: {{ retry.reason }}.
                } @else {
                  Publishing failed.
                }
                Fix the cause, then use Retry publish.
                @if (run()?.discardsAt; as discardsAt) {
                  Unpublished work is discarded on {{ discardsAt | date: 'medium' }}.
                }
              </span>
            }
            @case ('blocked') {
              <span class="badge publish-status__chip badge--retry-exhausted">Push blocked</span>
              <span class="publish-status__message">
                The diff gate found {{ secretCountText() }} in the run's changes, so nothing was pushed to
                @if (branchName(); as branch) {
                  <span class="publish-status__branch">{{ branch }}</span>.
                } @else {
                  the issue branch.
                }
              </span>
            }
            @case ('diverged') {
              <span class="badge publish-status__chip badge--branch-diverged">Branch diverged</span>
              <span class="publish-status__message">
                @if (forcePushProtected()) {
                  The worker rebased the issue branch onto the default branch, but {{ providerName() }} protects the
                  issue branch from force pushes. Foundry pushed the run's work to a separate branch instead.
                } @else {
                  Someone pushed to the issue branch while the worker ran, and the run's work could not join it.
                  Foundry pushed the run's work to a separate branch instead.
                }
              </span>
            }
          }
        </div>

        @if (v === 'blocked') {
          @if (run()?.secretFindings; as findings) {
            @if (findings.length > 0) {
              <fd-secret-findings [findings]="findings" [headingId]="findingsHeadingId" />
            }
          }
          <p class="publish-status__message publish-status__guidance">
            Foundry never publishes a run with a detected secret. Discard the run, then retry the issue to
            start a new worker from the branch. If a finding is a real credential, rotate it: the worker had
            it in its workspace.
            @if (run()?.discardsAt; as discardsAt) {
              If no one acts, the run is discarded automatically on {{ discardsAt | date: 'medium' }}.
            }
          </p>
        }

        @if (v === 'diverged') {
          <div class="publish-status__fields">
            <div class="publish-status__field">
              <span class="publish-status__key">Issue branch</span>
              @if (stateDetails().branchUrl | safeHref; as href) {
                <a
                  class="publish-status__branch publish-status__branch-link"
                  [href]="href"
                  target="_blank"
                  rel="noopener noreferrer"
                  [attr.aria-label]="branchLinkLabel()"
                >{{ branchName() }}</a>
              } @else {
                <span class="publish-status__branch">{{ branchName() }}</span>
              }
            </div>
            <div class="publish-status__field">
              <span class="publish-status__key">Run's work</span>
              @if (stateDetails().sideBranchUrl | safeHref; as href) {
                <a
                  class="publish-status__branch publish-status__branch-link"
                  [href]="href"
                  target="_blank"
                  rel="noopener noreferrer"
                  [attr.aria-label]="sideBranchLinkLabel()"
                >{{ stateDetails().sideBranchName }}</a>
              } @else {
                <span class="publish-status__branch">{{ stateDetails().sideBranchName }}</span>
              }
            </div>
          </div>
          <p class="publish-status__message publish-status__guidance">
            @if (forcePushProtected()) {
              <!-- The run's work is rebased: merging it into the issue branch would duplicate the branch's commits. -->
              Replace the issue branch with the run's branch on {{ providerName() }}, allowing a force push for it.
              Foundry continues automatically once the issue branch holds the run's work, or use Retry Issue.
            } @else {
              Merge the run's branch into the issue branch on {{ providerName() }}. Foundry continues
              automatically once the merge lands, or use Retry Issue.
            }
          </p>
        }
      }
    </div>
  `,
  styleUrl: './publish-status.scss',
})
export class PublishStatusComponent {
  readonly issueState: InputSignal<IssueState> = input.required<IssueState>();
  readonly run: InputSignal<WorkerRunDetail | null> = input.required<WorkerRunDetail | null>();
  readonly stateDetails: InputSignal<IssueStateDetails> = input.required<IssueStateDetails>();
  readonly providerType: InputSignal<string> = input.required<string>();

  protected readonly variant: Signal<PublishVariant | null> = computed(() => {
    const state = this.issueState();
    if (state === 'branch_diverged') {
      return 'diverged';
    }
    if (state === 'publish_failed') {
      return 'exhausted';
    }
    if (state === 'publish_blocked') {
      return 'blocked';
    }
    if (!WORKING_STATES.has(state)) {
      return null;
    }
    switch (this.run()?.state) {
      case 'publishing':
        return 'publishing';
      case 'publish_failed':
        return 'retrying';
      default:
        return null;
    }
  });

  protected readonly tint: Signal<'neutral' | 'warning' | 'error' | null> = computed(() => {
    const v = this.variant();
    return v === null ? null : VARIANT_TINT[v];
  });

  protected readonly findingsHeadingId: string = `publish-status-findings-${nextId++}`;

  protected readonly secretCountText: Signal<string> = computed(() => {
    const count = this.run()?.secretFindings?.length ?? 0;
    if (count === 0) {
      return 'possible secrets';
    }
    return count === 1 ? '1 possible secret' : `${count} possible secrets`;
  });

  protected readonly branchName: Signal<string | null> = computed(() => this.stateDetails().branchName);

  protected readonly forcePushProtected: Signal<boolean> = computed(
    () => this.stateDetails().divergenceReason === 'force-push-protected');

  protected readonly providerName: Signal<string> = computed(() => providerDisplayName(this.providerType()));

  protected readonly branchLinkLabel: Signal<string> = computed(
    () => `View branch ${this.branchName() ?? ''} on ${this.providerName()}`);

  protected readonly sideBranchLinkLabel: Signal<string> = computed(
    () => `View the run's branch ${this.stateDetails().sideBranchName ?? ''} on ${this.providerName()}`);
}

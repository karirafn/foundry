import {
  ChangeDetectionStrategy,
  Component,
  InputSignal,
  OutputEmitterRef,
  Signal,
  computed,
  inject,
  input,
  output,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { AgentActivity } from '../../../core/models/agent-activity.model';
import { WorkerSignalRService } from '../../../core/services/worker-signalr.service';
import { LifecycleRailComponent } from '../../../shared/components/lifecycle-rail/lifecycle-rail';
import { RunKindTagComponent } from '../../../shared/components/run-kind-tag/run-kind-tag';
import { SafeHrefPipe } from '../../../shared/pipes/safe-href.pipe';
import { formatElapsed } from '../../../shared/utils/agent-activity-format';
import { formatWaiting } from '../../../shared/utils/format-waiting';
import { providerDisplayName, providerTerminology } from '../../../shared/utils/provider.util';
import { ItemView } from '../board-state.service';
import { IssueRefComponent } from '../issue-ref/issue-ref';
import { LastEventComponent } from '../progress-band/worker-activity/last-event';
import { WorkerActivityComponent } from '../progress-band/worker-activity/worker-activity';
import {
  ActivityPanel,
  LastEventLine,
  activityPanel,
  lastEventLine,
} from '../progress-band/worker-activity/worker-activity.model';
import {
  LifecycleRowItem,
  RowStatus,
  currentStepLabel,
  isRunningRow,
  rowStatus,
} from './lifecycle-row.model';

interface ReviewLink {
  readonly href: string | null | undefined;
  readonly label: string;
  readonly hint: string;
}

/**
 * One issue in Needs you or In progress: its head, its whole lifecycle as a rail, and a status line
 * (why a person is needed, the running agent's activity, or the step Foundry's code runs).
 */
@Component({
  selector: 'fd-lifecycle-row',
  imports: [
    DatePipe,
    IssueRefComponent,
    LastEventComponent,
    LifecycleRailComponent,
    RunKindTagComponent,
    SafeHrefPipe,
    WorkerActivityComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '[attr.data-issue-id]': 'item().issue.id',
    '[style.view-transition-name]': "'fd-card-' + item().issue.id",
    '[style.view-transition-class]': "'fd-card'",
  },
  templateUrl: './lifecycle-row.html',
  styleUrl: './lifecycle-row.scss',
})
export class LifecycleRowComponent {
  readonly item: InputSignal<ItemView<LifecycleRowItem>> =
    input.required<ItemView<LifecycleRowItem>>();
  /** The current time, in epoch ms, from the board's shared tick. */
  readonly now: InputSignal<number> = input.required<number>();
  /** Emits the row's title button, so focus can return to it when the pane closes. */
  readonly open: OutputEmitterRef<HTMLElement> = output<HTMLElement>();

  private readonly _workerSignalR = inject(WorkerSignalRService);

  protected readonly idBase: Signal<string> = computed(
    () => `lifecycle-row-${this.item().issue.id}`,
  );
  protected readonly label: Signal<string> = computed(
    () => `${this.item().issue.title}, #${this.item().issue.issueNumber}`,
  );
  // A running Claude step is described by its task line and clock, not the bar between them.
  protected readonly describedBy: Signal<string> = computed(() => {
    const base = this.idBase();
    return this.status().kind === 'activity'
      ? `${base}-ref ${base}-task ${base}-clock`
      : `${base}-ref ${base}-status`;
  });
  protected readonly status: Signal<RowStatus> = computed(() =>
    rowStatus(this.item(), this.item().phase),
  );
  protected readonly running: Signal<boolean> = computed(() =>
    isRunningRow(this.item(), this.item().phase),
  );
  protected readonly stepLabel: Signal<string | null> = computed(() =>
    currentStepLabel(this.item().issue.lifecycle),
  );
  protected readonly elapsed: Signal<string> = computed(() =>
    formatElapsed(this.now() - Date.parse(this.item().issue.enteredStateAt)),
  );
  protected readonly waiting: Signal<string> = computed(() =>
    formatWaiting(this.now() - Date.parse(this.item().issue.enteredStateAt)),
  );

  private readonly _activity: Signal<AgentActivity | null> = computed(() =>
    this.status().kind === 'activity'
      ? this._workerSignalR.agentActivityForIssue(this.item().issue.id)
      : null,
  );
  protected readonly panel: Signal<ActivityPanel> = computed(() => activityPanel(this._activity()));
  protected readonly lastEvent: Signal<LastEventLine | null> = computed(() =>
    this.status().kind === 'activity' ? lastEventLine(this._activity(), this.now()) : null,
  );

  /** The row's edge: the needs reason's tone, or the state of what runs. */
  protected readonly tone: Signal<string> = computed(() => {
    const item = this.item();
    if (item.phase !== 'live') {
      return 'completed';
    }
    if (item.band === 'needs') {
      return item.reason.tone;
    }
    return this.lastEvent()?.kind === 'silent' ? 'warning' : 'working';
  });

  protected readonly reviewLink: Signal<ReviewLink | null> = computed(() => {
    const item = this.item();
    if (item.band !== 'needs') {
      return null;
    }
    const { issue, reason } = item;
    // A skipped merge or review still leaves a pull request for the person to review.
    const skipped = issue.mergeOutcome === 'skipped' || issue.reviewOutcome === 'skipped';
    const reviewable = reason.tone === 'ready' || (issue.state === 'review' && skipped);
    if (!reviewable || !issue.pullRequestUrl) {
      return null;
    }
    const provider = issue.providerType ?? 'github';
    return {
      href: issue.pullRequestUrl,
      label: `Review ${providerTerminology(provider).prAbbrev}`,
      hint: ` (opens ${providerDisplayName(provider)} in a new tab)`,
    };
  });
}

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
import { RouterLink } from '@angular/router';
import { AgentActivity, InFlightTool } from '../../../../core/models/agent-activity.model';
import { WorkerSignalRService } from '../../../../core/services/worker-signalr.service';
import { RoleTagComponent } from '../../../../shared/components/role-tag/role-tag';
import { formatElapsed } from '../../../../shared/utils/agent-activity-format';
import { ItemView } from '../../board-state.service';
import { WorkerItem } from '../../board.model';
import { IssueRefComponent } from '../../issue-ref/issue-ref';
import { WorkerActivityComponent } from '../worker-activity/worker-activity';
import {
  ActivityPanel,
  LastEventLine,
  activityPanel,
  lastEventLine,
} from '../worker-activity/worker-activity.model';

/**
 * A running worker: its role, issue, how long it has run and its live activity, with a way to
 * watch it on the Floor.
 */
@Component({
  selector: 'fd-worker-card',
  imports: [IssueRefComponent, RoleTagComponent, RouterLink, WorkerActivityComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '[attr.data-issue-id]': 'item().issue.id',
    '[style.view-transition-name]': "'fd-card-' + item().issue.id",
    '[style.view-transition-class]': "'fd-card'",
  },
  templateUrl: './worker-card.html',
  styleUrl: './worker-card.scss',
})
export class WorkerCardComponent {
  readonly item: InputSignal<ItemView<WorkerItem>> = input.required<ItemView<WorkerItem>>();
  /** The current time, in epoch ms, from the board's shared tick. */
  readonly now: InputSignal<number> = input.required<number>();
  /** Emits the card's button, so focus can return to it when the pane closes. */
  readonly open: OutputEmitterRef<HTMLElement> = output<HTMLElement>();

  private readonly _workerSignalR = inject(WorkerSignalRService);

  protected readonly idBase: Signal<string> = computed(() => `worker-card-${this.item().issue.id}`);
  protected readonly live: Signal<boolean> = computed(() => this.item().phase === 'live');
  protected readonly label: Signal<string> = computed(
    () => `${this.item().issue.title}, #${this.item().issue.issueNumber}`,
  );
  protected readonly describedBy: Signal<string> = computed(() => {
    const base = this.idBase();
    return this.live() ? `${base}-meta ${base}-task ${base}-status` : `${base}-meta ${base}-status`;
  });
  /** The Floor lane heading's id, which the Floor scrolls to and focuses. */
  protected readonly laneFragment: Signal<string> = computed(
    () => `floor-lane-${this.item().issue.id}`,
  );
  protected readonly elapsed: Signal<string> = computed(() =>
    formatElapsed(this.now() - Date.parse(this.item().issue.enteredStateAt)),
  );

  private readonly _activity: Signal<AgentActivity | null> = computed(() =>
    this._workerSignalR.agentActivityForIssue(this.item().issue.id),
  );
  // The in-flight tool clears between calls; the latest recorded call stays until the next.
  private readonly _latestTool: Signal<InFlightTool | null> = computed(() => {
    const activity = this._activity();
    if (activity === null) {
      return null;
    }
    return (
      this._workerSignalR.recentToolsForRun(activity.workerRunId).at(-1) ?? activity.inFlightTool
    );
  });

  /** The run's role once its activity names it; until then, the one the issue state implies. */
  protected readonly role: Signal<string> = computed(
    () => this._activity()?.role ?? this.item().role,
  );
  protected readonly panel: Signal<ActivityPanel> = computed(() =>
    activityPanel(this._activity(), this._latestTool()),
  );
  protected readonly lastEvent: Signal<LastEventLine | null> = computed(() =>
    this.live() ? lastEventLine(this._activity(), this.now()) : null,
  );
}

import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  InputSignal,
  Signal,
  WritableSignal,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { Observable } from 'rxjs';
import { AGENT_SILENCE_THRESHOLD_MINUTES, AgentActivity, InFlightTool } from '../../../core/models/agent-activity.model';
import { WorkerSignalRService } from '../../../core/services/worker-signalr.service';
import { LogViewComponent } from '../../../shared/components/log-view/log-view';
import { formatElapsed, laneStepText, taskOrdinal } from '../../../shared/utils/agent-activity-format';

/** The elapsed text ticks on the same cadence as the Floor's. */
const CLOCK_INTERVAL_MS = 5_000;

let nextId = 0;

interface StepLine {
  readonly text: string;
  readonly ordinal: string | null;
}

interface ElapsedDisplay {
  readonly text: string;
  readonly silent: boolean;
}

interface ToolLine {
  readonly key: string;
  readonly name: string;
  readonly summary: string | null;
}

/**
 * What the issue's running worker is doing (#863): its current step, task progress, latest tool and
 * time since the last event, with the live log collapsed behind Show log. The pane renders it only
 * while a worker runs.
 */
@Component({
  selector: 'fd-pane-activity',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [LogViewComponent],
  templateUrl: './pane-activity.html',
  styleUrl: './pane-activity.scss',
})
export class PaneActivityComponent {
  readonly issueId: InputSignal<string> = input.required<string>();
  /** The issue's run as the detail knows it; the live activity's own run wins once it arrives. */
  readonly workerRunId: InputSignal<string | null> = input<string | null>(null);

  protected readonly headingId = `pane-activity-heading-${nextId++}`;

  private readonly _workerSignalR = inject(WorkerSignalRService);

  protected readonly activity: Signal<AgentActivity | null> = computed(
    () => this._workerSignalR.agentActivityForIssue(this.issueId()));

  protected readonly stale: Signal<boolean> = computed(() => this._workerSignalR.connectionStatus() !== 'connected');

  private readonly _now: WritableSignal<number> = signal(Date.now());

  protected readonly runId: Signal<string | null> = computed(() => this.activity()?.workerRunId ?? this.workerRunId());

  /** One stream per run, so a reload of the activity never resubscribes the log. */
  protected readonly logStream: Signal<Observable<string> | null> = computed(() => {
    const runId = this.runId();
    return runId === null ? null : this._workerSignalR.streamLog(runId);
  });

  protected readonly step: Signal<StepLine | null> = computed(() => {
    const a = this.activity();
    if (a === null) {
      return null;
    }
    const task = a.currentTask;
    if (task === null) {
      return { text: laneStepText(a), ordinal: null };
    }
    const total = a.tasks.length;
    return {
      text: task.activeForm ?? task.subject,
      ordinal: total > 0 ? `task ${taskOrdinal(a)} of ${total}` : null,
    };
  });

  /** Completed share as a CSS width; null without a task list. */
  protected readonly progress: Signal<string | null> = computed(() => {
    const a = this.activity();
    if (a === null || a.tasks.length === 0) {
      return null;
    }
    return `${Math.min(100, (a.completedTaskCount / a.tasks.length) * 100)}%`;
  });

  protected readonly tool: Signal<ToolLine | null> = computed(() => {
    const a = this.activity();
    if (a === null) {
      return null;
    }
    const recent = this._workerSignalR.recentToolsForRun(a.workerRunId);
    const tool: InFlightTool | null = a.inFlightTool ?? (recent.length > 0 ? recent[recent.length - 1] : null);
    if (tool === null) {
      return null;
    }
    return { key: `${tool.name}|${tool.summary}|${tool.startedAt}`, name: tool.name, summary: tool.summary };
  });

  /** Same wording and silence rule as a Floor lane. */
  protected readonly elapsed: Signal<ElapsedDisplay | null> = computed(() => {
    const since = this.activity()?.lastEventAt ?? null;
    if (since === null) {
      return null;
    }
    const elapsedMs = this._now() - new Date(since).getTime();
    const silent = elapsedMs >= AGENT_SILENCE_THRESHOLD_MINUTES * 60_000;
    const text = formatElapsed(elapsedMs);
    if (!silent) {
      return { text: `last event ${text} ago`, silent };
    }
    const running = this.activity()?.inFlightTool?.name ?? null;
    return { text: `silent ${text}${running !== null ? ` · ${running} running` : ''}`, silent };
  });

  /** Polite live-region text: changes on step changes only, never on token or clock ticks. */
  protected readonly announcement: Signal<string> = computed(() => {
    const a = this.activity();
    return a === null ? '' : laneStepText(a);
  });

  constructor() {
    // The clock stops while disconnected, so the elapsed text freezes on the last known state.
    const handle = setInterval(() => {
      if (!this.stale()) {
        this._now.set(Date.now());
      }
    }, CLOCK_INTERVAL_MS);
    inject(DestroyRef).onDestroy(() => clearInterval(handle));
  }
}

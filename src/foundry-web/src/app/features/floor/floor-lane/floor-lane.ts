import { ChangeDetectionStrategy, Component, InputSignal, Signal, computed, input } from '@angular/core';
import { AGENT_SILENCE_THRESHOLD_MINUTES, ActiveSubagent, AgentTaskItem } from '../../../core/models/agent-activity.model';
import { SafeHrefPipe } from '../../../shared/pipes/safe-href.pipe';
import {
  FLOOR_SUBAGENT_VISIBLE_LIMIT,
  FloorLane,
  formatElapsed,
  formatTokenCount,
  laneStepText,
  splitSubagents,
} from '../floor.model';

interface SubagentGroup {
  readonly label: string;
  readonly noun: string;
  readonly visible: readonly ActiveSubagent[];
  readonly overflow: readonly ActiveSubagent[];
}

interface LastEventDisplay {
  readonly text: string;
  readonly silent: boolean;
}

function groupSubagents(label: string, noun: string, subagents: readonly ActiveSubagent[]): SubagentGroup {
  return {
    label,
    noun,
    visible: subagents.slice(0, FLOOR_SUBAGENT_VISIBLE_LIMIT),
    overflow: subagents.slice(FLOOR_SUBAGENT_VISIBLE_LIMIT),
  };
}

/** One Floor lane: a single active run's live Agent Activity. */
@Component({
  selector: 'fd-floor-lane',
  imports: [SafeHrefPipe],
  templateUrl: './floor-lane.html',
  styleUrl: './floor-lane.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FloorLaneComponent {
  readonly lane: InputSignal<FloorLane> = input.required<FloorLane>();
  /** Epoch ms used for "time since last event"; driven by the page's clock so lanes tick together. */
  readonly now: InputSignal<number> = input.required<number>();
  /** True while the worker hub is disconnected: the lane shows its last known state. */
  readonly stale: InputSignal<boolean> = input(false);

  protected readonly headingId: Signal<string> = computed(() => `floor-lane-${this.lane().issueId}`);

  protected readonly heading: Signal<string> = computed(() => {
    const issue = this.lane().issue;
    return issue !== null ? `#${issue.issueNumber} ${issue.title}` : 'Loading issue…';
  });

  protected readonly tasks: Signal<readonly AgentTaskItem[]> = computed(() => this.lane().activity.tasks);
  protected readonly currentTaskId: Signal<string | null> = computed(() => this.lane().activity.currentTask?.id ?? null);

  protected readonly taskProgress: Signal<string | null> = computed(() => {
    const activity = this.lane().activity;
    if (activity.tasks.length === 0) {
      return null;
    }
    return `${activity.completedTaskCount} of ${activity.tasks.length} done`;
  });

  /** Parallel (top-level) and nested subagents as separate groups; empty groups are omitted. */
  protected readonly subagentGroups: Signal<readonly SubagentGroup[]> = computed(() => {
    const { parallel, nested } = splitSubagents(this.lane().activity.subagents);
    return [
      groupSubagents('Parallel', 'parallel', parallel),
      groupSubagents('Nested', 'nested', nested),
    ].filter((group) => group.visible.length > 0);
  });

  protected readonly activeSkill: Signal<string | null> = computed(() => this.lane().activity.activeSkill);

  /** Recent tool calls, newest first. */
  protected readonly toolTicker = computed(() => [...this.lane().recentTools].reverse());
  protected readonly inFlightToolName: Signal<string | null> = computed(() => this.lane().activity.inFlightTool?.name ?? null);

  protected readonly tokens = computed(() => {
    const totals = this.lane().activity.tokens;
    return {
      input: formatTokenCount(totals.inputTokens),
      // Output is only final once the run completes; streamed counts are start-of-message stubs.
      output: totals.outputTokens !== null ? formatTokenCount(totals.outputTokens) : null,
    };
  });

  protected readonly lastEvent: Signal<LastEventDisplay | null> = computed(() => {
    const since = this.lane().activity.lastEventAt;
    if (since === null) {
      return null;
    }
    const elapsedMs = this.now() - new Date(since).getTime();
    const silent = elapsedMs >= AGENT_SILENCE_THRESHOLD_MINUTES * 60_000;
    const elapsed = formatElapsed(elapsedMs);
    if (!silent) {
      return { text: `last event ${elapsed} ago`, silent };
    }
    const tool = this.inFlightToolName();
    return { text: `silent ${elapsed}${tool !== null ? ` · ${tool} running` : ''}`, silent };
  });

  /** Polite live-region text: changes on step changes only, never on token or clock ticks. */
  protected readonly stepAnnouncement: Signal<string> = computed(() => {
    const issue = this.lane().issue;
    const prefix = issue !== null ? `Issue #${issue.issueNumber}: ` : '';
    return `${prefix}${laneStepText(this.lane().activity)}`;
  });
}

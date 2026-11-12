import { ChangeDetectionStrategy, Component, Signal, computed, input, InputSignal } from '@angular/core';
import { IssueState } from '../../utils/issue-state';
import { StateDisplay, resolveStateDisplay } from '../../utils/state-display';
import { RevisionTriggerKind } from '../../utils/revision-trigger-kind';

export type BadgePublishPhase = 'publishing' | 'retrying';

@Component({
  selector: 'fd-state-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span
      class="badge"
      role="img"
      [class]="badgeClass()"
      [attr.aria-label]="ariaLabel()"
    >{{ label() }}</span>
  `,
  styleUrl: './state-badge.scss',
})
export class StateBadgeComponent {
  readonly state: InputSignal<IssueState> = input.required<IssueState>();
  readonly failureClassification: InputSignal<string | undefined> = input<string | undefined>(undefined);
  /** Overrides the label of a working state while Foundry publishes the run's commits. */
  readonly publishPhase: InputSignal<BadgePublishPhase | null> = input<BadgePublishPhase | null>(null);

  readonly revisionTriggerKind: InputSignal<RevisionTriggerKind | null | undefined> =
    input<RevisionTriggerKind | null | undefined>(undefined);

  private readonly _display: Signal<StateDisplay> = computed(() =>
    resolveStateDisplay(this.state(), {
      failureClassification: this.failureClassification(),
      revisionTriggerKind: this.revisionTriggerKind(),
      publishPhase: this.publishPhase(),
    }));

  label(): string {
    return this._display().label;
  }

  ariaLabel(): string {
    return `State: ${this._display().ariaLabel}`;
  }

  badgeClass(): string {
    return `badge ${this._display().cssClass}`;
  }
}

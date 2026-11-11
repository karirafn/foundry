import { ChangeDetectionStrategy, Component, input, InputSignal } from '@angular/core';
import { IssueState, WORKING_STATES } from '../../utils/issue-state';
import { STATE_LABELS, STATE_ARIA_LABELS, STATE_CSS_CLASSES } from '../../utils/state-display';
import { getFailureCategoryDisplay } from '../../utils/failure-category';

const FAILED_STATES: ReadonlySet<IssueState> = new Set<IssueState>(['failed', 'continuable_failed']);

export type BadgePublishPhase = 'publishing' | 'retrying';

interface BadgeDisplay {
  readonly label: string;
  readonly aria: string;
  readonly cssClass: string;
}

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

  private overrideDisplay(): BadgeDisplay | null {
    const state = this.state();
    const phase = this.publishPhase();
    if (phase !== null && WORKING_STATES.has(state)) {
      if (phase === 'retrying') {
        return { label: 'PUBLISH RETRY', aria: 'publish retrying', cssClass: 'badge--retrying' };
      }
      return state === 'revision_in_progress'
        ? { label: 'REV PUBLISHING', aria: 'revision publishing', cssClass: 'badge--publishing' }
        : { label: 'PUBLISHING', aria: 'publishing', cssClass: 'badge--publishing' };
    }

    const classification = this.failureClassification();
    if (classification === undefined || !FAILED_STATES.has(state)) {
      return null;
    }
    const display = getFailureCategoryDisplay(classification);
    return display === null
      ? null
      : { label: display.label, aria: display.label.toLowerCase(), cssClass: display.cssClass };
  }

  label(): string {
    return this.overrideDisplay()?.label ?? STATE_LABELS[this.state()];
  }

  ariaLabel(): string {
    return `State: ${this.overrideDisplay()?.aria ?? STATE_ARIA_LABELS[this.state()]}`;
  }

  badgeClass(): string {
    return `badge ${this.overrideDisplay()?.cssClass ?? STATE_CSS_CLASSES[this.state()]}`;
  }
}

import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { IssueService } from '../issue.service';
import { STATE_GROUPS, StateGroup } from '../issue-lifecycle.model';

/**
 * Inline row of lifecycle-group filter chips (ADR 0045 groups). Each chip toggles its group
 * independently; zero-count chips are dimmed but stay focusable and toggleable so controls never
 * leave the tab order as counts change over SignalR.
 */
@Component({
  selector: 'fd-issue-group-filter',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="group-filter" role="group" aria-label="Filter issues by group">
      @for (group of groups; track group.id) {
        <button
          type="button"
          class="group-filter__chip"
          [class.group-filter__chip--pressed]="issueService.isGroupSelected(group.id)"
          [class.group-filter__chip--empty]="issueService.countForGroup(group.id) === 0"
          [attr.aria-pressed]="issueService.isGroupSelected(group.id)"
          [attr.aria-label]="chipAriaLabel(group)"
          [attr.data-group]="group.id"
          (click)="issueService.toggleGroup(group.id)"
        >
          <span class="group-filter__label">{{ group.label }}</span>
          <span class="group-filter__count">{{ issueService.countForGroup(group.id) }}</span>
        </button>
      }
    </div>
  `,
  styleUrl: './issue-group-filter.scss',
})
export class IssueGroupFilterComponent {
  protected readonly issueService = inject(IssueService);
  protected readonly groups = STATE_GROUPS;

  protected chipAriaLabel(group: StateGroup): string {
    const count = this.issueService.countForGroup(group.id);
    if (count === 0) {
      return `${group.label}, no issues`;
    }
    const unit = count === 1 ? 'issue' : 'issues';
    return `${group.label}, ${count} ${unit}`;
  }
}

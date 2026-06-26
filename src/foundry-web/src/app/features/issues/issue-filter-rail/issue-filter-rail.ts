import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { IssueService } from '../issue.service';
import { STATE_GROUPS } from '../issue-lifecycle.model';
import { IssueState } from '../issue.model';
import { STATE_RAIL_LABELS, STATE_COLOR_VARS } from '../state-display';

@Component({
  selector: 'fd-issue-filter-rail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="filter-rail" role="group" aria-label="Filter issues by state">
      @for (group of groups; track group.label) {
        <div class="filter-rail__group">
          <span class="filter-rail__group-label">{{ group.label }}</span>
          @for (state of group.states; track state) {
            <button
              type="button"
              class="filter-rail__toggle"
              [class.filter-rail__toggle--dimmed]="issueService.countFor(state) === 0"
              [class.filter-rail__toggle--pressed]="issueService.isStateSelected(state)"
              [attr.aria-pressed]="issueService.isStateSelected(state)"
              [disabled]="issueService.countFor(state) === 0"
              [attr.data-state]="state"
              (click)="issueService.toggleState(state)"
            >
              <span
                class="filter-rail__dot"
                [style.background]="dotColor(state)"
                aria-hidden="true"
              ></span>
              <span class="filter-rail__state-label">{{ stateLabel(state) }}</span>
              <span class="filter-rail__count">{{ issueService.countFor(state) }}</span>
            </button>
          }
        </div>
      }
    </div>
  `,
  styleUrl: './issue-filter-rail.scss',
})
export class IssueFilterRailComponent {
  protected readonly issueService = inject(IssueService);
  protected readonly groups = STATE_GROUPS;

  protected stateLabel(state: IssueState): string {
    return STATE_RAIL_LABELS[state];
  }

  protected dotColor(state: IssueState): string {
    return STATE_COLOR_VARS[state];
  }
}

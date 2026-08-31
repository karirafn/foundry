import { Component, ChangeDetectionStrategy, inject } from '@angular/core';
import { DatePipe, LowerCasePipe } from '@angular/common';
import { RateBudgetService } from '../../../../core/services/rate-budget.service';

@Component({
  selector: 'fd-rate-budget-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, LowerCasePipe],
  template: `
    <div role="status" aria-live="polite">
      @if (rateBudgetService.snapshot(); as snapshot) {
        <table class="rbd__table" aria-label="Provider rate budget headroom">
          <thead>
            <tr>
              <th scope="col" class="rbd__th">Provider</th>
              <th scope="col" class="rbd__th">Remaining / Limit</th>
              <th scope="col" class="rbd__th">Health</th>
              <th scope="col" class="rbd__th">Floor</th>
              <th scope="col" class="rbd__th">Resets at</th>
            </tr>
          </thead>
          <tbody>
            @for (entry of snapshot.budgets; track entry.budget) {
              <tr class="rbd__row">
                <td class="rbd__td">{{ entry.displayName }}</td>
                <td class="rbd__td">
                  @if (entry.remaining !== null && entry.remaining !== undefined) {
                    {{ entry.remaining }}{{ entry.limit !== null && entry.limit !== undefined ? ' / ' + entry.limit : '' }}
                  } @else {
                    <span class="rbd__no-data" aria-label="No data yet">—</span>
                  }
                </td>
                <td class="rbd__td">
                  @if (entry.health !== null && entry.health !== undefined) {
                    <span
                      class="rbd__badge rbd__badge--{{ entry.health | lowercase }}"
                      [attr.aria-label]="entry.displayName + ' health: ' + entry.health"
                    >{{ entry.health }}</span>
                  } @else {
                    <span class="rbd__no-data" aria-label="Health not evaluated">—</span>
                  }
                </td>
                <td class="rbd__td">
                  @if (entry.floor !== null && entry.floor !== undefined) {
                    {{ entry.floor }}
                  } @else {
                    <span class="rbd__visibility-only" aria-label="No floor enforced">visibility only</span>
                  }
                </td>
                <td class="rbd__td">
                  @if (entry.resetAt) {
                    {{ entry.resetAt | date: 'HH:mm' }}
                  } @else if (entry.observedAt) {
                    <span class="rbd__no-data">observed {{ entry.observedAt | date: 'HH:mm' }}</span>
                  } @else {
                    <span class="rbd__no-data">no data yet</span>
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>
      } @else {
        <p class="rbd__loading">Loading…</p>
      }
    </div>
  `,
  styleUrl: './rate-budget-panel.scss',
})
export class RateBudgetPanelComponent {
  protected readonly rateBudgetService = inject(RateBudgetService);
}

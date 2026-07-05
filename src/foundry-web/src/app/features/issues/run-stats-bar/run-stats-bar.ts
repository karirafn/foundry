import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { WorkerRunTotalsService } from '../../workers/run-totals.service';
import { StatsScope } from '../../workers/run-totals.model';
import { formatCost, formatDuration, formatTokens } from '../run-stats.format';

interface StatCard {
  readonly key: string;
  readonly label: string;
  readonly tintVar: string;
}

const STAT_CARDS = [
  { key: 'runCount',      label: 'Total runs',     tintVar: '--fd-text-warning'  },
  { key: 'durationMs',   label: 'Total duration',  tintVar: '--fd-stat-duration' },
  { key: 'numTurns',     label: 'Total turns',     tintVar: '--fd-stat-turns'    },
  { key: 'totalCostUsd', label: 'Total cost',      tintVar: '--fd-stat-cost'     },
  { key: 'inputTokens',  label: 'Input tokens',    tintVar: '--fd-stat-input'    },
  { key: 'outputTokens', label: 'Output tokens',   tintVar: '--fd-stat-output'   },
] as const satisfies readonly StatCard[];

let nextScopeId = 0;

@Component({
  selector: 'fd-run-stats-bar',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="run-stats-bar" [attr.aria-labelledby]="headingId">
      <div class="run-stats-bar__header">
        <h2 [id]="headingId" class="run-stats-bar__heading">
          Run statistics
          <span class="run-stats-bar__scope-caption">
            {{ service.scope() === 'month' ? 'This month' : 'All time' }}
          </span>
        </h2>

        <div
          class="run-stats-bar__scope-toggle"
          role="radiogroup"
          aria-label="Time range"
        >
          <label class="run-stats-bar__scope-label">
            <input
              class="run-stats-bar__scope-radio"
              type="radio"
              [name]="_radioGroupName"
              value="month"
              [checked]="service.scope() === 'month'"
              (change)="onScopeChange('month')"
            />
            Current month
          </label>
          <label class="run-stats-bar__scope-label">
            <input
              class="run-stats-bar__scope-radio"
              type="radio"
              [name]="_radioGroupName"
              value="all"
              [checked]="service.scope() === 'all'"
              (change)="onScopeChange('all')"
            />
            All time
          </label>
        </div>
      </div>

      @if (service.loading()) {
        <div class="run-stats-bar__skeleton" aria-hidden="true">
          @for (card of statCards; track card.key) {
            <div class="run-stats-bar__skeleton-pill"></div>
          }
        </div>
      } @else if (service.error()) {
        <div class="run-stats-bar__error" role="alert">
          <span class="run-stats-bar__error-message">Stats unavailable</span>
          <button
            class="run-stats-bar__retry"
            type="button"
            (click)="service.fetch()"
          >Retry</button>
        </div>
      } @else if (service.totals(); as totals) {
        <div
          class="run-stats-bar__grid"
          role="group"
          aria-label="Run statistics"
          tabindex="0"
        >
          @for (card of statCards; track card.key) {
            <div
              class="run-stats-bar__card"
              [style.--card-tint]="'var(' + card.tintVar + ')'"
            >
              <span class="sr-only">{{ card.label }}: {{ formatValue(card.key, _asRecord(totals)) }}</span>
              <svg
                class="run-stats-bar__icon"
                width="14"
                height="14"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                stroke-width="2"
                stroke-linecap="round"
                stroke-linejoin="round"
                aria-hidden="true"
              >
                @switch (card.key) {
                  @case ('runCount') {
                    <polyline points="23 4 23 10 17 10" />
                    <polyline points="1 20 1 14 7 14" />
                    <path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15" />
                  }
                  @case ('durationMs') {
                    <circle cx="12" cy="12" r="9" />
                    <polyline points="12 7 12 12 15 14" />
                  }
                  @case ('numTurns') {
                    <path d="M21 11.5a8.38 8.38 0 0 1-.9 3.8 8.5 8.5 0 0 1-7.6 4.7 8.38 8.38 0 0 1-3.8-.9L3 21l1.9-5.7a8.38 8.38 0 0 1-.9-3.8 8.5 8.5 0 0 1 4.7-7.6 8.38 8.38 0 0 1 3.8-.9h.5a8.48 8.48 0 0 1 8 8v.5z" />
                  }
                  @case ('totalCostUsd') {
                    <circle cx="12" cy="12" r="9" />
                    <path d="M14.8 9.3a2.6 2.6 0 0 0-2.8-1.3c-1.3.2-2.2 1-2.2 2 0 1.2 1.1 1.6 2.6 2 1.7.4 2.8.9 2.8 2.1 0 1.1-1 1.9-2.4 2a2.7 2.7 0 0 1-2.8-1.4" />
                    <line x1="12" y1="6" x2="12" y2="8" />
                    <line x1="12" y1="16" x2="12" y2="18" />
                  }
                  @case ('inputTokens') {
                    <line x1="12" y1="19" x2="12" y2="5" />
                    <polyline points="6 11 12 5 18 11" />
                  }
                  @case ('outputTokens') {
                    <line x1="12" y1="5" x2="12" y2="19" />
                    <polyline points="6 13 12 19 18 13" />
                  }
                }
              </svg>
              <span class="run-stats-bar__card-body" aria-hidden="true">
                <span class="run-stats-bar__card-label">{{ card.label }}</span>
                <span class="run-stats-bar__card-value">{{ formatValue(card.key, _asRecord(totals)) }}</span>
              </span>
            </div>
          }
        </div>
      }
    </section>
  `,
  styleUrl: './run-stats-bar.scss',
})
export class RunStatsBarComponent {
  protected readonly service = inject(WorkerRunTotalsService);
  protected readonly statCards = STAT_CARDS;
  protected readonly _radioGroupName = `run-stats-scope-${++nextScopeId}`;
  protected readonly headingId = `run-stats-bar-heading-${nextScopeId}`;

  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  protected _asRecord(totals: unknown): Record<string, number> {
    return totals as Record<string, number>;
  }

  protected formatValue(key: string, totals: Record<string, number>): string {
    switch (key) {
      case 'durationMs':
        return formatDuration(totals['durationMs']);
      case 'totalCostUsd':
        return formatCost(totals['totalCostUsd']);
      case 'inputTokens':
        return formatTokens(totals['inputTokens']);
      case 'outputTokens':
        return formatTokens(totals['outputTokens']);
      default:
        return String(totals[key]);
    }
  }

  onScopeChange(scope: StatsScope): void {
    this.service.setScope(scope);
  }
}

import { Component, InputSignal, OutputEmitterRef, computed, inject, input, output } from '@angular/core';
import { DecimalPipe, NgClass } from '@angular/common';
import { IssueSummary, RunStats, LIVE_STATES } from '../issue.model';
import { STATE_ARIA_LABELS, cardAccentFor } from '../../../shared/utils/state-display';
import { StateBadgeComponent } from '../../../shared/components/state-badge/state-badge';
import { SafeHrefPipe } from '../../../shared/pipes/safe-href.pipe';
import { TickerService } from '../../../core/services/ticker.service';
import { formatCost as _formatCostImpl, formatDuration as _formatDurationImpl } from '../run-stats.format';

const REPO_WARNING_STATES = new Set<string>(['queued', 'detected', 'revision_queued', 'continuation_queued']);
const SILENCE_THRESHOLD_MINUTES = 5;

// Re-export shared helpers so existing imports from this module continue to work.
export { formatCost, formatDuration } from '../run-stats.format';

const WARNING_CLASSES: Record<string, string> = {
  ineligible: 'issue-card__repo-warning--ineligible',
  unreachable: 'issue-card__repo-warning--unreachable',
};

function timeAgo(dateString: string): string {
  const now = Date.now();
  const then = new Date(dateString).getTime();
  const diffMs = now - then;
  const diffSeconds = Math.floor(diffMs / 1000);

  if (diffSeconds < 60) {
    return 'just now';
  }

  const diffMinutes = Math.floor(diffSeconds / 60);
  if (diffMinutes < 60) {
    return `${diffMinutes} minute${diffMinutes === 1 ? '' : 's'} ago`;
  }

  const diffHours = Math.floor(diffMinutes / 60);
  if (diffHours < 24) {
    return `${diffHours} hour${diffHours === 1 ? '' : 's'} ago`;
  }

  const diffDays = Math.floor(diffHours / 24);
  if (diffDays < 30) {
    return `${diffDays} day${diffDays === 1 ? '' : 's'} ago`;
  }

  const diffMonths = Math.floor(diffDays / 30);
  if (diffMonths < 12) {
    return `${diffMonths} month${diffMonths === 1 ? '' : 's'} ago`;
  }

  const diffYears = Math.floor(diffMonths / 12);
  return `${diffYears} year${diffYears === 1 ? '' : 's'} ago`;
}

function silentMinutes(lastActivityAt: string): number | null {
  const now = Date.now();
  const then = new Date(lastActivityAt).getTime();
  const diffMinutes = Math.floor((now - then) / 60000);
  return diffMinutes >= SILENCE_THRESHOLD_MINUTES ? diffMinutes : null;
}

function commitPhrase(count: number): string {
  if (count === 0) {
    return 'no commits yet';
  }
  return count === 1 ? '1 commit' : `${count} commits`;
}

function hasVisiblePills(stats: RunStats): boolean {
  return (
    stats.runCount > 1 ||
    stats.durationMs !== null ||
    stats.numTurns !== null ||
    stats.totalCostUsd !== null ||
    stats.inputTokens !== null ||
    stats.outputTokens !== null
  );
}

@Component({
  selector: 'fd-issue-card',
  standalone: true,
  imports: [StateBadgeComponent, SafeHrefPipe, NgClass, DecimalPipe],
  template: `
    <button
      type="button"
      class="issue-card"
      [ngClass]="_accentClass()"
      [attr.aria-expanded]="expanded().toString()"
      [attr.aria-controls]="'detail-' + issue().id"
      [attr.aria-label]="issueAriaLabel()"
      (click)="onCardClick()"
      (keydown)="onKeydown($event)"
    >
      <div class="issue-card__meta">
        <span class="issue-card__number">#{{ issue().issueNumber }}</span>
        <span class="issue-card__separator" aria-hidden="true">·</span>
        <span class="issue-card__slug">{{ issue().repositorySlug }}</span>
        <div class="issue-card__badge">
          @if (isNextUp()) {
            <span class="issue-card__next-up" aria-hidden="true">Next up</span>
          }
          <fd-state-badge [state]="issue().state" [failureClassification]="issue().failureClassification" />
          @if (repoWarningLabel()) {
            <span
              class="issue-card__repo-warning"
              [ngClass]="_warningClass()"
            >
              <svg
                class="issue-card__repo-warning-icon"
                xmlns="http://www.w3.org/2000/svg"
                width="11"
                height="11"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                stroke-width="2.5"
                stroke-linecap="round"
                stroke-linejoin="round"
                aria-hidden="true"
              >
                <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z" />
                <line x1="12" y1="9" x2="12" y2="13" />
                <line x1="12" y1="17" x2="12.01" y2="17" />
              </svg>
              {{ repoWarningLabel() }}
            </span>
          }
        </div>
      </div>

      <div
        class="issue-card__title"
        [attr.title]="issue().title"
      >{{ issue().title }}</div>

      @if (_visibleRunStats(); as stats) {
        <div class="issue-card__run-stats">
          @if (stats.runCount > 1) {
            <span class="issue-card__stat-pill issue-card__stat-pill--run-count issue-card__stat-pill--warning">
              <svg class="issue-card__stat-icon" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                <polyline points="23 4 23 10 17 10" />
                <polyline points="1 20 1 14 7 14" />
                <path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15" />
              </svg>
              <span class="sr-only">Run count: </span><span class="issue-card__stat-value">{{ stats.runCount }}<span class="sr-only"> runs</span></span>
            </span>
          }
          @if (stats.durationMs !== null) {
            <span class="issue-card__stat-pill issue-card__stat-pill--duration">
              <svg class="issue-card__stat-icon" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                <circle cx="12" cy="12" r="9" />
                <polyline points="12 7 12 12 15 14" />
              </svg>
              <span class="sr-only">Duration: </span><span class="issue-card__stat-value">{{ _formatDuration(stats.durationMs) }}</span>
            </span>
          }
          @if (stats.numTurns !== null) {
            <span class="issue-card__stat-pill issue-card__stat-pill--turns">
              <svg class="issue-card__stat-icon" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                <path d="M21 11.5a8.38 8.38 0 0 1-.9 3.8 8.5 8.5 0 0 1-7.6 4.7 8.38 8.38 0 0 1-3.8-.9L3 21l1.9-5.7a8.38 8.38 0 0 1-.9-3.8 8.5 8.5 0 0 1 4.7-7.6 8.38 8.38 0 0 1 3.8-.9h.5a8.48 8.48 0 0 1 8 8v.5z" />
              </svg>
              <span class="sr-only">Turns: </span><span class="issue-card__stat-value">{{ stats.numTurns }}<span class="sr-only"> turns</span></span>
            </span>
          }
          @if (stats.totalCostUsd !== null) {
            <span class="issue-card__stat-pill issue-card__stat-pill--cost">
              <svg class="issue-card__stat-icon" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                <circle cx="12" cy="12" r="9" />
                <path d="M14.8 9.3a2.6 2.6 0 0 0-2.8-1.3c-1.3.2-2.2 1-2.2 2 0 1.2 1.1 1.6 2.6 2 1.7.4 2.8.9 2.8 2.1 0 1.1-1 1.9-2.4 2a2.7 2.7 0 0 1-2.8-1.4" />
                <line x1="12" y1="6" x2="12" y2="8" />
                <line x1="12" y1="16" x2="12" y2="18" />
              </svg>
              <span class="sr-only">Cost: </span><span class="issue-card__stat-value">{{ _formatCost(stats.totalCostUsd) }}<span class="sr-only"> USD</span></span>
            </span>
          }
          @if (stats.inputTokens !== null) {
            <span class="issue-card__stat-pill issue-card__stat-pill--input-tokens">
              <svg class="issue-card__stat-icon" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                <line x1="12" y1="19" x2="12" y2="5" />
                <polyline points="6 11 12 5 18 11" />
              </svg>
              <span class="sr-only">Input tokens: </span><span class="issue-card__stat-value">{{ stats.inputTokens | number }}<span class="sr-only"> input tokens</span></span>
            </span>
          }
          @if (stats.outputTokens !== null) {
            <span class="issue-card__stat-pill issue-card__stat-pill--output-tokens">
              <svg class="issue-card__stat-icon" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                <line x1="12" y1="5" x2="12" y2="19" />
                <polyline points="6 13 12 19 18 13" />
              </svg>
              <span class="sr-only">Output tokens: </span><span class="issue-card__stat-value">{{ stats.outputTokens | number }}<span class="sr-only"> output tokens</span></span>
            </span>
          }
        </div>
      }

      <div class="issue-card__footer">
        <span class="issue-card__timestamp">{{ timestamp() }}</span>
        @if (_activityLineVisible()) {
          <span class="issue-card__activity" aria-hidden="true">{{ _activityLineText() }}</span>
        }
        @if (issue().url | safeHref; as safeUrl) {
          <a
            class="issue-card__link"
            [href]="safeUrl"
            target="_blank"
            rel="noopener noreferrer"
            [attr.aria-label]="'View issue #' + issue().issueNumber + ' on ' + issue().repositorySlug"
            (click)="onLinkClick($event)"
          >
            <svg
              xmlns="http://www.w3.org/2000/svg"
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
              <path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6" />
              <polyline points="15 3 21 3 21 9" />
              <line x1="10" y1="14" x2="21" y2="3" />
            </svg>
          </a>
        }
      </div>
    </button>
  `,
  styleUrl: './issue-card.scss',
})
export class IssueCardComponent {
  readonly issue: InputSignal<IssueSummary> = input.required<IssueSummary>();
  readonly expanded: InputSignal<boolean> = input.required<boolean>();
  readonly lastActivityAt: InputSignal<string | null> = input<string | null>(null);
  readonly commitCount: InputSignal<number | null> = input<number | null>(null);
  readonly isNextUp: InputSignal<boolean> = input<boolean>(false);
  readonly toggle: OutputEmitterRef<void> = output<void>();

  private readonly _ticker = inject(TickerService);

  readonly _accentClass = computed(() => {
    const accent = cardAccentFor(this.issue().state);
    return accent === 'working' ? 'issue-card--working' : accent === 'ready' ? 'issue-card--ready' : '';
  });

  readonly _activityLine = computed(() => {
    // Depend on ticker to re-evaluate every ~30s
    void this._ticker.tick();
    const at = this.lastActivityAt();
    if (at === null || !LIVE_STATES.has(this.issue().state)) {
      return null;
    }
    const count = this.commitCount();
    const silenceMinutes = silentMinutes(at);
    const silencePart = silenceMinutes !== null ? ` · silent ${silenceMinutes}m` : '';
    // null count means pre-handshake: suppress commit phrase, show just "active"
    if (count === null) {
      return silenceMinutes !== null ? `silent ${silenceMinutes}m` : '';
    }
    return `${commitPhrase(count)}${silencePart}`;
  });

  /** Whether the activity line should render at all (live state + lastActivityAt present). */
  readonly _activityLineVisible = computed(() => this._activityLine() !== null);

  /** Visual text for the activity line (e.g. "active · 3 commits · silent 7m"). */
  readonly _activityLineText = computed(() => {
    const detail = this._activityLine();
    return detail ? `active · ${detail}` : 'active';
  });

  readonly _warningClass = computed(() => {
    const status = this.issue().repositoryEligibilityStatus;
    return status ? (WARNING_CLASSES[status] ?? '') : '';
  });

  readonly _visibleRunStats = computed((): RunStats | null => {
    const stats = this.issue().runStats ?? null;
    if (stats === null || !hasVisiblePills(stats)) {
      return null;
    }
    return stats;
  });

  readonly issueAriaLabel = computed(() => {
    // Depend on ticker so the silence phrase re-evaluates on the same cadence as _activityLine.
    void this._ticker.tick();
    const issue = this.issue();
    const stateLabel = STATE_ARIA_LABELS[issue.state] ?? issue.state;
    const nextUp = this.isNextUp() ? 'Next up. ' : '';
    const base = `${nextUp}Issue #${issue.issueNumber}: ${issue.title}. State: ${stateLabel}`;
    const warning = this.repoWarningLabel();
    const warningPart = warning ? ` ${warning}` : '';
    const runStatsPart = this._buildRunStatsAriaText();
    const activityPart = this._buildActivityAriaText();
    return `${base}${warningPart}${runStatsPart}${activityPart}`;
  });

  private _buildActivityAriaText(): string {
    const at = this.lastActivityAt();
    if (at === null || !LIVE_STATES.has(this.issue().state)) {
      return '';
    }
    const count = this.commitCount();
    if (count === null) {
      // Pre-handshake: no commit info yet
      return '';
    }
    const silenceMinutes = silentMinutes(at);
    const silencePart = silenceMinutes !== null
      ? `, silent ${silenceMinutes} minute${silenceMinutes === 1 ? '' : 's'}`
      : '';
    return ` Active: ${commitPhrase(count)}${silencePart}.`;
  }

  private _buildRunStatsAriaText(): string {
    const stats = this._visibleRunStats();
    if (stats === null) {
      return '';
    }

    const parts: string[] = [];

    if (stats.runCount > 1) {
      parts.push(`${stats.runCount} runs`);
    }
    if (stats.durationMs !== null) {
      parts.push(_formatDurationImpl(stats.durationMs));
    }
    if (stats.numTurns !== null) {
      parts.push(`${stats.numTurns} turns`);
    }
    if (stats.totalCostUsd !== null) {
      parts.push(_formatCostImpl(stats.totalCostUsd));
    }
    if (stats.inputTokens !== null) {
      parts.push(`${stats.inputTokens.toLocaleString()} input tokens`);
    }
    if (stats.outputTokens !== null) {
      parts.push(`${stats.outputTokens.toLocaleString()} output tokens`);
    }

    return ` Run stats: ${parts.join(', ')}.`;
  }

  repoWarningLabel(): string | null {
    const issue = this.issue();
    if (!REPO_WARNING_STATES.has(issue.state)) {
      return null;
    }
    const status = issue.repositoryEligibilityStatus;
    if (status === 'ineligible') {
      return 'Repo ineligible';
    }
    if (status === 'unreachable') {
      return 'Repo unreachable';
    }
    return null;
  }

  timestamp(): string {
    return timeAgo(this.issue().detectedAt);
  }

  protected _formatCost(value: number): string {
    return _formatCostImpl(value);
  }

  protected _formatDuration(ms: number): string {
    return _formatDurationImpl(ms);
  }

  onCardClick(): void {
    this.toggle.emit();
  }

  onLinkClick(event: MouseEvent): void {
    event.stopPropagation();
  }

  onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      this.toggle.emit();
    }
  }
}

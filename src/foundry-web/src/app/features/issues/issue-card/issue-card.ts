import { Component, InputSignal, OutputEmitterRef, computed, inject, input, output } from '@angular/core';
import { NgClass } from '@angular/common';
import { IssueSummary, LIVE_STATES } from '../issue.model';
import { STATE_ARIA_LABELS } from '../state-display';
import { StateBadgeComponent } from '../../../shared/components/state-badge/state-badge';
import { SafeHrefPipe } from '../../../shared/pipes/safe-href.pipe';
import { TickerService } from '../../../core/services/ticker.service';

const QUEUED_STATES = new Set<string>(['queued', 'detected']);

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

function silentDuration(lastActivityAt: string): string {
  const now = Date.now();
  const then = new Date(lastActivityAt).getTime();
  const diffMs = now - then;
  const diffMinutes = Math.floor(diffMs / 60000);

  if (diffMinutes < 1) {
    return 'silent <1m';
  }
  return `silent ${diffMinutes}m`;
}

@Component({
  selector: 'fd-issue-card',
  standalone: true,
  imports: [StateBadgeComponent, SafeHrefPipe, NgClass],
  template: `
    <button
      type="button"
      class="issue-card"
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

      <div class="issue-card__footer">
        <span class="issue-card__timestamp">{{ timestamp() }}</span>
        @if (_activityLine()) {
          <span class="issue-card__activity"><span class="sr-only">Active, </span>active · {{ _activityLine() }}</span>
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
  readonly toggle: OutputEmitterRef<void> = output<void>();

  private readonly _ticker = inject(TickerService);

  readonly _activityLine = computed(() => {
    // Depend on ticker to re-evaluate every ~30s
    void this._ticker.tick();
    const at = this.lastActivityAt();
    if (at === null || !LIVE_STATES.has(this.issue().state)) {
      return null;
    }
    return silentDuration(at);
  });

  readonly _warningClass = computed(() => {
    const status = this.issue().repositoryEligibilityStatus;
    return status ? (WARNING_CLASSES[status] ?? '') : '';
  });

  issueAriaLabel(): string {
    const issue = this.issue();
    const stateLabel = STATE_ARIA_LABELS[issue.state] ?? issue.state;
    const base = `Issue #${issue.issueNumber}: ${issue.title}. State: ${stateLabel}`;
    const warning = this.repoWarningLabel();
    return warning ? `${base}. ${warning}` : base;
  }

  repoWarningLabel(): string | null {
    const issue = this.issue();
    if (!QUEUED_STATES.has(issue.state)) {
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

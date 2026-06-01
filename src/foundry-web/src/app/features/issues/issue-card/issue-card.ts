import { Component, InputSignal, OutputEmitterRef, input, output } from '@angular/core';
import { IssueSummary } from '../issue.model';
import { StateBadgeComponent } from '../../../shared/components/state-badge/state-badge';
import { SafeHrefPipe } from '../../../shared/pipes/safe-href.pipe';

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

@Component({
  selector: 'fd-issue-card',
  standalone: true,
  imports: [StateBadgeComponent, SafeHrefPipe],
  template: `
    <button
      type="button"
      class="issue-card"
      [attr.aria-expanded]="expanded().toString()"
      [attr.aria-controls]="'detail-' + issue().id"
      [attr.aria-label]="'Issue #' + issue().issueNumber + ': ' + issue().title"
      (click)="onCardClick()"
      (keydown)="onKeydown($event)"
    >
      <div class="issue-card__meta">
        <span class="issue-card__number">#{{ issue().issueNumber }}</span>
        <span class="issue-card__separator" aria-hidden="true">·</span>
        <span class="issue-card__slug">{{ issue().repositorySlug }}</span>
        <div class="issue-card__badge">
          <fd-state-badge [state]="issue().state" />
        </div>
      </div>

      <div
        class="issue-card__title"
        [attr.title]="issue().title"
      >{{ issue().title }}</div>

      <div class="issue-card__footer">
        <span class="issue-card__timestamp">{{ timestamp() }}</span>
        @if (issue().url | safeHref; as safeUrl) {
          <a
            class="issue-card__link"
            [href]="safeUrl"
            target="_blank"
            rel="noopener noreferrer"
            [attr.aria-label]="'Open issue #' + issue().issueNumber + ' on GitHub'"
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
  readonly toggle: OutputEmitterRef<void> = output<void>();

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

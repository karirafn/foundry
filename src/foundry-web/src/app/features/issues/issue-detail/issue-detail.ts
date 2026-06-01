import { Component, InputSignal, OutputEmitterRef, input, output } from '@angular/core';
import { DatePipe } from '@angular/common';
import { IssueDetail } from '../issue.model';
import { SafeHrefPipe } from '../../../shared/pipes/safe-href.pipe';

@Component({
  selector: 'fd-issue-detail',
  standalone: true,
  imports: [DatePipe, SafeHrefPipe],
  template: `
    @if (error()) {
      <div class="issue-detail__error" role="alert">
        <span class="issue-detail__error-message">Failed to load details</span>
        <button
          class="issue-detail__error-retry"
          type="button"
          (click)="retry.emit()"
        >Retry</button>
      </div>
    } @else if (loading()) {
      <div class="issue-detail__skeleton" aria-busy="true" aria-label="Loading issue details">
        <div class="issue-detail__shimmer-bar" style="width: 100%"></div>
        <div class="issue-detail__shimmer-bar" style="width: 60%"></div>
        <div class="issue-detail__shimmer-bar" style="width: 40%"></div>
      </div>
    }

    @if (!error() && detail(); as d) {
      <div
        class="issue-detail__content"
        role="region"
        [attr.aria-label]="'Issue details for #' + d.issueNumber"
      >
        @if (d.body) {
          <div class="issue-detail__section">
            <p class="issue-detail__body">{{ d.body }}</p>
          </div>
        }

        @if (d.labels.length > 0) {
          <div class="issue-detail__section">
            <div class="issue-detail__labels">
              @for (label of d.labels; track label) {
                <span class="issue-detail__label-pill">{{ label }}</span>
              }
            </div>
          </div>
        }

        <div class="issue-detail__fields">
          @if (d.stateDetails.branchName) {
            <div class="issue-detail__field">
              <span class="issue-detail__field-key">Branch</span>
              <span class="issue-detail__branch issue-detail__field-value">{{ d.stateDetails.branchName }}</span>
            </div>
          }

          @if (d.stateDetails.pullRequestUrl | safeHref; as safePrUrl) {
            <div class="issue-detail__field">
              <span class="issue-detail__field-key">Pull Request</span>
              <a
                class="issue-detail__pr-link issue-detail__field-value"
                [href]="safePrUrl"
                target="_blank"
                rel="noopener noreferrer"
                [attr.aria-label]="'Open pull request for issue #' + d.issueNumber"
              >View PR</a>
            </div>
          }

          @if (d.stateDetails.workerRunId) {
            <div class="issue-detail__field">
              <span class="issue-detail__field-key">Worker Run</span>
              <span class="issue-detail__field-value">{{ d.stateDetails.workerRunId }}</span>
            </div>
          }

          @if (d.stateDetails.failureReason) {
            <div class="issue-detail__field">
              <span class="issue-detail__field-key">Failure Reason</span>
              <span class="issue-detail__field-value">{{ d.stateDetails.failureReason }}</span>
            </div>
          }

          @if (d.stateDetails.blockedBy) {
            <div class="issue-detail__field">
              <span class="issue-detail__field-key">Blocked By</span>
              <span class="issue-detail__field-value">{{ d.stateDetails.blockedBy }}</span>
            </div>
          }

          @if (d.stateDetails.completedAt) {
            <div class="issue-detail__field">
              <span class="issue-detail__field-key">Completed</span>
              <span class="issue-detail__field-value">{{ d.stateDetails.completedAt | date: 'medium' }}</span>
            </div>
          }

          @if (d.stateDetails.failedAt) {
            <div class="issue-detail__field">
              <span class="issue-detail__field-key">Failed At</span>
              <span class="issue-detail__field-value">{{ d.stateDetails.failedAt | date: 'medium' }}</span>
            </div>
          }

          @if (d.stateDetails.feedbackCutoffAt) {
            <div class="issue-detail__field">
              <span class="issue-detail__field-key">Feedback Cutoff</span>
              <span class="issue-detail__field-value">{{ d.stateDetails.feedbackCutoffAt | date: 'medium' }}</span>
            </div>
          }

          <div class="issue-detail__field">
            <span class="issue-detail__field-key">Author</span>
            <span class="issue-detail__field-value">{{ d.author }}</span>
          </div>
        </div>
      </div>
    }
  `,
  styleUrl: './issue-detail.scss',
})
export class IssueDetailComponent {
  readonly detail: InputSignal<IssueDetail | null> = input.required<IssueDetail | null>();
  readonly loading: InputSignal<boolean> = input.required<boolean>();
  readonly error: InputSignal<string | null> = input.required<string | null>();
  readonly retry: OutputEmitterRef<void> = output<void>();
}

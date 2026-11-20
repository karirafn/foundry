import { ChangeDetectionStrategy, Component, InputSignal, Signal, computed, input } from '@angular/core';
import { STATE_ARIA_LABELS } from '../../../shared/utils/state-display';
import { BlockingIssue, DeleteFailure } from './delete-failure.model';

@Component({
  selector: 'fd-delete-failure',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (_blockingIssues(); as blockingIssues) {
      <p class="delete-failure__lead">{{ lead() }}</p>
      <ul class="delete-failure__issues">
        @for (issue of blockingIssues; track issue.repositorySlug + '#' + issue.issueNumber) {
          <li>{{ issue.repositorySlug }}#{{ issue.issueNumber }} — {{ _stateLabels[issue.state] }}</li>
        }
      </ul>
      <p class="delete-failure__hint">Try again once these finish.</p>
    } @else {
      {{ _message() }}
    }
  `,
  styleUrl: './delete-failure.scss',
})
export class DeleteFailureComponent {
  readonly failure: InputSignal<DeleteFailure> = input.required<DeleteFailure>();
  readonly lead: InputSignal<string> = input.required<string>();

  protected readonly _stateLabels = STATE_ARIA_LABELS;

  protected readonly _blockingIssues: Signal<readonly BlockingIssue[] | null> = computed(() => {
    const failure = this.failure();
    return failure.kind === 'refused' ? failure.blockingIssues : null;
  });

  protected readonly _message: Signal<string> = computed(() => {
    const failure = this.failure();
    return failure.kind === 'failed' ? failure.message : '';
  });
}

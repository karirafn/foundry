import { ChangeDetectionStrategy, Component, InputSignal, input } from '@angular/core';

/**
 * Indeterminate "Adding…" indicator shown while a repository create request is in flight.
 * It carries no step information: the client never times or infers server steps it has not received.
 */
@Component({
  selector: 'fd-repository-add-progress',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="repository-add-progress" role="status" aria-live="polite">
      <span class="repository-add-progress__label">{{ label() }}</span>
      <span class="repository-add-progress__track" aria-hidden="true">
        <span class="repository-add-progress__bar"></span>
      </span>
    </div>
  `,
  styleUrl: './repository-add-progress.scss',
})
export class RepositoryAddProgressComponent {
  readonly label: InputSignal<string> = input<string>('Adding…');
}

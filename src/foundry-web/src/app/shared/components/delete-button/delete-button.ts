import { ChangeDetectionStrategy, Component, InputSignal, OutputEmitterRef, input, output } from '@angular/core';
import { SpinnerComponent } from '../spinner/spinner';

@Component({
  selector: 'fd-delete-button',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [SpinnerComponent],
  template: `
    <button
      class="row-actions__delete-btn"
      type="button"
      [attr.aria-label]="deleteLabel()"
      [attr.title]="deleteLabel()"
      [attr.aria-disabled]="deleteBusy() ? 'true' : null"
      (click)="onClick()"
    >
      @if (deleteBusy()) {
        <fd-spinner />
      } @else {
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
          <polyline points="3 6 5 6 21 6" />
          <path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6" />
          <path d="M10 11v6" />
          <path d="M14 11v6" />
          <path d="M9 6V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2" />
        </svg>
      }
    </button>
  `,
  styleUrl: './delete-button.scss',
})
export class DeleteButtonComponent {
  readonly deleteLabel = input.required<string>();
  readonly deleteBusy = input<boolean>(false);

  readonly delete: OutputEmitterRef<void> = output<void>();

  protected onClick(): void {
    if (this.deleteBusy()) {
      return;
    }
    this.delete.emit();
  }
}

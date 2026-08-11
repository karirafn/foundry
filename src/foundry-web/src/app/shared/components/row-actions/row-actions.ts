import { ChangeDetectionStrategy, Component, OutputEmitterRef, input, output } from '@angular/core';
import { SpinnerComponent } from '../spinner/spinner';

@Component({
  selector: 'fd-row-actions',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [SpinnerComponent],
  template: `
    <div class="row-actions">
      <button
        class="row-actions__edit-btn"
        type="button"
        [attr.aria-label]="editLabel()"
        [attr.title]="editLabel()"
        [attr.aria-disabled]="deleteBusy() ? 'true' : null"
        (click)="edit.emit()"
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
          <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7" />
          <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z" />
        </svg>
      </button>
      <button
        class="row-actions__delete-btn"
        type="button"
        [attr.aria-label]="deleteLabel()"
        [attr.title]="deleteLabel()"
        [attr.aria-disabled]="deleteBusy() ? 'true' : null"
        (click)="onDeleteClick()"
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
    </div>
  `,
  styleUrl: './row-actions.scss',
})
export class RowActionsComponent {
  readonly editLabel = input.required<string>();
  readonly deleteLabel = input.required<string>();
  readonly deleteBusy = input<boolean>(false);

  readonly edit: OutputEmitterRef<void> = output<void>();
  readonly delete: OutputEmitterRef<void> = output<void>();

  protected onDeleteClick(): void {
    if (this.deleteBusy()) {
      return;
    }
    this.delete.emit();
  }
}

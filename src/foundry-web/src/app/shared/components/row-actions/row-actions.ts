import { ChangeDetectionStrategy, Component, OutputEmitterRef, input, output } from '@angular/core';
import { DeleteButtonComponent } from '../delete-button/delete-button';

@Component({
  selector: 'fd-row-actions',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DeleteButtonComponent],
  template: `
    <div class="row-actions">
      <button
        class="row-actions__edit-btn"
        type="button"
        [attr.aria-label]="editLabel()"
        [attr.title]="editLabel()"
        [attr.aria-disabled]="deleteBusy() ? 'true' : null"
        (click)="onEditClick()"
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
      <fd-delete-button
        [deleteLabel]="deleteLabel()"
        [deleteBusy]="deleteBusy()"
        (delete)="delete.emit()"
      />
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

  protected onEditClick(): void {
    if (this.deleteBusy()) {
      return;
    }
    this.edit.emit();
  }
}

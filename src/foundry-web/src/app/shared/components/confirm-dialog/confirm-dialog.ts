import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  InputSignal,
  OutputEmitterRef,
  Signal,
  effect,
  input,
  output,
  viewChild,
} from '@angular/core';

let nextId = 0;

/**
 * Accessible confirmation modal built on the native `<dialog>` element and `showModal()`,
 * which provides focus trapping, Escape-to-dismiss and an inert backdrop for free.
 *
 * The parent owns the `open` state: it sets `open` to show the dialog and resets it on
 * `confirmed` or `dismissed`.
 */
@Component({
  selector: 'fd-confirm-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <dialog
      #dialog
      class="confirm-dialog"
      [attr.aria-labelledby]="_titleId"
      [attr.aria-describedby]="_messageId"
      (cancel)="onNativeCancel($event)"
    >
      <h2 class="confirm-dialog__title" [id]="_titleId">{{ title() }}</h2>
      <p class="confirm-dialog__message" [id]="_messageId">{{ message() }}</p>
      <div class="confirm-dialog__actions">
        <button
          class="confirm-dialog__cancel"
          type="button"
          autofocus
          (click)="dismiss()"
        >{{ cancelLabel() }}</button>
        <button
          class="confirm-dialog__confirm"
          type="button"
          [class.confirm-dialog__confirm--destructive]="destructive()"
          (click)="confirm()"
        >{{ confirmLabel() }}</button>
      </div>
    </dialog>
  `,
  styleUrl: './confirm-dialog.scss',
})
export class ConfirmDialogComponent {
  readonly open: InputSignal<boolean> = input<boolean>(false);
  readonly title: InputSignal<string> = input.required<string>();
  readonly message: InputSignal<string> = input.required<string>();
  readonly confirmLabel: InputSignal<string> = input<string>('Confirm');
  readonly cancelLabel: InputSignal<string> = input<string>('Cancel');
  readonly destructive: InputSignal<boolean> = input<boolean>(false);

  readonly confirmed: OutputEmitterRef<void> = output<void>();
  readonly dismissed: OutputEmitterRef<void> = output<void>();

  private readonly _id = nextId++;
  protected readonly _titleId = `fd-confirm-dialog-title-${this._id}`;
  protected readonly _messageId = `fd-confirm-dialog-message-${this._id}`;

  private readonly _dialog: Signal<ElementRef<HTMLDialogElement>> =
    viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  constructor() {
    effect(() => {
      const dialog = this._dialog().nativeElement;

      if (this.open()) {
        if (!dialog.open) {
          // showModal() is missing in some non-browser environments (e.g. older jsdom).
          if (typeof dialog.showModal === 'function') {
            dialog.showModal();
          } else {
            dialog.setAttribute('open', '');
          }
        }
      } else if (dialog.open) {
        this._closeDialog(dialog);
      }
    });
  }

  protected confirm(): void {
    this._closeDialog(this._dialog().nativeElement);
    this.confirmed.emit();
  }

  protected dismiss(): void {
    this._closeDialog(this._dialog().nativeElement);
    this.dismissed.emit();
  }

  /** Escape key: let the parent drive state rather than the browser closing the dialog itself. */
  protected onNativeCancel(event: Event): void {
    event.preventDefault();
    this.dismiss();
  }

  private _closeDialog(dialog: HTMLDialogElement): void {
    if (typeof dialog.close === 'function') {
      dialog.close();
    } else {
      dialog.removeAttribute('open');
    }
  }
}

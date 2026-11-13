import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  InputSignal,
  Injector,
  OutputEmitterRef,
  Signal,
  TemplateRef,
  afterNextRender,
  contentChild,
  effect,
  inject,
  input,
  output,
  untracked,
  viewChild,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';

/**
 * A right-hand pane shell (a full-width sheet on narrow screens): a non-modal dialog that hosts the
 * projected template, moves focus to its heading on open, and asks to be dismissed by its close
 * button, Escape, or a pointer down outside it. The parent owns `open` and focus return, and never
 * wraps the sheet in `@if`: the leave animation lives in this template.
 */
@Component({
  selector: 'fd-side-sheet',
  imports: [NgTemplateOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(document:pointerdown)': 'onDocumentPointerDown($event)' },
  templateUrl: './side-sheet.html',
  styleUrl: './side-sheet.scss',
})
export class SideSheetComponent {
  readonly open: InputSignal<boolean> = input.required<boolean>();
  /** Id of the heading inside the content; it labels the dialog and takes focus on open. */
  readonly labelledBy: InputSignal<string> = input.required<string>();
  readonly closeLabel: InputSignal<string> = input<string>('Close');
  readonly dismissed: OutputEmitterRef<void> = output<void>();

  protected readonly content: Signal<TemplateRef<unknown>> = contentChild.required(TemplateRef);
  private readonly _panel: Signal<ElementRef<HTMLElement> | undefined> = viewChild<ElementRef<HTMLElement>>('panel');
  private readonly _injector = inject(Injector);
  private readonly _host: HTMLElement = inject(ElementRef<HTMLElement>).nativeElement;

  constructor() {
    let wasOpen = false;
    effect(() => {
      const open = this.open();
      if (open && !wasOpen) {
        untracked(() => this._focusHeading());
      }
      wasOpen = open;
    });
  }

  protected onKeydown(event: KeyboardEvent): void {
    if (event.key !== 'Escape') {
      return;
    }
    // A confirm <dialog> inside the sheet owns its own Escape.
    if (event.target instanceof Element && event.target.closest('dialog[open]') !== null) {
      return;
    }
    event.preventDefault();
    this.dismissed.emit();
  }

  protected onDocumentPointerDown(event: Event): void {
    if (!this.open() || !(event.target instanceof Element)) {
      return;
    }
    const panel = this._panel()?.nativeElement;
    if (panel?.contains(event.target)) {
      return;
    }
    // A card swaps the sheet's content rather than closing it.
    if (event.target.closest('[data-issue-card]') !== null) {
      return;
    }
    this.dismissed.emit();
  }

  private _focusHeading(): void {
    afterNextRender(
      () => {
        const heading = this._host.ownerDocument.getElementById(this.labelledBy());
        heading?.focus();
      },
      { injector: this._injector },
    );
  }
}

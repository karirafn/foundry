import {
  ChangeDetectionStrategy,
  Component,
  InputSignal,
  OutputEmitterRef,
  Signal,
  computed,
  input,
  output,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';

/**
 * One band of the board: a section labelled by its `h2`, which holds the label and a projected
 * `[bandCount]`, with a projected `[bandAside]` beside the heading and the band's content below.
 * With `disclosure`, the heading's content is a button that toggles the content it `controls`.
 */
@Component({
  selector: 'fd-board-band',
  imports: [NgTemplateOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './board-band.html',
  styleUrl: './board-band.scss',
})
export class BoardBandComponent {
  /** Names the band's ids: the heading is `board-band-{bandId}`. */
  readonly bandId: InputSignal<string> = input.required<string>();
  readonly label: InputSignal<string> = input.required<string>();
  /** The one tinted surface on the page (Needs you, when it has items). */
  readonly tinted: InputSignal<boolean> = input<boolean>(false);
  readonly disclosure: InputSignal<boolean> = input<boolean>(false);
  readonly expanded: InputSignal<boolean> = input<boolean>(false);
  /** The id of the element the disclosure shows and hides. */
  readonly controls: InputSignal<string | null> = input<string | null>(null);
  readonly toggled: OutputEmitterRef<void> = output<void>();

  protected readonly headingId: Signal<string> = computed(() => `board-band-${this.bandId()}`);
}

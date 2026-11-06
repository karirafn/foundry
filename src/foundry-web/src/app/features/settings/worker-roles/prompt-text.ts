import {
  ChangeDetectionStrategy,
  Component,
  InputSignal,
  Signal,
  computed,
  input,
} from '@angular/core';
import { PromptSegment, splitPromptPlaceholders } from './prompt-segments';

/**
 * Renders prompt text verbatim as preformatted text, highlighting `{placeholder}` tokens
 * rather than filling them. Only text given a `regionLabel` scrolls, as a focusable named region. Text is bound through interpolation only, never innerHTML.
 */
@Component({
  selector: 'fd-prompt-text',
  changeDetection: ChangeDetectionStrategy.OnPush,
  // The <pre> content stays on one line: whitespace inside it is rendered verbatim.
  template: `<pre
    class="prompt-text"
    [class.prompt-text--scrollable]="regionLabel() !== null"
    [attr.role]="regionLabel() !== null ? 'region' : null"
    [attr.tabindex]="regionLabel() !== null ? 0 : null"
    [attr.aria-label]="regionLabel()"
  ><code>@for (segment of segments(); track $index) {@if (segment.placeholder) {<mark class="prompt-text__placeholder">{{ segment.text }}</mark>} @else {<span>{{ segment.text }}</span>}}</code></pre>`,
  styleUrl: './prompt-text.scss',
})
export class PromptTextComponent {
  readonly text: InputSignal<string> = input.required<string>();
  /** Names the scroll region; leave null for text that is shown in full. */
  readonly regionLabel: InputSignal<string | null> = input<string | null>(null);

  protected readonly segments: Signal<PromptSegment[]> = computed(() =>
    splitPromptPlaceholders(this.text()),
  );
}

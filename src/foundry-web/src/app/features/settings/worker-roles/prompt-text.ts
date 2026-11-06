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
 * rather than filling them. Text is bound through interpolation only, never innerHTML.
 */
@Component({
  selector: 'fd-prompt-text',
  changeDetection: ChangeDetectionStrategy.OnPush,
  // The <pre> content stays on one line: whitespace inside it is rendered verbatim.
  template: `<pre
    class="prompt-text"
    [attr.aria-label]="label() || null"
    tabindex="0"
  ><code>@for (segment of segments(); track $index) {@if (segment.placeholder) {<mark class="prompt-text__placeholder">{{ segment.text }}</mark>} @else {<span>{{ segment.text }}</span>}}</code></pre>`,
  styleUrl: './prompt-text.scss',
})
export class PromptTextComponent {
  readonly text: InputSignal<string> = input.required<string>();
  readonly label: InputSignal<string> = input<string>('');

  protected readonly segments: Signal<PromptSegment[]> = computed(() =>
    splitPromptPlaceholders(this.text()),
  );
}

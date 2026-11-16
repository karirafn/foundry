import { ChangeDetectionStrategy, Component, InputSignal, input } from '@angular/core';
import { ToolchainEntry } from '../../../core/models/toolchain.model';

/** A read-only name/version list of the tools a worker image or run carries, in the order they were reported. */
@Component({
  selector: 'fd-toolchain-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <dl class="toolchain-list">
      @for (tool of tools(); track $index) {
        <div class="toolchain-list__item">
          <dt>{{ tool.name }}</dt>
          <dd><code>{{ tool.version }}</code></dd>
        </div>
      }
    </dl>
  `,
  styleUrl: './toolchain-list.scss',
})
export class ToolchainListComponent {
  readonly tools: InputSignal<readonly ToolchainEntry[]> = input.required<readonly ToolchainEntry[]>();
}

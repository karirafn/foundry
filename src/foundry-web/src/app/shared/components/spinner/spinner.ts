import { ChangeDetectionStrategy, Component, input, InputSignal } from '@angular/core';

@Component({
  selector: 'fd-spinner',
  changeDetection: ChangeDetectionStrategy.OnPush,
  standalone: true,
  template: `
    <span class="spinner" aria-hidden="true" [style.width.px]="size()" [style.height.px]="size()"></span>
  `,
  styleUrl: './spinner.scss',
})
export class SpinnerComponent {
  readonly size: InputSignal<number> = input<number>(14);
}

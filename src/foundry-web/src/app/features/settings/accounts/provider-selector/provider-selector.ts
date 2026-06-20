import {
  ChangeDetectionStrategy,
  Component,
  InputSignal,
  OutputEmitterRef,
  input,
  output,
} from '@angular/core';
import { ProviderType } from '../account.model';

const GITHUB_BASE_URL = 'https://github.com';
const GITLAB_BASE_URL = 'https://gitlab.com';

let nextId = 0;

@Component({
  selector: 'fd-provider-selector',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div
      class="provider-selector"
      role="radiogroup"
      [attr.aria-labelledby]="ariaLabelledBy() ?? null"
      [attr.aria-label]="ariaLabelledBy() ? null : 'Provider type'"
    >
      <label class="provider-selector__label">
        <input
          class="provider-selector__radio"
          type="radio"
          [name]="_radioGroupName"
          value="GitHub"
          [checked]="provider() === 'GitHub'"
          [disabled]="disabled()"
          (change)="onProviderChange('GitHub')"
        />
        GitHub
      </label>
      <label class="provider-selector__label">
        <input
          class="provider-selector__radio"
          type="radio"
          [name]="_radioGroupName"
          value="GitLab"
          [checked]="provider() === 'GitLab'"
          [disabled]="disabled()"
          (change)="onProviderChange('GitLab')"
        />
        GitLab
      </label>
    </div>
  `,
  styleUrl: './provider-selector.scss',
})
export class ProviderSelectorComponent {
  readonly provider: InputSignal<ProviderType> = input.required<ProviderType>();
  readonly disabled: InputSignal<boolean> = input<boolean>(false);
  readonly ariaLabelledBy: InputSignal<string | undefined> = input<string | undefined>(undefined);

  readonly providerChange: OutputEmitterRef<ProviderType> = output<ProviderType>();
  readonly defaultBaseUrlChange: OutputEmitterRef<string> = output<string>();

  protected readonly _radioGroupName: string = `providerType-${++nextId}`;

  onProviderChange(provider: ProviderType): void {
    this.providerChange.emit(provider);
    const defaultUrl = provider === 'GitLab' ? GITLAB_BASE_URL : GITHUB_BASE_URL;
    this.defaultBaseUrlChange.emit(defaultUrl);
  }
}

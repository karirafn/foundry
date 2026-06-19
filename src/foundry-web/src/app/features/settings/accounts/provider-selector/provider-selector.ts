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

@Component({
  selector: 'fd-provider-selector',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div
      class="provider-selector"
      role="radiogroup"
      aria-label="Provider type"
    >
      <label class="provider-selector__label">
        <input
          class="provider-selector__radio"
          type="radio"
          name="providerType"
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
          name="providerType"
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

  readonly providerChange: OutputEmitterRef<ProviderType> = output<ProviderType>();
  readonly defaultBaseUrlChange: OutputEmitterRef<string> = output<string>();

  onProviderChange(provider: ProviderType): void {
    this.providerChange.emit(provider);
    const defaultUrl = provider === 'GitLab' ? GITLAB_BASE_URL : GITHUB_BASE_URL;
    this.defaultBaseUrlChange.emit(defaultUrl);
  }
}

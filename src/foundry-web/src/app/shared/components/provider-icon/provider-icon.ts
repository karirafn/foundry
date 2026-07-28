import { ChangeDetectionStrategy, Component, input, InputSignal } from '@angular/core';
import { providerDisplayName } from '../../utils/provider.util';

const GITHUB_PATH =
  'M12 1C5.923 1 1 5.923 1 12c0 4.867 3.149 8.979 7.521 10.436.55.096.756-.233.756-.522' +
  ' 0-.262-.013-1.128-.013-2.049-2.764.509-3.479-.674-3.699-1.292-.124-.317-.66-1.293' +
  '-1.127-1.554-.385-.207-.935-.715-.014-.729.866-.014 1.485.797 1.691 1.128.99 1.663' +
  ' 2.571 1.196 3.204.907.096-.715.385-1.196.701-1.471-2.448-.275-5.005-1.224-5.005' +
  '-5.432 0-1.196.426-2.186 1.128-2.956-.111-.275-.496-1.402.11-2.915 0 0 .921-.288' +
  ' 3.024 1.128a10.193 10.193 0 0 1 2.75-.371c.936 0 1.871.123 2.75.371 2.104-1.43' +
  ' 3.025-1.128 3.025-1.128.605 1.513.221 2.64.111 2.915.701.77 1.127 1.747 1.127' +
  ' 2.956 0 4.222-2.571 5.157-5.019 5.432.399.344.743 1.004.743 2.035 0 1.471-.014' +
  ' 2.654-.014 3.025 0 .289.206.632.756.522C19.851 20.979 23 16.854 23 12c0-6.077' +
  '-4.922-11-11-11Z';

const GITLAB_PATH =
  'm23.6 9.6-.03-.09-3.27-8.53a.85.85 0 0 0-.34-.4.87.87 0 0 0-1 .05.88.88 0 0 0-.29' +
  '.45L16.47 7.9H7.53L5.34 1.08a.87.87 0 0 0-.29-.45.87.87 0 0 0-1-.05.86.86 0 0 0' +
  '-.34.4L.43 9.51l-.03.09a6.06 6.06 0 0 0 2.01 7l.01.01.03.02 4.97 3.72 2.46 1.86' +
  ' 1.5 1.13a1.01 1.01 0 0 0 1.22 0l1.5-1.13 2.46-1.86 5-3.74.01-.01a6.06 6.06 0 0' +
  ' 0 2.01-7Z';

const FALLBACK_PATH =
  'M3 3h18a1 1 0 0 1 1 1v16a1 1 0 0 1-1 1H3a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1zm9 3a4 4' +
  ' 0 1 0 0 8 4 4 0 0 0 0-8zm0 10c-4.418 0-8 1.79-8 4v1h16v-1c0-2.21-3.582-4-8-4z';

@Component({
  selector: 'fd-provider-icon',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    'role': 'img',
    '[attr.aria-label]': 'providerDisplayName(providerType())',
    '[attr.title]': 'providerDisplayName(providerType())',
  },
  template: `
    @switch (providerType().toLowerCase()) {
      @case ('github') {
        <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
          <path [attr.d]="githubPath" />
        </svg>
      }
      @case ('gitlab') {
        <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
          <path [attr.d]="gitlabPath" />
        </svg>
      }
      @default {
        <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
          <path [attr.d]="fallbackPath" />
        </svg>
      }
    }
  `,
  styleUrl: './provider-icon.scss',
})
export class ProviderIconComponent {
  readonly providerType: InputSignal<string> = input.required<string>();

  readonly providerDisplayName = providerDisplayName;

  readonly githubPath = GITHUB_PATH;
  readonly gitlabPath = GITLAB_PATH;
  readonly fallbackPath = FALLBACK_PATH;
}

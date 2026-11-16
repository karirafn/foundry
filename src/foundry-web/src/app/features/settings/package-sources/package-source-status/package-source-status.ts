import { ChangeDetectionStrategy, Component, InputSignal, OutputEmitterRef, Signal, computed, input, output } from '@angular/core';
import { DatePipe } from '@angular/common';
import { SpinnerComponent } from '../../../../shared/components/spinner/spinner';
import { PackageSource, PendingCheckAction, assertNever } from '../package-source.model';

type Tone = 'reachable' | 'unreachable' | 'unchecked';

interface StatusView {
  tone: Tone;
  label: string;
  credentialItem: string | null;
  credentialUsername: string | null;
  showTime: boolean;
}

@Component({
  selector: 'fd-package-source-status',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, SpinnerComponent],
  template: `
    <div class="package-source-status__strip" role="group" [attr.aria-label]="'Status of ' + source().url">
      @if (pending(); as p) {
        <span class="package-source-status__state">
          <fd-spinner />
          <span class="package-source-status__label">{{ _pendingLabel(p.action) }}</span>
        </span>
      } @else {
        @let view = _view();
        <span class="package-source-status__state">
          <span
            [class]="'package-source-status__dot package-source-status__dot--' + view.tone"
            aria-hidden="true"
          ></span>
          <span [class]="'package-source-status__label package-source-status__label--' + view.tone">{{ view.label }}</span>
        </span>
        @if (view.credentialItem !== null) {
          <span class="package-source-status__meta">
            {{ view.credentialItem }}
            @if (view.credentialUsername !== null) {
              <span class="package-source-status__username">{{ view.credentialUsername }}</span>
            }
          </span>
        }
        @if (view.showTime && source().lastCheckedAt; as checkedAt) {
          <span class="package-source-status__meta">
            Checked <time [attr.datetime]="checkedAt">{{ checkedAt | date: 'medium' }}</time>
          </span>
        }
      }
    </div>

    @if (!pending() && source().status.kind === 'unreachable') {
      <div class="package-source-status__warning" role="group" [attr.aria-label]="'Problem with ' + source().url">
        <div class="package-source-status__warning-line">
          <svg width="16" height="16" viewBox="0 0 24 24"
               fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"
               stroke-linejoin="round" aria-hidden="true">
            <path d="M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"></path>
            <line x1="12" y1="9" x2="12" y2="13"></line>
            <line x1="12" y1="17" x2="12.01" y2="17"></line>
          </svg>
          <span class="package-source-status__warning-text">
            {{ _problem() }}
            Repositories that declare it dispatch nothing. Foundry retries automatically after 15 minutes.
          </span>
          <button
            type="button"
            class="package-source-status__credential-btn"
            [attr.aria-label]="_credentialActionLabel() + ' for ' + source().url"
            (click)="editCredential.emit()"
          >{{ _credentialActionLabel() }}</button>
        </div>
      </div>
    }
  `,
  styleUrl: './package-source-status.scss',
})
export class PackageSourceStatusComponent {
  readonly source: InputSignal<PackageSource> = input.required<PackageSource>();
  readonly pending: InputSignal<{ action: PendingCheckAction } | null> = input<{ action: PendingCheckAction } | null>(null);

  readonly editCredential: OutputEmitterRef<void> = output<void>();

  protected readonly _view: Signal<StatusView> = computed(() => {
    const source = this.source();
    const status = source.status;
    const username = source.credential?.username ?? null;
    const credentialOrNone: Pick<StatusView, 'credentialItem' | 'credentialUsername'> =
      username !== null
        ? { credentialItem: 'credential', credentialUsername: username }
        : { credentialItem: 'no credential registered', credentialUsername: null };

    switch (status.kind) {
      case 'reachableViaCredential':
        return { tone: 'reachable', label: 'Reachable', credentialItem: 'via credential', credentialUsername: username, showTime: true };
      case 'reachableViaAccount':
        return {
          tone: 'reachable',
          label: 'Reachable',
          credentialItem: `via account ${status.accountName} · ${status.host}`,
          credentialUsername: null,
          showTime: true,
        };
      case 'reachableAnonymously':
        return { tone: 'reachable', label: 'Reachable', credentialItem: 'no credential needed', credentialUsername: null, showTime: true };
      case 'unreachable':
        return { tone: 'unreachable', label: 'Unreachable', ...credentialOrNone, showTime: true };
      case 'unchecked':
        return { tone: 'unchecked', label: 'Not checked yet', ...credentialOrNone, showTime: false };
      default:
        return assertNever(status);
    }
  });

  protected readonly _problem: Signal<string> = computed(() => {
    const source = this.source();
    const status = source.status;
    if (status.kind !== 'unreachable') {
      return '';
    }
    if (status.httpStatus === null) {
      return `Can't reach this source: ${status.reason}.`;
    }
    const withCred = source.credential ? ` with credential ${source.credential.username}` : '';
    return `Can't read this source${withCred} (HTTP ${status.httpStatus}): ${status.reason}.`;
  });

  protected readonly _credentialActionLabel: Signal<string> = computed(() =>
    this.source().credential ? 'Replace credential' : 'Add credential',
  );

  protected _pendingLabel(action: PendingCheckAction): string {
    switch (action) {
      case 'saved':
        return 'Credential saved — checking access…';
      case 'removed':
        return 'Credential removed — checking access…';
      default:
        return assertNever(action);
    }
  }
}

import { Component, ChangeDetectionStrategy, Signal, WritableSignal, input, output, signal } from '@angular/core';
import { DatePipe, NgTemplateOutlet } from '@angular/common';
import { OAuthStatus } from '../settings.model';

const COPY_LABEL = 'Copy';
const COPIED_LABEL = 'Copied';
const COPY_RESET_MS = 2000;

@Component({
  selector: 'fd-oauth-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, NgTemplateOutlet],
  template: `
    <div class="oauth-panel">
      <div class="oauth-panel__header">
        <span
          class="oauth-panel__badge"
          [class.oauth-panel__badge--success]="status() === 'Present'"
          [class.oauth-panel__badge--warning]="status() === 'ReLoginNeeded'"
          [class.oauth-panel__badge--error]="status() === 'NotConfigured'"
        >
          @if (status() === 'Present') { Signed in }
          @if (status() === 'ReLoginNeeded') { Re-login needed }
          @if (status() === 'NotConfigured') { Not configured }
        </span>
      </div>

      @if (status() === 'Present') {
        <div class="oauth-panel__present">
          <div class="oauth-panel__rows">
            <div class="oauth-panel__row">
              <span class="oauth-panel__row-label">Active account</span>
              <span class="oauth-panel__row-value">{{ subscriptionType() ?? 'Claude account' }}</span>
            </div>
            <div class="oauth-panel__row">
              <span class="oauth-panel__row-label">Access token expires</span>
              <span class="oauth-panel__expires-value">
                @if (expiresAt()) {
                  {{ expiresAt() | date:'medium' }}
                } @else {
                  —
                }
              </span>
            </div>
          </div>
          <p class="oauth-panel__hint">
            Claude Code refreshes this token automatically — Foundry never stores it.
          </p>
        </div>
      }

      @if (status() === 'ReLoginNeeded') {
        <div class="oauth-panel__relogin">
          <p class="oauth-panel__message">
            Your credential needs a refresh. Run the login command below to sign in again.
          </p>
          <ng-container *ngTemplateOutlet="loginCommandBlock" />
        </div>
      }

      @if (status() === 'NotConfigured') {
        <div class="oauth-panel__not-configured">
          <p class="oauth-panel__message">
            No credential found yet. Run this command in your terminal to sign in:
          </p>
          <ng-container *ngTemplateOutlet="loginCommandBlock" />
        </div>
      }
    </div>

    <ng-template #loginCommandBlock>
      <div class="oauth-panel__command-block">
        @if (loginCommandLoading()) {
          <div role="status" aria-live="polite" class="oauth-panel__loading">
            <span class="oauth-panel__spinner" aria-hidden="true"></span>
            Preparing login command…
          </div>
        } @else if (loginCommandError()) {
          <div role="alert" class="oauth-panel__command-error">
            Couldn't load the login command.
            <button
              class="oauth-panel__retry-command-btn"
              type="button"
              (click)="fetchCommand.emit()"
            >Retry</button>
          </div>
        } @else if (loginCommand()) {
          <div class="oauth-panel__command-wrapper">
            <pre
              class="oauth-panel__command-pre"
              tabindex="0"
              aria-label="OAuth login command"
            >{{ loginCommand() }}</pre>
            <button
              class="oauth-panel__copy-btn"
              type="button"
              aria-label="Copy login command"
              (click)="onCopy()"
            >{{ _copyLabel() }}</button>
          </div>
          <span
            class="oauth-panel__copy-announcement"
            aria-live="polite"
            aria-atomic="true"
          >{{ _copyAnnouncement() }}</span>
          <button
            class="oauth-panel__refresh-btn"
            type="button"
            (click)="refresh.emit()"
          >I've logged in — refresh</button>
        }
        @if (loginCommandError()) {
          <button
            class="oauth-panel__refresh-btn"
            type="button"
            (click)="refresh.emit()"
          >I've logged in — refresh</button>
        }
      </div>
    </ng-template>
  `,
  styleUrl: './oauth-panel.scss',
})
export class OAuthPanelComponent {
  readonly status = input.required<OAuthStatus>();
  readonly expiresAt = input<string | null>(null);
  readonly subscriptionType = input<string | null>(null);
  readonly loginCommand = input<string | null>(null);
  readonly loginCommandLoading = input<boolean>(false);
  readonly loginCommandError = input<string | null>(null);

  readonly refresh = output<void>();
  readonly fetchCommand = output<void>();

  private readonly _copyLabelSignal: WritableSignal<string> = signal(COPY_LABEL);
  protected readonly _copyLabel: Signal<string> = this._copyLabelSignal.asReadonly();

  private readonly _copyAnnouncementSignal: WritableSignal<string> = signal('');
  protected readonly _copyAnnouncement: Signal<string> = this._copyAnnouncementSignal.asReadonly();

  onCopy(): void {
    const command = this.loginCommand();
    if (!command) {
      return;
    }

    navigator.clipboard.writeText(command).then(
      () => {
        this._copyLabelSignal.set(COPIED_LABEL);
        this._copyAnnouncementSignal.set('Command copied to clipboard');
        setTimeout(() => {
          this._copyLabelSignal.set(COPY_LABEL);
          this._copyAnnouncementSignal.set('');
        }, COPY_RESET_MS);
      },
      () => {
        this._copyAnnouncementSignal.set('Copy failed — select the command text manually.');
      }
    );
  }
}

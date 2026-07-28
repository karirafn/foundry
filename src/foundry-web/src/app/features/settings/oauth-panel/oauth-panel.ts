import { Component, ChangeDetectionStrategy, input, output } from '@angular/core';
import { LoginError, LoginPhase, OAuthStatus } from '../../../core/models/settings.model';
import { LoginFlowComponent } from './login-flow/login-flow';

@Component({
  selector: 'fd-oauth-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [LoginFlowComponent],
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
              <span class="oauth-panel__row-label">Account</span>
              <span class="oauth-panel__row-value">{{ accountEmail() ?? 'Claude account' }}</span>
            </div>
            @if (accountOrgName()) {
              <div class="oauth-panel__row">
                <span class="oauth-panel__row-label">Organization</span>
                <span class="oauth-panel__row-value">{{ accountOrgName() }}</span>
              </div>
            }
            <div class="oauth-panel__row">
              <span class="oauth-panel__row-label">Plan</span>
              <span class="oauth-panel__row-value">{{ subscriptionType() ?? '—' }}</span>
            </div>
          </div>

          @if (!loginPhase()) {
            <button
              class="oauth-panel__switch-btn"
              type="button"
              (click)="startLogin.emit()"
            >Switch account</button>
          }
        </div>
      }

      @if (status() === 'ReLoginNeeded' && !loginPhase()) {
        <div class="oauth-panel__entry">
          <p class="oauth-panel__message">
            Your saved credential expired. Sign in again to resume workers.
          </p>
          <button
            class="oauth-panel__login-btn"
            type="button"
            (click)="startLogin.emit()"
          >Log in again</button>
        </div>
      }

      @if (status() === 'NotConfigured' && !loginPhase()) {
        <div class="oauth-panel__entry">
          <p class="oauth-panel__message">
            No account is signed in yet. Workers stay paused until you sign in.
          </p>
          <button
            class="oauth-panel__login-btn"
            type="button"
            (click)="startLogin.emit()"
          >Log in</button>
        </div>
      }

      @if (loginPhase()) {
        <fd-oauth-login-flow
          [phase]="loginPhase()!"
          [url]="loginUrl()"
          [error]="loginError()"
          [accountEmail]="accountEmail()"
          (submitCode)="onSubmitCode($event)"
          (retry)="startLogin.emit()"
          (cancel)="onCancel()"
        />
      }
    </div>
  `,
  styleUrl: './oauth-panel.scss',
})
export class OAuthPanelComponent {
  readonly status = input.required<OAuthStatus>();
  readonly subscriptionType = input<string | null>(null);
  readonly accountEmail = input<string | null>(null);
  readonly accountOrgName = input<string | null>(null);
  readonly loginPhase = input<LoginPhase | null>(null);
  readonly loginUrl = input<string | null>(null);
  readonly loginError = input<LoginError | null>(null);

  readonly startLogin = output<void>();
  readonly submitCode = output<string>();
  readonly cancel = output<void>();

  onSubmitCode(code: string): void {
    this.submitCode.emit(code);
  }

  onCancel(): void {
    this.cancel.emit();
  }
}

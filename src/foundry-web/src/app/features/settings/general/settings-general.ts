import {
  Component,
  ChangeDetectionStrategy,
  ElementRef,
  Injector,
  Signal,
  ViewChild,
  WritableSignal,
  afterNextRender,
  computed,
  effect,
  inject,
  runInInjectionContext,
  signal,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { SettingsService } from '../../../core/services/settings.service';
import { DispatchService } from '../../../core/services/dispatch.service';
import { RateBudgetPanelComponent } from './rate-budget-panel/rate-budget-panel';
import { ApiKeyStatus, AuthMode, ImageBuildStatus, OAuthStatus, UpdatePromptTemplatesRequest, WorkerImageFlags } from '../../../core/models/settings.model';
import { OAuthPanelComponent } from '../oauth-panel/oauth-panel';

const RATE_BUDGET_FLOOR = 500;

@Component({
  selector: 'fd-settings-general',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, OAuthPanelComponent, RateBudgetPanelComponent],
  providers: [DatePipe],
  template: `
    <div class="general-settings">
      <section class="general-settings__section">
        <h2 class="general-settings__section-title" #authHeading tabindex="-1">Worker Authentication</h2>
        <p class="general-settings__section-description">
          Configure how worker containers authenticate with the AI provider.
        </p>

        <fieldset class="general-settings__mode-fieldset">
          <legend class="sr-only">Authentication mode</legend>
          <div class="general-settings__mode-selector">
            <label class="general-settings__mode-option">
              <input
                type="radio"
                name="authMode"
                value="api_key"
                [checked]="_selectedMode() === 'api_key'"
                (change)="onModeChange('api_key')"
              />
              <span class="general-settings__mode-label">API Key</span>
            </label>
            <label class="general-settings__mode-option">
              <input
                type="radio"
                name="authMode"
                value="oauth"
                [checked]="_selectedMode() === 'oauth'"
                (change)="onModeChange('oauth')"
              />
              <span class="general-settings__mode-label">OAuth</span>
            </label>
          </div>
        </fieldset>

        @if (_selectedMode() === 'api_key') {
          <div class="general-settings__api-key-form">
            <div class="general-settings__field">
              <label class="general-settings__field-label" for="apiKey">API Key</label>
              <div class="general-settings__api-key-wrapper">
                <input
                  class="general-settings__api-key-input"
                  [type]="_showApiKey() ? 'text' : 'password'"
                  id="apiKey"
                  autocomplete="off"
                  placeholder="Enter your API key"
                  [(ngModel)]="_apiKeyValue"
                  [attr.aria-invalid]="!!settingsService.saveError() || null"
                  aria-describedby="api-key-error api-key-configured"
                />
                <button
                  class="general-settings__toggle-visibility-btn"
                  type="button"
                  [attr.aria-label]="_showApiKey() ? 'Hide API key' : 'Show API key'"
                  (click)="_showApiKey.set(!_showApiKey())"
                >
                  @if (_showApiKey()) {
                    <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                      <path d="M17.94 17.94A10.07 10.07 0 0 1 12 20c-7 0-11-8-11-8a18.45 18.45 0 0 1 5.06-5.94"></path>
                      <path d="M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 8 11 8a18.5 18.5 0 0 1-2.16 3.19"></path>
                      <line x1="1" y1="1" x2="23" y2="23"></line>
                    </svg>
                  } @else {
                    <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                      <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"></path>
                      <circle cx="12" cy="12" r="3"></circle>
                    </svg>
                  }
                </button>
              </div>

              <!-- Persistent live-region wrapper: always in the DOM so aria-describedby never dangles.
                   role="alert" is set only for Unreadable — assertive announcement on page load
                   for the primary recovery scenario. Present/NotConfigured carry no live-region role. -->
              <div
                id="api-key-configured"
                [attr.role]="_apiKeyStatus() === 'Unreadable' ? 'alert' : null"
              >
                @if (_apiKeyStatus() === 'Present') {
                  <span class="general-settings__configured-indicator">
                    API key is configured
                  </span>
                }
                @if (_apiKeyStatus() === 'NotConfigured') {
                  <span class="general-settings__field-hint--credential-status">
                    No API key is set. Workers stay paused until you enter one.
                  </span>
                }
                @if (_apiKeyStatus() === 'Unreadable') {
                  <div class="general-settings__api-key-warning">
                    <svg class="general-settings__api-key-warning-icon" xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                      <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"></path>
                      <line x1="12" y1="9" x2="12" y2="13"></line>
                      <line x1="12" y1="17" x2="12.01" y2="17"></line>
                    </svg>
                    <span>
                      The saved API key can no longer be read (the encryption key was lost).
                      Re-enter your API key below and save, or switch to OAuth and log in.
                    </span>
                  </div>
                }
              </div>
            </div>

            <div id="api-key-error" class="general-settings__save-error" role="alert">{{ settingsService.saveError() ?? '' }}</div>

            <div class="general-settings__save-success" role="status">{{ settingsService.saveSuccess() ? 'Settings saved successfully' : '' }}</div>

            <button
              class="general-settings__save-btn"
              type="button"
              [disabled]="settingsService.saving() || !_apiKeyValue"
              (click)="saveApiKey()"
            >{{ settingsService.saving() ? 'Saving...' : 'Save' }}</button>
          </div>
        }

        @if (_selectedMode() === 'oauth') {
          <div class="general-settings__oauth-section">
            @if (_showSwitchAccountConfirm()) {
              <div class="general-settings__switch-confirm" role="group" aria-label="Confirm account switch">
                <p class="general-settings__switch-confirm-text">
                  Signing in will pause dispatch and wait for in-flight workers to finish. Continue?
                </p>
                <div class="general-settings__switch-confirm-actions">
                  <button
                    #switchConfirmBtn
                    class="general-settings__switch-confirm-btn"
                    type="button"
                    (click)="onConfirmSwitchAccount()"
                  >Continue</button>
                  <button
                    class="general-settings__switch-cancel-btn"
                    type="button"
                    (click)="onCancelSwitchAccount()"
                  >Cancel</button>
                </div>
              </div>
            }
            <!-- Drain gate renders before the login flow (inside fd-oauth-panel below). -->
            <!-- Note: fully separating Present card / drain / login-flow would require -->
            <!-- splitting fd-oauth-panel — deferred as a follow-up refactor. -->
            <!-- Persistent live region — text binding drives announcements, never @if-mounted. -->
            <div
              class="general-settings__drain-gate"
              [class.general-settings__drain-gate--empty]="!_drainGateText()"
              role="status"
            >{{ _drainGateText() }}</div>
            @if (_showResumeAfterSwitch()) {
              <button
                class="general-settings__resume-btn"
                type="button"
                [disabled]="dispatchService.resuming()"
                (click)="dispatchService.resumeDispatch()"
              >{{ dispatchService.resuming() ? 'Resuming...' : 'Resume dispatch' }}</button>
            }
            <div role="alert" class="general-settings__switch-error">{{ (_switchAccountDraining() && dispatchService.pauseResumeError()) ? dispatchService.pauseResumeError() : '' }}</div>
            <fd-oauth-panel
              [status]="_oauthStatus()"
              [subscriptionType]="settingsService.authSettings()?.oauth?.subscriptionType ?? null"
              [accountEmail]="settingsService.authSettings()?.accountEmail ?? null"
              [accountOrgName]="settingsService.authSettings()?.accountOrgName ?? null"
              [loginPhase]="settingsService.loginPhase()"
              [loginUrl]="settingsService.loginUrl()"
              [loginError]="settingsService.loginError()"
              (startLogin)="onStartLogin()"
              (submitCode)="settingsService.submitLoginCode($event)"
              (cancel)="onCancelLogin()"
            />
            <div role="alert" class="general-settings__start-login-error">{{ (!settingsService.loginPhase() && settingsService.startLoginError()) ? settingsService.startLoginError() : '' }}</div>
          </div>
        }
      </section>

      <section class="general-settings__section">
        <h2 class="general-settings__section-title">Worker Image</h2>
        <p class="general-settings__section-description">
          Choose which toolchains are preinstalled in the worker container image. Saving rebuilds the image in the background; dispatch pauses until the build finishes.
        </p>

        <div class="general-settings__image-form">
          <fieldset class="general-settings__image-fieldset">
            <legend class="sr-only">Preinstalled toolchains</legend>

            <div class="general-settings__image-flags">
              <div class="general-settings__image-flag">
                <input
                  type="checkbox"
                  id="installDotnet"
                  [ngModel]="_installDotnetValue()"
                  (ngModelChange)="_installDotnetValue.set($event)"
                  [attr.disabled]="settingsService.imageBuildStatus() === 'Building' || null"
                  aria-describedby="installDotnet-hint"
                />
                <label for="installDotnet">Install .NET SDK</label>
                <span id="installDotnet-hint" class="general-settings__field-hint">Adds the .NET SDK to the worker image.</span>
              </div>

              <div class="general-settings__image-flag">
                <input
                  type="checkbox"
                  id="installAngular"
                  [ngModel]="_installAngularValue()"
                  (ngModelChange)="_installAngularValue.set($event)"
                  [attr.disabled]="settingsService.imageBuildStatus() === 'Building' || null"
                  aria-describedby="installAngular-hint"
                />
                <label for="installAngular">Install Angular CLI</label>
                <span id="installAngular-hint" class="general-settings__field-hint">Adds the Angular CLI (ng).</span>
              </div>

              <div class="general-settings__image-flag">
                <input
                  type="checkbox"
                  id="installChromium"
                  [ngModel]="_installChromiumValue()"
                  (ngModelChange)="_installChromiumValue.set($event)"
                  [attr.disabled]="settingsService.imageBuildStatus() === 'Building' || null"
                  aria-describedby="installChromium-hint"
                />
                <label for="installChromium">Install Chromium (headless)</label>
                <span id="installChromium-hint" class="general-settings__field-hint">Adds a headless Chromium browser for Angular component tests.</span>
              </div>

              <div class="general-settings__image-flag">
                <input
                  type="checkbox"
                  id="installDocker"
                  [ngModel]="_installDockerValue()"
                  (ngModelChange)="_installDockerValue.set($event)"
                  [attr.disabled]="settingsService.imageBuildStatus() === 'Building' || null"
                  aria-describedby="installDocker-hint"
                />
                <label for="installDocker">Install Docker engine + CLI</label>
                <span id="installDocker-hint" class="general-settings__field-hint general-settings__field-hint--warning">Runs Testcontainers integration tests inside the worker via a nested rootless Docker daemon. Security cost: the worker runs with CAP_SYS_ADMIN and seccomp/AppArmor unconfined, so a privilege escalation inside a worker can reach the Docker host (the WSL VM on Docker Desktop). Off by default; enable only for repositories you trust.</span>
              </div>
            </div>
          </fieldset>

          <span class="sr-only general-settings__image-failure-alert" role="alert" aria-live="assertive">{{ imageFailureAlertText() }}</span>
          <div class="general-settings__image-status" role="status" aria-live="polite">
            @if (settingsService.imageBuildStatus() === 'Building') {
              <span class="general-settings__image-spinner" aria-hidden="true"></span>
            }
            {{ _imageBuildingText() }}
          </div>
          <div class="general-settings__image-status" role="status" aria-live="polite">{{ _imageFailedText() }}</div>

          @if (settingsService.imageBuildStatus() === 'Failed' && settingsService.imageBuildLogTail()) {
            <pre
              class="general-settings__image-log"
              tabindex="0"
              aria-label="Build log output"
            >{{ settingsService.imageBuildLogTail() }}</pre>
          }

          <div id="image-flags-error" role="alert" class="general-settings__save-error">{{ settingsService.saveImageFlagsError() ?? '' }}</div>

          <div role="status" class="general-settings__save-success">{{ settingsService.saveImageFlagsSuccess() ? 'Worker image settings saved — building now…' : '' }}</div>

          <div class="general-settings__image-actions">
            <button
              class="general-settings__save-btn"
              type="button"
              [disabled]="settingsService.savingImageFlags() || settingsService.imageBuildStatus() === 'Building' || !_isImageFlagsDirty()"
              (click)="saveImageFlags()"
            >{{ settingsService.savingImageFlags() ? 'Saving...' : 'Save' }}</button>

            @if (settingsService.imageBuildStatus() === 'Failed') {
              <button
                class="general-settings__retry-btn"
                type="button"
                aria-label="Retry image build now"
                [disabled]="settingsService.savingImageFlags()"
                (click)="retryImageBuild()"
              >Retry</button>
            }
          </div>
        </div>
      </section>

      <section class="general-settings__section">
        <h2 class="general-settings__section-title">Worker Prompts</h2>
        <p class="general-settings__section-description">
          Customize the system and worker prompts sent to Claude Code containers. Use &#123;issueNumber&#125;, &#123;issueContent&#125;, and &#123;branchNamingInstruction&#125; as template variables.
        </p>

        <div class="general-settings__prompts-form">
          <div class="general-settings__field">
            <label class="general-settings__field-label" for="systemPromptTemplate">System Prompt Template</label>
            <textarea
              class="general-settings__textarea"
              id="systemPromptTemplate"
              rows="6"
              [ngModel]="_systemPromptValue()"
              (ngModelChange)="_systemPromptValue.set($event)"
              aria-describedby="prompts-error"
            ></textarea>
          </div>

          <div class="general-settings__field">
            <label class="general-settings__field-label" for="workerPromptTemplate">Worker Prompt Template</label>
            <textarea
              class="general-settings__textarea"
              id="workerPromptTemplate"
              rows="4"
              [ngModel]="_workerPromptValue()"
              (ngModelChange)="_workerPromptValue.set($event)"
              aria-describedby="prompts-error"
            ></textarea>
          </div>

          <div id="prompts-error" role="alert" class="general-settings__save-error">{{ settingsService.savePromptsError() ?? '' }}</div>

          <div role="status" class="general-settings__save-success">{{ settingsService.savePromptsSuccess() ? 'Prompt templates saved successfully' : '' }}</div>

          <button
            class="general-settings__save-btn"
            type="button"
            [disabled]="settingsService.savingPrompts()"
            (click)="savePrompts()"
          >{{ settingsService.savingPrompts() ? 'Saving...' : 'Save' }}</button>
        </div>
      </section>

      <section class="general-settings__section">
        <h2 class="general-settings__section-title">Provider Rate Budget</h2>
        <p class="general-settings__section-description">
          Current provider API request headroom. GitHub budgets show a health status relative to the
          configured floor ({{ RATE_BUDGET_FLOOR }} remaining). GitLab is recorded for visibility only — no floor is enforced.
        </p>
        <fd-rate-budget-panel />
      </section>

      <section class="general-settings__section">
        <h2 class="general-settings__section-title">Provider Hosts</h2>
        <p class="general-settings__section-description">
          Allowlist of self-hosted GitHub Enterprise and GitLab hostnames that workers may reach. Repositories on any other host are rejected before a token is sent.
        </p>

        <div class="general-settings__hosts-form">
          <div class="general-settings__field">
            <label class="general-settings__field-label" for="allowedProviderHosts">Allowed hostnames</label>
            <textarea
              class="general-settings__textarea"
              id="allowedProviderHosts"
              rows="6"
              spellcheck="false"
              autocapitalize="off"
              autocorrect="off"
              aria-describedby="allowed-provider-hosts-hint allowed-provider-hosts-error"
              placeholder="git.example.com&#10;gitlab.internal.corp"
              [ngModel]="_allowedHostsValue()"
              (ngModelChange)="_allowedHostsValue.set($event)"
              [attr.aria-invalid]="!!settingsService.saveHostsError() || null"
            ></textarea>
            <span id="allowed-provider-hosts-hint" class="general-settings__field-hint">One bare hostname per line or comma-separated, e.g. git.example.com — no scheme, port, or path. Maximum 50 hostnames. Leave empty to allow only the public providers.</span>
          </div>

          <div id="allowed-provider-hosts-error" role="alert" class="general-settings__save-error">{{ settingsService.saveHostsError() ?? '' }}</div>

          <div role="status" class="general-settings__save-success">{{ settingsService.saveHostsSuccess() ? 'Provider hosts saved successfully' : '' }}</div>

          <button
            class="general-settings__save-btn"
            type="button"
            [disabled]="settingsService.savingHosts()"
            (click)="saveHosts()"
          >{{ settingsService.savingHosts() ? 'Saving...' : 'Save' }}</button>
        </div>
      </section>
    </div>
  `,
  styleUrl: './settings-general.scss',
})
export class SettingsGeneralComponent {
  protected readonly settingsService = inject(SettingsService);
  protected readonly dispatchService = inject(DispatchService);
  private readonly _datePipe = inject(DatePipe);
  private readonly _injector = inject(Injector);
  private readonly _elementRef = inject(ElementRef);

  @ViewChild('authHeading') private readonly _authHeading?: ElementRef<HTMLHeadingElement>;
  @ViewChild('switchConfirmBtn') private readonly _switchConfirmBtn?: ElementRef<HTMLButtonElement>;

  protected readonly RATE_BUDGET_FLOOR = RATE_BUDGET_FLOOR;

  protected readonly _selectedMode: WritableSignal<AuthMode> = signal('api_key');
  protected readonly _showApiKey: WritableSignal<boolean> = signal(false);
  protected _apiKeyValue = '';
  private _modeInitialized = false;

  protected readonly _showSwitchAccountConfirm: WritableSignal<boolean> = signal(false);
  protected readonly _switchAccountDraining: WritableSignal<boolean> = signal(false);
  protected readonly _showResumeAfterSwitch: WritableSignal<boolean> = signal(false);

  protected readonly _oauthStatus: Signal<OAuthStatus> = computed(
    () => this.settingsService.authSettings()?.oauth?.status ?? 'NotConfigured'
  );

  protected readonly _apiKeyStatus: Signal<ApiKeyStatus> = computed(
    () => this.settingsService.authSettings()?.apiKeyStatus ?? 'NotConfigured'
  );

  protected readonly _systemPromptValue: WritableSignal<string> = signal('');
  protected readonly _workerPromptValue: WritableSignal<string> = signal('');
  private _promptsInitialized = false;

  protected readonly _allowedHostsValue: WritableSignal<string> = signal('');
  private _hostsInitialized = false;

  protected readonly _installDotnetValue: WritableSignal<boolean> = signal(false);
  protected readonly _installAngularValue: WritableSignal<boolean> = signal(false);
  protected readonly _installChromiumValue: WritableSignal<boolean> = signal(false);
  protected readonly _installDockerValue: WritableSignal<boolean> = signal(false);
  private _imageInitialized = false;

  protected readonly _isImageFlagsDirty: Signal<boolean> = computed(() => {
    const saved = this.settingsService.workerImageFlags();
    if (saved === null) {
      return false;
    }
    return (
      this._installDotnetValue() !== saved.installDotnet ||
      this._installAngularValue() !== saved.installAngular ||
      this._installChromiumValue() !== saved.installChromium ||
      this._installDockerValue() !== saved.installDocker
    );
  });

  protected readonly _drainGateText: Signal<string> = computed(() =>
    this._switchAccountDraining()
      ? 'Dispatch is paused. Wait for running workers to finish (watch the Issues board), then log in to the new account below.'
      : ''
  );

  protected readonly _imageBuildingText: Signal<string> = computed(() =>
    this.settingsService.imageBuildStatus() === 'Building' ? 'Building worker image…' : ''
  );

  protected readonly _imageFailedText: Signal<string> = computed(() => {
    if (this.settingsService.imageBuildStatus() !== 'Failed') {
      return '';
    }
    const retryAt = this.settingsService.imageBuildNextRetryAt();
    const attempt = this.settingsService.imageBuildAttempt();
    const formattedTime = retryAt ? this._datePipe.transform(retryAt, 'HH:mm') : null;
    const hasRetryAt = formattedTime !== null;
    const hasAttempt = attempt >= 1;

    if (hasRetryAt && hasAttempt) {
      return `Worker image build failed — retrying at ${formattedTime} (attempt ${attempt})`;
    }
    if (hasRetryAt) {
      return `Worker image build failed — retrying at ${formattedTime}`;
    }
    if (hasAttempt) {
      return `Worker image build failed (attempt ${attempt})`;
    }
    return 'Worker image build failed.';
  });

  private _imagePreviousStatus: ImageBuildStatus = 'Idle';
  private readonly _imageFailureAlertTextSignal: WritableSignal<string> = signal('');
  protected readonly imageFailureAlertText: Signal<string> = this._imageFailureAlertTextSignal.asReadonly();

  constructor() {
    effect(() => {
      const settings = this.settingsService.authSettings();
      if (settings !== null && !this._modeInitialized) {
        this._modeInitialized = true;
        this._selectedMode.set(settings.mode);
      }
    });

    effect(() => {
      const limits = this.settingsService.workerLimits();
      if (limits !== null && !this._promptsInitialized) {
        this._promptsInitialized = true;
        this._systemPromptValue.set(this.settingsService.systemPromptTemplate() ?? '');
        this._workerPromptValue.set(this.settingsService.workerPromptTemplate() ?? '');
      }
    });

    effect(() => {
      const hosts = this.settingsService.allowedProviderHosts();
      if (this.settingsService.settings() !== null && !this._hostsInitialized) {
        this._hostsInitialized = true;
        this._allowedHostsValue.set(hosts.join('\n'));
      }
    });

    effect(() => {
      const flags = this.settingsService.workerImageFlags();
      if (flags === null) {
        this._imageInitialized = false;
        return;
      }
      if (!this._imageInitialized) {
        this._imageInitialized = true;
        this._installDotnetValue.set(flags.installDotnet);
        this._installAngularValue.set(flags.installAngular);
        this._installChromiumValue.set(flags.installChromium);
        this._installDockerValue.set(flags.installDocker);
      }
    });

    // Manage drain-gate state in response to login phase transitions.
    effect(() => {
      const phase = this.settingsService.loginPhase();
      if (phase === null && this._switchAccountDraining()) {
        // Login succeeded (phase cleared after reload) — tear down drain UI.
        this._switchAccountDraining.set(false);
        this._showResumeAfterSwitch.set(false);
      }
      if (phase === 'Failed' && this._switchAccountDraining()) {
        // Login failed while draining — surface Resume so the operator can recover.
        this._showResumeAfterSwitch.set(true);
      }
      if (phase === 'Succeeded') {
        // Move focus to auth heading on Succeeded regardless of drain state (Finding 4).
        runInInjectionContext(this._injector, () =>
          afterNextRender(() => this._authHeading?.nativeElement.focus())
        );
      }
    });

    // If startLogin POST itself fails while draining, reset drain state so the
    // operator is not stranded (they can retry or close from the error message).
    effect(() => {
      const error = this.settingsService.startLoginError();
      if (error !== null && this._switchAccountDraining()) {
        this._switchAccountDraining.set(false);
        this._showResumeAfterSwitch.set(false);
      }
    });

    // Fire a one-shot assertive alert when the image build status first transitions to Failed.
    effect(() => {
      const status = this.settingsService.imageBuildStatus();
      const prevStatus = this._imagePreviousStatus;
      this._imagePreviousStatus = status;

      if (status === 'Failed' && prevStatus !== 'Failed') {
        this._imageFailureAlertTextSignal.set('Worker image build failed.');
        // Clear after the AT has announced it; countdown ticks must not re-trigger the alert.
        setTimeout(() => this._imageFailureAlertTextSignal.set(''), 0);
      } else if (status !== 'Failed') {
        this._imageFailureAlertTextSignal.set('');
      }
    });
  }

  onModeChange(mode: AuthMode): void {
    this._selectedMode.set(mode);
    if (mode === 'oauth') {
      const currentMode = this.settingsService.authSettings()?.mode;
      if (currentMode !== 'oauth') {
        this.settingsService.markOAuthConfigured();
      }
    }
    runInInjectionContext(this._injector, () =>
      afterNextRender(() => this._authHeading?.nativeElement.focus())
    );
  }

  onStartLogin(): void {
    // Both "Switch account" (Present) and "Log in again" (ReLoginNeeded) go through
    // the confirm → pause → drain-gate flow before starting the login flow.
    const status = this._oauthStatus();
    if (status === 'Present' || status === 'ReLoginNeeded') {
      this._showSwitchAccountConfirm.set(true);
      runInInjectionContext(this._injector, () =>
        afterNextRender(() => this._switchConfirmBtn?.nativeElement.focus())
      );
    } else {
      this.settingsService.startLogin();
    }
  }

  onCancelSwitchAccount(): void {
    this._showSwitchAccountConfirm.set(false);
    runInInjectionContext(this._injector, () =>
      afterNextRender(() => this._authHeading?.nativeElement.focus())
    );
  }

  onCancelLogin(): void {
    this.settingsService.cancelLogin();
    // Return focus to the entry login button (visible when not draining), else auth heading.
    runInInjectionContext(this._injector, () =>
      afterNextRender(() => {
        const host = this._elementRef.nativeElement as HTMLElement;
        const entryBtn = host.querySelector<HTMLButtonElement>(
          '.oauth-panel__login-btn, .oauth-panel__switch-btn'
        );
        if (entryBtn) {
          entryBtn.focus();
        } else {
          this._authHeading?.nativeElement.focus();
        }
      })
    );
  }

  onConfirmSwitchAccount(): void {
    this._showSwitchAccountConfirm.set(false);
    this._switchAccountDraining.set(true);
    this.dispatchService.pauseDispatch();
    this.settingsService.startLogin();
    runInInjectionContext(this._injector, () =>
      afterNextRender(() => this._authHeading?.nativeElement.focus())
    );
  }

  saveApiKey(): void {
    this.settingsService.updateAuthMode('api_key', this._apiKeyValue);
  }

  savePrompts(): void {
    const request: UpdatePromptTemplatesRequest = {
      systemPromptTemplate: this._systemPromptValue() || null,
      workerPromptTemplate: this._workerPromptValue() || null,
    };
    this.settingsService.updatePromptTemplates(request);
  }

  saveImageFlags(): void {
    const flags: WorkerImageFlags = {
      installDotnet: this._installDotnetValue(),
      installAngular: this._installAngularValue(),
      installChromium: this._installChromiumValue(),
      installDocker: this._installDockerValue(),
    };
    this.settingsService.updateWorkerImageFlags(flags);
  }

  retryImageBuild(): void {
    this.settingsService.retryImageBuild();
  }

  saveHosts(): void {
    const hosts = this._allowedHostsValue()
      .split(/[\n,]+/)
      .map((h) => h.trim())
      .filter((h) => h.length > 0);
    this.settingsService.updateAllowedProviderHosts(hosts);
  }
}

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
  inject,
  runInInjectionContext,
  signal,
  effect,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { SettingsService } from '../settings.service';
import { DispatchService } from '../../../core/services/dispatch.service';
import { AuthMode, OAuthStatus, UpdatePromptTemplatesRequest, WorkerImageFlags } from '../settings.model';
import { OAuthPanelComponent } from '../oauth-panel/oauth-panel';

const MAX_CONCURRENT_MIN = 1;
const MAX_CONCURRENT_MAX = 20;
const TIMEOUT_MINUTES_MIN = 1;
const TIMEOUT_MINUTES_MAX = 1440;
const COOLDOWN_MINUTES_MIN = 1;
const COOLDOWN_MINUTES_MAX = 1440;

@Component({
  selector: 'fd-settings-general',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, OAuthPanelComponent],
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

              @if (settingsService.authSettings()?.apiKeyConfigured) {
                <span id="api-key-configured" class="general-settings__configured-indicator">
                  API key is configured
                </span>
              }
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
            <fd-oauth-panel
              [status]="_oauthStatus()"
              [expiresAt]="settingsService.authSettings()?.oauth?.expiresAt ?? null"
              [subscriptionType]="settingsService.authSettings()?.oauth?.subscriptionType ?? null"
              [loginCommand]="settingsService.loginCommand()"
              [loginCommandLoading]="settingsService.loginCommandLoading()"
              [loginCommandError]="settingsService.loginCommandError()"
              (refresh)="onOAuthRefresh()"
              (fetchCommand)="settingsService.fetchLoginCommand()"
            />

            @if (_showSwitchAccountConfirm()) {
              <div class="general-settings__switch-confirm" role="group" aria-label="Confirm account switch">
                <p class="general-settings__switch-confirm-text">
                  Switching account pauses dispatch and waits for in-flight workers to finish. Continue?
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
            <div
              class="general-settings__drain-gate"
              role="status"
            >
              @if (_switchAccountDraining()) {
                Dispatch is paused. Wait for running workers to finish (watch the Issues board), then log in to the new account below.

                @if (_showResumeAfterSwitch()) {
                  <button
                    class="general-settings__resume-btn"
                    type="button"
                    [disabled]="dispatchService.resuming()"
                    (click)="dispatchService.resumeDispatch()"
                  >{{ dispatchService.resuming() ? 'Resuming...' : 'Resume dispatch' }}</button>
                }
              }
            </div>
            <div role="alert" class="general-settings__switch-error">{{ (_switchAccountDraining() && dispatchService.pauseResumeError()) ? dispatchService.pauseResumeError() : '' }}</div>
            @if (!_showSwitchAccountConfirm() && !_switchAccountDraining() && _oauthStatus() === 'Present') {
              <button
                #switchAccountBtn
                class="general-settings__switch-account-btn"
                type="button"
                (click)="onSwitchAccountClick()"
              >Switch account</button>
            }
          </div>
        }
      </section>

      <section class="general-settings__section">
        <h2 class="general-settings__section-title">Worker Limits</h2>
        <p class="general-settings__section-description">
          Control the maximum number of concurrent workers and the timeout for each worker run.
        </p>

        <div class="general-settings__limits-form">
          <div class="general-settings__limits-fields">
            <div class="general-settings__field">
              <label class="general-settings__field-label" for="maxConcurrent">Max Concurrent Workers</label>
              <input
                class="general-settings__number-input"
                type="number"
                id="maxConcurrent"
                [min]="MAX_CONCURRENT_MIN"
                [max]="MAX_CONCURRENT_MAX"
                step="1"
                [ngModel]="_maxConcurrentValue()"
                (ngModelChange)="_maxConcurrentValue.set($event)"
                aria-describedby="max-concurrent-hint limits-error"
              />
              <span id="max-concurrent-hint" class="general-settings__field-hint">1–20 workers</span>
            </div>
            <div class="general-settings__field">
              <label class="general-settings__field-label" for="timeoutMinutes">Timeout (minutes)</label>
              <input
                class="general-settings__number-input"
                type="number"
                id="timeoutMinutes"
                [min]="TIMEOUT_MINUTES_MIN"
                [max]="TIMEOUT_MINUTES_MAX"
                step="1"
                [ngModel]="_timeoutMinutesValue()"
                (ngModelChange)="_timeoutMinutesValue.set($event)"
                aria-describedby="timeout-minutes-hint limits-error"
              />
              <span id="timeout-minutes-hint" class="general-settings__field-hint">1–1,440 minutes</span>
            </div>
          </div>

          <div id="limits-error" role="alert" class="general-settings__save-error">{{ settingsService.saveLimitsError() ?? '' }}</div>

          <div role="status" class="general-settings__save-success">{{ settingsService.saveLimitsSuccess() ? 'Worker limits saved successfully' : '' }}</div>

          <button
            class="general-settings__save-btn"
            type="button"
            [disabled]="settingsService.savingLimits() || !isLimitsFormValid()"
            (click)="saveLimits()"
          >{{ settingsService.savingLimits() ? 'Saving...' : 'Save' }}</button>
        </div>
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
                  id="installGlab"
                  [ngModel]="_installGlabValue()"
                  (ngModelChange)="_installGlabValue.set($event)"
                  [attr.disabled]="settingsService.imageBuildStatus() === 'Building' || null"
                  aria-describedby="installGlab-hint"
                />
                <label for="installGlab">Install GitLab CLI (glab)</label>
                <span id="installGlab-hint" class="general-settings__field-hint">Adds the glab command-line tool.</span>
              </div>

              <div class="general-settings__image-flag">
                <input
                  type="checkbox"
                  id="installGh"
                  [ngModel]="_installGhValue()"
                  (ngModelChange)="_installGhValue.set($event)"
                  [attr.disabled]="settingsService.imageBuildStatus() === 'Building' || null"
                  aria-describedby="installGh-hint"
                />
                <label for="installGh">Install GitHub CLI (gh)</label>
                <span id="installGh-hint" class="general-settings__field-hint">Adds the gh command-line tool.</span>
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
                <span id="installDocker-hint" class="general-settings__field-hint">Adds a rootless-capable Docker engine and CLI for Testcontainers.</span>
              </div>
            </div>
          </fieldset>

          <div class="general-settings__image-status" role="status" aria-live="polite">
            @if (settingsService.imageBuildStatus() === 'Building') {
              <span class="general-settings__image-spinner" aria-hidden="true"></span>
            }
            {{ _imageBuildingText() }}
          </div>
          <div class="general-settings__image-status" role="alert">{{ _imageFailedText() }}</div>

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
        <h2 class="general-settings__section-title">Dispatch Settings</h2>
        <p class="general-settings__section-description">
          Configure automatic pause and resume behavior for worker dispatch.
        </p>

        <div class="general-settings__dispatch-form">
          <label class="general-settings__checkbox-label" for="autoResume">
            <input
              type="checkbox"
              id="autoResume"
              [ngModel]="_autoResumeValue()"
              (ngModelChange)="_autoResumeValue.set($event)"
              [attr.aria-describedby]="settingsService.saveDispatchError() ? 'dispatch-error' : null"
            />
            Auto-resume when usage limit resets
          </label>

          <div class="general-settings__field">
            <label class="general-settings__field-label" for="defaultCooldown">Default Cooldown (minutes)</label>
            <input
              class="general-settings__number-input"
              type="number"
              id="defaultCooldown"
              [min]="COOLDOWN_MINUTES_MIN"
              [max]="COOLDOWN_MINUTES_MAX"
              step="1"
              [attr.disabled]="!_autoResumeValue() || null"
              [ngModel]="_cooldownValue()"
              (ngModelChange)="_cooldownValue.set($event)"
              aria-describedby="cooldown-hint cooldown-auto-resume-hint dispatch-error"
            />
            <span id="cooldown-hint" class="general-settings__field-hint">1–1,440 minutes</span>
            <span id="cooldown-auto-resume-hint" class="general-settings__field-hint">Used when auto-resume is enabled.</span>
          </div>

          <div id="dispatch-error" role="alert" class="general-settings__save-error">{{ settingsService.saveDispatchError() ?? '' }}</div>

          <div role="status" class="general-settings__save-success">{{ settingsService.saveDispatchSuccess() ? 'Dispatch settings saved successfully' : '' }}</div>

          <button
            class="general-settings__save-btn"
            type="button"
            [disabled]="settingsService.savingDispatch() || !isDispatchFormValid()"
            (click)="saveDispatch()"
          >{{ settingsService.savingDispatch() ? 'Saving...' : 'Save' }}</button>
        </div>
      </section>
    </div>
  `,
  styleUrl: './settings-general.scss',
})
export class SettingsGeneralComponent {
  protected readonly settingsService = inject(SettingsService);
  protected readonly dispatchService = inject(DispatchService);
  private readonly _injector = inject(Injector);

  @ViewChild('authHeading') private readonly _authHeading?: ElementRef<HTMLHeadingElement>;
  @ViewChild('switchAccountBtn') private readonly _switchAccountBtn?: ElementRef<HTMLButtonElement>;
  @ViewChild('switchConfirmBtn') private readonly _switchConfirmBtn?: ElementRef<HTMLButtonElement>;

  protected readonly MAX_CONCURRENT_MIN = MAX_CONCURRENT_MIN;
  protected readonly MAX_CONCURRENT_MAX = MAX_CONCURRENT_MAX;
  protected readonly TIMEOUT_MINUTES_MIN = TIMEOUT_MINUTES_MIN;
  protected readonly TIMEOUT_MINUTES_MAX = TIMEOUT_MINUTES_MAX;
  protected readonly COOLDOWN_MINUTES_MIN = COOLDOWN_MINUTES_MIN;
  protected readonly COOLDOWN_MINUTES_MAX = COOLDOWN_MINUTES_MAX;

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

  protected readonly _maxConcurrentValue: WritableSignal<number> = signal(MAX_CONCURRENT_MIN);
  protected readonly _timeoutMinutesValue: WritableSignal<number> = signal(TIMEOUT_MINUTES_MIN);
  private _limitsInitialized = false;

  protected readonly _systemPromptValue: WritableSignal<string> = signal('');
  protected readonly _workerPromptValue: WritableSignal<string> = signal('');
  private _promptsInitialized = false;

  protected readonly _autoResumeValue: WritableSignal<boolean> = signal(true);
  protected readonly _cooldownValue: WritableSignal<number> = signal(60);
  private _dispatchInitialized = false;

  protected readonly isDispatchFormValid: Signal<boolean> = computed(
    () => this._cooldownValue() >= COOLDOWN_MINUTES_MIN && this._cooldownValue() <= COOLDOWN_MINUTES_MAX
  );

  protected readonly _installDotnetValue: WritableSignal<boolean> = signal(false);
  protected readonly _installAngularValue: WritableSignal<boolean> = signal(false);
  protected readonly _installGlabValue: WritableSignal<boolean> = signal(false);
  protected readonly _installGhValue: WritableSignal<boolean> = signal(false);
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
      this._installGlabValue() !== saved.installGlab ||
      this._installGhValue() !== saved.installGh ||
      this._installChromiumValue() !== saved.installChromium ||
      this._installDockerValue() !== saved.installDocker
    );
  });

  protected readonly _imageBuildingText: Signal<string> = computed(() =>
    this.settingsService.imageBuildStatus() === 'Building' ? 'Building worker image…' : ''
  );

  protected readonly _imageFailedText: Signal<string> = computed(() =>
    this.settingsService.imageBuildStatus() === 'Failed' ? 'Worker image build failed.' : ''
  );

  constructor() {
    effect(() => {
      const settings = this.settingsService.authSettings();
      if (settings !== null && !this._modeInitialized) {
        this._modeInitialized = true;
        this._selectedMode.set(settings.mode);
        if (settings.mode === 'oauth' && settings.oauth?.status !== 'Present') {
          this.settingsService.fetchLoginCommand();
        }
      }
    });

    effect(() => {
      const limits = this.settingsService.workerLimits();
      if (limits !== null && !this._limitsInitialized) {
        this._limitsInitialized = true;
        this._maxConcurrentValue.set(limits.maxConcurrent);
        this._timeoutMinutesValue.set(limits.timeoutMinutes);
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
      const settings = this.settingsService.settings();
      if (settings !== null && !this._dispatchInitialized) {
        this._dispatchInitialized = true;
        this._autoResumeValue.set(settings.autoResumeOnUsageReset);
        this._cooldownValue.set(settings.defaultCooldownMinutes);
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
        this._installGlabValue.set(flags.installGlab);
        this._installGhValue.set(flags.installGh);
        this._installChromiumValue.set(flags.installChromium);
        this._installDockerValue.set(flags.installDocker);
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
      if (this._oauthStatus() !== 'Present') {
        this.settingsService.fetchLoginCommand();
      }
    }
    runInInjectionContext(this._injector, () =>
      afterNextRender(() => this._authHeading?.nativeElement.focus())
    );
  }

  onSwitchAccountClick(): void {
    this._showSwitchAccountConfirm.set(true);
    runInInjectionContext(this._injector, () =>
      afterNextRender(() => this._switchConfirmBtn?.nativeElement.focus())
    );
  }

  onCancelSwitchAccount(): void {
    this._showSwitchAccountConfirm.set(false);
    runInInjectionContext(this._injector, () =>
      afterNextRender(() => {
        const target = this._switchAccountBtn?.nativeElement ?? this._authHeading?.nativeElement;
        target?.focus();
      })
    );
  }

  onOAuthRefresh(): void {
    this.settingsService.loadSettings();
    if (this._switchAccountDraining()) {
      this._showResumeAfterSwitch.set(true);
    }
  }

  onConfirmSwitchAccount(): void {
    this._showSwitchAccountConfirm.set(false);
    this._switchAccountDraining.set(true);
    this.dispatchService.pauseDispatch();
    this.settingsService.fetchLoginCommand();
    runInInjectionContext(this._injector, () =>
      afterNextRender(() => this._authHeading?.nativeElement.focus())
    );
  }

  saveApiKey(): void {
    this.settingsService.updateAuthMode('api_key', this._apiKeyValue);
  }

  isLimitsFormValid(): boolean {
    const maxConcurrent = this._maxConcurrentValue();
    const timeoutMinutes = this._timeoutMinutesValue();
    return (
      maxConcurrent >= MAX_CONCURRENT_MIN &&
      maxConcurrent <= MAX_CONCURRENT_MAX &&
      timeoutMinutes >= TIMEOUT_MINUTES_MIN &&
      timeoutMinutes <= TIMEOUT_MINUTES_MAX
    );
  }

  saveLimits(): void {
    this.settingsService.updateWorkerLimits(this._maxConcurrentValue(), this._timeoutMinutesValue());
  }

  savePrompts(): void {
    const request: UpdatePromptTemplatesRequest = {
      systemPromptTemplate: this._systemPromptValue() || null,
      workerPromptTemplate: this._workerPromptValue() || null,
    };
    this.settingsService.updatePromptTemplates(request);
  }

  saveDispatch(): void {
    this.settingsService.updateDispatchSettings(this._autoResumeValue(), this._cooldownValue());
  }

  saveImageFlags(): void {
    const flags: WorkerImageFlags = {
      installDotnet: this._installDotnetValue(),
      installAngular: this._installAngularValue(),
      installGlab: this._installGlabValue(),
      installGh: this._installGhValue(),
      installChromium: this._installChromiumValue(),
      installDocker: this._installDockerValue(),
    };
    this.settingsService.updateWorkerImageFlags(flags);
  }

  retryImageBuild(): void {
    this.settingsService.retryImageBuild();
  }
}

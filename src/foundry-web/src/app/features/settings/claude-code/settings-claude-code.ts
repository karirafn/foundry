import {
  Component,
  ChangeDetectionStrategy,
  ElementRef,
  Injector,
  Signal,
  ViewChild,
  ChangeDetectorRef,
  untracked,
  WritableSignal,
  afterNextRender,
  computed,
  effect,
  inject,
  runInInjectionContext,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  FormArray,
  FormControl,
  FormsModule,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { SettingsService } from '../../../core/services/settings.service';
import { DispatchService } from '../../../core/services/dispatch.service';
import { ApiKeyStatus, AuthMode, ClaudeCodeSettings, OAuthStatus } from '../../../core/models/settings.model';
import { OAuthPanelComponent } from '../oauth-panel/oauth-panel';
import { UnsavedChanges } from '../guards/unsaved-changes.guard';
import {
  CI_CD_DENY_RULES,
  MODEL_MAX_LENGTH,
  denyRuleFormatValidator,
  trimmedMaxLengthValidator,
} from './settings-claude-code.validators';

const SERVER_ERROR_KEY = 'server';
const SAVING_TEXT = 'Saving Claude Code settings...';
const SAVED_TEXT = 'Claude Code settings saved';
const QUOTED_RULE_PATTERN = /'(.*)'/s;
const MODEL_TOO_LONG_CODE = 'Settings.ModelTooLong';
const INVALID_DENY_RULE_CODE = 'Settings.InvalidDenyRule';
const MODEL_TOO_LONG_TEXT = `Model must be ${MODEL_MAX_LENGTH} characters or fewer.`;
const RULE_REQUIRED_TEXT = 'Deny rule is required.';
const RULE_FORMAT_TEXT =
  'Use Tool(pattern), e.g. Edit(.github/workflows/**). Allowed tools: Bash, Edit, Read, Write, MultiEdit, Glob, Grep.';

@Component({
  selector: 'fd-settings-claude-code',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, ReactiveFormsModule, OAuthPanelComponent],
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

      <form class="claude-code" [formGroup]="form" (ngSubmit)="_save()" novalidate>
        <fieldset class="claude-code__fieldset" [disabled]="settingsService.savingClaudeCode()">
          <legend class="sr-only">Claude Code configuration</legend>

          <section class="general-settings__section">
            <h2 class="general-settings__section-title">Model</h2>
            <p class="general-settings__section-description">
              Pin the Claude model workers run.
            </p>
            <div class="general-settings__field">
              <label class="general-settings__field-label" for="model">Model</label>
              <input
                id="model"
                type="text"
                class="general-settings__text-input"
                formControlName="model"
                autocomplete="off"
                autocapitalize="off"
                autocorrect="off"
                spellcheck="false"
                placeholder="claude-sonnet-4-5"
                aria-describedby="model-hint model-error"
                [attr.aria-invalid]="_modelInvalid() || null"
              />
              <span id="model-hint" class="general-settings__field-hint">Leave empty to use the CLI default.</span>
              <span id="model-error" class="general-settings__field-error" role="alert">{{ _modelError() }}</span>
            </div>
          </section>

          <section class="general-settings__section">
            <h2 class="general-settings__section-title">CI/CD file protection</h2>
            <p class="general-settings__section-description">
              Keep workers from editing pipeline and container-build files.
            </p>
            <label class="general-settings__checkbox-label" for="blockCiCdEdits">
              <input
                type="checkbox"
                id="blockCiCdEdits"
                formControlName="blockCiCdEdits"
                aria-describedby="blockCiCdEdits-detail"
              />
              Block edits to CI/CD files
            </label>
            <div id="blockCiCdEdits-detail" class="claude-code__cicd-detail">
              @if (form.controls.blockCiCdEdits.value) {
                <p class="general-settings__field-hint">These deny rules are applied to every worker:</p>
                <ul class="claude-code__rule-list" role="list">
                  @for (rule of ciCdDenyRules; track rule) {
                    <li class="claude-code__rule-item"><code>{{ rule }}</code></li>
                  }
                </ul>
              } @else {
                <p class="general-settings__callout general-settings__callout--warning">
                  <span class="general-settings__callout-icon" aria-hidden="true">⚠</span>
                  <span>
                    <strong class="general-settings__callout-lead">Warning:</strong>
                    CI/CD files are not blocked by permission rules. The system prompt still instructs workers
                    to leave CI/CD files alone, but this is soft guidance a worker can override.
                  </span>
                </p>
              }
            </div>
          </section>

          <section class="general-settings__section">
            <h2 class="general-settings__section-title">Additional deny rules</h2>
            <p id="denyRulesGuidance" class="general-settings__section-description">
              Extra permission deny rules applied to every worker, on top of the base and CI/CD rules. Each rule is
              a tool pattern such as <code>Bash(rm:*)</code> or <code>Edit(secrets/**)</code>.
            </p>

            <div formArrayName="denyRules">
              @if (denyRules.length === 0) {
                <p class="general-settings__field-hint claude-code__empty">
                  No additional deny rules. Workers run with the base and CI/CD deny rules only. Add a rule to deny
                  more tools or paths.
                </p>
              }

              @for (row of denyRules.controls; track row; let i = $index) {
                <div class="claude-code__row" role="group" [attr.aria-labelledby]="'denyRuleLabel-' + i">
                  <span class="sr-only" [id]="'denyRuleLabel-' + i">Deny rule {{ i + 1 }}</span>
                  <div class="general-settings__field">
                    <label class="sr-only" [for]="'denyRule-' + i">Deny rule {{ i + 1 }}</label>
                    <input
                      type="text"
                      [id]="'denyRule-' + i"
                      [formControlName]="i"
                      class="general-settings__text-input"
                      aria-required="true"
                      placeholder="Edit(secrets/**)"
                      autocomplete="off"
                      autocapitalize="off"
                      autocorrect="off"
                      spellcheck="false"
                      [attr.aria-describedby]="'denyRulesGuidance denyRule-' + i + '-error'"
                      [attr.aria-invalid]="_ruleInvalid(i) || null"
                    />
                    <span [id]="'denyRule-' + i + '-error'" class="general-settings__field-error" role="alert">{{ _ruleError(i) }}</span>
                  </div>
                  <button
                    type="button"
                    class="general-settings__toggle-visibility-btn"
                    [id]="'removeRule-' + i"
                    [attr.aria-label]="_removeLabel(i)"
                    (click)="_removeRule(i)"
                  >
                    <span aria-hidden="true">×</span>
                  </button>
                </div>
              }

              <button type="button" id="addRule" class="general-settings__scan-btn" (click)="_addRule()">Add rule</button>
            </div>
          </section>
        </fieldset>

        <div class="claude-code__actions">
          <span class="general-settings__save-error" role="alert">{{ _summaryError() }}</span>
          <span class="general-settings__save-success" role="status" aria-live="polite">{{ _statusText() }}</span>
          <button
            type="submit"
            class="general-settings__save-btn"
            [disabled]="settingsService.savingClaudeCode() || !_isDirty()"
          >
            {{ settingsService.savingClaudeCode() ? 'Saving...' : 'Save' }}
          </button>
        </div>
      </form>
    </div>
  `,
  styleUrl: './settings-claude-code.scss',
})
export class SettingsClaudeCodeComponent implements UnsavedChanges {
  protected readonly settingsService = inject(SettingsService);
  protected readonly dispatchService = inject(DispatchService);
  private readonly _injector = inject(Injector);
  private readonly _elementRef = inject(ElementRef);

  @ViewChild('authHeading') private readonly _authHeading?: ElementRef<HTMLHeadingElement>;
  @ViewChild('switchConfirmBtn') private readonly _switchConfirmBtn?: ElementRef<HTMLButtonElement>;

  protected readonly _selectedMode: WritableSignal<AuthMode> = signal('api_key');
  protected readonly _showApiKey: WritableSignal<boolean> = signal(false);
  protected _apiKeyValue = '';
  private _modeInitialized = false;

  private readonly _fb = inject(NonNullableFormBuilder);
  private readonly _cdr = inject(ChangeDetectorRef);

  protected readonly ciCdDenyRules: readonly string[] = CI_CD_DENY_RULES;

  protected readonly form = this._fb.group({
    model: this._fb.control('', [trimmedMaxLengthValidator]),
    blockCiCdEdits: this._fb.control(true),
    denyRules: this._fb.array<FormControl<string>>([]),
  });

  protected get denyRules(): FormArray<FormControl<string>> {
    return this.form.controls.denyRules;
  }

  protected readonly _summaryError: WritableSignal<string> = signal('');
  private readonly _formValue: WritableSignal<ClaudeCodeSettings> = signal(this._readForm());

  protected readonly _isDirty: Signal<boolean> = computed(() => {
    const baseline: ClaudeCodeSettings | null = this.settingsService.claudeCodeSettings();
    return baseline !== null && !settingsEqual(baseline, this._formValue());
  });

  protected readonly _statusText: Signal<string> = computed(() => {
    if (this.settingsService.savingClaudeCode()) {
      return SAVING_TEXT;
    }
    return this.settingsService.saveClaudeCodeSuccess() && !this._isDirty() ? SAVED_TEXT : '';
  });

  protected readonly _showSwitchAccountConfirm: WritableSignal<boolean> = signal(false);
  protected readonly _switchAccountDraining: WritableSignal<boolean> = signal(false);
  protected readonly _showResumeAfterSwitch: WritableSignal<boolean> = signal(false);

  protected readonly _oauthStatus: Signal<OAuthStatus> = computed(
    () => this.settingsService.authSettings()?.oauth?.status ?? 'NotConfigured'
  );

  protected readonly _apiKeyStatus: Signal<ApiKeyStatus> = computed(
    () => this.settingsService.authSettings()?.apiKeyStatus ?? 'NotConfigured'
  );

  protected readonly _drainGateText: Signal<string> = computed(() =>
    this._switchAccountDraining()
      ? 'Dispatch is paused. Wait for running workers to finish (watch the Issues board), then log in to the new account below.'
      : ''
  );

  constructor() {
    this.form.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => {
      this._formValue.set(this._readForm());
      this._summaryError.set(this._attentionSummary());
    });

    effect(() => {
      const settings: ClaudeCodeSettings | null = this.settingsService.claudeCodeSettings();
      if (settings !== null) {
        untracked(() => this._seed(settings));
      }
    });

    effect(() => {
      const message: string | null = this.settingsService.saveClaudeCodeError();
      const code: string | null = this.settingsService.saveClaudeCodeErrorCode();
      if (message !== null) {
        untracked(() => this._showServerError(code, message));
      }
    });

    effect(() => {
      const settings = this.settingsService.authSettings();
      if (settings !== null && !this._modeInitialized) {
        this._modeInitialized = true;
        this._selectedMode.set(settings.mode);
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
        // Move focus to auth heading on Succeeded regardless of drain state.
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
  }

  hasUnsavedChanges(): boolean {
    return (this._selectedMode() === 'api_key' && this._apiKeyValue.length > 0) || this._isDirty();
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

  protected _save(): void {
    if (!this._isDirty() || this.settingsService.savingClaudeCode()) {
      return;
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this._summaryError.set(this._attentionSummary());
      this._focusFirstInvalid();
      return;
    }
    this._clearServerErrors();
    const { model, ...rest } = this._readForm();
    this.settingsService.updateClaudeCodeSettings({ model: model.trim(), ...rest });
  }

  protected _addRule(): void {
    this.denyRules.push(this._createRule(''));
    const index: number = this.denyRules.length - 1;
    afterNextRender(() => this._focus(`denyRule-${index}`), { injector: this._injector });
  }

  protected _removeRule(index: number): void {
    this.denyRules.removeAt(index);
    const remaining: number = this.denyRules.length;
    let targetId = 'addRule';
    if (index < remaining) {
      targetId = `removeRule-${index}`;
    } else if (remaining > 0) {
      targetId = `removeRule-${remaining - 1}`;
    }
    afterNextRender(() => this._focus(targetId), { injector: this._injector });
  }

  protected _removeLabel(index: number): string {
    return `Remove deny rule ${this.denyRules.at(index)?.value || index + 1}`;
  }

  protected _modelInvalid(): boolean {
    return this._modelError() !== '';
  }

  protected _modelError(): string {
    const control: FormControl<string> = this.form.controls.model;
    const errors = control.errors;
    if (errors?.[SERVER_ERROR_KEY]) {
      return errors[SERVER_ERROR_KEY];
    }
    return control.touched && errors?.['modelTooLong'] ? MODEL_TOO_LONG_TEXT : '';
  }

  protected _ruleInvalid(index: number): boolean {
    return this._ruleError(index) !== '';
  }

  protected _ruleError(index: number): string {
    const control: FormControl<string> | undefined = this.denyRules.at(index);
    if (control === undefined) {
      return '';
    }
    const errors = control.errors;
    if (errors?.[SERVER_ERROR_KEY]) {
      return errors[SERVER_ERROR_KEY];
    }
    if (!control.touched) {
      return '';
    }
    if (errors?.['required']) {
      return RULE_REQUIRED_TEXT;
    }
    return errors?.['denyRuleFormat'] ? RULE_FORMAT_TEXT : '';
  }

  private _readForm(): ClaudeCodeSettings {
    const { model, blockCiCdEdits, denyRules } = this.form.getRawValue();
    return { model, blockCiCdEdits, additionalDenyRules: denyRules };
  }

  private _createRule(rule: string): FormControl<string> {
    return this._fb.control(rule, [Validators.required, denyRuleFormatValidator]);
  }

  private _seed(settings: ClaudeCodeSettings): void {
    if (settingsEqual(settings, this._readForm())) {
      this.form.markAsPristine();
      this.form.updateValueAndValidity();
      this._cdr.markForCheck();
      return;
    }
    this.denyRules.clear({ emitEvent: false });
    settings.additionalDenyRules.forEach((rule) => this.denyRules.push(this._createRule(rule), { emitEvent: false }));
    this.form.patchValue({ model: settings.model, blockCiCdEdits: settings.blockCiCdEdits }, { emitEvent: false });
    this.form.markAsPristine();
    this.form.markAsUntouched();
    this.form.updateValueAndValidity();
    this._cdr.markForCheck();
  }

  private _showServerError(code: string | null, detail: string): void {
    if (code === MODEL_TOO_LONG_CODE) {
      this.form.controls.model.setErrors({ [SERVER_ERROR_KEY]: detail });
    } else if (!(code === INVALID_DENY_RULE_CODE && this._attachToRuleRow(detail))) {
      this._summaryError.set(detail);
      this._cdr.markForCheck();
      return;
    }
    this._summaryError.set(this._attentionSummary());
    this._cdr.markForCheck();
    this._focusFirstInvalid();
  }

  private _attachToRuleRow(detail: string): boolean {
    const offendingRule: string | undefined = QUOTED_RULE_PATTERN.exec(detail)?.[1];
    const row: FormControl<string> | undefined =
      offendingRule === undefined || offendingRule === ''
        ? undefined
        : this.denyRules.controls.find((control) => control.value === offendingRule);
    row?.setErrors({ [SERVER_ERROR_KEY]: detail });
    return row !== undefined;
  }

  private _clearServerErrors(): void {
    [this.form.controls.model, ...this.denyRules.controls]
      .filter((control) => control.errors?.[SERVER_ERROR_KEY])
      .forEach((control) => control.updateValueAndValidity({ emitEvent: false }));
    this._summaryError.set('');
  }

  private _attentionSummary(): string {
    const count: number = [this.form.controls.model, ...this.denyRules.controls].filter(
      (control) => control.errors?.[SERVER_ERROR_KEY]
    ).length;
    if (count === 0) {
      return '';
    }
    return count === 1 ? '1 field needs attention.' : `${count} fields need attention.`;
  }

  private _focusFirstInvalid(): void {
    afterNextRender(
      () => this._elementRef.nativeElement.querySelector('form.claude-code [aria-invalid="true"]')?.focus(),
      { injector: this._injector }
    );
  }

  private _focus(id: string): void {
    (this._elementRef.nativeElement as HTMLElement).querySelector<HTMLElement>(`#${id}`)?.focus();
  }
}

function settingsEqual(left: ClaudeCodeSettings, right: ClaudeCodeSettings): boolean {
  return (
    left.model === right.model &&
    left.blockCiCdEdits === right.blockCiCdEdits &&
    left.additionalDenyRules.length === right.additionalDenyRules.length &&
    left.additionalDenyRules.every((rule, index) => rule === right.additionalDenyRules[index])
  );
}

import { Injectable, Signal, WritableSignal, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { extractErrorCode, extractErrorMessage } from '../http/extract-error-message';
import { Observable, forkJoin, merge } from 'rxjs';
import { debounceTime, switchMap } from 'rxjs/operators';
import {
  ApiKeyStatus,
  AuthMode,
  AuthSettings,
  ClaudeAccountSummary,
  ClaudeCodeSettings,
  GlobalSettingsResponse,
  ImageBuildStatus,
  LoginError,
  LoginPhase,
  LoginSessionUpdate,
  OAuthCredentialInfo,
  WorkerEnvironment,
  WorkerImageFlags,
  WorkerLimits,
} from '../models/settings.model';
import { DispatchService } from './dispatch.service';
import { AccountPresenceService } from './account-presence.service';
import { SystemSignalRService } from './system-signalr.service';

const LOAD_SETTINGS_ERROR = 'Failed to load settings';
const SAVE_SETTINGS_ERROR = 'Failed to save settings';
const SAVE_LIMITS_ERROR = 'Failed to save worker limits';
const SAVE_DISPATCH_ERROR = 'Failed to save dispatch settings';
const SAVE_WEBHOOKS_ERROR = 'Failed to save webhook settings';
const SAVE_IMAGE_FLAGS_ERROR = 'Failed to save worker image settings';
const SAVE_WORKER_ENVIRONMENT_ERROR = 'Failed to save worker environment';
const SAVE_CLAUDE_CODE_ERROR = 'Failed to save Claude Code settings';
const SAVE_HOSTS_ERROR = 'Failed to save provider hosts';
const START_LOGIN_ERROR = 'Failed to start login';

@Injectable({ providedIn: 'root' })
export class SettingsService {
  private readonly _http = inject(HttpClient);
  private readonly _dispatchService = inject(DispatchService);
  private readonly _accountPresence = inject(AccountPresenceService);
  private readonly _signalR = inject(SystemSignalRService);

  constructor() {
    merge(this._signalR.reconnected, this._signalR.reloadTrigger)
      .pipe(
        debounceTime(300),
        switchMap(() => this._fetchSettings()),
        takeUntilDestroyed()
      )
      .subscribe();

    this._signalR.loginSessionUpdate
      .pipe(takeUntilDestroyed())
      .subscribe((update: LoginSessionUpdate) => {
        this._loginPhaseSignal.set(update.phase);
        this._loginUrlSignal.set(update.authorizationUrl);
        this._loginErrorSignal.set(update.failureReason);

        if (update.phase === 'SigningIn' || update.phase === 'Succeeded' || update.phase === 'Failed') {
          this._codeSubmittingSignal.set(false);
        }

        if (update.phase === 'Succeeded') {
          this._fetchSettings().subscribe(() => {
            // Clear the phase only after settings reload confirms the new credential
            // is in place; this prevents a race where the panel re-reads stale status.
            this._loginPhaseSignal.set(null);
          });
        }
      });
  }

  private readonly _settingsSignal: WritableSignal<GlobalSettingsResponse | null> = signal(null);
  readonly settings: Signal<GlobalSettingsResponse | null> = this._settingsSignal.asReadonly();

  readonly authSettings: WritableSignal<AuthSettings | null> = signal(null);
  readonly loading: WritableSignal<boolean> = signal(false);
  readonly saving: WritableSignal<boolean> = signal(false);
  readonly switching: WritableSignal<boolean> = signal(false);
  readonly saveSuccess: WritableSignal<boolean> = signal(false);

  private readonly _workerLimitsSignal: WritableSignal<WorkerLimits | null> = signal(null);
  readonly workerLimits: Signal<WorkerLimits | null> = this._workerLimitsSignal.asReadonly();

  private readonly _savingLimitsSignal: WritableSignal<boolean> = signal(false);
  readonly savingLimits: Signal<boolean> = this._savingLimitsSignal.asReadonly();

  private readonly _saveLimitsSuccessSignal: WritableSignal<boolean> = signal(false);
  readonly saveLimitsSuccess: Signal<boolean> = this._saveLimitsSuccessSignal.asReadonly();

  private readonly _loadErrorSignal: WritableSignal<string | null> = signal(null);
  readonly loadError: Signal<string | null> = this._loadErrorSignal.asReadonly();

  private readonly _saveErrorSignal: WritableSignal<string | null> = signal(null);
  readonly saveError: Signal<string | null> = this._saveErrorSignal.asReadonly();

  private readonly _switchErrorSignal: WritableSignal<string | null> = signal(null);
  readonly switchError: Signal<string | null> = this._switchErrorSignal.asReadonly();

  private readonly _saveLimitsErrorSignal: WritableSignal<string | null> = signal(null);
  readonly saveLimitsError: Signal<string | null> = this._saveLimitsErrorSignal.asReadonly();

  private readonly _savingDispatchSignal: WritableSignal<boolean> = signal(false);
  readonly savingDispatch: Signal<boolean> = this._savingDispatchSignal.asReadonly();

  private readonly _saveDispatchSuccessSignal: WritableSignal<boolean> = signal(false);
  readonly saveDispatchSuccess: Signal<boolean> = this._saveDispatchSuccessSignal.asReadonly();

  private readonly _saveDispatchErrorSignal: WritableSignal<string | null> = signal(null);
  readonly saveDispatchError: Signal<string | null> = this._saveDispatchErrorSignal.asReadonly();

  private readonly _savingWebhooksSignal: WritableSignal<boolean> = signal(false);
  readonly savingWebhooks: Signal<boolean> = this._savingWebhooksSignal.asReadonly();

  private readonly _saveWebhooksSuccessSignal: WritableSignal<boolean> = signal(false);
  readonly saveWebhooksSuccess: Signal<boolean> = this._saveWebhooksSuccessSignal.asReadonly();

  private readonly _saveWebhooksErrorSignal: WritableSignal<string | null> = signal(null);
  readonly saveWebhooksError: Signal<string | null> = this._saveWebhooksErrorSignal.asReadonly();

  /** Error code of the last failed webhook save — keys the error to its field (e.g. Settings.InvalidWebhookPublicBaseUrl). */
  private readonly _saveWebhooksErrorCodeSignal: WritableSignal<string | null> = signal(null);
  readonly saveWebhooksErrorCode: Signal<string | null> = this._saveWebhooksErrorCodeSignal.asReadonly();

  private readonly _workerImageFlagsSignal: WritableSignal<WorkerImageFlags | null> = signal(null);
  readonly workerImageFlags: Signal<WorkerImageFlags | null> = this._workerImageFlagsSignal.asReadonly();

  private readonly _imageBuildStatusSignal: WritableSignal<ImageBuildStatus> = signal('Idle');
  readonly imageBuildStatus: Signal<ImageBuildStatus> = this._imageBuildStatusSignal.asReadonly();

  private readonly _imageBuildLogTailSignal: WritableSignal<string | null> = signal(null);
  readonly imageBuildLogTail: Signal<string | null> = this._imageBuildLogTailSignal.asReadonly();

  private readonly _imageBuildNextRetryAtSignal: WritableSignal<string | null> = signal(null);
  readonly imageBuildNextRetryAt: Signal<string | null> = this._imageBuildNextRetryAtSignal.asReadonly();

  private readonly _imageBuildAttemptSignal: WritableSignal<number> = signal(0);
  readonly imageBuildAttempt: Signal<number> = this._imageBuildAttemptSignal.asReadonly();

  private readonly _hasUsableImageSignal: WritableSignal<boolean> = signal(false);
  readonly hasUsableImage: Signal<boolean> = this._hasUsableImageSignal.asReadonly();

  readonly isColdBuildBlocking: Signal<boolean> = computed(
    () => this._accountPresence.hasAccounts() && !this._hasUsableImageSignal()
  );

  private readonly _savingImageFlagsSignal: WritableSignal<boolean> = signal(false);
  readonly savingImageFlags: Signal<boolean> = this._savingImageFlagsSignal.asReadonly();

  private readonly _saveImageFlagsSuccessSignal: WritableSignal<boolean> = signal(false);
  readonly saveImageFlagsSuccess: Signal<boolean> = this._saveImageFlagsSuccessSignal.asReadonly();

  private readonly _saveImageFlagsErrorSignal: WritableSignal<string | null> = signal(null);
  readonly saveImageFlagsError: Signal<string | null> = this._saveImageFlagsErrorSignal.asReadonly();

  private readonly _workerEnvironmentSignal: WritableSignal<WorkerEnvironment | null> = signal(null);
  readonly workerEnvironment: Signal<WorkerEnvironment | null> = this._workerEnvironmentSignal.asReadonly();

  private readonly _savingWorkerEnvironmentSignal: WritableSignal<boolean> = signal(false);
  readonly savingWorkerEnvironment: Signal<boolean> = this._savingWorkerEnvironmentSignal.asReadonly();

  private readonly _saveWorkerEnvironmentSuccessSignal: WritableSignal<boolean> = signal(false);
  readonly saveWorkerEnvironmentSuccess: Signal<boolean> = this._saveWorkerEnvironmentSuccessSignal.asReadonly();

  private readonly _saveWorkerEnvironmentErrorSignal: WritableSignal<string | null> = signal(null);
  readonly saveWorkerEnvironmentError: Signal<string | null> = this._saveWorkerEnvironmentErrorSignal.asReadonly();

  private readonly _saveWorkerEnvironmentErrorCodeSignal: WritableSignal<string | null> = signal(null);
  readonly saveWorkerEnvironmentErrorCode: Signal<string | null> = this._saveWorkerEnvironmentErrorCodeSignal.asReadonly();

  private readonly _savingClaudeCodeSignal: WritableSignal<boolean> = signal(false);
  readonly savingClaudeCode: Signal<boolean> = this._savingClaudeCodeSignal.asReadonly();

  private readonly _saveClaudeCodeSuccessSignal: WritableSignal<boolean> = signal(false);
  readonly saveClaudeCodeSuccess: Signal<boolean> = this._saveClaudeCodeSuccessSignal.asReadonly();

  private readonly _saveClaudeCodeErrorSignal: WritableSignal<string | null> = signal(null);
  readonly saveClaudeCodeError: Signal<string | null> = this._saveClaudeCodeErrorSignal.asReadonly();

  private readonly _saveClaudeCodeErrorCodeSignal: WritableSignal<string | null> = signal(null);
  readonly saveClaudeCodeErrorCode: Signal<string | null> = this._saveClaudeCodeErrorCodeSignal.asReadonly();

  private readonly _claudeCodeSettingsSignal: WritableSignal<ClaudeCodeSettings | null> = signal(null);
  readonly claudeCodeSettings: Signal<ClaudeCodeSettings | null> = this._claudeCodeSettingsSignal.asReadonly();

  private readonly _savingHostsSignal: WritableSignal<boolean> = signal(false);
  readonly savingHosts: Signal<boolean> = this._savingHostsSignal.asReadonly();

  private readonly _saveHostsSuccessSignal: WritableSignal<boolean> = signal(false);
  readonly saveHostsSuccess: Signal<boolean> = this._saveHostsSuccessSignal.asReadonly();

  private readonly _saveHostsErrorSignal: WritableSignal<string | null> = signal(null);
  readonly saveHostsError: Signal<string | null> = this._saveHostsErrorSignal.asReadonly();

  private readonly _allowedProviderHostsSignal: WritableSignal<readonly string[]> = signal([]);
  readonly allowedProviderHosts: Signal<readonly string[]> = this._allowedProviderHostsSignal.asReadonly();

  private readonly _loginPhaseSignal: WritableSignal<LoginPhase | null> = signal(null);
  readonly loginPhase: Signal<LoginPhase | null> = this._loginPhaseSignal.asReadonly();

  private readonly _loginUrlSignal: WritableSignal<string | null> = signal(null);
  readonly loginUrl: Signal<string | null> = this._loginUrlSignal.asReadonly();

  private readonly _loginErrorSignal: WritableSignal<LoginError | null> = signal(null);
  readonly loginError: Signal<LoginError | null> = this._loginErrorSignal.asReadonly();

  private readonly _codeSubmittingSignal: WritableSignal<boolean> = signal(false);
  readonly codeSubmitting: Signal<boolean> = this._codeSubmittingSignal.asReadonly();

  private readonly _startLoginErrorSignal: WritableSignal<string | null> = signal(null);
  readonly startLoginError: Signal<string | null> = this._startLoginErrorSignal.asReadonly();

  loadSettings(): Promise<void> {
    return new Promise<void>((resolve) => {
      this._fetchSettings().subscribe({ next: resolve, error: resolve });
    });
  }

  private _fetchSettings(): Observable<void> {
    this._loadErrorSignal.set(null);
    this._saveErrorSignal.set(null);
    this._switchErrorSignal.set(null);
    this._saveLimitsErrorSignal.set(null);
    this._saveDispatchErrorSignal.set(null);
    this._saveImageFlagsErrorSignal.set(null);
    this._saveHostsErrorSignal.set(null);
    this._saveWorkerEnvironmentErrorSignal.set(null);
    this._saveWorkerEnvironmentErrorCodeSignal.set(null);
    this._saveWorkerEnvironmentSuccessSignal.set(false);
    this._savingWorkerEnvironmentSignal.set(false);
    this._saveClaudeCodeErrorSignal.set(null);
    this._saveClaudeCodeErrorCodeSignal.set(null);
    this._saveClaudeCodeSuccessSignal.set(false);
    this._savingClaudeCodeSignal.set(false);
    this.saveSuccess.set(false);
    this._saveLimitsSuccessSignal.set(false);
    this._saveDispatchSuccessSignal.set(false);
    this._saveImageFlagsSuccessSignal.set(false);
    this._saveHostsSuccessSignal.set(false);
    this.saving.set(false);
    this._savingLimitsSignal.set(false);
    this._savingDispatchSignal.set(false);
    this._savingImageFlagsSignal.set(false);
    this._savingHostsSignal.set(false);
    this.switching.set(false);
    this.loading.set(true);

    return new Observable<void>((observer) => {
      const subscription = forkJoin({
        settings: this._http.get<GlobalSettingsResponse>('/api/settings'),
        credentials: this._http.get<ClaudeAccountSummary>('/api/credentials'),
      }).subscribe({
        next: ({ settings, credentials }) => {
          this._settingsSignal.set(settings);
          this.authSettings.set(this._mapToAuthSettings(credentials));
          this._workerLimitsSignal.set({ maxConcurrent: settings.maxConcurrent, timeoutMinutes: settings.timeoutMinutes });
          this._allowedProviderHostsSignal.set(settings.allowedProviderHosts ?? []);
          this._dispatchService.updateFromSettings(settings);
          this._workerImageFlagsSignal.set(this._mapToWorkerImageFlags(settings));
          this._workerEnvironmentSignal.set(this._mapToWorkerEnvironment(settings));
          this._claudeCodeSettingsSignal.set(this._mapToClaudeCodeSettings(settings));
          this._applyImageBuildResponse(settings);
          this.loading.set(false);
          observer.next();
          observer.complete();
        },
        error: (err: HttpErrorResponse) => {
          console.error(err);
          this._loadErrorSignal.set(LOAD_SETTINGS_ERROR);
          this.loading.set(false);
          observer.error(err);
        },
      });

      return () => subscription.unsubscribe();
    });
  }

  updateAuthMode(mode: AuthMode, apiKey?: string): void {
    this._saveErrorSignal.set(null);
    this.saveSuccess.set(false);
    this.saving.set(true);

    const body = apiKey !== undefined ? { mode, apiKey } : { mode };

    this._http.put<ClaudeAccountSummary>('/api/credentials/auth', body).subscribe({
      next: (summary) => {
        this.authSettings.set(this._mapToAuthSettings(summary));
        this.saving.set(false);
        this.saveSuccess.set(true);
      },
      error: (err: HttpErrorResponse) => {
        console.error(err);
        this._saveErrorSignal.set(SAVE_SETTINGS_ERROR);
        this.saving.set(false);
        this.saveSuccess.set(false);
      },
    });
  }

  markOAuthConfigured(): void {
    this._saveErrorSignal.set(null);
    this.saveSuccess.set(false);
    this.saving.set(true);

    this._http.put<ClaudeAccountSummary>('/api/credentials/auth', { mode: 'oauth' }).subscribe({
      next: (summary) => {
        this.authSettings.set(this._mapToAuthSettings(summary));
        this.saving.set(false);
        this.saveSuccess.set(true);
      },
      error: (err: HttpErrorResponse) => {
        console.error(err);
        this._saveErrorSignal.set(SAVE_SETTINGS_ERROR);
        this.saving.set(false);
        this.saveSuccess.set(false);
      },
    });
  }

  startLogin(): void {
    this._startLoginErrorSignal.set(null);
    this._loginPhaseSignal.set('Starting');
    this._loginUrlSignal.set(null);
    this._loginErrorSignal.set(null);
    this._codeSubmittingSignal.set(false);

    this._http.post<{ sessionId: string }>('/api/credentials/login/start', null).subscribe({
      next: () => {
        // Phase transitions arrive via SignalR; Starting was set optimistically above
      },
      error: (err: HttpErrorResponse) => {
        console.error(err);
        this._loginPhaseSignal.set(null);
        this._startLoginErrorSignal.set(START_LOGIN_ERROR);
      },
    });
  }

  submitLoginCode(code: string): void {
    this._codeSubmittingSignal.set(true);
    this._loginPhaseSignal.set('SigningIn');

    this._http.post<void>('/api/credentials/login/code', { code }).subscribe({
      next: () => {
        // Success/failure arrives via SignalR
      },
      error: (err: HttpErrorResponse) => {
        console.error(err);
        this._codeSubmittingSignal.set(false);
        // Revert optimistic SigningIn if the HTTP call itself failed (network/4xx before SignalR)
        this._loginPhaseSignal.update((current) => current === 'SigningIn' ? 'WaitingForAuthorization' : current);
      },
    });
  }

  cancelLogin(): void {
    this._loginPhaseSignal.set(null);
    this._loginUrlSignal.set(null);
    this._loginErrorSignal.set(null);
    this._codeSubmittingSignal.set(false);
    this._startLoginErrorSignal.set(null);
  }

  updateWorkerLimits(maxConcurrent: number, timeoutMinutes: number): void {
    this._saveLimitsErrorSignal.set(null);
    this._saveLimitsSuccessSignal.set(false);
    this._savingLimitsSignal.set(true);

    this._http.put<GlobalSettingsResponse>('/api/settings/limits', { maxConcurrent, timeoutMinutes }).subscribe({
      next: (response) => {
        this._workerLimitsSignal.set({ maxConcurrent: response.maxConcurrent, timeoutMinutes: response.timeoutMinutes });
        this._savingLimitsSignal.set(false);
        this._saveLimitsSuccessSignal.set(true);
      },
      error: (err: HttpErrorResponse) => {
        console.error(err);
        this._saveLimitsErrorSignal.set(SAVE_LIMITS_ERROR);
        this._savingLimitsSignal.set(false);
      },
    });
  }

  updateAllowedProviderHosts(hosts: string[]): void {
    this._saveHostsErrorSignal.set(null);
    this._saveHostsSuccessSignal.set(false);
    this._savingHostsSignal.set(true);

    this._http.put<GlobalSettingsResponse>('/api/settings/allowed-provider-hosts', { hosts }).subscribe({
      next: (response) => {
        this._allowedProviderHostsSignal.set(response.allowedProviderHosts);
        this._savingHostsSignal.set(false);
        this._saveHostsSuccessSignal.set(true);
      },
      error: (err: HttpErrorResponse) => {
        console.error(err);
        this._saveHostsErrorSignal.set(extractErrorMessage(err) ?? SAVE_HOSTS_ERROR);
        this._savingHostsSignal.set(false);
      },
    });
  }

  updateDispatchSettings(autoResumeOnUsageReset: boolean, probeIntervalMinutes: number, pollIntervalSeconds: number): void {
    this._saveDispatchErrorSignal.set(null);
    this._saveDispatchSuccessSignal.set(false);
    this._savingDispatchSignal.set(true);

    this._http.put<GlobalSettingsResponse>('/api/settings/dispatch', { autoResumeOnUsageReset, probeIntervalMinutes, pollIntervalSeconds }).subscribe({
      next: (response) => {
        this._settingsSignal.set(response);
        this._dispatchService.updateFromSettings(response);
        this._savingDispatchSignal.set(false);
        this._saveDispatchSuccessSignal.set(true);
      },
      error: (err: HttpErrorResponse) => {
        console.error(err);
        this._saveDispatchErrorSignal.set(SAVE_DISPATCH_ERROR);
        this._savingDispatchSignal.set(false);
      },
    });
  }

  updateWebhookSettings(webhookPublicBaseUrl: string | null, backstopIntervalMinutes: number): void {
    this._saveWebhooksErrorSignal.set(null);
    this._saveWebhooksErrorCodeSignal.set(null);
    this._saveWebhooksSuccessSignal.set(false);
    this._savingWebhooksSignal.set(true);

    this._http.put<GlobalSettingsResponse>('/api/settings/webhooks', { webhookPublicBaseUrl, backstopIntervalMinutes }).subscribe({
      next: (response) => {
        this._settingsSignal.set(response);
        this._savingWebhooksSignal.set(false);
        this._saveWebhooksSuccessSignal.set(true);
      },
      error: (err: HttpErrorResponse) => {
        console.error(err);
        this._saveWebhooksErrorSignal.set(extractErrorMessage(err) ?? SAVE_WEBHOOKS_ERROR);
        this._saveWebhooksErrorCodeSignal.set(extractErrorCode(err));
        this._savingWebhooksSignal.set(false);
      },
    });
  }

  updateWorkerImageFlags(flags: WorkerImageFlags): void {
    this._saveImageFlagsErrorSignal.set(null);
    this._saveImageFlagsSuccessSignal.set(false);
    this._savingImageFlagsSignal.set(true);

    this._http.put<GlobalSettingsResponse>('/api/settings/worker-image', flags).subscribe({
      next: (response) => {
        this._workerImageFlagsSignal.set(this._mapToWorkerImageFlags(response));
        this._applyImageBuildResponse(response);
        this._savingImageFlagsSignal.set(false);
        this._saveImageFlagsSuccessSignal.set(true);
      },
      error: (err: HttpErrorResponse) => {
        console.error(err);
        this._saveImageFlagsErrorSignal.set(SAVE_IMAGE_FLAGS_ERROR);
        this._savingImageFlagsSignal.set(false);
      },
    });
  }

  updateWorkerEnvironment(environment: WorkerEnvironment): void {
    this._saveWorkerEnvironmentErrorSignal.set(null);
    this._saveWorkerEnvironmentErrorCodeSignal.set(null);
    this._saveWorkerEnvironmentSuccessSignal.set(false);
    this._savingWorkerEnvironmentSignal.set(true);

    this._http.put<GlobalSettingsResponse>('/api/settings/worker-environment', environment).subscribe({
      next: (response) => {
        this._workerEnvironmentSignal.set(this._mapToWorkerEnvironment(response));
        this._savingWorkerEnvironmentSignal.set(false);
        this._saveWorkerEnvironmentSuccessSignal.set(true);
      },
      error: (err: HttpErrorResponse) => {
        console.error(err);
        this._saveWorkerEnvironmentErrorSignal.set(extractErrorMessage(err) ?? SAVE_WORKER_ENVIRONMENT_ERROR);
        this._saveWorkerEnvironmentErrorCodeSignal.set(extractErrorCode(err));
        this._savingWorkerEnvironmentSignal.set(false);
      },
    });
  }

  updateClaudeCodeSettings(settings: ClaudeCodeSettings): void {
    this._saveClaudeCodeErrorSignal.set(null);
    this._saveClaudeCodeErrorCodeSignal.set(null);
    this._saveClaudeCodeSuccessSignal.set(false);
    this._savingClaudeCodeSignal.set(true);

    this._http.put<GlobalSettingsResponse>('/api/settings/claude-code', settings).subscribe({
      next: (response) => {
        this._claudeCodeSettingsSignal.set(this._mapToClaudeCodeSettings(response));
        this._savingClaudeCodeSignal.set(false);
        this._saveClaudeCodeSuccessSignal.set(true);
      },
      error: (err: HttpErrorResponse) => {
        console.error(err);
        this._saveClaudeCodeErrorSignal.set(extractErrorMessage(err) ?? SAVE_CLAUDE_CODE_ERROR);
        this._saveClaudeCodeErrorCodeSignal.set(extractErrorCode(err));
        this._savingClaudeCodeSignal.set(false);
      },
    });
  }

  retryImageBuild(): void {
    this._saveImageFlagsErrorSignal.set(null);
    this._saveImageFlagsSuccessSignal.set(false);
    this._savingImageFlagsSignal.set(true);

    this._http.post<GlobalSettingsResponse>('/api/settings/worker-image/retry', null).subscribe({
      next: (response) => {
        this._workerImageFlagsSignal.set(this._mapToWorkerImageFlags(response));
        this._applyImageBuildResponse(response);
        this._savingImageFlagsSignal.set(false);
      },
      error: (err: HttpErrorResponse) => {
        console.error(err);
        this._saveImageFlagsErrorSignal.set(SAVE_IMAGE_FLAGS_ERROR);
        this._savingImageFlagsSignal.set(false);
      },
    });
  }

  private _applyImageBuildResponse(response: GlobalSettingsResponse): void {
    this._imageBuildStatusSignal.set(response.imageBuildStatus);
    this._imageBuildLogTailSignal.set(response.lastImageBuildError);
    this._imageBuildNextRetryAtSignal.set(
      response.imageBuildStatus === 'Building' || response.imageBuildStatus === 'Idle'
        ? null
        : (response.nextRetryAt ?? null)
    );
    this._imageBuildAttemptSignal.set(response.attempt ?? 0);
    this._hasUsableImageSignal.set(response.hasUsableImage);
  }

  private _mapToWorkerImageFlags(response: GlobalSettingsResponse): WorkerImageFlags {
    return {
      imageTag: response.imageTag,
      buildEnabled: response.buildEnabled,
      installDotnet: response.installDotnet,
      installAngular: response.installAngular,
      installChromium: response.installChromium,
      installDocker: response.installDocker,
    };
  }

  private _mapToWorkerEnvironment(response: GlobalSettingsResponse): WorkerEnvironment {
    return {
      memoryLimitMb: response.memoryLimitMb,
      cpuLimit: response.cpuLimit,
      pidsLimit: response.pidsLimit,
    };
  }

  private _mapToClaudeCodeSettings(response: GlobalSettingsResponse): ClaudeCodeSettings {
    return {
      additionalDenyRules: [...(response.additionalDenyRules ?? [])],
    };
  }

  private _mapToAuthSettings(summary: ClaudeAccountSummary): AuthSettings {
    const mode: AuthMode = summary.authMode === 'OAuth' ? 'oauth' : 'api_key';
    const isOAuth = mode === 'oauth';

    let oauth: OAuthCredentialInfo | null = null;
    if (isOAuth) {
      oauth = {
        status: summary.oAuthStatus,
        subscriptionType: summary.subscriptionType,
      };
    }

    const apiKeyStatus: ApiKeyStatus = summary.apiKeyStatus;

    return {
      mode,
      apiKeyStatus,
      apiKeyConfigured: apiKeyStatus === 'Present',
      oauth,
      accountEmail: summary.oAuthAccountEmail,
      accountOrgName: summary.oAuthAccountOrgName,
    };
  }
}

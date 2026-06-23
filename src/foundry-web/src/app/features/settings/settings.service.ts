import { Injectable, Signal, WritableSignal, inject, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import {
  AuthMode,
  AuthSettings,
  GlobalSettingsResponse,
  ImageBuildStatus,
  OAuthCredentialInfo,
  OAuthScanResponse,
  UpdatePromptTemplatesRequest,
  WorkerImageFlags,
  WorkerLimits,
} from './settings.model';
import { DispatchService } from '../../core/services/dispatch.service';

const LOAD_SETTINGS_ERROR = 'Failed to load settings';
const SAVE_SETTINGS_ERROR = 'Failed to save settings';
const SWITCH_OAUTH_ERROR = 'Failed to switch to OAuth mode';
const SAVE_LIMITS_ERROR = 'Failed to save worker limits';
const SAVE_PROMPTS_ERROR = 'Failed to save prompt templates';
const SAVE_DISPATCH_ERROR = 'Failed to save dispatch settings';
const SAVE_IMAGE_FLAGS_ERROR = 'Failed to save worker image settings';

@Injectable({ providedIn: 'root' })
export class SettingsService {
  private readonly _http = inject(HttpClient);
  private readonly _dispatchService = inject(DispatchService);

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

  private readonly _systemPromptTemplateSignal: WritableSignal<string | null> = signal(null);
  readonly systemPromptTemplate: Signal<string | null> = this._systemPromptTemplateSignal.asReadonly();

  private readonly _workerPromptTemplateSignal: WritableSignal<string | null> = signal(null);
  readonly workerPromptTemplate: Signal<string | null> = this._workerPromptTemplateSignal.asReadonly();

  private readonly _savingPromptsSignal: WritableSignal<boolean> = signal(false);
  readonly savingPrompts: Signal<boolean> = this._savingPromptsSignal.asReadonly();

  private readonly _savePromptsSuccessSignal: WritableSignal<boolean> = signal(false);
  readonly savePromptsSuccess: Signal<boolean> = this._savePromptsSuccessSignal.asReadonly();

  private readonly _savePromptsErrorSignal: WritableSignal<string | null> = signal(null);
  readonly savePromptsError: Signal<string | null> = this._savePromptsErrorSignal.asReadonly();

  private readonly _savingDispatchSignal: WritableSignal<boolean> = signal(false);
  readonly savingDispatch: Signal<boolean> = this._savingDispatchSignal.asReadonly();

  private readonly _saveDispatchSuccessSignal: WritableSignal<boolean> = signal(false);
  readonly saveDispatchSuccess: Signal<boolean> = this._saveDispatchSuccessSignal.asReadonly();

  private readonly _saveDispatchErrorSignal: WritableSignal<string | null> = signal(null);
  readonly saveDispatchError: Signal<string | null> = this._saveDispatchErrorSignal.asReadonly();

  private readonly _workerImageFlagsSignal: WritableSignal<WorkerImageFlags | null> = signal(null);
  readonly workerImageFlags: Signal<WorkerImageFlags | null> = this._workerImageFlagsSignal.asReadonly();

  private readonly _imageBuildStatusSignal: WritableSignal<ImageBuildStatus> = signal('Idle');
  readonly imageBuildStatus: Signal<ImageBuildStatus> = this._imageBuildStatusSignal.asReadonly();

  private readonly _imageBuildLogTailSignal: WritableSignal<string | null> = signal(null);
  readonly imageBuildLogTail: Signal<string | null> = this._imageBuildLogTailSignal.asReadonly();

  private readonly _hasUsableImageSignal: WritableSignal<boolean> = signal(false);
  readonly hasUsableImage: Signal<boolean> = this._hasUsableImageSignal.asReadonly();

  private readonly _savingImageFlagsSignal: WritableSignal<boolean> = signal(false);
  readonly savingImageFlags: Signal<boolean> = this._savingImageFlagsSignal.asReadonly();

  private readonly _saveImageFlagsSuccessSignal: WritableSignal<boolean> = signal(false);
  readonly saveImageFlagsSuccess: Signal<boolean> = this._saveImageFlagsSuccessSignal.asReadonly();

  private readonly _saveImageFlagsErrorSignal: WritableSignal<string | null> = signal(null);
  readonly saveImageFlagsError: Signal<string | null> = this._saveImageFlagsErrorSignal.asReadonly();

  loadSettings(): Promise<void> {
    this._loadErrorSignal.set(null);
    this._saveErrorSignal.set(null);
    this._switchErrorSignal.set(null);
    this._saveLimitsErrorSignal.set(null);
    this._savePromptsErrorSignal.set(null);
    this._saveDispatchErrorSignal.set(null);
    this._saveImageFlagsErrorSignal.set(null);
    this.saveSuccess.set(false);
    this._saveLimitsSuccessSignal.set(false);
    this._savePromptsSuccessSignal.set(false);
    this._saveDispatchSuccessSignal.set(false);
    this._saveImageFlagsSuccessSignal.set(false);
    this.saving.set(false);
    this._savingLimitsSignal.set(false);
    this._savingPromptsSignal.set(false);
    this._savingDispatchSignal.set(false);
    this._savingImageFlagsSignal.set(false);
    this.switching.set(false);
    this.loading.set(true);

    return new Promise<void>((resolve) => {
      this._http.get<GlobalSettingsResponse>('/api/settings').subscribe({
        next: (response) => {
          this._settingsSignal.set(response);
          this.authSettings.set(this._mapToAuthSettings(response));
          this._workerLimitsSignal.set({ maxConcurrent: response.maxConcurrent, timeoutMinutes: response.timeoutMinutes });
          this._systemPromptTemplateSignal.set(response.systemPromptTemplate);
          this._workerPromptTemplateSignal.set(response.workerPromptTemplate);
          this._dispatchService.updateFromSettings(response);
          this._workerImageFlagsSignal.set(this._mapToWorkerImageFlags(response));
          this._imageBuildStatusSignal.set(response.imageBuildStatus);
          this._imageBuildLogTailSignal.set(response.lastImageBuildError);
          this._hasUsableImageSignal.set(response.hasUsableImage);
          this.loading.set(false);
          resolve();
        },
        error: (err: HttpErrorResponse) => {
          console.error(err);
          this._loadErrorSignal.set(LOAD_SETTINGS_ERROR);
          this.loading.set(false);
          resolve();
        },
      });
    });
  }

  updateAuthMode(mode: AuthMode, apiKey?: string): void {
    this._saveErrorSignal.set(null);
    this.saveSuccess.set(false);
    this.saving.set(true);

    const body = apiKey !== undefined ? { mode, apiKey } : { mode };

    this._http.put<GlobalSettingsResponse>('/api/settings/auth', body).subscribe({
      next: (response) => {
        this.authSettings.set(this._mapToAuthSettings(response));
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

  updatePromptTemplates(request: UpdatePromptTemplatesRequest): void {
    this._savePromptsErrorSignal.set(null);
    this._savePromptsSuccessSignal.set(false);
    this._savingPromptsSignal.set(true);

    this._http.put<GlobalSettingsResponse>('/api/settings/prompts', request).subscribe({
      next: (response) => {
        this._systemPromptTemplateSignal.set(response.systemPromptTemplate);
        this._workerPromptTemplateSignal.set(response.workerPromptTemplate);
        this._savingPromptsSignal.set(false);
        this._savePromptsSuccessSignal.set(true);
      },
      error: (err: HttpErrorResponse) => {
        console.error(err);
        this._savePromptsErrorSignal.set(SAVE_PROMPTS_ERROR);
        this._savingPromptsSignal.set(false);
      },
    });
  }

  updateDispatchSettings(autoResumeOnUsageReset: boolean, defaultCooldownMinutes: number): void {
    this._saveDispatchErrorSignal.set(null);
    this._saveDispatchSuccessSignal.set(false);
    this._savingDispatchSignal.set(true);

    this._http.put<GlobalSettingsResponse>('/api/settings/dispatch', { autoResumeOnUsageReset, defaultCooldownMinutes }).subscribe({
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

  scanOAuthCredentials(): void {
    this._switchErrorSignal.set(null);
    this.switching.set(true);

    this._http.get<OAuthScanResponse>('/api/settings/oauth/scan').subscribe({
      next: () => {
        this._http.put<GlobalSettingsResponse>('/api/settings/auth', { mode: 'oauth' }).subscribe({
          next: (response) => {
            this.authSettings.set(this._mapToAuthSettings(response));
            this.switching.set(false);
            this.saveSuccess.set(true);
          },
          error: (err: HttpErrorResponse) => {
            console.error(err);
            this._switchErrorSignal.set(SWITCH_OAUTH_ERROR);
            this.switching.set(false);
          },
        });
      },
      error: (err: HttpErrorResponse) => {
        console.error(err);
        this._switchErrorSignal.set(SWITCH_OAUTH_ERROR);
        this.switching.set(false);
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
        this._imageBuildStatusSignal.set(response.imageBuildStatus);
        this._imageBuildLogTailSignal.set(response.lastImageBuildError);
        this._hasUsableImageSignal.set(response.hasUsableImage);
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

  retryImageBuild(): void {
    this._saveImageFlagsErrorSignal.set(null);
    this._saveImageFlagsSuccessSignal.set(false);
    this._savingImageFlagsSignal.set(true);

    this._http.post<GlobalSettingsResponse>('/api/settings/worker-image/retry', null).subscribe({
      next: (response) => {
        this._workerImageFlagsSignal.set(this._mapToWorkerImageFlags(response));
        this._imageBuildStatusSignal.set(response.imageBuildStatus);
        this._imageBuildLogTailSignal.set(response.lastImageBuildError);
        this._hasUsableImageSignal.set(response.hasUsableImage);
        this._savingImageFlagsSignal.set(false);
      },
      error: (err: HttpErrorResponse) => {
        console.error(err);
        this._saveImageFlagsErrorSignal.set(SAVE_IMAGE_FLAGS_ERROR);
        this._savingImageFlagsSignal.set(false);
      },
    });
  }

  setImageBuildStatus(status: ImageBuildStatus, logTail: string | null): void {
    this._imageBuildStatusSignal.set(status);
    this._imageBuildLogTailSignal.set(logTail);
  }

  private _mapToWorkerImageFlags(response: GlobalSettingsResponse): WorkerImageFlags {
    return {
      installDotnet: response.installDotnet,
      installAngular: response.installAngular,
      installGlab: response.installGlab,
      installGh: response.installGh,
      installChromium: response.installChromium,
      installDocker: response.installDocker,
    };
  }

  private _mapToAuthSettings(response: GlobalSettingsResponse): AuthSettings {
    const mode: AuthMode = response.authMode === 'OAuth' ? 'oauth' : 'api_key';
    const isOAuth = mode === 'oauth';

    let oauth: OAuthCredentialInfo | null = null;
    if (isOAuth) {
      oauth = {
        accessTokenPresent: response.accessTokenPresent,
        refreshTokenPresent: response.refreshTokenPresent,
        expiresAt: response.expiresAt,
        subscriptionType: response.subscriptionType,
        status: this._resolveOAuthStatus(response),
      };
    }

    return {
      mode,
      apiKeyConfigured: false,
      oauth,
    };
  }

  private _resolveOAuthStatus(response: GlobalSettingsResponse): 'valid' | 'expired' | 'missing' {
    if (!response.accessTokenPresent) {
      return 'missing';
    }
    if (response.expiresAt !== null && new Date(response.expiresAt) < new Date()) {
      return 'expired';
    }
    return 'valid';
  }
}

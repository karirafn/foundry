import { Injectable, Signal, WritableSignal, inject, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { AuthMode, AuthSettings, OAuthCredentialInfo, OAuthScanResponse, WorkerLimits } from './settings.model';

const LOAD_SETTINGS_ERROR = 'Failed to load settings';
const SAVE_SETTINGS_ERROR = 'Failed to save settings';
const SWITCH_OAUTH_ERROR = 'Failed to switch to OAuth mode';
const SAVE_LIMITS_ERROR = 'Failed to save worker limits';

interface GlobalSettingsResponse {
  authMode: string;
  maxConcurrent: number;
  timeoutMinutes: number;
  accessTokenPresent: boolean;
  refreshTokenPresent: boolean;
  expiresAt: string | null;
  subscriptionType: string | null;
}

@Injectable({ providedIn: 'root' })
export class SettingsService {
  private readonly _http = inject(HttpClient);

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

  loadSettings(): void {
    this._loadErrorSignal.set(null);
    this._saveErrorSignal.set(null);
    this._switchErrorSignal.set(null);
    this._saveLimitsErrorSignal.set(null);
    this.saveSuccess.set(false);
    this._saveLimitsSuccessSignal.set(false);
    this.saving.set(false);
    this._savingLimitsSignal.set(false);
    this.switching.set(false);
    this.loading.set(true);

    this._http.get<GlobalSettingsResponse>('/api/settings').subscribe({
      next: (response) => {
        this.authSettings.set(this._mapToAuthSettings(response));
        this._workerLimitsSignal.set({ maxConcurrent: response.maxConcurrent, timeoutMinutes: response.timeoutMinutes });
        this.loading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        console.error(err);
        this._loadErrorSignal.set(LOAD_SETTINGS_ERROR);
        this.loading.set(false);
      },
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

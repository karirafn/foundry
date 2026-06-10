import { Injectable, Signal, WritableSignal, inject, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { AuthMode, AuthSettings, OAuthCredentialInfo } from './settings.model';

const LOAD_SETTINGS_ERROR = 'Failed to load settings';
const SAVE_SETTINGS_ERROR = 'Failed to save settings';
const SCAN_OAUTH_ERROR = 'Failed to scan for OAuth credentials';

interface GlobalSettingsResponse {
  authMode: string;
  maxConcurrent: number;
  timeoutMinutes: number;
  accessTokenPresent: boolean;
  refreshTokenPresent: boolean;
  expiresAt: string | null;
  subscriptionType: string | null;
}

interface OAuthScanResponse {
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
  subscriptionType: string;
}

@Injectable({ providedIn: 'root' })
export class SettingsService {
  private readonly _http = inject(HttpClient);

  readonly authSettings: WritableSignal<AuthSettings | null> = signal(null);
  readonly loading: WritableSignal<boolean> = signal(false);
  readonly saving: WritableSignal<boolean> = signal(false);
  readonly switching: WritableSignal<boolean> = signal(false);
  readonly saveSuccess: WritableSignal<boolean> = signal(false);

  private readonly _loadErrorSignal: WritableSignal<string | null> = signal(null);
  readonly loadError: Signal<string | null> = this._loadErrorSignal.asReadonly();

  private readonly _saveErrorSignal: WritableSignal<string | null> = signal(null);
  readonly saveError: Signal<string | null> = this._saveErrorSignal.asReadonly();

  private readonly _switchErrorSignal: WritableSignal<string | null> = signal(null);
  readonly switchError: Signal<string | null> = this._switchErrorSignal.asReadonly();

  loadSettings(): void {
    this._loadErrorSignal.set(null);
    this.loading.set(true);

    this._http.get<GlobalSettingsResponse>('/api/settings').subscribe({
      next: (response) => {
        this.authSettings.set(this._mapToAuthSettings(response));
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

  scanOAuthCredentials(): void {
    this._switchErrorSignal.set(null);
    this.switching.set(true);

    this._http.get<OAuthScanResponse>('/api/settings/oauth/scan').subscribe({
      next: () => {
        this.switching.set(false);
      },
      error: (err: HttpErrorResponse) => {
        console.error(err);
        this._switchErrorSignal.set(SCAN_OAUTH_ERROR);
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
      apiKeyConfigured: !isOAuth,
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

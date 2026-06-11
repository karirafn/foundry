export type AuthMode = 'api_key' | 'oauth';

export interface OAuthScanResponse {
  accessTokenPresent: boolean;
  refreshTokenPresent: boolean;
  expiresAt: string | null;
  subscriptionType: string | null;
}

export interface OAuthCredentialInfo {
  accessTokenPresent: boolean;
  refreshTokenPresent: boolean;
  expiresAt: string | null;
  subscriptionType: string | null;
  status: 'valid' | 'expired' | 'missing';
}

export interface AuthSettings {
  mode: AuthMode;
  apiKeyConfigured: boolean;
  oauth: OAuthCredentialInfo | null;
}

export interface UpdateAuthModeRequest {
  mode: AuthMode;
  apiKey?: string;
}

export interface WorkerLimits {
  maxConcurrent: number;
  timeoutMinutes: number;
}

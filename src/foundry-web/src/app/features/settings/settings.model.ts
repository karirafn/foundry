export type AuthMode = 'api_key' | 'oauth';

export type ImageBuildStatus = 'Idle' | 'Building' | 'Failed';

export interface WorkerImageFlags {
  installDotnet: boolean;
  installAngular: boolean;
  installGlab: boolean;
  installGh: boolean;
}

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

export interface GlobalSettingsResponse {
  authMode: string;
  maxConcurrent: number;
  timeoutMinutes: number;
  accessTokenPresent: boolean;
  refreshTokenPresent: boolean;
  expiresAt: string | null;
  subscriptionType: string | null;
  systemPromptTemplate: string | null;
  workerPromptTemplate: string | null;
  usageLimitResetsAt: string | null;
  isDispatchPaused: boolean;
  autoResumeOnUsageReset: boolean;
  defaultCooldownMinutes: number;
  installDotnet: boolean;
  installAngular: boolean;
  installGlab: boolean;
  installGh: boolean;
  imageBuildStatus: ImageBuildStatus;
  lastImageBuildError: string | null;
}

export interface UpdatePromptTemplatesRequest {
  systemPromptTemplate: string | null;
  workerPromptTemplate: string | null;
}

export interface DispatchSettings {
  autoResumeOnUsageReset: boolean;
  defaultCooldownMinutes: number;
}

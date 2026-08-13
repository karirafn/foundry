export type AuthMode = 'api_key' | 'oauth';

export type OAuthStatus = 'NotConfigured' | 'Present' | 'ReLoginNeeded';

export type ImageBuildStatus = 'Idle' | 'Building' | 'Failed';

export type LoginPhase = 'Starting' | 'WaitingForAuthorization' | 'SigningIn' | 'Succeeded' | 'Failed';

export type LoginError = 'InvalidCode' | 'UrlTimeout' | 'CodeTimeout' | 'Unknown';

export interface LoginSessionUpdate {
  sessionId: string;
  phase: LoginPhase;
  authorizationUrl: string | null;
  failureReason: LoginError | null;
  failureMessage: string | null;
}

export interface WorkerImageFlags {
  installDotnet: boolean;
  installAngular: boolean;
  installGlab: boolean;
  installGh: boolean;
  installChromium: boolean;
  installDocker: boolean;
}

export interface OAuthCredentialInfo {
  status: OAuthStatus;
  subscriptionType: string | null;
}

export interface AuthSettings {
  mode: AuthMode;
  apiKeyConfigured: boolean;
  oauth: OAuthCredentialInfo | null;
  accountEmail: string | null;
  accountOrgName: string | null;
}

export interface UpdateAuthModeRequest {
  mode: AuthMode;
  apiKey?: string;
}

export interface WorkerLimits {
  maxConcurrent: number;
  timeoutMinutes: number;
}

export interface ClaudeAccountSummary {
  accountId: string;
  authMode: string;
  oAuthStatus: OAuthStatus;
  subscriptionType: string | null;
  oAuthAccountEmail: string | null;
  oAuthAccountOrgName: string | null;
  nextProbeAt?: string | null;
}

export interface GlobalSettingsResponse {
  maxConcurrent: number;
  timeoutMinutes: number;
  probeIntervalMinutes: number;
  systemPromptTemplate: string | null;
  workerPromptTemplate: string | null;
  usageLimitResetsAt: string | null;
  isDispatchPaused: boolean;
  autoResumeOnUsageReset: boolean;
  installDotnet: boolean;
  installAngular: boolean;
  installGlab: boolean;
  installGh: boolean;
  installChromium: boolean;
  installDocker: boolean;
  imageBuildStatus: ImageBuildStatus;
  lastImageBuildError: string | null;
  hasUsableImage: boolean;
  nextRetryAt: string | null;
  attempt: number;
}

export interface UpdatePromptTemplatesRequest {
  systemPromptTemplate: string | null;
  workerPromptTemplate: string | null;
}

export interface DispatchSettings {
  autoResumeOnUsageReset: boolean;
}

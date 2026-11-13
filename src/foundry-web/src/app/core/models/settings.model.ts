export type AuthMode = 'api_key' | 'oauth';

export type OAuthStatus = 'NotConfigured' | 'Present' | 'ReLoginNeeded';

export type ApiKeyStatus = 'NotConfigured' | 'Present' | 'Unreadable';

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
  imageTag: string;
  buildEnabled: boolean;
  installDotnet: boolean;
  installAngular: boolean;
  installChromium: boolean;
  installDocker: boolean;
}

export interface OAuthCredentialInfo {
  status: OAuthStatus;
  subscriptionType: string | null;
}

export interface AuthSettings {
  mode: AuthMode;
  apiKeyStatus: ApiKeyStatus;
  /** @deprecated Derive as `apiKeyStatus === 'Present'` instead — `apiKeyConfigured` is a boolean
   *  projection of `apiKeyStatus` that predates the three-state model. */
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

export interface WorkerEnvironment {
  memoryLimitMb: number;
  cpuLimit: number;
  pidsLimit: number;
}

export interface ClaudeCodeSettings {
  additionalDenyRules: string[];
}

export interface ClaudeAccountSummary {
  accountId: string;
  authMode: string;
  oAuthStatus: OAuthStatus;
  apiKeyStatus: ApiKeyStatus;
  subscriptionType: string | null;
  oAuthAccountEmail: string | null;
  oAuthAccountOrgName: string | null;
  nextProbeAt: string | null;
}

/** A provider host whose dispatch is held until a probe reaches it again. */
export interface PausedHost {
  host: string;
  pausedAt: string;
  nextProbeAt: string;
  probeAttempts: number;
}

export interface GlobalSettingsResponse {
  maxConcurrent: number;
  timeoutMinutes: number;
  probeIntervalMinutes: number;
  pollIntervalSeconds: number;
  usageLimitResetsAt: string | null;
  isDispatchPaused: boolean;
  autoResumeOnUsageReset: boolean;
  imageTag: string;
  buildEnabled: boolean;
  installDotnet: boolean;
  installAngular: boolean;
  installChromium: boolean;
  installDocker: boolean;
  imageBuildStatus: ImageBuildStatus;
  lastImageBuildError: string | null;
  hasUsableImage: boolean;
  nextRetryAt: string | null;
  attempt: number;
  allowedProviderHosts: string[];
  memoryLimitMb: number;
  cpuLimit: number;
  pidsLimit: number;
  additionalDenyRules: string[];
  pausedHosts: PausedHost[];
  /** Public https base URL the operator's tunnel forwards to the webhook port (ADR 0085). */
  webhookPublicBaseUrl?: string | null;
  /** Poll interval for repositories whose webhook is active. */
  backstopIntervalMinutes?: number;
}

export interface DispatchSettings {
  autoResumeOnUsageReset: boolean;
}

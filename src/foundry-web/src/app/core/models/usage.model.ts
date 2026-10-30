export interface UsageRateLimit {
  readonly kind: string;
  readonly percent: number;
  readonly resetsAt: string;
  readonly modelDisplayName: string | null;
  /** Anthropic-assigned severity: 'normal' | 'warning' | 'critical' or any future value. */
  readonly severity: string;
  readonly isActive: boolean;
}

export interface UsageExtraCredits {
  readonly isEnabled: boolean;
  readonly monthlyLimit: number;
  readonly usedCredits: number;
  readonly utilization: number;
  readonly currency: string;
}

export interface UsageSnapshot {
  readonly limits: UsageRateLimit[];
  readonly extraUsage: UsageExtraCredits | null;
  readonly capturedAt: string;
  readonly accountEmail: string;
  readonly lastRefreshFailedAt: string | null;
}

/** Severity values known at authoring time. The client must accept unrecognised values (falls back to neutral). */
export const KNOWN_SEVERITY_VALUES = ['normal', 'warning', 'critical'] as const;
export type KnownSeverity = (typeof KNOWN_SEVERITY_VALUES)[number];

export interface UsageRateLimit {
  readonly kind: string;
  readonly percent: number;
  /** Null when the server reports no reset time for the window. */
  readonly resetsAt: string | null;
  readonly modelDisplayName: string | null;
  /** Anthropic-assigned severity: 'normal' | 'warning' | 'critical' or any future value. */
  readonly severity: string;
  readonly isActive: boolean;
}

export interface UsageExtraCredits {
  readonly isEnabled: boolean;
  /** Minor units of currency. Null when the plan reports no monthly limit (amount-only). */
  readonly monthlyLimit: number | null;
  readonly usedCredits: number;
  /** Server-reported utilization, 0-100, when it sends one. */
  readonly utilization: number | null;
  readonly currency: string;
  /** Server-computed share of the monthly limit, 0-100+; null when amount-only. */
  readonly percent: number | null;
  /** Server-computed severity of the credit spend; null when amount-only. */
  readonly severity: string | null;
}

export interface UsageSnapshot {
  readonly limits: UsageRateLimit[];
  readonly extraUsage: UsageExtraCredits | null;
  /**
   * Null when usage is unavailable: no poll has succeeded yet and the last attempt failed
   * (lastRefreshFailedAt is then set and limits is empty).
   */
  readonly capturedAt: string | null;
  readonly accountEmail: string;
  readonly lastRefreshFailedAt: string | null;
  /**
   * Which CLI output the figures came from: 'usageReport' (/usage) or 'probeTurn' (the fallback).
   * Null when usage is unavailable.
   */
  readonly source: UsageSnapshotSource | null;
}

/** Known values of {@link UsageSnapshot.source}. */
export type UsageSnapshotSource = 'usageReport' | 'probeTurn';

/** Severity values known at authoring time. The client must accept unrecognised values (falls back to neutral). */
export const KNOWN_SEVERITY_VALUES = ['normal', 'warning', 'critical'] as const;
export type KnownSeverity = (typeof KNOWN_SEVERITY_VALUES)[number];

/** Body of POST /api/credentials/usage/refresh — never carries a snapshot. */
export interface UsageRefreshResponse {
  /** 'started' | 'inFlight' (202), 'current' (200), 'unavailable' | 'loginActive' (409). */
  readonly status: string;
}

/** Client-side state of the popover's Refresh action. */
export type UsageRefreshState = 'idle' | 'refreshing' | 'upToDate' | 'unavailable' | 'error';

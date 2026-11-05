import type { TokenTotal } from '../workers/run-totals.model';

const SUB_CENT_THRESHOLD = 0.005;

export function formatCost(value: number): string {
  if (value === 0) {
    return '$0.00';
  }
  if (value > 0 && value < SUB_CENT_THRESHOLD) {
    return '<$0.01';
  }
  return `$${value.toFixed(2)}`;
}

export function formatDuration(ms: number): string {
  const seconds = Math.floor(ms / 1000);
  const minutes = Math.floor(seconds / 60);
  const hours = Math.floor(minutes / 60);

  if (hours > 0) {
    return `${hours}h ${minutes % 60}m`;
  }
  if (minutes > 0) {
    return `${minutes}m ${seconds % 60}s`;
  }
  return `${seconds}s`;
}

export function formatTokens(value: number): string {
  return new Intl.NumberFormat('en-US').format(value);
}

/** Rendered for a token total no contributing run knows. */
export const UNKNOWN_TOKENS = '-';

/** Prefix marking a token total as a lower bound. */
export const LOWER_BOUND_PREFIX = '≥ ';

/**
 * How a token total reads:
 * - `none`: no run contributed (renders as a plain zero);
 * - `exact`: every contributing run is known;
 * - `partial`: some runs are known and some unknown (a lower bound);
 * - `unknown`: every contributing run is unknown.
 */
export type TokenTotalKind = 'none' | 'exact' | 'partial' | 'unknown';

export function tokenTotalKind(tokens: TokenTotal): TokenTotalKind {
  if (tokens.unknownRunCount === 0) {
    return tokens.knownRunCount === 0 ? 'none' : 'exact';
  }
  return tokens.knownRunCount === 0 ? 'unknown' : 'partial';
}

/** Visible value: `-` when unknown, `≥ N` when partial, otherwise the plain total. */
export function formatTokenTotal(tokens: TokenTotal): string {
  switch (tokenTotalKind(tokens)) {
    case 'unknown':
      return UNKNOWN_TOKENS;
    case 'partial':
      return `${LOWER_BOUND_PREFIX}${formatTokens(tokens.total)}`;
    default:
      return formatTokens(tokens.total);
  }
}

/** "1 run untracked" / "N runs untracked", or null when every contributing run is known. */
export function untrackedRunsText(tokens: TokenTotal): string | null {
  const count = tokens.unknownRunCount;
  if (count === 0) {
    return null;
  }
  return count === 1 ? '1 run untracked' : `${count} runs untracked`;
}

/**
 * Screen-reader phrasing of a token total, e.g. "at least 1,234 input tokens, 2 runs untracked"
 * when `noun` is "input tokens". A partial total reads as a lower bound, never as exact.
 */
export function describeTokenTotal(tokens: TokenTotal, noun?: string): string {
  const suffix = noun ? ` ${noun}` : '';
  const untracked = untrackedRunsText(tokens);
  switch (tokenTotalKind(tokens)) {
    case 'unknown':
      return `unknown${suffix}, ${untracked}`;
    case 'partial':
      return `at least ${formatTokens(tokens.total)}${suffix}, ${untracked}`;
    default:
      return `${formatTokens(tokens.total)}${suffix}`;
  }
}

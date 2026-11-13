/** The first retries after a drop, in ms; most blips recover on the first, immediate one. */
export const RETRY_DELAYS_MS: readonly number[] = [0, 1_000, 2_000, 5_000, 10_000];
/** After the first minute every retry waits this long, so a restarted server isn't stormed. */
export const STEADY_RETRY_MS = 30_000;
/** The steady delay varies by this fraction either way, so open tabs spread their retries. */
export const STEADY_RETRY_JITTER = 0.2;

/**
 * How long to wait before attempt number `attempt` (0-based) of a retry run: 0, 1, 2, 5 and 10 s,
 * then 30 s ± 20 % for ever. `random` returns a value in [0, 1).
 */
export function retryDelayMs(attempt: number, random: () => number = Math.random): number {
  if (attempt < RETRY_DELAYS_MS.length) {
    return RETRY_DELAYS_MS[attempt];
  }
  return Math.round(
    STEADY_RETRY_MS * (1 - STEADY_RETRY_JITTER + 2 * STEADY_RETRY_JITTER * random()),
  );
}

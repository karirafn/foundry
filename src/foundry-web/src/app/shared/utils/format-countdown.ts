/**
 * Formats a remaining-milliseconds value into a human-readable countdown string.
 * Returns "Nh Nm" when an hour or more remains, "Nm Ns" for minutes, "Ns" for seconds.
 */
export function formatCountdown(remainingMs: number): string {
  const totalSeconds = Math.ceil(remainingMs / 1000);
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;

  if (hours >= 1) {
    return `${hours}h ${minutes}m`;
  }

  if (totalSeconds >= 60) {
    return `${minutes}m ${seconds}s`;
  }

  return `${seconds}s`;
}

export interface SystemStatusResponse {
  dockerAvailable: boolean;
  invalidTokenAccountCount: number;
  /** Server-built banner copy; null when no account has an invalid token. */
  accountTokenMessage: string | null;
  expiringTokenAccountCount: number;
  expiredTokenAccountCount: number;
  /** Server-built banner copy; null when no Valid account's token is expiring soon or expired. */
  accountTokenExpiryMessage: string | null;
}

export const DOCKER_UNAVAILABLE_MESSAGE =
  'Docker is unavailable — worker dispatch and login are paused. Reconnecting automatically…';

export interface SystemStatusResponse {
  dockerAvailable: boolean;
}

export const DOCKER_UNAVAILABLE_MESSAGE =
  'Docker is unavailable — worker dispatch and login are paused. Reconnecting automatically…';

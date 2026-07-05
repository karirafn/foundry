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

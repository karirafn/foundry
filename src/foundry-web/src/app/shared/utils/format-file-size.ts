const UNITS: readonly string[] = ['B', 'KB', 'MB', 'GB', 'TB'];

function roundToTenth(value: number): number {
  return Math.round(value * 10) / 10;
}

/**
 * Formats a byte count for display, base 1024 with one decimal and a trailing ".0" dropped:
 * "940 B", "12.4 KB", "3.1 MB".
 */
export function formatFileSize(bytes: number): string {
  if (bytes < 1024) {
    return `${Math.max(0, Math.round(bytes))} B`;
  }

  let value = bytes;
  let unit = 0;
  // Compare the rounded value, so 1023.99 KB reads as "1 MB" rather than "1024 KB".
  while (unit < UNITS.length - 1 && roundToTenth(value) >= 1024) {
    value /= 1024;
    unit++;
  }

  const rounded = roundToTenth(value);
  return `${Number.isInteger(rounded) ? rounded.toFixed(0) : rounded.toFixed(1)} ${UNITS[unit]}`;
}

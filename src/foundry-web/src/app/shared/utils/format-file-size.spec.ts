import { formatFileSize } from './format-file-size';

describe('formatFileSize', () => {
  it('should show 0 bytes as "0 B"', () => {
    // Arrange / Act / Assert
    expect(formatFileSize(0)).toBe('0 B');
  });

  it('should show sizes below 1 KB in bytes', () => {
    // Arrange / Act / Assert
    expect(formatFileSize(940)).toBe('940 B');
    expect(formatFileSize(1023)).toBe('1023 B');
  });

  it('should drop a trailing .0 at an exact kilobyte', () => {
    // Arrange / Act / Assert
    expect(formatFileSize(1024)).toBe('1 KB');
  });

  it('should show kilobytes with one decimal, base 1024', () => {
    // Arrange / Act / Assert
    expect(formatFileSize(1536)).toBe('1.5 KB');
    expect(formatFileSize(12_698)).toBe('12.4 KB');
  });

  it('should show megabytes with one decimal, base 1024', () => {
    // Arrange / Act / Assert
    expect(formatFileSize(3_250_586)).toBe('3.1 MB');
    expect(formatFileSize(50_541_363)).toBe('48.2 MB');
    expect(formatFileSize(2 * 1024 * 1024)).toBe('2 MB');
  });

  it('should move to the next unit when rounding reaches 1024', () => {
    // Arrange / Act / Assert
    expect(formatFileSize(1024 * 1024 - 1)).toBe('1 MB');
  });
});

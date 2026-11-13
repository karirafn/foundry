import { retryDelayMs } from './retry-schedule';

describe('retryDelayMs', () => {
  it('should wait 0, 1, 2, 5 and 10 s for the first five attempts', () => {
    // Act
    const delays = [0, 1, 2, 3, 4].map((attempt) => retryDelayMs(attempt, () => 0.5));

    // Assert
    expect(delays).toEqual([0, 1_000, 2_000, 5_000, 10_000]);
  });

  it('should wait 30 s for every later attempt, without a limit', () => {
    // Act
    const delays = [5, 6, 50, 5_000].map((attempt) => retryDelayMs(attempt, () => 0.5));

    // Assert
    expect(delays).toEqual([30_000, 30_000, 30_000, 30_000]);
  });

  it('should jitter the steady delay by 20 % either way', () => {
    // Act
    const shortest = retryDelayMs(5, () => 0);
    const longest = retryDelayMs(5, () => 0.999_999);

    // Assert
    expect(shortest).toBe(24_000);
    expect(longest).toBe(36_000);
  });
});

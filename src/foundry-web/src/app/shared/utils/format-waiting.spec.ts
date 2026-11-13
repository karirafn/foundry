import { formatWaiting } from './format-waiting';

describe('formatWaiting', () => {
  it.each([
    [-5_000, 'just now'],
    [0, 'just now'],
    [59_999, 'just now'],
    [60_000, '1m'],
    [12 * 60_000 + 30_000, '12m'],
    [59 * 60_000, '59m'],
    [60 * 60_000, '1h'],
    [3 * 3_600_000 + 59 * 60_000, '3h'],
    [24 * 3_600_000, '1d'],
    [2 * 24 * 3_600_000 + 5 * 3_600_000, '2d'],
  ])('formats %d ms as %s', (ms, text) => {
    expect(formatWaiting(ms)).toBe(text);
  });
});

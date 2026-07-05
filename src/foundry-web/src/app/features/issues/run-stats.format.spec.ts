import { formatCost, formatDuration, formatTokens } from './run-stats.format';

describe('run-stats format helpers', () => {
  // --- formatCost ---

  describe('formatCost', () => {
    it('should format 0 as $0.00', () => {
      // Arrange / Act / Assert
      expect(formatCost(0)).toBe('$0.00');
    });

    it('should format a value less than 0.005 as <$0.01', () => {
      // Arrange / Act / Assert
      expect(formatCost(0.004)).toBe('<$0.01');
    });

    it('should format 0.005 as $0.01 (at the sub-cent threshold boundary)', () => {
      // Arrange / Act / Assert
      expect(formatCost(0.005)).toBe('$0.01');
    });

    it('should format 1.239 as $1.24', () => {
      // Arrange / Act / Assert
      expect(formatCost(1.239)).toBe('$1.24');
    });

    it('should format 12.5 as $12.50', () => {
      // Arrange / Act / Assert
      expect(formatCost(12.5)).toBe('$12.50');
    });
  });

  // --- formatDuration ---

  describe('formatDuration', () => {
    it('should format 0ms as 0s', () => {
      // Arrange / Act / Assert
      expect(formatDuration(0)).toBe('0s');
    });

    it('should format 45000ms as 45s', () => {
      // Arrange / Act / Assert
      expect(formatDuration(45000)).toBe('45s');
    });

    it('should format 90000ms as 1m 30s', () => {
      // Arrange / Act / Assert
      expect(formatDuration(90000)).toBe('1m 30s');
    });

    it('should format 3660000ms as 1h 1m', () => {
      // Arrange / Act / Assert
      expect(formatDuration(3660000)).toBe('1h 1m');
    });
  });

  // --- formatTokens ---

  describe('formatTokens', () => {
    it('should format 0 as "0"', () => {
      // Arrange / Act / Assert
      expect(formatTokens(0)).toBe('0');
    });

    it('should format 1000 with thousands separator', () => {
      // Arrange / Act / Assert
      expect(formatTokens(1000)).toBe('1,000');
    });

    it('should format 12345 as "12,345"', () => {
      // Arrange / Act / Assert
      expect(formatTokens(12345)).toBe('12,345');
    });

    it('should format 1000000 as "1,000,000"', () => {
      // Arrange / Act / Assert
      expect(formatTokens(1_000_000)).toBe('1,000,000');
    });

    it('should format 999 as "999" (no separator needed)', () => {
      // Arrange / Act / Assert
      expect(formatTokens(999)).toBe('999');
    });
  });
});

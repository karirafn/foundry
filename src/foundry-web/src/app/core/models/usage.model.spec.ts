import { UsageSnapshot, UsageRateLimit, UsageExtraCredits } from './usage.model';

// Authoritative fixture — shaped like the real C# STJ camelCase serialization
// from the GET /api/credentials/usage integration test (step 7).
const FIXTURE_SNAPSHOT = {
  accountEmail: 'user@example.com',
  capturedAt: '2025-01-15T12:00:00+00:00',
  lastRefreshFailedAt: null,
  extraUsage: null,
  limits: [
    {
      kind: 'session',
      percent: 42.5,
      resetsAt: '2025-01-15T17:00:00+00:00',
      modelDisplayName: null,
      severity: 'normal',
      isActive: true,
    },
    {
      kind: 'weekly_all',
      percent: 71.0,
      resetsAt: '2025-01-20T00:00:00+00:00',
      modelDisplayName: null,
      severity: 'warning',
      isActive: false,
    },
    {
      kind: 'weekly_scoped',
      percent: 15.3,
      resetsAt: '2025-01-20T00:00:00+00:00',
      modelDisplayName: 'claude-opus-4',
      severity: 'normal',
      isActive: false,
    },
  ],
} as const satisfies UsageSnapshot;

const FIXTURE_WITH_EXTRA_USAGE = {
  accountEmail: 'user@example.com',
  capturedAt: '2025-01-15T12:00:00+00:00',
  lastRefreshFailedAt: '2025-01-15T11:55:00+00:00',
  extraUsage: {
    isEnabled: true,
    monthlyLimit: 50000,
    usedCredits: 16568,
    utilization: 0.33136,
    currency: 'USD',
  },
  limits: [],
} as const satisfies UsageSnapshot;

describe('UsageSnapshot interface', () => {
  // Cycle 1: fixture narrows to UsageSnapshot and property access is correct
  it('should accept a null-extra-usage payload and expose accountEmail', () => {
    // Arrange
    const snapshot: UsageSnapshot = FIXTURE_SNAPSHOT;

    // Act
    const email = snapshot.accountEmail;

    // Assert
    expect(email).toBe('user@example.com');
  });

  // Cycle 2: capturedAt and lastRefreshFailedAt (null) properties are accessible
  it('should expose capturedAt as a string and null lastRefreshFailedAt', () => {
    // Arrange
    const snapshot: UsageSnapshot = FIXTURE_SNAPSHOT;

    // Act
    const capturedAt = snapshot.capturedAt;
    const failedAt = snapshot.lastRefreshFailedAt;

    // Assert
    expect(capturedAt).toBe('2025-01-15T12:00:00+00:00');
    expect(failedAt).toBeNull();
  });

  // Cycle 3: limits array maps kind, percent, resetsAt, modelDisplayName, severity, isActive
  it('should expose limits with the expected shape', () => {
    // Arrange
    const snapshot: UsageSnapshot = FIXTURE_SNAPSHOT;

    // Act
    const sessionLimit = snapshot.limits[0];

    // Assert
    expect(sessionLimit.kind).toBe('session');
    expect(sessionLimit.percent).toBe(42.5);
    expect(sessionLimit.resetsAt).toBe('2025-01-15T17:00:00+00:00');
    expect(sessionLimit.modelDisplayName).toBeNull();
    expect(sessionLimit.severity).toBe('normal');
    expect(sessionLimit.isActive).toBe(true);
  });

  // Cycle 4: limits with non-null modelDisplayName (weekly_scoped)
  it('should expose modelDisplayName as a string for weekly_scoped limits', () => {
    // Arrange
    const snapshot: UsageSnapshot = FIXTURE_SNAPSHOT;

    // Act
    const scopedLimit = snapshot.limits[2];

    // Assert
    expect(scopedLimit.kind).toBe('weekly_scoped');
    expect(scopedLimit.modelDisplayName).toBe('claude-opus-4');
  });

  // Cycle 5: extraUsage populated — all fields present
  it('should expose extraUsage fields when present', () => {
    // Arrange
    const snapshot: UsageSnapshot = FIXTURE_WITH_EXTRA_USAGE;

    // Act
    const extra = snapshot.extraUsage as UsageExtraCredits;

    // Assert
    expect(extra.isEnabled).toBe(true);
    expect(extra.monthlyLimit).toBe(50000);
    expect(extra.usedCredits).toBe(16568);
    expect(extra.utilization).toBeCloseTo(0.33136);
    expect(extra.currency).toBe('USD');
  });

  // Cycle 6: lastRefreshFailedAt as a non-null string
  it('should expose lastRefreshFailedAt as a string when set', () => {
    // Arrange
    const snapshot: UsageSnapshot = FIXTURE_WITH_EXTRA_USAGE;

    // Act
    const failedAt = snapshot.lastRefreshFailedAt;

    // Assert
    expect(failedAt).toBe('2025-01-15T11:55:00+00:00');
  });

  // Cycle 7: severity accepts unrecognised values (string, not a union)
  it('should accept an unrecognised severity value without type error', () => {
    // Arrange
    const limit: UsageRateLimit = {
      kind: 'session',
      percent: 10,
      resetsAt: '2025-01-20T00:00:00+00:00',
      modelDisplayName: null,
      severity: 'future-unknown-severity',
      isActive: false,
    };

    // Act
    const severity = limit.severity;

    // Assert
    expect(severity).toBe('future-unknown-severity');
  });
});

import {
  AccountSummary,
  accountProblem,
  accountStatusTag,
  isTokenExpiryWarning,
  resolveTokenExpiry,
} from './account.model';

const EXPIRES_AT = '2026-12-01T00:00:00+00:00';

const ACCOUNT: AccountSummary = {
  id: '00000000-0000-0000-0000-000000000001',
  name: 'alice',
  providerType: 'gitlab',
  baseUrl: 'https://gitlab.com',
  hasToken: true,
  tokenStatus: 'present',
  deniedRepositories: [],
  tokenInvalidSince: null,
  nonConformingToken: null,
  namespaces: [],
  tokenExpiry: { status: 'notChecked', expiresAt: null },
};

function withExpiry(status: string, expiresAt: string | null, overrides: Partial<AccountSummary> = {}): AccountSummary {
  return { ...ACCOUNT, ...overrides, tokenExpiry: { status, expiresAt } };
}

describe('account.model token expiry', () => {
  describe('resolveTokenExpiry', () => {
    it('should read a missing field as notChecked', () => {
      // Act
      const expiry = resolveTokenExpiry(undefined);

      // Assert
      expect(expiry).toEqual({ status: 'notChecked' });
    });

    it('should read an unknown status as notChecked', () => {
      // Act
      const expiry = resolveTokenExpiry({ status: 'soon-ish', expiresAt: EXPIRES_AT });

      // Assert
      expect(expiry).toEqual({ status: 'notChecked' });
    });

    it('should read a dated status with a null date as notChecked', () => {
      // Act
      const expiry = resolveTokenExpiry({ status: 'expiringSoon', expiresAt: null });

      // Assert
      expect(expiry).toEqual({ status: 'notChecked' });
    });

    it('should keep the date of a dated status', () => {
      // Act
      const expiry = resolveTokenExpiry({ status: 'current', expiresAt: EXPIRES_AT });

      // Assert
      expect(expiry).toEqual({ status: 'current', expiresAt: EXPIRES_AT });
    });

    it('should pass an undated status through', () => {
      // Act
      const expiry = resolveTokenExpiry({ status: 'never', expiresAt: null });

      // Assert
      expect(expiry).toEqual({ status: 'never' });
    });
  });

  describe('isTokenExpiryWarning', () => {
    it.each([
      ['expiringSoon', true],
      ['expired', true],
      ['current', false],
    ] as const)('should return %s → %s', (status, warning) => {
      // Act
      const result = isTokenExpiryWarning({ status, expiresAt: EXPIRES_AT });

      // Assert
      expect(result).toBe(warning);
    });
  });

  describe('accountStatusTag', () => {
    it('should tag an expired token "Token expired" in the warning tone', () => {
      // Act
      const tag = accountStatusTag(withExpiry('expired', EXPIRES_AT));

      // Assert
      expect(tag).toEqual({ kind: 'token-expired', label: 'Token expired', tone: 'warning' });
    });

    it('should tag an expiring token "Token expires soon" in the warning tone', () => {
      // Act
      const tag = accountStatusTag(withExpiry('expiringSoon', EXPIRES_AT));

      // Assert
      expect(tag).toEqual({ kind: 'token-expiring', label: 'Token expires soon', tone: 'warning' });
    });

    it.each(['notChecked', 'unavailable', 'never'])('should not tag a %s expiry', status => {
      // Act
      const tag = accountStatusTag(withExpiry(status, null));

      // Assert
      expect(tag).toBeNull();
    });

    it('should not tag a current expiry', () => {
      // Act
      const tag = accountStatusTag(withExpiry('current', EXPIRES_AT));

      // Assert
      expect(tag).toBeNull();
    });

    it.each([
      ['invalid', 'token-invalid'],
      ['unreadable', 'token-unreadable'],
      ['absent', 'not-configured'],
    ])('should show only the %s token tag when the expiry has passed', (tokenStatus, kind) => {
      // Act
      const tag = accountStatusTag(withExpiry('expired', EXPIRES_AT, { tokenStatus, hasToken: tokenStatus !== 'absent' }));

      // Assert
      expect(tag?.kind).toBe(kind);
    });
  });

  describe('accountProblem', () => {
    it('should ask to replace an expired token, naming the provider', () => {
      // Act
      const problem = accountProblem(withExpiry('expired', EXPIRES_AT));

      // Assert
      expect(problem).toEqual({
        kind: 'token',
        tone: 'warning',
        expiry: { status: 'expired', expiresAt: EXPIRES_AT },
        message: 'GitLab can reject it at any time, which pauses monitoring.',
        actionLabel: 'Replace token',
      });
    });

    it('should ask to replace an expiring token before it expires', () => {
      // Act
      const problem = accountProblem(withExpiry('expiringSoon', EXPIRES_AT));

      // Assert
      expect(problem).toEqual({
        kind: 'token',
        tone: 'warning',
        expiry: { status: 'expiringSoon', expiresAt: EXPIRES_AT },
        message: 'Enter a replacement token before it expires.',
        actionLabel: 'Replace token',
      });
    });

    it('should report no problem for a current expiry', () => {
      // Act
      const problem = accountProblem(withExpiry('current', EXPIRES_AT));

      // Assert
      expect(problem).toBeNull();
    });

    it.each(['invalid', 'unreadable', 'absent'])('should not show the expiry in the %s token problem', tokenStatus => {
      // Act
      const problem = accountProblem(withExpiry('expired', EXPIRES_AT, { tokenStatus, hasToken: tokenStatus !== 'absent' }));

      // Assert
      expect(problem?.expiry).toBeUndefined();
    });
  });
});

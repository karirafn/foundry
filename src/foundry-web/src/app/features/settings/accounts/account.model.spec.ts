import {
  AccountSummary,
  accountProblem,
  accountStatusTag,
  affectedStatusLabel,
  resolveBranchRulesTokenStatus,
  resolveTokenStatus,
} from './account.model';

const ACCOUNT: AccountSummary = {
  id: '00000000-0000-0000-0000-000000000001',
  name: 'my-github',
  providerType: 'GitHub',
  baseUrl: 'https://github.com',
  hasToken: true,
  tokenStatus: 'present',
  branchRulesTokenStatus: 'absent',
  tokenInvalidSince: null,
  namespaces: [],
};

describe('account.model', () => {
  it('should pass the invalid token status through', () => {
    // Act
    const status = resolveTokenStatus({ ...ACCOUNT, tokenStatus: 'invalid', tokenInvalidSince: '2026-11-12T15:04:05Z' });

    // Assert
    expect(status).toBe('invalid');
  });

  it('should read a stray invalid branch-rules token status as absent', () => {
    // Act
    const status = resolveBranchRulesTokenStatus({ ...ACCOUNT, branchRulesTokenStatus: 'invalid' });

    // Assert
    expect(status).toBe('absent');
  });

  it('should label the credential-invalid affected status "Token invalid"', () => {
    // Act
    const label = affectedStatusLabel('credential-invalid');

    // Assert
    expect(label).toBe('Token invalid');
  });

  describe('accountStatusTag', () => {
    it.each([
      ['present', null],
      ['absent', { kind: 'not-configured', label: 'Not configured', tone: 'neutral' }],
      ['unreadable', { kind: 'token-unreadable', label: 'Token unreadable', tone: 'warning' }],
      ['invalid', { kind: 'token-invalid', label: 'Token invalid', tone: 'error' }],
    ])('should map token status %s to its tag', (tokenStatus, expected) => {
      // Act
      const tag = accountStatusTag({ ...ACCOUNT, tokenStatus });

      // Assert
      expect(tag).toEqual(expected);
    });

    it('should read a missing token status without a token as not configured', () => {
      // Arrange
      const account: Partial<AccountSummary> = { ...ACCOUNT, hasToken: false };
      delete account.tokenStatus;

      // Act
      const tag = accountStatusTag(account as AccountSummary);

      // Assert
      expect(tag?.kind).toBe('not-configured');
    });
  });

  describe('accountProblem', () => {
    it.each(['present', 'absent'])('should return no problem for token status %s', tokenStatus => {
      // Act
      const problem = accountProblem({ ...ACCOUNT, tokenStatus });

      // Assert
      expect(problem).toBeNull();
    });

    it('should ask to re-enter an unreadable token', () => {
      // Act
      const problem = accountProblem({ ...ACCOUNT, tokenStatus: 'unreadable' });

      // Assert
      expect(problem).toEqual({
        tone: 'warning',
        message: "The account token can't be decrypted. Re-enter it on the account to resume.",
        actionLabel: 'Re-enter token',
      });
    });

    it('should ask for a new token when the provider rejected it', () => {
      // Act
      const problem = accountProblem({ ...ACCOUNT, tokenStatus: 'invalid' });

      // Assert
      expect(problem).toEqual({
        tone: 'error',
        message: 'The provider rejected the account token. Enter a new token on the account to resume.',
        actionLabel: 'Enter new token',
      });
    });
  });
});

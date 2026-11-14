import {
  AccountSummary,
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
});

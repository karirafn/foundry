import {
  AccountSummary,
  DeniedRepositorySummary,
  MissingPermissionsProblem,
  accountProblem,
  accountStatusTag,
} from './account.model';

const RECHECK =
  "Foundry re-checks these repositories every hour; a repository's Re-check button checks it right away.";

const GITHUB_ACCOUNT: AccountSummary = {
  id: '00000000-0000-0000-0000-000000000001',
  name: 'octocat',
  providerType: 'github',
  baseUrl: 'https://github.com',
  hasToken: true,
  tokenStatus: 'present',
  branchRulesTokenStatus: 'absent',
  deniedRepositories: [],
  tokenInvalidSince: null,
  namespaces: ['octocat'],
  tokenExpiry: { status: 'current', expiresAt: '2027-06-01T00:00:00Z' },
};

const GITLAB_ACCOUNT: AccountSummary = {
  ...GITHUB_ACCOUNT,
  name: 'gl-user',
  providerType: 'gitlab',
  baseUrl: 'https://gitlab.example.com',
};

function denied(slug: string, ...missingPermissions: string[]): DeniedRepositorySummary {
  return { id: `id-${slug}`, slug, missingPermissions };
}

function permissionsProblem(account: AccountSummary): MissingPermissionsProblem {
  const problem = accountProblem(account);
  if (problem?.kind !== 'missing-permissions') {
    throw new Error(`Expected a missing-permissions problem, got ${JSON.stringify(problem)}`);
  }
  return problem;
}

describe('account.model missing permissions', () => {
  describe('accountStatusTag', () => {
    it('should tag a present token with denied repositories as missing permissions', () => {
      // Act
      const tag = accountStatusTag({ ...GITHUB_ACCOUNT, deniedRepositories: [denied('o/a', 'Administration (read and write)')] });

      // Assert
      expect(tag).toEqual({ kind: 'missing-permissions', label: 'Missing permissions', tone: 'warning' });
    });

    it('should prefer missing permissions over an expiry tag', () => {
      // Act
      const tag = accountStatusTag({
        ...GITHUB_ACCOUNT,
        tokenExpiry: { status: 'expiringSoon', expiresAt: '2026-11-25T00:00:00Z' },
        deniedRepositories: [denied('o/a', 'Administration (read and write)')],
      });

      // Assert
      expect(tag?.kind).toBe('missing-permissions');
    });

    it.each([
      ['absent', 'not-configured'],
      ['unreadable', 'token-unreadable'],
      ['invalid', 'token-invalid'],
    ])('should keep the %s token tag ahead of missing permissions', (tokenStatus, kind) => {
      // Act
      const tag = accountStatusTag({
        ...GITHUB_ACCOUNT,
        tokenStatus,
        hasToken: tokenStatus !== 'absent',
        deniedRepositories: [denied('o/a', 'Administration (read and write)')],
      });

      // Assert
      expect(tag?.kind).toBe(kind);
    });

    it('should read a response without denied repositories as healthy', () => {
      // Arrange
      const account: Partial<AccountSummary> = { ...GITHUB_ACCOUNT };
      delete account.deniedRepositories;

      // Act
      const tag = accountStatusTag(account as AccountSummary);

      // Assert
      expect(tag).toBeNull();
    });
  });

  describe('accountProblem', () => {
    it('should describe a GitHub fine-grained token missing Administration and Webhooks', () => {
      // Act
      const problem = permissionsProblem({
        ...GITHUB_ACCOUNT,
        deniedRepositories: [
          denied('octocat/api', 'Administration (read and write)', 'Webhooks (read and write)'),
          denied('octocat/web', 'Administration (read and write)', 'Webhooks (read and write)'),
        ],
      });

      // Assert
      expect(problem).toEqual({
        kind: 'missing-permissions',
        tone: 'warning',
        message:
          "This token is missing permissions Foundry needs, so Foundry won't start work on 2 repositories until it has them.",
        missingPermissions: ['Administration (read and write)', 'Webhooks (read and write)'],
        repositories: [
          denied('octocat/api', 'Administration (read and write)', 'Webhooks (read and write)'),
          denied('octocat/web', 'Administration (read and write)', 'Webhooks (read and write)'),
        ],
        remedy: 'github-fine-grained',
        remedyText: `Edit the token's permissions on GitHub, or create a new token, then re-check. ${RECHECK}`,
        editToken: { url: 'https://github.com/settings/personal-access-tokens', host: 'github.com' },
      });
    });

    it('should use the singular for one repository', () => {
      // Act
      const problem = permissionsProblem({
        ...GITHUB_ACCOUNT,
        deniedRepositories: [denied('octocat/api', 'Administration (read and write)')],
      });

      // Assert
      expect(problem.message).toBe(
        "This token is missing permissions Foundry needs, so Foundry won't start work on 1 repository until it has them.",
      );
    });

    it('should merge the missing labels of every repository in permission-list order', () => {
      // Act
      const problem = permissionsProblem({
        ...GITHUB_ACCOUNT,
        deniedRepositories: [
          denied('o/a', 'Contents (read and write)', 'Webhooks (read and write)'),
          denied('o/b', 'Administration (read and write)', 'Webhooks (read and write)', 'Actions (read)'),
        ],
      });

      // Assert
      expect(problem.missingPermissions).toEqual([
        'Contents (read and write)',
        'Administration (read and write)',
        'Webhooks (read and write)',
        'Actions (read)',
      ]);
    });

    it('should point a GitHub Enterprise token at its own host', () => {
      // Act
      const problem = permissionsProblem({
        ...GITHUB_ACCOUNT,
        baseUrl: 'https://ghe.example.com/',
        deniedRepositories: [denied('o/a', 'Administration (read and write)')],
      });

      // Assert
      expect(problem.editToken).toEqual({
        url: 'https://ghe.example.com/settings/personal-access-tokens',
        host: 'ghe.example.com',
      });
    });

    it('should give a classic GitHub token missing the admin role the classic remedy', () => {
      // Act
      const problem = permissionsProblem({
        ...GITHUB_ACCOUNT,
        deniedRepositories: [denied('o/a', 'Admin role on the repository')],
      });

      // Assert
      expect(problem.remedy).toBe('github-classic');
      expect(problem.remedyText).toBe(
        `Ask a repository admin to give the token's user, octocat, the Admin role on these repositories, then re-check. ${RECHECK}`,
      );
      expect(problem.editToken).toBeNull();
    });

    it('should give a GitLab token below Maintainer the GitLab remedy', () => {
      // Act
      const problem = permissionsProblem({
        ...GITLAB_ACCOUNT,
        deniedRepositories: [denied('group/app', 'Maintainer role')],
      });

      // Assert
      expect(problem.remedy).toBe('gitlab');
      expect(problem.missingPermissions).toEqual(['Maintainer role']);
      expect(problem.remedyText).toBe(
        `Give the token's user, gl-user, the Maintainer role on these repositories, then re-check. ${RECHECK}`,
      );
      expect(problem.editToken).toBeNull();
    });

    it('should carry the expiry when it also warrants a warning', () => {
      // Act
      const problem = permissionsProblem({
        ...GITHUB_ACCOUNT,
        tokenExpiry: { status: 'expired', expiresAt: '2026-11-01T00:00:00Z' },
        deniedRepositories: [denied('o/a', 'Administration (read and write)')],
      });

      // Assert
      expect(problem.expiry).toEqual({ status: 'expired', expiresAt: '2026-11-01T00:00:00Z' });
    });

    it('should leave out a current expiry', () => {
      // Act
      const problem = permissionsProblem({
        ...GITHUB_ACCOUNT,
        deniedRepositories: [denied('o/a', 'Administration (read and write)')],
      });

      // Assert
      expect(problem.expiry).toBeUndefined();
    });

    it.each(['absent', 'unreadable', 'invalid'])(
      'should keep the %s token problem ahead of missing permissions',
      (tokenStatus) => {
        // Act
        const problem = accountProblem({
          ...GITHUB_ACCOUNT,
          tokenStatus,
          hasToken: tokenStatus !== 'absent',
          deniedRepositories: [denied('o/a', 'Administration (read and write)')],
        });

        // Assert
        expect(problem?.kind ?? null).not.toBe('missing-permissions');
      },
    );
  });
});

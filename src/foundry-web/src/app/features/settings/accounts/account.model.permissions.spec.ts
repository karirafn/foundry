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
  deniedRepositories: [],
  tokenInvalidSince: null,
  nonConformingToken: null,
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

    it('should tag a present token of the wrong kind as wrong token kind', () => {
      // Act
      const tag = accountStatusTag({
        ...GITHUB_ACCOUNT,
        nonConformingToken: { wrongKind: 'github-classic', notAllowed: [], broadReach: [] },
        deniedRepositories: [denied('o/a')],
      });

      // Assert
      expect(tag).toEqual({ kind: 'wrong-token-kind', label: 'Wrong token kind', tone: 'warning' });
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
        reasons: [],
        tokenLink: { url: 'https://github.com/settings/personal-access-tokens', host: 'github.com', mode: 'edit' },
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
          denied('o/b', 'Administration (read and write)', 'Webhooks (read and write)', 'Actions (read and write)'),
        ],
      });

      // Assert
      expect(problem.missingPermissions).toEqual([
        'Contents (read and write)',
        'Administration (read and write)',
        'Webhooks (read and write)',
        'Actions (read and write)',
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
      expect(problem.tokenLink).toEqual({
        url: 'https://ghe.example.com/settings/personal-access-tokens',
        host: 'ghe.example.com',
        mode: 'edit',
      });
    });

    it('should give a GitHub token of the wrong kind its own message, reason, remedy and create link', () => {
      // Act
      const problem = permissionsProblem({
        ...GITHUB_ACCOUNT,
        nonConformingToken: { wrongKind: 'github-classic', notAllowed: [], broadReach: [] },
        deniedRepositories: [denied('o/a'), denied('o/b')],
      });

      // Assert
      expect(problem.remedy).toBe('github-wrong-kind');
      expect(problem.message).toBe(
        "This token doesn't meet Foundry's requirements, so Foundry won't start work on 2 repositories until it does.",
      );
      expect(problem.reasons).toEqual(['Wrong token kind: classic token.']);
      expect(problem.missingPermissions).toEqual([]);
      expect(problem.remedyText).toBe(
        `Create a fine-grained personal access token on GitHub, then replace this one. A classic token can't be converted. ${RECHECK}`,
      );
      expect(problem.tokenLink).toEqual({
        url: 'https://github.com/settings/personal-access-tokens/new',
        host: 'github.com',
        mode: 'create',
      });
    });

    it('should read an unknown wrong kind generically', () => {
      // Act
      const problem = permissionsProblem({
        ...GITHUB_ACCOUNT,
        nonConformingToken: { wrongKind: 'github-something-new', notAllowed: [], broadReach: [] },
        deniedRepositories: [denied('o/a')],
      });

      // Assert
      expect(problem.reasons).toEqual(['Wrong token kind: not a fine-grained personal access token.']);
    });

    it('should prepend the Maintainer sentence to the GitLab token remedy when the role is missing', () => {
      // Act
      const problem = permissionsProblem({
        ...GITLAB_ACCOUNT,
        deniedRepositories: [denied('group/app', 'Maintainer role')],
      });

      // Assert
      expect(problem.remedy).toBe('gitlab-token');
      expect(problem.missingPermissions).toEqual(['Maintainer role']);
      expect(problem.remedyText).toBe(
        "Give the token's user, gl-user, the Maintainer role on these repositories, then re-check. "
          + 'Create a fine-grained token on GitLab with exactly the required permissions, on all groups and projects or on selected projects only, '
          + `then replace this one. ${RECHECK}`,
      );
      expect(problem.tokenLink).toEqual({
        url: 'https://gitlab.example.com/-/user_settings/personal_access_tokens',
        host: 'gitlab.example.com',
        mode: 'create',
      });
    });

    it('should list a GitLab token reaching too much and holding extra permissions as reasons', () => {
      // Act
      const account: AccountSummary = {
        ...GITLAB_ACCOUNT,
        deniedRepositories: [denied('group/app'), denied('group/lib')],
        nonConformingToken: {
          wrongKind: null,
          notAllowed: ['Webhook (Integrations): Test', 'delete_project'],
          broadReach: ['all-projects', 'instance'],
        },
      };
      const problem = permissionsProblem(account);

      // Assert
      expect(accountStatusTag(account)?.label).toBe('Token reaches too much');
      expect(problem.message).toBe(
        "This token doesn't meet Foundry's requirements, so Foundry won't start work on 2 repositories until it does.",
      );
      expect(problem.reasons).toEqual([
        'Reaches: all projects; the instance.',
        'Not allowed: Webhook (Integrations): Test; delete_project.',
      ]);
      expect(problem.remedyText).toBe(
        'Create a fine-grained token on GitLab with exactly the required permissions, on all groups and projects or on selected projects only, '
          + `then replace this one. ${RECHECK}`,
      );
    });

    it('should tag a GitLab token holding only extra permissions as Extra permissions', () => {
      // Act
      const tag = accountStatusTag({
        ...GITLAB_ACCOUNT,
        deniedRepositories: [denied('group/app')],
        nonConformingToken: { wrongKind: null, notAllowed: ['delete_project'], broadReach: [] },
      });

      // Assert
      expect(tag?.label).toBe('Extra permissions');
    });

    it.each([
      ['gitlab-legacy', 'Wrong token kind: legacy personal access token.'],
      ['gitlab-resource-token', 'Wrong token kind: group or project access token.'],
    ])('should name the %s wrong kind and point at a new token', (wrongKind, reason) => {
      // Act
      const account: AccountSummary = {
        ...GITLAB_ACCOUNT,
        deniedRepositories: [denied('group/app')],
        nonConformingToken: { wrongKind, notAllowed: [], broadReach: ['instance'] },
      };
      const problem = permissionsProblem(account);

      // Assert
      expect(accountStatusTag(account)?.label).toBe('Wrong token kind');
      expect(problem.reasons[0]).toBe(reason);
      expect(problem.remedy).toBe('gitlab-token');
    });

    it('should tell an old GitLab to upgrade, with no token link', () => {
      // Act
      const problem = permissionsProblem({
        ...GITLAB_ACCOUNT,
        deniedRepositories: [denied('group/app')],
        nonConformingToken: { wrongKind: 'gitlab-version', notAllowed: [], broadReach: [] },
      });

      // Assert
      expect(problem.reasons).toEqual(['Wrong token kind: GitLab older than 19.5.']);
      expect(problem.remedy).toBe('gitlab-version');
      expect(problem.remedyText).toBe(`Upgrade GitLab to 19.5 or later, then re-check. ${RECHECK}`);
      expect(problem.tokenLink).toBeNull();
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

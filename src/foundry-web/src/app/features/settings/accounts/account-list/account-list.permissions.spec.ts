import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AccountSummary, DeniedRepositorySummary } from '../account.model';
import { AccountListComponent } from './account-list';

const NOW = new Date(2026, 10, 17, 12, 0);

const ADMIN = 'Administration (read and write)';
const WEBHOOKS = 'Webhooks (read and write)';
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
  tokenExpiry: { status: 'never', expiresAt: null },
};

const GITLAB_ACCOUNT: AccountSummary = {
  ...GITHUB_ACCOUNT,
  id: '00000000-0000-0000-0000-000000000002',
  name: 'gl-user',
  providerType: 'gitlab',
  baseUrl: 'https://gitlab.example.com',
};

function denied(index: number, ...missingPermissions: string[]): DeniedRepositorySummary {
  return { id: `00000000-0000-0000-0000-00000000010${index}`, slug: `octocat/repo-${index}`, missingPermissions };
}

function setup(accounts: AccountSummary[]) {
  const fixture = TestBed.createComponent(AccountListComponent);
  fixture.componentRef.setInput('accounts', accounts);
  fixture.detectChanges();
  return { fixture, component: fixture.componentInstance, el: fixture.nativeElement as HTMLElement };
}

function text(el: Element | null | undefined): string | undefined {
  return el?.textContent?.replace(/\s+/g, ' ').trim();
}

function problemLines(el: HTMLElement): (string | undefined)[] {
  return Array.from(el.querySelectorAll('.account-list__problem .account-list__problem-text'), (line) => text(line));
}

function actions(el: HTMLElement): HTMLElement[] {
  return Array.from(el.querySelectorAll<HTMLElement>('.account-list__problem-actions > *'));
}

describe('AccountListComponent missing permissions', () => {
  beforeEach(async () => {
    vi.useFakeTimers();
    vi.setSystemTime(NOW);
    await TestBed.configureTestingModule({
      imports: [AccountListComponent],
      providers: [provideRouter([])],
    }).compileComponents();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('should name the missing permissions, list the repositories and offer the GitHub edit link', () => {
    // Act
    const { el } = setup([
      { ...GITHUB_ACCOUNT, deniedRepositories: [denied(1, ADMIN, WEBHOOKS), denied(2, ADMIN, WEBHOOKS)] },
    ]);

    // Assert
    expect(text(el.querySelector('.account-list__status--side'))).toBe('Missing permissions');
    const block = el.querySelector('.account-list__problem')!;
    expect(block.classList).toContain('account-list__problem--warning');
    expect(block.getAttribute('role')).toBe('group');
    expect(block.getAttribute('aria-label')).toBe('Token permissions for octocat');
    expect(problemLines(el)).toEqual([
      "This token is missing permissions Foundry needs, so Foundry won't start work on 2 repositories until it has them.",
      'Missing: Administration (read and write); Webhooks (read and write).',
      'Repositories: octocat/repo-1, octocat/repo-2',
      `Edit the token's permissions on GitHub, or create a new token, then re-check. ${RECHECK}`,
    ]);
    const links = Array.from(block.querySelectorAll<HTMLAnchorElement>('.account-list__repo-link'));
    expect(links.map((link) => link.getAttribute('href'))).toEqual([
      '/settings/repositories/00000000-0000-0000-0000-000000000101',
      '/settings/repositories/00000000-0000-0000-0000-000000000102',
    ]);
  });

  it('should make the edit-token link primary and Replace token secondary on a fine-grained token', () => {
    // Act
    const { el } = setup([{ ...GITHUB_ACCOUNT, deniedRepositories: [denied(1, ADMIN)] }]);

    // Assert
    const [link, replace] = actions(el);
    expect(link.tagName).toBe('A');
    expect(link.getAttribute('href')).toBe('https://github.com/settings/personal-access-tokens');
    expect(link.getAttribute('target')).toBe('_blank');
    expect(link.getAttribute('rel')).toBe('noopener noreferrer');
    expect(link.classList).toContain('account-list__btn--primary');
    expect(text(link)).toBe('Edit token on github.com ↗(opens in a new tab)');
    expect(link.querySelector('[aria-hidden="true"]')?.textContent).toBe('↗');
    expect(replace.tagName).toBe('BUTTON');
    expect(replace.classList).not.toContain('account-list__btn--primary');
    expect(replace.getAttribute('aria-label')).toBe('Replace token for octocat');
  });

  it('should emit edit when Replace token is clicked', () => {
    // Arrange
    const account = { ...GITHUB_ACCOUNT, deniedRepositories: [denied(1, ADMIN)] };
    const { el, component } = setup([account]);
    const emitted: AccountSummary[] = [];
    component.edit.subscribe((value) => emitted.push(value));

    // Act
    (actions(el).find((action) => action.tagName === 'BUTTON') as HTMLButtonElement).click();

    // Assert
    expect(emitted).toEqual([account]);
  });

  it('should lead with the expiry and make Replace token primary when the token also expires soon', () => {
    // Act
    const { el } = setup([
      {
        ...GITHUB_ACCOUNT,
        tokenExpiry: { status: 'expiringSoon', expiresAt: new Date(2026, 10, 27, 12, 0).toISOString() },
        deniedRepositories: [denied(1, ADMIN)],
      },
    ]);

    // Assert
    expect(problemLines(el)[0]).toBe(
      "Warning: Expires on Nov 27, 2026 (in 10 days). This token is missing permissions Foundry needs, so Foundry won't start work on 1 repository until it has them.",
    );
    const [replace, link] = actions(el);
    expect(replace.tagName).toBe('BUTTON');
    expect(replace.classList).toContain('account-list__btn--primary');
    expect(link.tagName).toBe('A');
    expect(link.classList).not.toContain('account-list__btn--primary');
  });

  it('should give a classic GitHub token the wrong-kind tag, reason, remedy and create link', () => {
    // Act
    const { el } = setup([
      {
        ...GITHUB_ACCOUNT,
        nonConformingToken: { wrongKind: 'github-classic', notAllowed: [], broadReach: [] },
        deniedRepositories: [denied(1)],
      },
    ]);

    // Assert
    expect(text(el.querySelector('.account-list__status--side'))).toBe('Wrong token kind');
    expect(el.querySelector('.account-list__problem')?.getAttribute('aria-label')).toBe('Token permissions for octocat');
    expect(problemLines(el)).toEqual([
      "This token doesn't meet Foundry's requirements, so Foundry won't start work on 1 repository until it does.",
      'Wrong token kind: classic token.',
      'Repositories: octocat/repo-1',
      `Create a fine-grained personal access token on GitHub, then replace this one. A classic token can't be converted. ${RECHECK}`,
    ]);
    const [link, replace] = actions(el);
    expect(link.tagName).toBe('A');
    expect(link.getAttribute('href')).toBe('https://github.com/settings/personal-access-tokens/new');
    expect(link.getAttribute('target')).toBe('_blank');
    expect(link.getAttribute('rel')).toBe('noopener noreferrer');
    expect(link.getAttribute('aria-label')).toBe('Create token on github.com for octocat (opens in a new tab)');
    expect(text(link)).toBe('Create token on github.com ↗(opens in a new tab)');
    expect(replace.tagName).toBe('BUTTON');
  });

  it('should name the Maintainer role and link to a new GitLab token', () => {
    // Act
    const { el } = setup([{ ...GITLAB_ACCOUNT, deniedRepositories: [denied(1, 'Maintainer role')] }]);

    // Assert
    const lines = problemLines(el);
    expect(lines[1]).toBe('Missing: Maintainer role.');
    expect(lines[3]).toBe(
      "Give the token's user, gl-user, the Maintainer role on these repositories, then re-check. "
        + 'Create a fine-grained token on GitLab with exactly the required permissions, on all groups and projects or on selected projects only, '
        + `then replace this one. ${RECHECK}`,
    );
    expect(text(el.querySelector('.account-list__problem-actions a'))).toContain('Create token on');
  });

  it('should show each GitLab reason line in order, then the missing permissions', () => {
    // Act
    const { el } = setup([
      {
        ...GITLAB_ACCOUNT,
        deniedRepositories: [denied(1, 'Webhook (Integrations): Trigger')],
        nonConformingToken: { wrongKind: null, notAllowed: ['test_webhook'], broadReach: ['personal-projects'] },
      },
    ]);

    // Assert
    const lines = problemLines(el);
    expect(text(el.querySelector('.account-list__status--side'))).toBe('Token reaches too much');
    expect(lines.slice(0, 4)).toEqual([
      "This token doesn't meet Foundry's requirements, so Foundry won't start work on 1 repository until it does.",
      'Reaches: your personal projects.',
      'Not allowed: test_webhook.',
      'Missing: Webhook (Integrations): Trigger.',
    ]);
  });

  it('should tell an old GitLab to upgrade, with no token link', () => {
    // Act
    const { el } = setup([
      {
        ...GITLAB_ACCOUNT,
        deniedRepositories: [denied(1)],
        nonConformingToken: { wrongKind: 'gitlab-version', notAllowed: [], broadReach: [] },
      },
    ]);

    // Assert
    expect(problemLines(el)).toContain(`Upgrade GitLab to 19.5 or later, then re-check. ${RECHECK}`);
    expect(el.querySelector('.account-list__problem-actions a')).toBeNull();
  });

  it.each([
    ['unreadable', 'Token unreadable', 'Token status for octocat'],
    ['invalid', 'Token invalid', 'Token status for octocat'],
  ])('should show the %s token problem instead of missing permissions', (tokenStatus, tag, blockLabel) => {
    // Act
    const { el } = setup([{ ...GITHUB_ACCOUNT, tokenStatus, deniedRepositories: [denied(1, ADMIN)] }]);

    // Assert
    expect(text(el.querySelector('.account-list__status--side'))).toBe(tag);
    expect(el.querySelector('.account-list__problem')?.getAttribute('aria-label')).toBe(blockLabel);
    expect(el.querySelector('.account-list__repo-link')).toBeNull();
  });

  it('should show the not-configured tag and no block for an absent token', () => {
    // Act
    const { el } = setup([
      { ...GITHUB_ACCOUNT, tokenStatus: 'absent', hasToken: false, deniedRepositories: [denied(1, ADMIN)] },
    ]);

    // Assert
    expect(text(el.querySelector('.account-list__status--side'))).toBe('Not configured');
    expect(el.querySelector('.account-list__problem')).toBeNull();
  });

  it('should show no block when no repository is denied', () => {
    // Act
    const { el } = setup([GITHUB_ACCOUNT]);

    // Assert
    expect(el.querySelector('.account-list__status')).toBeNull();
    expect(el.querySelector('.account-list__problem')).toBeNull();
  });

  describe('more than three repositories', () => {
    const MANY = { ...GITHUB_ACCOUNT, deniedRepositories: [1, 2, 3, 4, 5].map((index) => denied(index, ADMIN)) };

    it('should show the first three and a "+2 more" toggle', () => {
      // Act
      const { el } = setup([MANY]);

      // Assert
      expect(el.querySelectorAll('.account-list__repo-link')).toHaveLength(3);
      const toggle = el.querySelector<HTMLButtonElement>('.account-list__problem .account-list__more-btn')!;
      expect(text(toggle)).toBe('+2 more');
      expect(toggle.getAttribute('aria-expanded')).toBe('false');
      expect(toggle.getAttribute('aria-label')).toBe('+2 more repositories for octocat');
      const controlled = el.querySelector(`#${toggle.getAttribute('aria-controls')}`);
      expect(controlled?.querySelectorAll('.account-list__repo-link')).toHaveLength(3);
    });

    it('should show every repository and "Show fewer" when expanded', () => {
      // Arrange
      const { el, fixture } = setup([MANY]);
      const toggle = el.querySelector<HTMLButtonElement>('.account-list__problem .account-list__more-btn')!;

      // Act
      toggle.click();
      fixture.detectChanges();

      // Assert
      expect(el.querySelectorAll('.account-list__repo-link')).toHaveLength(5);
      expect(text(toggle)).toBe('Show fewer');
      expect(toggle.getAttribute('aria-expanded')).toBe('true');
      expect(toggle.getAttribute('aria-label')).toBe('Show fewer repositories for octocat');
    });

    it('should collapse back to three when toggled twice', () => {
      // Arrange
      const { el, fixture } = setup([MANY]);
      const toggle = el.querySelector<HTMLButtonElement>('.account-list__problem .account-list__more-btn')!;

      // Act
      toggle.click();
      fixture.detectChanges();
      toggle.click();
      fixture.detectChanges();

      // Assert
      expect(el.querySelectorAll('.account-list__repo-link')).toHaveLength(3);
      expect(toggle.getAttribute('aria-expanded')).toBe('false');
    });

    it('should show no toggle for exactly three repositories', () => {
      // Act
      const { el } = setup([{ ...GITHUB_ACCOUNT, deniedRepositories: [1, 2, 3].map((index) => denied(index, ADMIN)) }]);

      // Assert
      expect(el.querySelectorAll('.account-list__repo-link')).toHaveLength(3);
      expect(el.querySelector('.account-list__problem .account-list__more-btn')).toBeNull();
    });
  });
});

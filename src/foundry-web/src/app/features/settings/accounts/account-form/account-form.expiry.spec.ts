import { TestBed } from '@angular/core/testing';
import { AccountFormComponent } from './account-form';
import { AccountSummary, NamespaceConflict, TokenRequirements, TokenValidationResult } from '../account.model';
import { AccountService } from '../account.service';

const MOCK_ACCOUNT: AccountSummary = {
  id: '00000000-0000-0000-0000-000000000001',
  name: 'my-github',
  providerType: 'GitHub',
  baseUrl: 'https://github.com',
  hasToken: true,
  tokenStatus: 'present',
  deniedRepositories: [],
  tokenInvalidSince: null,
  namespaces: [],
  tokenExpiry: { status: 'notChecked', expiresAt: null },
};

const VALID_RESULT: TokenValidationResult = {
  kind: 'authenticated',
  missingScopes: [],
  accountName: 'octocat',
  detectedProvider: null,
  tokenExpiry: { status: 'notChecked', expiresAt: null },
};

const MISSING_SCOPES_RESULT: TokenValidationResult = {
  kind: 'authenticated',
  missingScopes: ['repo', 'workflow'],
  accountName: 'octocat',
  detectedProvider: null,
  tokenExpiry: { status: 'notChecked', expiresAt: null },
};

const SCOPES_UNVERIFIABLE_RESULT: TokenValidationResult = {
  kind: 'scopesUnverifiable',
  missingScopes: [],
  accountName: 'octocat',
  detectedProvider: null,
  tokenExpiry: { status: 'notChecked', expiresAt: null },
};

const GITHUB_REQUIREMENTS: TokenRequirements = {
  providerType: 'github',
  tokenTypeLabel: 'GitHub fine-grained personal access token',
  permissions: [
    { label: 'Contents (read and write)', verification: 'probe' },
    { label: 'Issues (read and write)', verification: 'probe' },
    { label: 'Pull requests (read and write)', verification: 'probe' },
    { label: 'Metadata (read)', verification: 'always-granted' },
    { label: 'Workflows (read and write)', verification: 'unverifiable' },
  ],
  creationUrlTemplate: '{baseUrl}/settings/personal-access-tokens/new?name=Foundry&contents=write&issues=write&pull_requests=write&workflows=write',
  resourceOwnerHint: 'Fine-grained tokens are bound to a single resource owner. To reach an organization\'s repositories, choose that organization as the token\'s resource owner when creating the token.',
};

const GITLAB_REQUIREMENTS: TokenRequirements = {
  providerType: 'gitlab',
  tokenTypeLabel: 'Personal Access Token',
  permissions: [
    { label: 'api', verification: 'scope' },
    { label: 'Maintainer role', verification: 'role' },
  ],
  creationUrlTemplate: '{baseUrl}/-/user_settings/personal_access_tokens',
  resourceOwnerHint: null,
};

class FakeAccountService {
  getTokenRequirements(_provider: string): Promise<TokenRequirements> {
    return Promise.reject(new Error('Not implemented — override via spy'));
  }
}

function makeDefaultRequirementsMap(): Map<string, TokenRequirements> {
  return new Map([
    ['GitHub', GITHUB_REQUIREMENTS],
    ['GitLab', GITLAB_REQUIREMENTS],
  ]);
}

function setup(overrides: {
  account?: AccountSummary | null;
  accounts?: AccountSummary[];
  saving?: boolean;
  validating?: boolean;
  validationResult?: TokenValidationResult | null;
  saveError?: string | null;
  validationError?: string | null;
  conflicts?: NamespaceConflict[];
  requirementsMap?: Map<string, TokenRequirements>;
  requirementsProvider?: (provider: string) => Promise<TokenRequirements>;
} = {}) {
  const requirementsProvider = overrides.requirementsProvider
    ?? ((p: string) => {
      const map = overrides.requirementsMap ?? makeDefaultRequirementsMap();
      const req = map.get(p);
      if (req !== undefined) {
        return Promise.resolve(req);
      }
      return Promise.reject(new Error(`Unknown provider: ${p}`));
    });

  const fakeService = new FakeAccountService();
  const getTokenRequirementsSpy = vi.spyOn(fakeService, 'getTokenRequirements').mockImplementation(requirementsProvider);

  TestBed.overrideProvider(AccountService, { useValue: fakeService });

  const fixture = TestBed.createComponent(AccountFormComponent);
  fixture.componentRef.setInput('account', overrides.account ?? null);
  fixture.componentRef.setInput('accounts', overrides.accounts ?? []);
  fixture.componentRef.setInput('saving', overrides.saving ?? false);
  fixture.componentRef.setInput('validating', overrides.validating ?? false);
  fixture.componentRef.setInput('validationResult', overrides.validationResult ?? null);
  fixture.componentRef.setInput('saveError', overrides.saveError ?? null);
  fixture.componentRef.setInput('validationError', overrides.validationError ?? null);
  fixture.componentRef.setInput('conflicts', overrides.conflicts ?? []);
  fixture.detectChanges();
  return { fixture, component: fixture.componentInstance, el: fixture.nativeElement as HTMLElement, getTokenRequirementsSpy };
}

const NOW = new Date(2026, 10, 17, 12, 0);

function inDays(days: number): string {
  return new Date(NOW.getFullYear(), NOW.getMonth(), NOW.getDate() + days, 12, 0).toISOString();
}

function text(el: Element | null | undefined): string | undefined {
  return el?.textContent?.replace(/\s+/g, ' ').trim();
}

function storedAccount(status: string, expiresAt: string | null): AccountSummary {
  return { ...MOCK_ACCOUNT, tokenExpiry: { status, expiresAt } };
}

function checkToken(validationResult: TokenValidationResult) {
  const rendered = setup({ validationResult });
  const tokenInput = rendered.el.querySelector('#account-form-token') as HTMLInputElement;
  tokenInput.value = 'ghp_test_token';
  tokenInput.dispatchEvent(new Event('input'));
  tokenInput.dispatchEvent(new Event('blur'));
  rendered.fixture.detectChanges();
  return rendered;
}

describe('AccountFormComponent token expiry', () => {
  beforeEach(async () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(NOW);
    await TestBed.configureTestingModule({ imports: [AccountFormComponent] }).compileComponents();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  describe('stored token', () => {
    it('should show the stored expiry as a hint next to the token on file', () => {
      // Act
      const { el } = setup({ account: storedAccount('current', inDays(30)) });

      // Assert
      const line = el.querySelector('#account-form-token-expiry');
      expect(line?.classList).toContain('account-form__field-hint');
      expect(text(line)).toBe('Expires on Dec 17, 2026 (in 30 days)');
    });

    it('should warn about a stored token that expires soon', () => {
      // Act
      const { el } = setup({ account: storedAccount('expiringSoon', inDays(10)) });

      // Assert
      const line = el.querySelector('#account-form-token-expiry');
      expect(line?.classList).toContain('account-form__validation-block');
      expect(line?.querySelector('.account-form__validation-message--warning')).toBeTruthy();
      expect(text(line)).toBe('Warning: Expires on Nov 27, 2026 (in 10 days). Enter a replacement token below before it expires.');
    });

    it('should warn that an expired stored token can be rejected at any time', () => {
      // Act
      const { el } = setup({ account: storedAccount('expired', inDays(-1)) });

      // Assert
      expect(text(el.querySelector('#account-form-token-expiry'))).toBe(
        'Warning: Expired on Nov 16, 2026 (yesterday). GitHub can reject it at any time, which pauses monitoring — enter a replacement token below.',
      );
    });

    it('should describe the token input by the on-file line, then the expiry, then the hint', () => {
      // Act
      const { el } = setup({ account: storedAccount('current', inDays(30)) });

      // Assert
      const describedBy = el.querySelector('#account-form-token')?.getAttribute('aria-describedby');
      expect(describedBy?.startsWith('account-form-token-on-file account-form-token-expiry account-form-token-hint')).toBe(true);
    });

    it('should show no stored expiry for an invalid token', () => {
      // Act
      const { el } = setup({
        account: { ...storedAccount('expired', inDays(-1)), tokenStatus: 'invalid', tokenInvalidSince: inDays(-1) },
      });

      // Assert
      expect(el.querySelector('#account-form-token-expiry')).toBeNull();
    });
  });

  describe('token check', () => {
    it('should show the checked token expiry under "Authenticated as"', () => {
      // Act
      const { el } = checkToken({ ...VALID_RESULT, tokenExpiry: { status: 'current', expiresAt: inDays(90) } });

      // Assert
      const blocks = el.querySelectorAll('#account-token-validation .account-form__validation-block');
      expect(blocks.length).toBe(2);
      expect(text(blocks[1])).toBe('Expires on Feb 15, 2027 (in 90 days)');
    });

    it('should show "never expires" for a token without an expiry', () => {
      // Act
      const { el } = checkToken({ ...VALID_RESULT, tokenExpiry: { status: 'never', expiresAt: null } });

      // Assert
      expect(text(el.querySelector('#account-token-validation'))).toContain('Never expires');
    });

    it('should flag a checked token that expires soon', () => {
      // Act
      const { el } = checkToken({ ...VALID_RESULT, tokenExpiry: { status: 'expiringSoon', expiresAt: inDays(3) } });

      // Assert
      const blocks = el.querySelectorAll('#account-token-validation .account-form__validation-block');
      expect(blocks[1].querySelector('.account-form__validation-dot--warning')).toBeTruthy();
      expect(text(blocks[1])).toBe(
        'Warning: Expires on Nov 20, 2026 (in 3 days) — pick a longer-lived token to avoid replacing it again soon',
      );
    });

    it('should show the expiry of a token whose scopes cannot be verified', () => {
      // Act
      const { el } = checkToken({ ...SCOPES_UNVERIFIABLE_RESULT, tokenExpiry: { status: 'unavailable', expiresAt: null } });

      // Assert
      expect(text(el.querySelector('#account-token-validation'))).toContain('GitHub reported no expiry for this token');
    });

    it('should hide the expiry when it was not checked', () => {
      // Act
      const { el } = checkToken(VALID_RESULT);

      // Assert
      expect(el.querySelectorAll('#account-token-validation .account-form__validation-block').length).toBe(1);
    });

    it('should hide the expiry when scopes are missing', () => {
      // Act
      const { el } = checkToken({ ...MISSING_SCOPES_RESULT, tokenExpiry: { status: 'current', expiresAt: inDays(90) } });

      // Assert
      expect(el.querySelector('#account-token-validation fd-token-expiry')).toBeNull();
    });
  });
});

import { TestBed } from '@angular/core/testing';
import { AccountFormComponent } from './account-form';
import { AccountSummary, CreateAccountRequest, NamespaceConflict, TokenRequirements, TokenValidationResult, UpdateAccountRequest } from '../account.model';
import { AccountService } from '../account.service';

const MOCK_ACCOUNT: AccountSummary = {
  id: '00000000-0000-0000-0000-000000000001',
  name: 'my-github',
  providerType: 'GitHub',
  baseUrl: 'https://github.com',
  hasToken: true,
  namespaces: [],
};

const MOCK_ACCOUNT_2: AccountSummary = {
  id: '00000000-0000-0000-0000-000000000002',
  name: 'work-github',
  providerType: 'GitHub',
  baseUrl: 'https://github.com',
  hasToken: true,
  namespaces: [],
};

const VALID_RESULT: TokenValidationResult = {
  isValid: true,
  isAuthFailure: false,
  missingScopes: [],
  accountName: 'octocat',
};

const AUTH_FAIL_RESULT: TokenValidationResult = {
  isValid: false,
  isAuthFailure: true,
  missingScopes: [],
  accountName: null,
};

const MISSING_SCOPES_RESULT: TokenValidationResult = {
  isValid: false,
  isAuthFailure: false,
  missingScopes: ['repo', 'workflow'],
  accountName: null,
};

const VALID_NULL_IDENTITY_RESULT: TokenValidationResult = {
  isValid: true,
  isAuthFailure: false,
  missingScopes: [],
  accountName: null,
};

const GITHUB_REQUIREMENTS: TokenRequirements = {
  providerType: 'github',
  tokenTypeLabel: 'GitHub fine-grained personal access token',
  scopes: ['Contents (read and write)', 'Issues (read and write)', 'Pull requests (read and write)', 'Workflows (write)', 'Metadata (read)'],
  creationUrlTemplate: '{baseUrl}/settings/personal-access-tokens/new?name=Foundry&contents=write&issues=write&pull_requests=write&workflows=write',
  resourceOwnerHint: 'Select a resource owner (your user or an organization) to scope access.',
};

const GITLAB_REQUIREMENTS: TokenRequirements = {
  providerType: 'gitlab',
  tokenTypeLabel: 'Personal Access Token',
  scopes: ['api'],
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

describe('AccountFormComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AccountFormComponent],
    }).compileComponents();
  });

  // Cycle 1: add mode renders heading "Add Account"
  it('should render "Add Account" heading in add mode', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({ account: null });

    // Assert
    const heading = el.querySelector('.account-form__heading');
    expect(heading?.textContent?.trim()).toBe('Add Account');
  });

  // Cycle 2: edit mode renders heading "Edit Account"
  it('should render "Edit Account" heading in edit mode', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({ account: MOCK_ACCOUNT });

    // Assert
    const heading = el.querySelector('.account-form__heading');
    expect(heading?.textContent?.trim()).toBe('Edit Account');
  });

  // Cycle 3: cancel link is rendered
  it('should render a cancel link', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup();

    // Assert
    const cancelLink = el.querySelector('.account-form__cancel-link');
    expect(cancelLink).toBeTruthy();
    expect(cancelLink?.textContent).toContain('Cancel');
  });

  // Cycle 4: cancel link emits cancel event
  it('should emit cancel when cancel link is clicked', () => {
    // Arrange
    const { el, component } = setup();
    let emitted = false;
    component.cancel.subscribe(() => { emitted = true; });

    // Act
    const cancelLink = el.querySelector('.account-form__cancel-link') as HTMLElement;
    cancelLink.click();

    // Assert
    expect(emitted).toBe(true);
  });

  // Cycle 5: Name field absent in create mode
  it('should NOT render a name field in add mode', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({ account: null });

    // Assert
    const nameInput = el.querySelector('#account-form-name');
    expect(nameInput).toBeNull();
    const nameLabel = el.querySelector('label[for="account-form-name"]');
    expect(nameLabel).toBeNull();
  });

  // Cycle 6: base URL field rendered with label
  it('should render the base URL field with label and input', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup();

    // Assert
    const label = el.querySelector('label[for="account-form-base-url"]');
    expect(label).toBeTruthy();
    const input = el.querySelector('#account-form-base-url') as HTMLInputElement;
    expect(input).toBeTruthy();
  });

  // Cycle 7: base URL defaults to https://github.com in add mode
  it('should default base URL to https://github.com in add mode', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({ account: null });

    // Assert
    const input = el.querySelector('#account-form-base-url') as HTMLInputElement;
    expect(input.value).toBe('https://github.com');
  });

  // Cycle 8: base URL pre-filled from account in edit mode
  it('should pre-fill base URL from account in edit mode', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({ account: MOCK_ACCOUNT });

    // Assert
    const input = el.querySelector('#account-form-base-url') as HTMLInputElement;
    expect(input.value).toBe('https://github.com');
  });

  // Cycle 9: provider selector component is shown in add mode
  it('should show fd-provider-selector in add mode', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({ account: null });

    // Assert
    const selector = el.querySelector('fd-provider-selector');
    expect(selector).toBeTruthy();
    const radios = el.querySelectorAll('input[type="radio"]');
    expect(radios.length).toBe(2);
  });

  // Cycle 10: changing provider to GitLab updates the base URL
  it('should update base URL to https://gitlab.com when GitLab is selected', () => {
    // Arrange
    const { el, fixture } = setup({ account: null });

    // Act
    const radios = el.querySelectorAll('input[type="radio"]') as NodeListOf<HTMLInputElement>;
    const gitlabRadio = Array.from(radios).find((r) => r.value === 'GitLab')!;
    gitlabRadio.click();
    fixture.detectChanges();

    // Assert
    const baseUrlInput = el.querySelector('#account-form-base-url') as HTMLInputElement;
    expect(baseUrlInput.value).toBe('https://gitlab.com');
  });

  // Cycle 11: edit mode shows provider badge, not selector
  it('should show provider badge in edit mode, not the selector', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({ account: MOCK_ACCOUNT });

    // Assert
    const badge = el.querySelector('.account-form__provider-badge');
    expect(badge).toBeTruthy();
    expect(badge?.textContent?.trim()).toContain('GitHub');
    const selector = el.querySelector('fd-provider-selector');
    expect(selector).toBeNull();
  });

  // Cycle 12: token field label "Token" in create mode
  it('should label the token field "Token" in add mode', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({ account: null });

    // Assert
    const label = el.querySelector('label[for="account-form-token"]');
    expect(label?.textContent?.trim()).toBe('Token');
  });

  // Cycle 13: token field label "Replace token" in edit mode
  it('should label the token field "Replace token" in edit mode', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({ account: MOCK_ACCOUNT });

    // Assert
    const label = el.querySelector('label[for="account-form-token"]');
    expect(label?.textContent?.trim()).toBe('Replace token');
  });

  // Cycle 14: edit mode token hint shown
  it('should show "Leave empty to keep the current token" hint in edit mode', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({ account: MOCK_ACCOUNT });

    // Assert
    const hint = el.querySelector('#account-form-token-hint');
    expect(hint?.textContent).toContain('Leave empty to keep the current token');
  });

  // Cycle 15: toggle visibility button shows/hides token
  it('should toggle token input between password and text on visibility button click', () => {
    // Arrange
    const { el, fixture } = setup();
    const input = el.querySelector('#account-form-token') as HTMLInputElement;
    expect(input.type).toBe('password');

    // Act
    const toggleBtn = el.querySelector('.account-form__toggle-visibility-btn') as HTMLButtonElement;
    toggleBtn.click();
    fixture.detectChanges();

    // Assert
    expect(input.type).toBe('text');
  });

  // Cycle 16: no "Validate Token" button (replaced by auto-resolve)
  it('should NOT render the validate token button', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup();

    // Assert
    const btn = el.querySelector('.account-form__validate-btn');
    expect(btn).toBeNull();
  });

  // Cycle 17: token blur with non-empty token + baseUrl emits validateToken
  it('should emit validateToken with token and baseUrl when token input loses focus', () => {
    // Arrange
    const { el, component, fixture } = setup();
    let emitted: { token: string; baseUrl: string } | undefined;
    component.validateToken.subscribe((v: { token: string; baseUrl: string }) => { emitted = v; });

    const tokenInput = el.querySelector('#account-form-token') as HTMLInputElement;
    tokenInput.value = 'ghp_test_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Act
    tokenInput.dispatchEvent(new Event('blur'));

    // Assert
    expect(emitted).toEqual({ token: 'ghp_test_token', baseUrl: 'https://github.com' });
  });

  // Cycle 18: token blur with empty token does NOT emit
  it('should not emit validateToken when token is empty on blur', () => {
    // Arrange
    const { el, component } = setup();
    let emitted: unknown;
    component.validateToken.subscribe((v: unknown) => { emitted = v; });

    const tokenInput = el.querySelector('#account-form-token') as HTMLInputElement;

    // Act — blur with no value
    tokenInput.dispatchEvent(new Event('blur'));

    // Assert
    expect(emitted).toBeUndefined();
  });

  // Cycle 19: paste on token field emits validateToken on next tick
  it('should emit validateToken after paste on token field (next tick, baseUrl present)', async () => {
    // Arrange
    const { el, component, fixture } = setup();
    let emitted: { token: string; baseUrl: string } | undefined;
    component.validateToken.subscribe((v: { token: string; baseUrl: string }) => { emitted = v; });

    const tokenInput = el.querySelector('#account-form-token') as HTMLInputElement;
    tokenInput.value = 'ghp_pasted_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Act
    tokenInput.dispatchEvent(new Event('paste'));
    await new Promise(resolve => setTimeout(resolve, 0));
    fixture.detectChanges();

    // Assert
    expect(emitted).toEqual({ token: 'ghp_pasted_token', baseUrl: 'https://github.com' });
  });

  // Cycle 20: resolving state — live region shows "Resolving identity…"
  it('should show "Resolving identity…" in the live region while validating', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({ validating: true });

    // Assert
    const region = el.querySelector('#account-token-validation');
    expect(region?.textContent).toContain('Resolving identity…');
  });

  // Cycle 21: idle state — token wrapper has no aria-busy
  it('should not set aria-busy on token wrapper (role-less div, inert attribute)', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({ validating: false });

    // Assert
    const wrapper = el.querySelector('.account-form__token-wrapper') as HTMLElement;
    expect(wrapper.getAttribute('aria-busy')).toBeNull();
  });

  // Cycle 22: status region always present
  it('should render the status region at all times', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({ validationResult: null });

    // Assert
    const region = el.querySelector('#account-token-validation');
    expect(region).toBeTruthy();
    expect(region?.getAttribute('role')).toBe('status');
    expect(region?.getAttribute('aria-live')).toBe('polite');
  });

  // Cycle 23: resolving state shows "Resolving identity…"
  it('should show "Resolving identity…" in the status region while validating', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({ validating: true });

    // Assert
    const region = el.querySelector('#account-token-validation');
    expect(region?.textContent).toContain('Resolving identity…');
  });

  // Cycle 24: authenticated state shows green dot + "Authenticated as {name}"
  it('should show "Authenticated as {name}" with green dot when token is valid and has accountName', () => {
    // Arrange
    const { el, fixture } = setup({ validationResult: VALID_RESULT });
    const tokenInput = el.querySelector('#account-form-token') as HTMLInputElement;
    tokenInput.value = 'ghp_test_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Act — blur triggers resolution, setting _lastResolvedPair
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    // Assert
    const dot = el.querySelector('.account-form__validation-dot--valid');
    expect(dot).toBeTruthy();
    const msg = el.querySelector('.account-form__validation-message--valid');
    expect(msg?.textContent).toContain('Authenticated as octocat');
  });

  // Cycle 25: auth-failure state
  it('should show auth failure message with error dot', () => {
    // Arrange
    const { el, fixture } = setup({ validationResult: AUTH_FAIL_RESULT });
    const tokenInput = el.querySelector('#account-form-token') as HTMLInputElement;
    tokenInput.value = 'bad_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Act
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    // Assert
    const dot = el.querySelector('.account-form__validation-dot--error');
    expect(dot).toBeTruthy();
    const msg = el.querySelector('.account-form__validation-message--error');
    expect(msg?.textContent).toContain('Authentication failed — check that the token is correct');
  });

  // Cycle 26: missing scopes state
  it('should show missing scopes message with warning dot', () => {
    // Arrange
    const { el, fixture } = setup({ validationResult: MISSING_SCOPES_RESULT });
    const tokenInput = el.querySelector('#account-form-token') as HTMLInputElement;
    tokenInput.value = 'limited_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Act
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    // Assert
    const dot = el.querySelector('.account-form__validation-dot--warning');
    expect(dot).toBeTruthy();
    const msg = el.querySelector('.account-form__validation-message--warning');
    expect(msg?.textContent).toContain('Missing required scopes: repo, workflow');
  });

  // Cycle 27: valid-but-null identity state
  it('should show error message when token valid but accountName is null', () => {
    // Arrange
    const { el, fixture } = setup({ validationResult: VALID_NULL_IDENTITY_RESULT });
    const tokenInput = el.querySelector('#account-form-token') as HTMLInputElement;
    tokenInput.value = 'anon_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Act
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    // Assert
    const dot = el.querySelector('.account-form__validation-dot--error');
    expect(dot).toBeTruthy();
    const msg = el.querySelector('.account-form__validation-message--error');
    expect(msg?.textContent).toContain('Token is valid, but the account identity could not be resolved from the provider');
  });

  // Cycle 28: idle status region empty when validationResult null and not validating
  it('should show empty status region when validationResult is null and not validating', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({ validationResult: null, validating: false });

    // Assert
    const region = el.querySelector('#account-token-validation');
    expect(region?.textContent?.trim()).toBeFalsy();
  });

  // Cycle 29: validation error (network) is shown in the alert region
  it('should show validation error when validationError is set', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({ validationError: 'Token validation failed' });

    // Assert
    const errorEl = el.querySelector('.account-form__validation-error');
    expect(errorEl).toBeTruthy();
    expect(errorEl?.getAttribute('role')).toBe('alert');
    expect(errorEl?.textContent).toContain('Token validation failed');
  });

  // Cycle 30: save error is shown
  it('should show save error when saveError is set', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({ saveError: 'Something went wrong' });

    // Assert
    const errorEl = el.querySelector('.account-form__save-error');
    expect(errorEl).toBeTruthy();
    expect(errorEl?.getAttribute('role')).toBe('alert');
    expect(errorEl?.textContent).toContain('Something went wrong');
  });

  // Cycle 31: save button rendered
  it('should render the save button', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup();

    // Assert
    const btn = el.querySelector('.account-form__save-btn');
    expect(btn).toBeTruthy();
    expect(btn?.textContent?.trim()).toBe('Save');
  });

  // Cycle 32: save disabled in create mode without valid resolution
  it('should disable save button in add mode without a valid resolved accountName', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({ account: null, validationResult: null });

    // Assert
    const btn = el.querySelector('.account-form__save-btn') as HTMLButtonElement;
    expect(btn.disabled).toBe(true);
  });

  // Cycle 33: save disabled in create mode when auth failed
  it('should disable save button in add mode when auth failed', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({ account: null, validationResult: AUTH_FAIL_RESULT });

    // Assert
    const btn = el.querySelector('.account-form__save-btn') as HTMLButtonElement;
    expect(btn.disabled).toBe(true);
  });

  // Cycle 34: save disabled in create mode when valid but null identity
  it('should disable save button in add mode when identity could not be resolved', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({ account: null, validationResult: VALID_NULL_IDENTITY_RESULT });

    // Assert
    const btn = el.querySelector('.account-form__save-btn') as HTMLButtonElement;
    expect(btn.disabled).toBe(true);
  });

  // Cycle 35: save enabled in create mode with valid result and accountName
  it('should enable save button in add mode when valid and accountName is resolved', () => {
    // Arrange
    const { el, fixture } = setup({ account: null, validationResult: VALID_RESULT });

    // Act — set token input and blur to trigger resolution
    const tokenInput = el.querySelector('#account-form-token') as HTMLInputElement;
    tokenInput.value = 'ghp_test_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    // Assert
    const btn = el.querySelector('.account-form__save-btn') as HTMLButtonElement;
    expect(btn.disabled).toBe(false);
  });

  // Cycle 36: save disabled while saving
  it('should disable save button when saving is true', () => {
    // Arrange
    const { el, fixture } = setup({ account: null, saving: true, validationResult: VALID_RESULT });
    const tokenInput = el.querySelector('#account-form-token') as HTMLInputElement;
    tokenInput.value = 'ghp_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    // Assert
    const btn = el.querySelector('.account-form__save-btn') as HTMLButtonElement;
    expect(btn.disabled).toBe(true);
  });

  // Cycle 37: save emits CreateAccountRequest without name in add mode
  it('should emit CreateAccountRequest without name on save in add mode', () => {
    // Arrange
    const { el, component, fixture } = setup({ account: null, validationResult: VALID_RESULT });
    let emitted: CreateAccountRequest | UpdateAccountRequest | undefined;
    component.save.subscribe((v: CreateAccountRequest | UpdateAccountRequest) => { emitted = v; });

    const tokenInput = el.querySelector('#account-form-token') as HTMLInputElement;
    tokenInput.value = 'ghp_newtoken';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    // Act
    const saveBtn = el.querySelector('.account-form__save-btn') as HTMLButtonElement;
    saveBtn.click();

    // Assert
    expect(emitted).toEqual({
      providerType: 'GitHub',
      baseUrl: 'https://github.com',
      token: 'ghp_newtoken',
    });
  });

  // Cycle 37b: save emits CreateAccountRequest with GitLab provider when GitLab selected
  it('should emit CreateAccountRequest with GitLab providerType when GitLab is selected', () => {
    // Arrange
    const { el, component, fixture } = setup({ account: null, validationResult: VALID_RESULT });
    let emitted: CreateAccountRequest | UpdateAccountRequest | undefined;
    component.save.subscribe((v: CreateAccountRequest | UpdateAccountRequest) => { emitted = v; });

    const radios = el.querySelectorAll('input[type="radio"]') as NodeListOf<HTMLInputElement>;
    const gitlabRadio = Array.from(radios).find((r) => r.value === 'GitLab')!;
    gitlabRadio.click();
    fixture.detectChanges();

    const tokenInput = el.querySelector('#account-form-token') as HTMLInputElement;
    tokenInput.value = 'glpat_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    // Act
    const saveBtn = el.querySelector('.account-form__save-btn') as HTMLButtonElement;
    saveBtn.click();

    // Assert
    expect((emitted as CreateAccountRequest).providerType).toBe('GitLab');
    expect((emitted as CreateAccountRequest).token).toBe('glpat_token');
    const r = emitted as unknown as Record<string, unknown>;
    expect(r['name']).toBeUndefined();
  });

  // Cycle 38: edit mode — token-on-file panel shown when hasToken + name
  it('should show token-on-file panel in edit mode when account has token', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({ account: MOCK_ACCOUNT });

    // Assert
    const panel = el.querySelector('.account-form__token-on-file');
    expect(panel).toBeTruthy();
    expect(panel?.textContent).toContain('Token on file — authenticated as my-github');
  });

  // Cycle 39: edit mode — token-on-file panel NOT shown when hasToken is false
  it('should not show token-on-file panel when account has no token', () => {
    // Arrange
    const accountWithoutToken: AccountSummary = { ...MOCK_ACCOUNT, hasToken: false };

    // Act
    const { el } = setup({ account: accountWithoutToken });

    // Assert
    const panel = el.querySelector('.account-form__token-on-file');
    expect(panel).toBeNull();
  });

  // Cycle 40: save enabled in edit mode with no new token (name unchanged)
  it('should enable save button in edit mode when no new token is entered', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({ account: MOCK_ACCOUNT });

    // Assert
    const btn = el.querySelector('.account-form__save-btn') as HTMLButtonElement;
    expect(btn.disabled).toBe(false);
  });

  // Cycle 41: save emits UpdateAccountRequest without name, without token (edit mode, no new token)
  it('should emit UpdateAccountRequest without name and without token when no replacement token entered', () => {
    // Arrange
    const { el, component } = setup({ account: MOCK_ACCOUNT });
    let emitted: CreateAccountRequest | UpdateAccountRequest | undefined;
    component.save.subscribe((v: CreateAccountRequest | UpdateAccountRequest) => { emitted = v; });

    // Act
    const saveBtn = el.querySelector('.account-form__save-btn') as HTMLButtonElement;
    saveBtn.click();

    // Assert
    expect(emitted).toEqual({
      baseUrl: 'https://github.com',
    });
    const r = emitted as unknown as Record<string, unknown>;
    expect(r['name']).toBeUndefined();
    expect(r['token']).toBeUndefined();
  });

  // Cycle 42: edit mode with new token includes token in UpdateAccountRequest
  it('should include token in UpdateAccountRequest when replacement token entered', () => {
    // Arrange
    const { el, component, fixture } = setup({ account: MOCK_ACCOUNT, validationResult: VALID_RESULT });
    let emitted: CreateAccountRequest | UpdateAccountRequest | undefined;
    component.save.subscribe((v: CreateAccountRequest | UpdateAccountRequest) => { emitted = v; });

    const tokenInput = el.querySelector('#account-form-token') as HTMLInputElement;
    tokenInput.value = 'ghp_newtoken';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Act — blur triggers resolution, enabling Save
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
    const saveBtn = el.querySelector('.account-form__save-btn') as HTMLButtonElement;
    saveBtn.click();

    // Assert
    expect((emitted as UpdateAccountRequest).token).toBe('ghp_newtoken');
    const r = emitted as unknown as Record<string, unknown>;
    expect(r['name']).toBeUndefined();
  });

  // Cycle 43: edit mode with new token and pending resolution — save disabled
  it('should disable save in edit mode when new token entered but no valid resolution yet', () => {
    // Arrange
    const { el, fixture } = setup({ account: MOCK_ACCOUNT, validationResult: null });
    const tokenInput = el.querySelector('#account-form-token') as HTMLInputElement;
    tokenInput.value = 'ghp_newtoken';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Assert
    const btn = el.querySelector('.account-form__save-btn') as HTMLButtonElement;
    expect(btn.disabled).toBe(true);
  });

  // Cycle 44: duplicate detection in create mode — duplicate warning shown, save disabled
  it('should show duplicate warning and disable save when account with same name+baseUrl exists in create mode', () => {
    // Arrange
    const { el, fixture } = setup({
      account: null,
      accounts: [MOCK_ACCOUNT],
      validationResult: { isValid: true, isAuthFailure: false, missingScopes: [], accountName: 'my-github' },
    });

    const tokenInput = el.querySelector('#account-form-token') as HTMLInputElement;
    tokenInput.value = 'ghp_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Act — blur triggers resolution, making result visible
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    // Assert — duplicate warning visible inside live region
    const region = el.querySelector('#account-token-validation');
    expect(region?.textContent).toContain('An account for "my-github" already exists on github.com');

    // Assert — save disabled
    const btn = el.querySelector('.account-form__save-btn') as HTMLButtonElement;
    expect(btn.disabled).toBe(true);
  });

  // Cycle 45: duplicate detection excludes self in edit mode
  it('should NOT show duplicate warning when the matching account is the account being edited', () => {
    // Arrange — resolves to same name/baseUrl as MOCK_ACCOUNT, but MOCK_ACCOUNT is the account being edited
    const { el, fixture } = setup({
      account: MOCK_ACCOUNT,
      accounts: [MOCK_ACCOUNT, MOCK_ACCOUNT_2],
      validationResult: { isValid: true, isAuthFailure: false, missingScopes: [], accountName: 'my-github' },
    });

    const tokenInput = el.querySelector('#account-form-token') as HTMLInputElement;
    tokenInput.value = 'ghp_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Act — blur triggers resolution, making result visible
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    // Assert — no duplicate warning in live region
    const region = el.querySelector('#account-token-validation');
    expect(region?.textContent).not.toContain('already exists');

    // Assert — save enabled
    const btn = el.querySelector('.account-form__save-btn') as HTMLButtonElement;
    expect(btn.disabled).toBe(false);
  });

  // Cycle 46: live-region divs render unconditionally so screen readers announce changes
  it('should render validation-error div even when validationError is null', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({ validationError: null });

    // Assert
    const errorEl = el.querySelector('.account-form__validation-error');
    expect(errorEl).toBeTruthy();
    expect(errorEl?.getAttribute('role')).toBe('alert');
  });

  it('should render save-error div even when saveError is null', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({ saveError: null });

    // Assert
    const errorEl = el.querySelector('.account-form__save-error');
    expect(errorEl).toBeTruthy();
    expect(errorEl?.getAttribute('role')).toBe('alert');
  });

  // Cycle 47: inner radiogroup inside fd-provider-selector is labelled via aria-labelledby
  it('should associate the Provider label with the inner radiogroup via aria-labelledby', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({ account: null });

    // Assert
    const labelSpan = el.querySelector('#account-form-provider-label');
    expect(labelSpan).toBeTruthy();
    const radiogroup = el.querySelector('[role="radiogroup"]');
    expect(radiogroup?.getAttribute('aria-labelledby')).toBe('account-form-provider-label');
  });

  // Cycle 48: edit mode rename notice shown when resolved identity differs from account.name
  it('should show rename notice in edit mode when resolved identity differs from account name', () => {
    // Arrange
    const { el, fixture } = setup({
      account: MOCK_ACCOUNT,
      validationResult: { isValid: true, isAuthFailure: false, missingScopes: [], accountName: 'new-identity' },
    });

    const tokenInput = el.querySelector('#account-form-token') as HTMLInputElement;
    tokenInput.value = 'ghp_newtoken';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Act — blur triggers resolution, making result visible
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    // Assert
    const region = el.querySelector('#account-token-validation[role="status"]');
    expect(region?.textContent).toContain('Saving will rename this account to "new-identity"');
  });

  // Cycle 49: token input has required attribute in add mode
  it('should have required attribute on token input in add mode', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({ account: null });

    // Assert
    const tokenInput = el.querySelector('#account-form-token') as HTMLInputElement;
    expect(tokenInput.required).toBe(true);
  });

  // Cycle 50: token input does not have required attribute in edit mode
  it('should not have required attribute on token input in edit mode', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({ account: MOCK_ACCOUNT });

    // Assert
    const tokenInput = el.querySelector('#account-form-token') as HTMLInputElement;
    expect(tokenInput.required).toBe(false);
  });

  // Cycle 51: stale validation result is hidden after base URL edit
  it('should hide "Authenticated as" badge after the base URL field is edited', () => {
    // Arrange
    const { el, fixture } = setup({ validationResult: VALID_RESULT });

    // Simulate that token was resolved at old baseUrl: trigger resolution then edit base URL
    const tokenInput = el.querySelector('#account-form-token') as HTMLInputElement;
    tokenInput.value = 'ghp_test_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    // Act — edit base URL to simulate host change
    const baseUrlInput = el.querySelector('#account-form-base-url') as HTMLInputElement;
    baseUrlInput.value = 'https://github.example.com';
    baseUrlInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Assert — stale "Authenticated as" no longer visible
    const region = el.querySelector('#account-token-validation');
    expect(region?.textContent).not.toContain('Authenticated as');
  });

  // Cycle 52: stale validation result is hidden after token edit
  it('should hide "Authenticated as" badge after the token field is edited', () => {
    // Arrange
    const { el, fixture } = setup({ validationResult: VALID_RESULT });

    const tokenInput = el.querySelector('#account-form-token') as HTMLInputElement;
    tokenInput.value = 'ghp_test_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    // Act — change the token value
    tokenInput.value = 'ghp_different_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Assert — stale result no longer shown
    const region = el.querySelector('#account-token-validation');
    expect(region?.textContent).not.toContain('Authenticated as');
  });

  // Cycle 53: duplicate warning is inside the live status region
  it('should render duplicate warning inside the role="status" live region', () => {
    // Arrange
    const { el, fixture } = setup({
      account: null,
      accounts: [MOCK_ACCOUNT],
      validationResult: { isValid: true, isAuthFailure: false, missingScopes: [], accountName: 'my-github' },
    });

    const tokenInput = el.querySelector('#account-form-token') as HTMLInputElement;
    tokenInput.value = 'ghp_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    // Assert — warning text is inside the polite live region
    const region = el.querySelector('#account-token-validation[role="status"]');
    expect(region?.textContent).toContain('already exists on');
  });

  // Cycle 54: rename notice is inside the live status region
  it('should render rename notice inside the role="status" live region', () => {
    // Arrange
    const { el, fixture } = setup({
      account: MOCK_ACCOUNT,
      validationResult: { isValid: true, isAuthFailure: false, missingScopes: [], accountName: 'new-identity' },
    });

    const tokenInput = el.querySelector('#account-form-token') as HTMLInputElement;
    tokenInput.value = 'ghp_newtoken';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    // Assert — notice text is inside the polite live region
    const region = el.querySelector('#account-token-validation[role="status"]');
    expect(region?.textContent).toContain('Saving will rename this account to');
  });

  // Cycle 55: validation-error div has a stable id
  it('should render the validation-error div with id "account-form-validation-error"', () => {
    // Arrange / Act
    const { el } = setup({ validationError: null });

    // Assert
    const errorEl = el.querySelector('#account-form-validation-error');
    expect(errorEl).toBeTruthy();
    expect(errorEl?.getAttribute('role')).toBe('alert');
  });

  // Cycle 56: add mode — requirements block renders token type label and scope chips
  it('should render token requirements block with label and scope chips in add mode', async () => {
    // Arrange
    const { el, fixture } = setup({ account: null });

    // Act — wait for async fetch
    await fixture.whenStable();
    fixture.detectChanges();

    // Assert
    const section = el.querySelector('.account-form__requirements');
    expect(section).toBeTruthy();
    const text = section!.querySelector('.account-form__requirements-text');
    expect(text?.textContent).toContain('Personal Access Token');
    const scopes = section!.querySelectorAll('.account-form__requirements-scope code');
    expect(scopes.length).toBe(1);
    expect(scopes[0].textContent).toBe('repo');
  });

  // Cycle 57: provider radio change refetches requirements and updates displayed label/scope
  it('should refetch and display GitLab requirements when provider changes from GitHub to GitLab', async () => {
    // Arrange
    const { el, fixture } = setup({ account: null });
    await fixture.whenStable();
    fixture.detectChanges();

    // Act — switch to GitLab
    const radios = el.querySelectorAll('input[type="radio"]') as NodeListOf<HTMLInputElement>;
    const gitlabRadio = Array.from(radios).find((r) => r.value === 'GitLab')!;
    gitlabRadio.click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    // Assert
    const scopes = el.querySelectorAll('.account-form__requirements-scope code');
    expect(scopes.length).toBe(1);
    expect(scopes[0].textContent).toBe('api');
  });

  // Cycle 58: edit mode — block shows provider requirements normalized from lowercase stored value
  it('should render requirements block for account provider in edit mode (lowercase normalization)', async () => {
    // Arrange — providerType stored as lowercase 'github'
    const account: AccountSummary = { ...MOCK_ACCOUNT, providerType: 'github' };
    const { el, fixture } = setup({ account });

    // Act
    await fixture.whenStable();
    fixture.detectChanges();

    // Assert
    const section = el.querySelector('.account-form__requirements');
    expect(section).toBeTruthy();
    const text = section!.querySelector('.account-form__requirements-text');
    expect(text?.textContent).toContain('Personal Access Token');
    const scopes = section!.querySelectorAll('.account-form__requirements-scope code');
    expect(scopes.length).toBe(1);
    expect(scopes[0].textContent).toBe('repo');
  });

  // Cycle 59: valid base URL — "Create token" anchor is rendered with correct href
  it('should render "Create token" anchor with substituted href when base URL is valid', async () => {
    // Arrange — GitLab uses {baseUrl} template; account has a GitLab base URL
    const account: AccountSummary = {
      id: '00000000-0000-0000-0000-000000000003',
      name: 'gl-account',
      providerType: 'gitlab',
      baseUrl: 'https://gitlab.example.com',
      hasToken: true,
      namespaces: [],
    };
    const gitlabRequirements: TokenRequirements = {
      ...GITLAB_REQUIREMENTS,
      creationUrlTemplate: '{baseUrl}/-/user_settings/personal_access_tokens',
    };
    const requirementsMap = new Map([
      ['GitHub', GITHUB_REQUIREMENTS],
      ['GitLab', gitlabRequirements],
    ]);
    const { el, fixture } = setup({ account, requirementsMap });

    // Act
    await fixture.whenStable();
    fixture.detectChanges();

    // Assert
    const link = el.querySelector('.account-form__requirements-link') as HTMLAnchorElement;
    expect(link).toBeTruthy();
    expect(link.href).toContain('https://gitlab.example.com/-/user_settings/personal_access_tokens');
    expect(link.target).toBe('_blank');
    expect(link.rel).toContain('noopener');
    expect(link.rel).toContain('noreferrer');
  });

  // Cycle 60: empty/invalid base URL — no anchor rendered; fallback hint text present
  it('should render fallback hint text (no anchor) when base URL is empty', async () => {
    // Arrange — GITHUB_REQUIREMENTS uses {baseUrl} template; empty base URL triggers fallback
    const { el, fixture } = setup({ account: null });
    await fixture.whenStable();
    fixture.detectChanges();

    // Clear base URL the way a user would
    const baseUrlInput = el.querySelector('#account-form-base-url') as HTMLInputElement;
    baseUrlInput.value = '';
    baseUrlInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Assert — no link
    const link = el.querySelector('.account-form__requirements-link');
    expect(link).toBeNull();
    // Fallback hint present
    const hint = el.querySelector('.account-form__requirements-link-hint');
    expect(hint?.textContent).toContain('Enter a Base URL above');
  });

  // Cycle 61: requirements block absent when fetch rejects (unknown provider)
  it('should not render requirements block when fetch rejects', async () => {
    // Arrange
    const { el, fixture } = setup({
      account: null,
      requirementsProvider: () => Promise.reject(new Error('Unknown provider')),
    });

    // Act
    await fixture.whenStable();
    fixture.detectChanges();

    // Assert
    const section = el.querySelector('.account-form__requirements');
    expect(section).toBeNull();
  });

  // Conflict panel — Cycle 62: no conflict panel when conflicts is empty
  it('should not render the conflict panel when there are no conflicts', () => {
    // Arrange / Act
    const { el } = setup({ conflicts: [] });

    // Assert
    const panel = el.querySelector('.account-form__conflict-panel');
    expect(panel).toBeNull();
  });

  // Conflict panel — Cycle 63: conflict panel rendered when conflicts present
  it('should render the conflict panel when conflicts are provided', () => {
    // Arrange
    const conflicts: NamespaceConflict[] = [
      { namespace: 'myorg', holderCredentialId: 'cred-1', holderName: 'Old Account' },
    ];

    // Act
    const { el } = setup({ conflicts });

    // Assert
    const panel = el.querySelector('.account-form__conflict-panel');
    expect(panel).toBeTruthy();
    expect(panel?.getAttribute('role')).toBe('region');
  });

  // Conflict panel — Cycle 64: each conflict has a default-checked checkbox
  it('should render a default-checked checkbox for each conflict namespace', () => {
    // Arrange
    const conflicts: NamespaceConflict[] = [
      { namespace: 'myorg', holderCredentialId: 'cred-1', holderName: 'Old Account' },
      { namespace: 'myorg2', holderCredentialId: 'cred-2', holderName: 'Other Account' },
    ];

    // Act
    const { el } = setup({ conflicts });

    // Assert
    const checkboxes = el.querySelectorAll('.account-form__conflict-panel input[type="checkbox"]');
    expect(checkboxes.length).toBe(2);
    checkboxes.forEach(cb => expect((cb as HTMLInputElement).checked).toBe(true));
  });

  // Conflict panel — Cycle 65: conflict shows namespace and holder name
  it('should display namespace and holder name for each conflict', () => {
    // Arrange
    const conflicts: NamespaceConflict[] = [
      { namespace: 'myorg', holderCredentialId: 'cred-1', holderName: 'Old Account' },
    ];

    // Act
    const { el } = setup({ conflicts });

    // Assert
    const panel = el.querySelector('.account-form__conflict-panel');
    expect(panel?.textContent).toContain('myorg');
    expect(panel?.textContent).toContain('Old Account');
  });

  // Conflict panel — Cycle 66: submit label becomes "Transfer selected & add account" when conflicts present
  it('should show "Transfer selected & add account" label when conflicts are present', () => {
    // Arrange
    const conflicts: NamespaceConflict[] = [
      { namespace: 'myorg', holderCredentialId: 'cred-1', holderName: 'Old Account' },
    ];

    // Act
    const { el } = setup({ conflicts });

    // Assert
    const btn = el.querySelector('.account-form__save-btn');
    expect(btn?.textContent?.trim()).toBe('Transfer selected & add account');
  });

  // Conflict panel — Cycle 67: submit label is "Save" when no conflicts
  it('should show "Save" label when no conflicts are present', () => {
    // Arrange / Act
    const { el } = setup({ conflicts: [] });

    // Assert
    const btn = el.querySelector('.account-form__save-btn');
    expect(btn?.textContent?.trim()).toBe('Save');
  });

  // Conflict panel — Cycle 70: editing token clears conflict panel
  it('should clear conflict panel when token input is edited', () => {
    // Arrange
    const conflicts: NamespaceConflict[] = [
      { namespace: 'myorg', holderCredentialId: 'cred-1', holderName: 'Old Account' },
    ];
    const { el, fixture } = setup({ conflicts });

    // Act
    const tokenInput = el.querySelector('#account-form-token') as HTMLInputElement;
    tokenInput.value = 'new_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Assert
    const panel = el.querySelector('.account-form__conflict-panel');
    expect(panel).toBeNull();
  });

  // Conflict panel — Cycle 71: editing baseUrl clears conflict panel
  it('should clear conflict panel when base URL input is edited', () => {
    // Arrange
    const conflicts: NamespaceConflict[] = [
      { namespace: 'myorg', holderCredentialId: 'cred-1', holderName: 'Old Account' },
    ];
    const { el, fixture } = setup({ conflicts });

    // Act
    const baseUrlInput = el.querySelector('#account-form-base-url') as HTMLInputElement;
    baseUrlInput.value = 'https://github.example.com';
    baseUrlInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Assert
    const panel = el.querySelector('.account-form__conflict-panel');
    expect(panel).toBeNull();
  });

  // Conflict panel — Cycle 72: panel has aria-labelledby
  it('should give the conflict panel an aria-labelledby heading', () => {
    // Arrange
    const conflicts: NamespaceConflict[] = [
      { namespace: 'myorg', holderCredentialId: 'cred-1', holderName: 'Old Account' },
    ];

    // Act
    const { el } = setup({ conflicts });

    // Assert
    const panel = el.querySelector('.account-form__conflict-panel[role="region"]');
    const labelledBy = panel?.getAttribute('aria-labelledby');
    expect(labelledBy).toBeTruthy();
    const heading = el.querySelector(`#${labelledBy}`);
    expect(heading).toBeTruthy();
  });

  // Conflict panel — Cycle 62 (revised): unchecking a conflict omits it from takeoverNamespaces on save
  // Production path: token resolved first, then conflicts pushed in by parent (simulating 409 response)
  it('should omit unchecked namespace from takeoverNamespaces — production path', () => {
    // Arrange — start without conflicts; token resolution happens first
    const conflicts: NamespaceConflict[] = [
      { namespace: 'myorg', holderCredentialId: 'cred-1', holderName: 'Old Account' },
      { namespace: 'myorg2', holderCredentialId: 'cred-2', holderName: 'Other Account' },
    ];
    const { el, component, fixture } = setup({
      conflicts: [],
      account: null,
      validationResult: VALID_RESULT,
    });
    let emitted: CreateAccountRequest | UpdateAccountRequest | undefined;
    component.save.subscribe((v: CreateAccountRequest | UpdateAccountRequest) => { emitted = v; });

    // Resolve token (simulates user typing and blurring token field)
    const tokenInput = el.querySelector('#account-form-token') as HTMLInputElement;
    tokenInput.value = 'ghp_test_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    // Parent pushes conflicts (simulates 409 response from server)
    fixture.componentRef.setInput('conflicts', conflicts);
    fixture.detectChanges();

    // Uncheck the first checkbox
    const checkboxes = el.querySelectorAll('.account-form__conflict-panel input[type="checkbox"]') as NodeListOf<HTMLInputElement>;
    checkboxes[0].click();
    fixture.detectChanges();

    // Act
    const saveBtn = el.querySelector('.account-form__save-btn') as HTMLButtonElement;
    saveBtn.click();

    // Assert
    const req = emitted as CreateAccountRequest;
    expect(req.takeoverNamespaces).toEqual(['myorg2']);
  });

  // Conflict panel — Cycle 63 (revised): all checked sends all takeoverNamespaces — production path
  it('should include all conflict namespaces in takeoverNamespaces when all checked — production path', () => {
    // Arrange — start without conflicts; token resolution happens first
    const conflicts: NamespaceConflict[] = [
      { namespace: 'myorg', holderCredentialId: 'cred-1', holderName: 'Old Account' },
      { namespace: 'myorg2', holderCredentialId: 'cred-2', holderName: 'Other Account' },
    ];
    const { el, component, fixture } = setup({
      conflicts: [],
      account: null,
      validationResult: VALID_RESULT,
    });
    let emitted: CreateAccountRequest | UpdateAccountRequest | undefined;
    component.save.subscribe((v: CreateAccountRequest | UpdateAccountRequest) => { emitted = v; });

    // Resolve token (simulates user typing and blurring token field)
    const tokenInput = el.querySelector('#account-form-token') as HTMLInputElement;
    tokenInput.value = 'ghp_test_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    // Parent pushes conflicts (simulates 409 response from server)
    fixture.componentRef.setInput('conflicts', conflicts);
    fixture.detectChanges();

    // Act — all checkboxes remain checked
    const saveBtn = el.querySelector('.account-form__save-btn') as HTMLButtonElement;
    saveBtn.click();

    // Assert
    const req = emitted as CreateAccountRequest;
    expect(req.takeoverNamespaces).toEqual(['myorg', 'myorg2']);
  });

  // F-1: save button disabled when conflict panel visible but no namespaces selected
  it('should disable save button when conflicts are visible but none are selected', () => {
    // Arrange — token resolution first, then conflicts pushed in
    const conflicts: NamespaceConflict[] = [
      { namespace: 'myorg', holderCredentialId: 'cred-1', holderName: 'Old Account' },
      { namespace: 'myorg2', holderCredentialId: 'cred-2', holderName: 'Other Account' },
    ];
    const { el, fixture } = setup({ conflicts: [], account: null, validationResult: VALID_RESULT });

    // Resolve token via production path
    const tokenInput = el.querySelector('#account-form-token') as HTMLInputElement;
    tokenInput.value = 'ghp_test_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    // Parent pushes conflicts (simulates 409 response)
    fixture.componentRef.setInput('conflicts', conflicts);
    fixture.detectChanges();

    // Uncheck all checkboxes
    const checkboxes = el.querySelectorAll('.account-form__conflict-panel input[type="checkbox"]') as NodeListOf<HTMLInputElement>;
    checkboxes.forEach(cb => { cb.click(); });
    fixture.detectChanges();

    // Assert
    const btn = el.querySelector('.account-form__save-btn') as HTMLButtonElement;
    expect(btn.disabled).toBe(true);
  });

  // F-6: save label accurate when conflicts visible but zero selected
  it('should show "Select namespaces to transfer" label when conflicts visible but none selected', () => {
    // Arrange — token resolution first, then conflicts pushed in
    const conflicts: NamespaceConflict[] = [
      { namespace: 'myorg', holderCredentialId: 'cred-1', holderName: 'Old Account' },
    ];
    const { el, fixture } = setup({ conflicts: [], account: null, validationResult: VALID_RESULT });

    const tokenInput = el.querySelector('#account-form-token') as HTMLInputElement;
    tokenInput.value = 'ghp_test_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    // Parent pushes conflicts (simulates 409 response)
    fixture.componentRef.setInput('conflicts', conflicts);
    fixture.detectChanges();

    // Uncheck the only checkbox
    const checkbox = el.querySelector('.account-form__conflict-panel input[type="checkbox"]') as HTMLInputElement;
    checkbox.click();
    fixture.detectChanges();

    // Assert
    const btn = el.querySelector('.account-form__save-btn') as HTMLButtonElement;
    expect(btn.textContent?.trim()).toBe('Select namespaces to transfer');
  });

  // F-9: conflict checkbox label contains both namespace and holder name
  it('should have a wrapping label whose text contains the namespace and holder name', () => {
    // Arrange
    const conflicts: NamespaceConflict[] = [
      { namespace: 'myorg', holderCredentialId: 'cred-1', holderName: 'Old Account' },
    ];

    // Act
    const { el } = setup({ conflicts });

    // Assert
    const label = el.querySelector('.account-form__conflict-label') as HTMLLabelElement;
    expect(label).toBeTruthy();
    expect(label.textContent).toContain('myorg');
    expect(label.textContent).toContain('Old Account');
  });

  // F-5: conflict panel contains transfer-consequence explanation
  it('should render a transfer-consequence explanation inside the conflict panel', () => {
    // Arrange
    const conflicts: NamespaceConflict[] = [
      { namespace: 'myorg', holderCredentialId: 'cred-1', holderName: 'Old Account' },
    ];

    // Act
    const { el } = setup({ conflicts });

    // Assert
    const panel = el.querySelector('.account-form__conflict-panel');
    expect(panel?.textContent).toContain('current holder');
  });
});

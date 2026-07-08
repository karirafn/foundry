import { TestBed } from '@angular/core/testing';
import { AccountFormComponent } from './account-form';
import { AccountSummary, CreateAccountRequest, TokenValidationResult, UpdateAccountRequest } from '../account.model';

const MOCK_ACCOUNT: AccountSummary = {
  id: '00000000-0000-0000-0000-000000000001',
  name: 'my-github',
  providerType: 'GitHub',
  baseUrl: 'https://github.com',
  hasToken: true,
};

const MOCK_ACCOUNT_2: AccountSummary = {
  id: '00000000-0000-0000-0000-000000000002',
  name: 'work-github',
  providerType: 'GitHub',
  baseUrl: 'https://github.com',
  hasToken: true,
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

function setup(overrides: {
  account?: AccountSummary | null;
  accounts?: AccountSummary[];
  saving?: boolean;
  validating?: boolean;
  validationResult?: TokenValidationResult | null;
  saveError?: string | null;
  validationError?: string | null;
} = {}) {
  const fixture = TestBed.createComponent(AccountFormComponent);
  fixture.componentRef.setInput('account', overrides.account ?? null);
  fixture.componentRef.setInput('accounts', overrides.accounts ?? []);
  fixture.componentRef.setInput('saving', overrides.saving ?? false);
  fixture.componentRef.setInput('validating', overrides.validating ?? false);
  fixture.componentRef.setInput('validationResult', overrides.validationResult ?? null);
  fixture.componentRef.setInput('saveError', overrides.saveError ?? null);
  fixture.componentRef.setInput('validationError', overrides.validationError ?? null);
  fixture.detectChanges();
  return { fixture, component: fixture.componentInstance, el: fixture.nativeElement as HTMLElement };
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

  // Cycle 20: resolving state — aria-busy set on token wrapper
  it('should set aria-busy="true" on token wrapper while validating', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({ validating: true });

    // Assert
    const wrapper = el.querySelector('.account-form__token-wrapper') as HTMLElement;
    expect(wrapper.getAttribute('aria-busy')).toBe('true');
  });

  // Cycle 21: idle state — no aria-busy on token wrapper
  it('should not set aria-busy on token wrapper when not validating', () => {
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
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({ validationResult: VALID_RESULT });

    // Assert
    const dot = el.querySelector('.account-form__validation-dot--valid');
    expect(dot).toBeTruthy();
    const msg = el.querySelector('.account-form__validation-message--valid');
    expect(msg?.textContent).toContain('Authenticated as octocat');
  });

  // Cycle 25: auth-failure state
  it('should show auth failure message with error dot', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({ validationResult: AUTH_FAIL_RESULT });

    // Assert
    const dot = el.querySelector('.account-form__validation-dot--error');
    expect(dot).toBeTruthy();
    const msg = el.querySelector('.account-form__validation-message--error');
    expect(msg?.textContent).toContain('Authentication failed — check that the token is correct');
  });

  // Cycle 26: missing scopes state
  it('should show missing scopes message with warning dot', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({ validationResult: MISSING_SCOPES_RESULT });

    // Assert
    const dot = el.querySelector('.account-form__validation-dot--warning');
    expect(dot).toBeTruthy();
    const msg = el.querySelector('.account-form__validation-message--warning');
    expect(msg?.textContent).toContain('Missing required scopes: repo, workflow');
  });

  // Cycle 27: valid-but-null identity state
  it('should show error message when token valid but accountName is null', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({ validationResult: VALID_NULL_IDENTITY_RESULT });

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

    // Act — set token input to match what would have been resolved
    const tokenInput = el.querySelector('#account-form-token') as HTMLInputElement;
    tokenInput.value = 'ghp_test_token';
    tokenInput.dispatchEvent(new Event('input'));
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

    // Act
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

    // Assert — duplicate warning visible
    const warning = el.querySelector('.account-form__duplicate-warning');
    expect(warning?.textContent).toContain('An account for "my-github" already exists on github.com');

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

    // Assert — no duplicate warning
    const warning = el.querySelector('.account-form__duplicate-warning');
    expect(warning).toBeNull();

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

    // Assert
    const notice = el.querySelector('.account-form__rename-notice');
    expect(notice?.textContent).toContain('Saving will rename this account to "new-identity"');
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
});

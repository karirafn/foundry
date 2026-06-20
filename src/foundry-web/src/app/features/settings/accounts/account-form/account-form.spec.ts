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

function setup(overrides: {
  account?: AccountSummary | null;
  saving?: boolean;
  validating?: boolean;
  validationResult?: TokenValidationResult | null;
  saveError?: string | null;
  validationError?: string | null;
} = {}) {
  const fixture = TestBed.createComponent(AccountFormComponent);
  fixture.componentRef.setInput('account', overrides.account ?? null);
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

  // Cycle 5: name field is rendered with label and input
  it('should render the name field with label and input', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup();

    // Assert
    const label = el.querySelector('label[for="account-form-name"]');
    expect(label).toBeTruthy();
    const input = el.querySelector('#account-form-name') as HTMLInputElement;
    expect(input).toBeTruthy();
    expect(input.type).toBe('text');
  });

  // Cycle 6: in edit mode name field pre-filled from account
  it('should pre-fill name from account in edit mode', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({ account: MOCK_ACCOUNT });

    // Assert
    const input = el.querySelector('#account-form-name') as HTMLInputElement;
    expect(input.value).toBe('my-github');
  });

  // Cycle 7: base URL field rendered with label
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

  // Cycle 8: base URL defaults to https://github.com in add mode
  it('should default base URL to https://github.com in add mode', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({ account: null });

    // Assert
    const input = el.querySelector('#account-form-base-url') as HTMLInputElement;
    expect(input.value).toBe('https://github.com');
  });

  // Cycle 9: base URL pre-filled from account in edit mode
  it('should pre-fill base URL from account in edit mode', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({ account: MOCK_ACCOUNT });

    // Assert
    const input = el.querySelector('#account-form-base-url') as HTMLInputElement;
    expect(input.value).toBe('https://github.com');
  });

  // Cycle 10: provider selector component is shown in add mode
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

  // Cycle 11: changing provider to GitLab updates the base URL
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

  // Cycle 12: edit mode shows provider badge, not selector
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

  // Cycle 13: token field rendered with label
  it('should render the token field with label and password input', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup();

    // Assert
    const label = el.querySelector('label[for="account-form-token"]');
    expect(label).toBeTruthy();
    const input = el.querySelector('#account-form-token') as HTMLInputElement;
    expect(input).toBeTruthy();
    expect(input.type).toBe('password');
  });

  // Cycle 14: edit mode token hint is shown
  it('should show "Leave empty to keep current token" hint in edit mode', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({ account: MOCK_ACCOUNT });

    // Assert
    const hint = el.querySelector('.account-form__field-hint');
    expect(hint?.textContent).toContain('Leave empty to keep current token');
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

  // Cycle 16: validate token button rendered
  it('should render the validate token button', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup();

    // Assert
    const btn = el.querySelector('.account-form__validate-btn');
    expect(btn).toBeTruthy();
    expect(btn?.textContent?.trim()).toBe('Validate Token');
  });

  // Cycle 17: clicking validate emits validateToken with token and baseUrl
  it('should emit validateToken with token and baseUrl when validate is clicked', () => {
    // Arrange
    const { el, component, fixture } = setup();
    let emitted: { token: string; baseUrl: string } | undefined;
    component.validateToken.subscribe((v: { token: string; baseUrl: string }) => { emitted = v; });

    const tokenInput = el.querySelector('#account-form-token') as HTMLInputElement;
    tokenInput.value = 'ghp_test_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Act
    const validateBtn = el.querySelector('.account-form__validate-btn') as HTMLButtonElement;
    validateBtn.click();

    // Assert
    expect(emitted).toEqual({ token: 'ghp_test_token', baseUrl: 'https://github.com' });
  });

  // Cycle 18: validate button is disabled when token is empty
  it('should disable validate button when token is empty', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup();

    // Assert
    const btn = el.querySelector('.account-form__validate-btn') as HTMLButtonElement;
    expect(btn.disabled).toBe(true);
  });

  // Cycle 19: validating state - button shows "Validating..."
  it('should show "Validating..." and disable validate button when validating', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({ validating: true });

    // Assert
    const btn = el.querySelector('.account-form__validate-btn') as HTMLButtonElement;
    expect(btn.textContent?.trim()).toBe('Validating...');
    expect(btn.disabled).toBe(true);
  });

  // Cycle 20: validation result - valid
  it('should show valid validation result with green dot', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({
      validationResult: { isValid: true, isAuthFailure: false, missingScopes: [] },
    });

    // Assert
    const result = el.querySelector('.account-form__validation-result');
    expect(result).toBeTruthy();
    const dot = el.querySelector('.account-form__validation-dot--valid');
    expect(dot).toBeTruthy();
    const msg = el.querySelector('.account-form__validation-message--valid');
    expect(msg?.textContent).toContain('Token is valid');
  });

  // Cycle 21: validation result - auth failure
  it('should show auth failure validation result with error dot', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({
      validationResult: { isValid: false, isAuthFailure: true, missingScopes: [] },
    });

    // Assert
    const dot = el.querySelector('.account-form__validation-dot--error');
    expect(dot).toBeTruthy();
    const msg = el.querySelector('.account-form__validation-message--error');
    expect(msg?.textContent).toContain('Authentication failed');
  });

  // Cycle 22: validation result - missing scopes
  it('should show missing scopes validation result with warning dot', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({
      validationResult: { isValid: false, isAuthFailure: false, missingScopes: ['repo', 'workflow'] },
    });

    // Assert
    const dot = el.querySelector('.account-form__validation-dot--warning');
    expect(dot).toBeTruthy();
    const msg = el.querySelector('.account-form__validation-message--warning');
    expect(msg?.textContent).toContain('Missing required scopes');
    expect(msg?.textContent).toContain('repo');
    expect(msg?.textContent).toContain('workflow');
  });

  // Cycle 23: no validation result content shown when null
  it('should not show validation result content when validationResult is null', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({ validationResult: null });

    // Assert — wrapper is always present (aria-live must persist), content is empty
    const result = el.querySelector('.account-form__validation-result');
    expect(result).toBeTruthy();
    const dot = el.querySelector('.account-form__validation-dot');
    expect(dot).toBeNull();
  });

  // Cycle 24: server error is shown
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

  // Cycle 25: no error content shown when saveError null
  it('should not show save error content when saveError is null', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({ saveError: null });

    // Assert — wrapper always present, but inner content is empty
    const errorEl = el.querySelector('.account-form__save-error');
    expect(errorEl?.textContent?.trim()).toBeFalsy();
  });

  // Cycle 26: save button is rendered
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

  // Cycle 27: save button disabled when name empty in add mode
  it('should disable save button when name is empty', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({ account: null });

    // Assert
    const btn = el.querySelector('.account-form__save-btn') as HTMLButtonElement;
    expect(btn.disabled).toBe(true);
  });

  // Cycle 28: save button disabled when token empty in add mode (even with name)
  it('should disable save button when token is empty in add mode', () => {
    // Arrange
    const { el, fixture } = setup({ account: null });
    const nameInput = el.querySelector('#account-form-name') as HTMLInputElement;
    nameInput.value = 'my-account';
    nameInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Assert
    const btn = el.querySelector('.account-form__save-btn') as HTMLButtonElement;
    expect(btn.disabled).toBe(true);
  });

  // Cycle 29: save button disabled while saving
  it('should disable save button when saving is true', () => {
    // Arrange
    const { el, fixture } = setup({ account: null, saving: true });
    const nameInput = el.querySelector('#account-form-name') as HTMLInputElement;
    nameInput.value = 'my-account';
    nameInput.dispatchEvent(new Event('input'));
    const tokenInput = el.querySelector('#account-form-token') as HTMLInputElement;
    tokenInput.value = 'ghp_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Assert
    const btn = el.querySelector('.account-form__save-btn') as HTMLButtonElement;
    expect(btn.disabled).toBe(true);
  });

  // Cycle 30: save emits CreateAccountRequest in add mode
  it('should emit CreateAccountRequest on save in add mode', () => {
    // Arrange
    const { el, component, fixture } = setup({ account: null });
    let emitted: CreateAccountRequest | UpdateAccountRequest | undefined;
    component.save.subscribe((v: CreateAccountRequest | UpdateAccountRequest) => { emitted = v; });

    const nameInput = el.querySelector('#account-form-name') as HTMLInputElement;
    nameInput.value = 'new-account';
    nameInput.dispatchEvent(new Event('input'));
    const tokenInput = el.querySelector('#account-form-token') as HTMLInputElement;
    tokenInput.value = 'ghp_newtoken';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Act
    const saveBtn = el.querySelector('.account-form__save-btn') as HTMLButtonElement;
    saveBtn.click();

    // Assert
    expect(emitted).toEqual({
      name: 'new-account',
      providerType: 'GitHub',
      baseUrl: 'https://github.com',
      token: 'ghp_newtoken',
    });
  });

  // Cycle 30b: save emits CreateAccountRequest with GitLab provider when GitLab is selected
  it('should emit CreateAccountRequest with GitLab providerType when GitLab is selected', () => {
    // Arrange
    const { el, component, fixture } = setup({ account: null });
    let emitted: CreateAccountRequest | UpdateAccountRequest | undefined;
    component.save.subscribe((v: CreateAccountRequest | UpdateAccountRequest) => { emitted = v; });

    const radios = el.querySelectorAll('input[type="radio"]') as NodeListOf<HTMLInputElement>;
    const gitlabRadio = Array.from(radios).find((r) => r.value === 'GitLab')!;
    gitlabRadio.click();
    fixture.detectChanges();

    const nameInput = el.querySelector('#account-form-name') as HTMLInputElement;
    nameInput.value = 'my-gitlab';
    nameInput.dispatchEvent(new Event('input'));
    const tokenInput = el.querySelector('#account-form-token') as HTMLInputElement;
    tokenInput.value = 'glpat_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Act
    const saveBtn = el.querySelector('.account-form__save-btn') as HTMLButtonElement;
    saveBtn.click();

    // Assert
    expect(emitted).toEqual({
      name: 'my-gitlab',
      providerType: 'GitLab',
      baseUrl: 'https://gitlab.com',
      token: 'glpat_token',
    });
  });

  // Cycle 31: save emits UpdateAccountRequest in edit mode
  it('should emit UpdateAccountRequest on save in edit mode', () => {
    // Arrange
    const { el, component, fixture } = setup({ account: MOCK_ACCOUNT });
    let emitted: CreateAccountRequest | UpdateAccountRequest | undefined;
    component.save.subscribe((v: CreateAccountRequest | UpdateAccountRequest) => { emitted = v; });

    const nameInput = el.querySelector('#account-form-name') as HTMLInputElement;
    nameInput.value = 'renamed-account';
    nameInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Act
    const saveBtn = el.querySelector('.account-form__save-btn') as HTMLButtonElement;
    saveBtn.click();

    // Assert
    expect(emitted).toEqual({
      name: 'renamed-account',
      baseUrl: 'https://github.com',
    });
  });

  // Cycle 32: save in edit mode with token emits token in UpdateAccountRequest
  it('should include token in UpdateAccountRequest when token is entered in edit mode', () => {
    // Arrange
    const { el, component, fixture } = setup({ account: MOCK_ACCOUNT });
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
  });

  // Cycle 33: save button enabled in edit mode with name but empty token
  it('should enable save button in edit mode when name is filled but token is empty', () => {
    // Arrange
    const { el } = setup({ account: MOCK_ACCOUNT });

    // Assert
    const btn = el.querySelector('.account-form__save-btn') as HTMLButtonElement;
    expect(btn.disabled).toBe(false);
  });

  // Cycle 34: validationError is shown when set
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

  // Cycle 35: no validationError content shown when null
  it('should not show validation error content when validationError is null', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({ validationError: null });

    // Assert — wrapper always present, content is empty
    const errorEl = el.querySelector('.account-form__validation-error');
    expect(errorEl?.textContent?.trim()).toBeFalsy();
  });

  // Cycle 36: name input has required attribute
  it('should have required attribute on name input', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({ account: null });

    // Assert
    const nameInput = el.querySelector('#account-form-name') as HTMLInputElement;
    expect(nameInput.required).toBe(true);
  });

  // Cycle 37: token input has required attribute in add mode
  it('should have required attribute on token input in add mode', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({ account: null });

    // Assert
    const tokenInput = el.querySelector('#account-form-token') as HTMLInputElement;
    expect(tokenInput.required).toBe(true);
  });

  // Cycle 38: token input does not have required attribute in edit mode
  it('should not have required attribute on token input in edit mode', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({ account: MOCK_ACCOUNT });

    // Assert
    const tokenInput = el.querySelector('#account-form-token') as HTMLInputElement;
    expect(tokenInput.required).toBe(false);
  });

  // Cycle 39: live-region divs render unconditionally so screen readers announce changes
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

  // Cycle 40: fd-provider-selector is labelled via aria-labelledby
  it('should associate the Provider label with fd-provider-selector via aria-labelledby', () => {
    // Arrange
    // (TestBed configured in beforeEach)

    // Act
    const { el } = setup({ account: null });

    // Assert
    const labelSpan = el.querySelector('#account-form-provider-label');
    expect(labelSpan).toBeTruthy();
    const selector = el.querySelector('fd-provider-selector');
    expect(selector?.getAttribute('aria-labelledby')).toBe('account-form-provider-label');
  });
});

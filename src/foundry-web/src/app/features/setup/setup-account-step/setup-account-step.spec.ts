import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { SetupAccountStepComponent } from './setup-account-step';
import { AccountSummary, CredentialCreationResult, TokenRequirements, TokenValidationResult } from '../../settings/accounts/account.model';

function setup() {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    imports: [SetupAccountStepComponent],
    providers: [provideHttpClient(), provideHttpClientTesting()],
  });

  const fixture = TestBed.createComponent(SetupAccountStepComponent);
  const httpMock = TestBed.inject(HttpTestingController);
  return { fixture, component: fixture.componentInstance, httpMock };
}

const CREATED_ACCOUNT: AccountSummary = {
  id: 'account-42',
  name: 'octocat',
  providerType: 'GitHub',
  baseUrl: 'https://github.com',
  hasToken: true,
  tokenStatus: 'present',
  deniedRepositories: [],
  tokenInvalidSince: null,
  nonConformingToken: null,
  namespaces: [],
  tokenExpiry: { status: 'notChecked', expiresAt: null },
};

const CREATED_ACCOUNT_RESULT: CredentialCreationResult = {
  credential: CREATED_ACCOUNT,
  affectedRepositories: [],
};

const VALID_RESULT: TokenValidationResult = {
  kind: 'authenticated',
  missingScopes: [],
  accountName: 'octocat',
  detectedProvider: null,
  wrongKind: null,
  notAllowed: [],
  broadReach: [],
  tokenExpiry: { status: 'notChecked', expiresAt: null },
};

const AUTH_FAIL_RESULT: TokenValidationResult = {
  kind: 'authenticationFailed',
  missingScopes: [],
  accountName: null,
  detectedProvider: null,
  wrongKind: null,
  notAllowed: [],
  broadReach: [],
  tokenExpiry: { status: 'notChecked', expiresAt: null },
};

const MISSING_SCOPES_RESULT: TokenValidationResult = {
  kind: 'authenticated',
  missingScopes: ['repo', 'workflow'],
  accountName: 'octocat',
  detectedProvider: null,
  wrongKind: null,
  notAllowed: [],
  broadReach: [],
  tokenExpiry: { status: 'notChecked', expiresAt: null },
};

const IDENTITY_UNRESOLVED_RESULT: TokenValidationResult = {
  kind: 'identityUnresolved',
  missingScopes: [],
  accountName: null,
  detectedProvider: null,
  wrongKind: null,
  notAllowed: [],
  broadReach: [],
  tokenExpiry: { status: 'notChecked', expiresAt: null },
};

const PROVIDER_MISMATCH_RESULT: TokenValidationResult = {
  kind: 'providerMismatch',
  missingScopes: [],
  accountName: null,
  detectedProvider: 'gitlab',
  wrongKind: null,
  notAllowed: [],
  broadReach: [],
  tokenExpiry: { status: 'notChecked', expiresAt: null },
};

describe('SetupAccountStepComponent', () => {
  afterEach(() => {
    const httpMock = TestBed.inject(HttpTestingController);
    // The account step loads the token requirements list on every provider change; it is guidance only.
    httpMock.match((request) => request.url.endsWith('/token-requirements'));
    httpMock.verify();
  });

  it('should make its heading programmatically focusable', () => {
    // Arrange
    const { fixture } = setup();

    // Act
    fixture.detectChanges();

    // Assert
    const heading = (fixture.nativeElement as HTMLElement).querySelector('h2');
    expect(heading?.getAttribute('tabindex')).toBe('-1');
  });

  // Step 9: Name field absent
  it('should not render an account name input', () => {
    // Arrange / Act
    const { fixture } = setup();
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('input[id="setup-name"]')).toBeFalsy();
  });

  // Step 9: No Validate button
  it('should not render a Validate Token button', () => {
    // Arrange / Act
    const { fixture } = setup();
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('button.setup-account-step__validate-btn')).toBeFalsy();
  });

  // Renders required fields and action buttons
  it('should render token input, and action buttons', () => {
    // Arrange / Act
    const { fixture } = setup();
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('input[id="setup-token"]')).toBeTruthy();
    expect(el.querySelector('button.setup-account-step__create-btn')).toBeTruthy();
    expect(el.querySelector('button.setup-account-step__back-btn')).toBeTruthy();
  });

  // Cycle 2: Create button is disabled when token is empty and identity unresolved
  it('should disable the Create Account button when token is empty', () => {
    // Arrange / Act
    const { fixture } = setup();
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const btn = el.querySelector('button.setup-account-step__create-btn') as HTMLButtonElement;
    expect(btn.disabled).toBe(true);
  });

  // Advance gated until identity resolved
  it('should disable the Create Account button when token is present but identity not yet resolved', () => {
    // Arrange
    const { fixture } = setup();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    // Act — fill token but do not trigger validation
    const tokenInput = el.querySelector('input[id="setup-token"]') as HTMLInputElement;
    tokenInput.value = 'ghp_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Assert
    const btn = el.querySelector('button.setup-account-step__create-btn') as HTMLButtonElement;
    expect(btn.disabled).toBe(true);
  });

  // Step 9: blur triggers a validate
  it('should call validate-token when the token input loses focus with a non-empty token', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const tokenInput = el.querySelector('input[id="setup-token"]') as HTMLInputElement;
    tokenInput.value = 'ghp_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Act
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    // Assert
    const req = httpMock.expectOne('/api/accounts/validate-token');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ token: 'ghp_token', baseUrl: 'https://github.com', providerType: 'GitHub' });

    // Cleanup
    req.flush(VALID_RESULT);
  });

  // Step 9: paste triggers a validate
  it('should call validate-token when the token input receives a paste event', async () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const tokenInput = el.querySelector('input[id="setup-token"]') as HTMLInputElement;
    tokenInput.value = 'ghp_pasted_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Act
    tokenInput.dispatchEvent(new Event('paste'));
    await new Promise<void>(resolve => setTimeout(resolve, 0));
    fixture.detectChanges();

    // Assert
    const req = httpMock.expectOne('/api/accounts/validate-token');
    expect(req.request.body).toEqual({ token: 'ghp_pasted_token', baseUrl: 'https://github.com', providerType: 'GitHub' });

    // Cleanup
    req.flush(VALID_RESULT);
  });

  // Step 9: Authenticated as {name} rendered on valid result
  it('should render "Authenticated as {name}" when validation succeeds with an account name', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const tokenInput = el.querySelector('input[id="setup-token"]') as HTMLInputElement;
    tokenInput.value = 'ghp_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Act
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
    httpMock.expectOne('/api/accounts/validate-token').flush(VALID_RESULT);
    fixture.detectChanges();

    // Assert
    const statusRegion = el.querySelector('[role="status"]');
    expect(statusRegion?.textContent).toContain('Authenticated as');
    expect(statusRegion?.textContent).toContain('octocat');
  });

  // Step 9: advance gated until identity resolved — enabled when valid identity present
  it('should enable the Create Account button when identity is successfully resolved', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const tokenInput = el.querySelector('input[id="setup-token"]') as HTMLInputElement;
    tokenInput.value = 'ghp_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Act
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
    httpMock.expectOne('/api/accounts/validate-token').flush(VALID_RESULT);
    fixture.detectChanges();

    // Assert
    const btn = el.querySelector('button.setup-account-step__create-btn') as HTMLButtonElement;
    expect(btn.disabled).toBe(false);
  });

  // Create request omits name
  it('should call AccountService.createAccount() without a name field', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const tokenInput = el.querySelector('input[id="setup-token"]') as HTMLInputElement;
    tokenInput.value = 'ghp_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
    httpMock.expectOne('/api/accounts/validate-token').flush(VALID_RESULT);
    fixture.detectChanges();

    // Act
    const btn = el.querySelector('button.setup-account-step__create-btn') as HTMLButtonElement;
    btn.click();
    fixture.detectChanges();

    // Assert
    const req = httpMock.expectOne('/api/accounts');
    expect(req.request.method).toBe('POST');
    const body = req.request.body as Record<string, unknown>;
    expect(body).not.toHaveProperty('name');
    expect(body).toEqual({
      providerType: 'GitHub',
      baseUrl: 'https://github.com',
      token: 'ghp_token',
    });

    // Cleanup
    req.flush(CREATED_ACCOUNT_RESULT);
  });

  // Selecting GitLab sends correct providerType and baseUrl
  it('should send GitLab providerType and baseUrl when GitLab provider is selected', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    const radios = el.querySelectorAll('input[type="radio"]') as NodeListOf<HTMLInputElement>;
    const gitlabRadio = Array.from(radios).find((r) => r.value === 'GitLab')!;
    gitlabRadio.click();
    fixture.detectChanges();

    const tokenInput = el.querySelector('input[id="setup-token"]') as HTMLInputElement;
    tokenInput.value = 'glpat_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
    httpMock.expectOne('/api/accounts/validate-token').flush(VALID_RESULT);
    fixture.detectChanges();

    // Act
    const btn = el.querySelector('button.setup-account-step__create-btn') as HTMLButtonElement;
    btn.click();
    fixture.detectChanges();

    // Assert
    const req = httpMock.expectOne('/api/accounts');
    expect(req.request.body).toEqual({
      providerType: 'GitLab',
      baseUrl: 'https://gitlab.com',
      token: 'glpat_token',
    });

    // Cleanup
    req.flush({ credential: { ...CREATED_ACCOUNT, providerType: 'GitLab' }, affectedRepositories: [] });
  });

  // GitLab provider — validate-token request includes providerType 'GitLab'
  it('should include providerType GitLab in the validate-token request when GitLab is selected', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    const radios = el.querySelectorAll('input[type="radio"]') as NodeListOf<HTMLInputElement>;
    const gitlabRadio = Array.from(radios).find((r) => r.value === 'GitLab')!;
    gitlabRadio.click();
    fixture.detectChanges();

    const tokenInput = el.querySelector('input[id="setup-token"]') as HTMLInputElement;
    tokenInput.value = 'glpat_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Act
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    // Assert
    const req = httpMock.expectOne('/api/accounts/validate-token');
    expect(req.request.body).toEqual({ token: 'glpat_token', baseUrl: 'https://gitlab.com', providerType: 'GitLab' });

    // Cleanup
    req.flush(VALID_RESULT);
  });

  // Emits complete with account ID on successful create
  it('should emit the complete event with the account ID after a successful create', () => {
    // Arrange
    const { fixture, component, httpMock } = setup();
    fixture.detectChanges();

    let emittedId: string | undefined;
    component.complete.subscribe((id: string) => (emittedId = id));

    const el = fixture.nativeElement as HTMLElement;
    const tokenInput = el.querySelector('input[id="setup-token"]') as HTMLInputElement;
    tokenInput.value = 'ghp_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
    httpMock.expectOne('/api/accounts/validate-token').flush(VALID_RESULT);
    fixture.detectChanges();

    // Act
    const btn = el.querySelector('button.setup-account-step__create-btn') as HTMLButtonElement;
    btn.click();
    fixture.detectChanges();
    httpMock.expectOne('/api/accounts').flush(CREATED_ACCOUNT_RESULT);
    fixture.detectChanges();

    // Assert
    expect(emittedId).toBe('account-42');
  });

  // Back button emits back output
  it('should emit the back event when the Back button is clicked', () => {
    // Arrange
    const { fixture, component } = setup();
    fixture.detectChanges();

    let emitted = false;
    component.back.subscribe(() => (emitted = true));

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const btn = el.querySelector('button.setup-account-step__back-btn') as HTMLButtonElement;
    btn.click();
    fixture.detectChanges();

    // Assert
    expect(emitted).toBe(true);
  });

  // Shows save error on failure
  it('should display an error message when the create request fails', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const tokenInput = el.querySelector('input[id="setup-token"]') as HTMLInputElement;
    tokenInput.value = 'ghp_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
    httpMock.expectOne('/api/accounts/validate-token').flush(VALID_RESULT);
    fixture.detectChanges();
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);

    // Act
    const btn = el.querySelector('button.setup-account-step__create-btn') as HTMLButtonElement;
    btn.click();
    fixture.detectChanges();
    httpMock.expectOne('/api/accounts').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    fixture.detectChanges();

    // Assert
    const errorEl = el.querySelector('[role="alert"]');
    expect(errorEl?.textContent?.trim()).toBeTruthy();
    expect(consoleError).toHaveBeenCalledWith(
      expect.objectContaining({ status: 500, url: '/api/accounts' })
    );
  });

  // Does not emit complete on failure
  it('should not emit the complete event when the create request fails', () => {
    // Arrange
    const { fixture, component, httpMock } = setup();
    fixture.detectChanges();

    let emitted = false;
    component.complete.subscribe(() => (emitted = true));

    const el = fixture.nativeElement as HTMLElement;
    const tokenInput = el.querySelector('input[id="setup-token"]') as HTMLInputElement;
    tokenInput.value = 'ghp_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
    httpMock.expectOne('/api/accounts/validate-token').flush(VALID_RESULT);
    fixture.detectChanges();
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);

    // Act
    const btn = el.querySelector('button.setup-account-step__create-btn') as HTMLButtonElement;
    btn.click();
    fixture.detectChanges();
    httpMock.expectOne('/api/accounts').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    fixture.detectChanges();

    // Assert
    expect(emitted).toBe(false);
    expect(consoleError).toHaveBeenCalledWith(
      expect.objectContaining({ status: 500, url: '/api/accounts' })
    );
  });

  // Does not emit complete when saveSuccess is already true from a previous unrelated save
  it('should not emit complete if saveSuccess is already true when the component initializes', () => {
    // Arrange
    const { fixture, component, httpMock } = setup();
    // Access private writable signal to prime stale service state (simulates prior wizard navigation)
    (component['_accountService'] as unknown as { _saveSuccessSignal: { set: (v: boolean) => void } })._saveSuccessSignal.set(true);

    let emitted = false;
    component.complete.subscribe(() => (emitted = true));

    // Act — detect changes without user clicking Create Account
    fixture.detectChanges();

    // Assert — must not auto-complete on mount
    expect(emitted).toBe(false);

    // Cleanup
    httpMock.expectNone('/api/accounts');
  });

  // Auth failure message
  it('should render auth failure message when validation returns auth failure', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const tokenInput = el.querySelector('input[id="setup-token"]') as HTMLInputElement;
    tokenInput.value = 'bad_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Act
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
    httpMock.expectOne('/api/accounts/validate-token').flush(AUTH_FAIL_RESULT);
    fixture.detectChanges();

    // Assert
    const statusRegion = el.querySelector('[role="status"]');
    expect(statusRegion?.textContent).toContain('Authentication failed — check that the token is correct');
  });

  // Missing scopes message
  it('should render missing scopes message when validation returns missing scopes', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const tokenInput = el.querySelector('input[id="setup-token"]') as HTMLInputElement;
    tokenInput.value = 'ghp_limited';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Act
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
    httpMock.expectOne('/api/accounts/validate-token').flush(MISSING_SCOPES_RESULT);
    fixture.detectChanges();

    // Assert
    const statusRegion = el.querySelector('[role="status"]');
    expect(statusRegion?.textContent).toContain('Missing permissions:');
    expect(Array.from(statusRegion?.querySelectorAll('li') ?? [], (li) => li.textContent?.trim())).toEqual(['repo', 'workflow']);
  });

  // identityUnresolved message
  it('should render identity-unresolved message when kind is identityUnresolved', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const tokenInput = el.querySelector('input[id="setup-token"]') as HTMLInputElement;
    tokenInput.value = 'ghp_no_identity';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Act
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
    httpMock.expectOne('/api/accounts/validate-token').flush(IDENTITY_UNRESOLVED_RESULT);
    fixture.detectChanges();

    // Assert
    const statusRegion = el.querySelector('[role="status"]');
    expect(statusRegion?.textContent).toContain('Token accepted, but the account identity could not be resolved from the provider');
  });

  // Resolving identity message shown while validating
  it('should render "Resolving identity…" while validation is in progress', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const tokenInput = el.querySelector('input[id="setup-token"]') as HTMLInputElement;
    tokenInput.value = 'ghp_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Act — trigger blur to start validation, but do not flush
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    // Assert — status region should show resolving text before response arrives
    const statusRegion = el.querySelector('[role="status"]');
    expect(statusRegion?.textContent).toContain('Resolving identity…');

    // Cleanup
    httpMock.expectOne('/api/accounts/validate-token').flush(VALID_RESULT);
  });

  // aria-live validation message region is permanently in the DOM
  it('should keep the validation status region in the DOM with role="status" when no result is present', () => {
    // Arrange / Act
    const { fixture } = setup();
    fixture.detectChanges();

    // Assert — region is always rendered so screen readers can track text changes
    const el = fixture.nativeElement as HTMLElement;
    const statusRegion = el.querySelector('[role="status"]');
    expect(statusRegion).toBeTruthy();
  });

  // setup-token-validation and setup-save-error ids are present
  it('should have setup-token-validation and setup-save-error element ids', () => {
    // Arrange / Act
    const { fixture } = setup();
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('#setup-token-validation')).toBeTruthy();
    expect(el.querySelector('#setup-save-error')).toBeTruthy();
  });

  // Token input aria-describedby
  it('should reference setup-token-validation and setup-save-error in token input aria-describedby', () => {
    // Arrange / Act
    const { fixture } = setup();
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const tokenInput = el.querySelector('input[id="setup-token"]');
    expect(tokenInput?.getAttribute('aria-describedby')).toBe('setup-token-validation setup-save-error');
  });

  // Inner radiogroup is labelled via aria-labelledby
  it('should associate the Provider label with the inner radiogroup via aria-labelledby', () => {
    // Arrange / Act
    const { fixture } = setup();
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const labelSpan = el.querySelector('#setup-provider-label');
    expect(labelSpan).toBeTruthy();
    const radiogroup = el.querySelector('[role="radiogroup"]');
    expect(radiogroup?.getAttribute('aria-labelledby')).toBe('setup-provider-label');
  });

  // Stale result is cleared when base URL is edited
  it('should hide "Authenticated as" after base URL is edited', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const tokenInput = el.querySelector('input[id="setup-token"]') as HTMLInputElement;
    tokenInput.value = 'ghp_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
    httpMock.expectOne('/api/accounts/validate-token').flush(VALID_RESULT);
    fixture.detectChanges();

    // Act — edit base URL
    const baseUrlInput = el.querySelector('input[id="setup-base-url"]') as HTMLInputElement;
    baseUrlInput.value = 'https://github.example.com';
    baseUrlInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Assert — stale result no longer visible
    const statusRegion = el.querySelector('[role="status"]');
    expect(statusRegion?.textContent).not.toContain('Authenticated as');
  });

  // Stale result is cleared when token input is edited
  it('should hide "Authenticated as" after the token is edited', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const tokenInput = el.querySelector('input[id="setup-token"]') as HTMLInputElement;
    tokenInput.value = 'ghp_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
    httpMock.expectOne('/api/accounts/validate-token').flush(VALID_RESULT);
    fixture.detectChanges();

    // Act — edit token
    tokenInput.value = 'ghp_different';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Assert — stale result no longer visible
    const statusRegion = el.querySelector('[role="status"]');
    expect(statusRegion?.textContent).not.toContain('Authenticated as');
  });

  // Token wrapper has no aria-busy (role-less div, attribute is inert there)
  it('should not set aria-busy on the token wrapper div', () => {
    // Arrange / Act
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const tokenInput = el.querySelector('input[id="setup-token"]') as HTMLInputElement;
    tokenInput.value = 'ghp_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    // Assert — wrapper has no aria-busy even while validating
    const wrapper = el.querySelector('.setup-account-step__token-wrapper') as HTMLElement;
    expect(wrapper.getAttribute('aria-busy')).toBeNull();

    // Cleanup
    httpMock.expectOne('/api/accounts/validate-token').flush(VALID_RESULT);
  });

  function checkToken(result: TokenValidationResult): HTMLElement {
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const tokenInput = el.querySelector('input[id="setup-token"]') as HTMLInputElement;
    tokenInput.value = 'glpat_checked';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
    httpMock.expectOne('/api/accounts/validate-token').flush(result);
    fixture.detectChanges();
    return el;
  }

  it('should show broad reach and not-allowed permissions separately and disable Create', () => {
    // Arrange / Act
    const el = checkToken({ ...VALID_RESULT, broadReach: ['all-projects'], notAllowed: ['delete_project'] });

    // Assert
    const errors = [...el.querySelectorAll('[role="status"] .setup-account-step__validation-message--error')].map(
      (m) => m.textContent ?? '',
    );
    expect(errors).toHaveLength(2);
    expect(errors[0]).toContain('This token reaches all projects. Foundry needs a token limited to selected projects.');
    expect(errors[1]).toContain("This token grants permissions Foundry doesn't allow:");
    expect(errors[1]).toContain('delete_project');
    expect(el.querySelector('[role="status"]')?.textContent).not.toContain('Authenticated as');
    expect((el.querySelector('button.setup-account-step__create-btn') as HTMLButtonElement).disabled).toBe(true);
  });

  it('should tell an old GitLab to upgrade, with no create link', () => {
    // Arrange / Act
    const el = checkToken({ ...VALID_RESULT, kind: 'wrongTokenKind', wrongKind: 'gitlab-version' });

    // Assert
    const msg = el.querySelector('[role="status"] .setup-account-step__validation-message--error');
    expect(msg?.textContent).toContain('Upgrade GitLab to 19.5 or later.');
    expect(msg?.querySelector('a')).toBeNull();
  });

  // Cycle K2: authenticated with non-empty missingScopes — Create DISABLED
  it('should disable Create when kind is authenticated but missingScopes non-empty', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const tokenInput = el.querySelector('input[id="setup-token"]') as HTMLInputElement;
    tokenInput.value = 'limited_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Act
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
    httpMock.expectOne('/api/accounts/validate-token').flush(MISSING_SCOPES_RESULT);
    fixture.detectChanges();

    // Assert
    const statusRegion = el.querySelector('[role="status"]');
    expect(statusRegion?.textContent).toContain('Missing permissions:');
    expect(Array.from(statusRegion?.querySelectorAll('li') ?? [], (li) => li.textContent?.trim())).toEqual(['repo', 'workflow']);
    const btn = el.querySelector('button.setup-account-step__create-btn') as HTMLButtonElement;
    expect(btn.disabled).toBe(true);
  });

  // Cycle K3: providerMismatch — shows error message naming detected provider, inline switch button, Create DISABLED
  it('should show providerMismatch error with switch button naming the detected provider', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const tokenInput = el.querySelector('input[id="setup-token"]') as HTMLInputElement;
    tokenInput.value = 'glpat_wrong';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Act
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
    httpMock.expectOne('/api/accounts/validate-token').flush(PROVIDER_MISMATCH_RESULT);
    fixture.detectChanges();

    // Assert — error message names both providers
    const statusRegion = el.querySelector('[role="status"]');
    expect(statusRegion?.textContent).toContain('GitLab');
    expect(statusRegion?.textContent).toContain('GitHub');

    // Assert — inline switch button present
    const switchBtn = el.querySelector('.setup-account-step__switch-provider-btn') as HTMLButtonElement;
    expect(switchBtn).toBeTruthy();
    expect(switchBtn.textContent).toContain('Switch to GitLab');

    // Assert — Create disabled
    const btn = el.querySelector('button.setup-account-step__create-btn') as HTMLButtonElement;
    expect(btn.disabled).toBe(true);
  });

  // Cycle K4: clicking the switch button for providerMismatch changes the provider
  it('should switch provider to detected provider when switch button is clicked', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const tokenInput = el.querySelector('input[id="setup-token"]') as HTMLInputElement;
    tokenInput.value = 'glpat_wrong';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
    httpMock.expectOne('/api/accounts/validate-token').flush(PROVIDER_MISMATCH_RESULT);
    fixture.detectChanges();

    // Act — click the switch button
    const switchBtn = el.querySelector('.setup-account-step__switch-provider-btn') as HTMLButtonElement;
    switchBtn.click();
    fixture.detectChanges();

    // Assert — GitLab radio is now selected
    const radios = el.querySelectorAll('input[type="radio"]') as NodeListOf<HTMLInputElement>;
    const gitlabRadio = Array.from(radios).find(r => r.value === 'GitLab');
    expect(gitlabRadio?.checked).toBe(true);

    // Cleanup — absorb re-triggered validate request
    httpMock.expectOne('/api/accounts/validate-token').flush(VALID_RESULT);
  });

  // Cycle K5: identityUnresolved — Create DISABLED
  it('should disable Create when kind is identityUnresolved', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const tokenInput = el.querySelector('input[id="setup-token"]') as HTMLInputElement;
    tokenInput.value = 'ghp_no_identity';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Act
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
    httpMock.expectOne('/api/accounts/validate-token').flush(IDENTITY_UNRESOLVED_RESULT);
    fixture.detectChanges();

    // Assert
    const btn = el.querySelector('button.setup-account-step__create-btn') as HTMLButtonElement;
    expect(btn.disabled).toBe(true);
  });

  // Finding 1: provider change clears stale resolution + updates base URL
  it('should clear stale validation result when provider is changed via radio', () => {
    // Arrange — resolve token with GitHub
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const tokenInput = el.querySelector('input[id="setup-token"]') as HTMLInputElement;
    tokenInput.value = 'ghp_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
    httpMock.expectOne('/api/accounts/validate-token').flush(VALID_RESULT);
    fixture.detectChanges();

    // Assert result initially visible
    expect(el.querySelector('[role="status"]')?.textContent).toContain('Authenticated as');

    // Act — switch provider
    const radios = el.querySelectorAll('input[type="radio"]') as NodeListOf<HTMLInputElement>;
    const gitlabRadio = Array.from(radios).find(r => r.value === 'GitLab')!;
    gitlabRadio.click();
    fixture.detectChanges();

    // Assert — stale result is hidden
    const statusRegion = el.querySelector('[role="status"]');
    expect(statusRegion?.textContent).not.toContain('Authenticated as');
  });

  it('should update base URL to GitLab default when switching provider via switch button', () => {
    // Arrange — produce a providerMismatch result (github selected, gitlab detected)
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const tokenInput = el.querySelector('input[id="setup-token"]') as HTMLInputElement;
    tokenInput.value = 'glpat_wrong';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
    httpMock.expectOne('/api/accounts/validate-token').flush(PROVIDER_MISMATCH_RESULT);
    fixture.detectChanges();

    // Act — click switch button
    const switchBtn = el.querySelector('.setup-account-step__switch-provider-btn') as HTMLButtonElement;
    switchBtn.click();
    fixture.detectChanges();

    // Assert — base URL updated to GitLab default
    const baseUrlInput = el.querySelector('input[id="setup-base-url"]') as HTMLInputElement;
    expect(baseUrlInput.value).toBe('https://gitlab.com');

    // Cleanup — absorb re-triggered validate request
    httpMock.expectOne('/api/accounts/validate-token').flush(VALID_RESULT);
  });

  // Finding 2: switch button has min 44px target (class defined)
  it('should render switch provider button with the __switch-provider-btn class and no __cancel-link class', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const tokenInput = el.querySelector('input[id="setup-token"]') as HTMLInputElement;
    tokenInput.value = 'glpat_wrong';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
    httpMock.expectOne('/api/accounts/validate-token').flush(PROVIDER_MISMATCH_RESULT);
    fixture.detectChanges();

    // Assert
    const switchBtn = el.querySelector('.setup-account-step__switch-provider-btn') as HTMLButtonElement;
    expect(switchBtn).toBeTruthy();
    expect(switchBtn.classList.contains('setup-account-step__cancel-link')).toBe(false);
  });

  // Finding 3: switching provider emits notice into the live region
  it('should render a non-empty status after switching provider via switch button', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const tokenInput = el.querySelector('input[id="setup-token"]') as HTMLInputElement;
    tokenInput.value = 'glpat_wrong';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
    httpMock.expectOne('/api/accounts/validate-token').flush(PROVIDER_MISMATCH_RESULT);
    fixture.detectChanges();

    // Act
    const switchBtn = el.querySelector('.setup-account-step__switch-provider-btn') as HTMLButtonElement;
    switchBtn.click();
    // Absorb the re-triggered validate request
    fixture.detectChanges();
    httpMock.expectOne('/api/accounts/validate-token').flush(VALID_RESULT);
    fixture.detectChanges();

    // Assert — status region is not silent (re-triggered result is shown)
    const statusRegion = el.querySelector('[role="status"]');
    expect(statusRegion?.textContent?.trim()).toBeTruthy();
  });

  it('should clear the switch notice when the token input is edited after switching', () => {
    // Arrange — switch provider, which may show a notice
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const tokenInput = el.querySelector('input[id="setup-token"]') as HTMLInputElement;
    tokenInput.value = 'glpat_wrong';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
    httpMock.expectOne('/api/accounts/validate-token').flush(PROVIDER_MISMATCH_RESULT);
    fixture.detectChanges();

    const switchBtn = el.querySelector('.setup-account-step__switch-provider-btn') as HTMLButtonElement;
    switchBtn.click();
    fixture.detectChanges();
    // Absorb re-triggered validation
    const pending = httpMock.match('/api/accounts/validate-token');
    pending.forEach(r => r.flush(VALID_RESULT));
    fixture.detectChanges();

    // Act — edit token; notice should be cleared
    tokenInput.value = 'new_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Assert — no switch notice text (stale resolution cleared, region empty or just idle)
    const statusRegion = el.querySelector('[role="status"]');
    expect(statusRegion?.textContent).not.toContain('Provider switched');
  });

  // Step 6: Saving → label is Creating..., fd-spinner present, aria-disabled="true", NOT natively disabled
  it('should show Creating... label, an fd-spinner, aria-disabled="true", and no native disabled while saving', () => {
    // Arrange — get the component into saving state by clicking Create after resolving
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const tokenInput = el.querySelector('input[id="setup-token"]') as HTMLInputElement;
    tokenInput.value = 'ghp_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
    httpMock.expectOne('/api/accounts/validate-token').flush(VALID_RESULT);
    fixture.detectChanges();

    // Act — click Create; do NOT flush the POST yet (saving state is active)
    const btn = el.querySelector('button.setup-account-step__create-btn') as HTMLButtonElement;
    btn.click();
    fixture.detectChanges();

    // Assert — label
    expect(btn.textContent).toContain('Creating...');

    // Assert — spinner present inside the button
    expect(btn.querySelector('fd-spinner')).toBeTruthy();

    // Assert — aria-disabled="true" (focusable while saving)
    expect(btn.getAttribute('aria-disabled')).toBe('true');

    // Assert — NOT natively disabled
    expect(btn.disabled).toBe(false);

    // Cleanup
    httpMock.expectOne('/api/accounts').flush(CREATED_ACCOUNT_RESULT);
  });

  // Step 6: Form not yet valid (and not saving) → native disabled, no aria-disabled
  it('should have native disabled and no aria-disabled when form is not yet valid and not saving', () => {
    // Arrange / Act
    const { fixture } = setup();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    // Assert — native disabled when form is invalid and not saving
    const btn = el.querySelector('button.setup-account-step__create-btn') as HTMLButtonElement;
    expect(btn.disabled).toBe(true);
    expect(btn.getAttribute('aria-disabled')).toBeNull();
  });

  // Step 6: double-request guard — clicking twice while saving fires createAccount exactly once
  it('should fire the create request exactly once when the Create button is clicked twice while saving', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const tokenInput = el.querySelector('input[id="setup-token"]') as HTMLInputElement;
    tokenInput.value = 'ghp_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
    httpMock.expectOne('/api/accounts/validate-token').flush(VALID_RESULT);
    fixture.detectChanges();

    // Act — click Create twice (second click should be a no-op guard)
    const btn = el.querySelector('button.setup-account-step__create-btn') as HTMLButtonElement;
    btn.click();
    fixture.detectChanges();
    btn.click();
    fixture.detectChanges();

    // Assert — exactly one POST to /api/accounts
    const reqs = httpMock.match('/api/accounts');
    expect(reqs.length).toBe(1);

    // Cleanup
    reqs[0].flush(CREATED_ACCOUNT_RESULT);
  });

  // Step 6: aria-disabled="true" styling parity — attribute present when saving
  it('should have aria-disabled="true" on create button while saving (dimmed/not-allowed treatment)', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const tokenInput = el.querySelector('input[id="setup-token"]') as HTMLInputElement;
    tokenInput.value = 'ghp_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
    httpMock.expectOne('/api/accounts/validate-token').flush(VALID_RESULT);
    fixture.detectChanges();

    // Act — click Create
    const btn = el.querySelector('button.setup-account-step__create-btn') as HTMLButtonElement;
    btn.click();
    fixture.detectChanges();

    // Assert — aria-disabled attribute present (CSS rule targets [aria-disabled="true"])
    expect(btn.getAttribute('aria-disabled')).toBe('true');

    // Cleanup
    httpMock.expectOne('/api/accounts').flush(CREATED_ACCOUNT_RESULT);
  });

  // Finding 4: unknown kind — fallback @default renders error message, Create disabled
  it('should render a fallback error message and keep Create disabled when kind is unknown', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const tokenInput = el.querySelector('input[id="setup-token"]') as HTMLInputElement;
    tokenInput.value = 'some_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
    const unknownResult: TokenValidationResult = {
      kind: 'unknownKindFromFuture' as TokenValidationResult['kind'],
      missingScopes: [],
      accountName: null,
      detectedProvider: null,
      wrongKind: null,
      notAllowed: [],
      broadReach: [],
      tokenExpiry: { status: 'notChecked', expiresAt: null },
    };
    httpMock.expectOne('/api/accounts/validate-token').flush(unknownResult);
    fixture.detectChanges();

    // Assert — a message is shown (not silent)
    const statusRegion = el.querySelector('[role="status"]');
    expect(statusRegion?.textContent?.trim()).toBeTruthy();

    // Assert — Create button disabled
    const btn = el.querySelector('button.setup-account-step__create-btn') as HTMLButtonElement;
    expect(btn.disabled).toBe(true);
  });

  // FIX 4: root container exposes role="region" and aria-label so AT can track aria-busy
  it('should expose role="region" and aria-label="Add account" on the root container', () => {
    // Arrange / Act
    const { fixture } = setup();
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const container = el.querySelector('.setup-account-step') as HTMLElement;
    expect(container.getAttribute('role')).toBe('region');
    expect(container.getAttribute('aria-label')).toBe('Add account');
  });

  it('should set aria-busy="true" on the root container while saving', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const tokenInput = el.querySelector('input[id="setup-token"]') as HTMLInputElement;
    tokenInput.value = 'ghp_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
    httpMock.expectOne('/api/accounts/validate-token').flush({
      kind: 'authenticated', missingScopes: [], accountName: 'octocat', detectedProvider: null,
    });
    fixture.detectChanges();

    // Act — click Create; do NOT flush the POST so saving state stays active
    const btn = el.querySelector('button.setup-account-step__create-btn') as HTMLButtonElement;
    btn.click();
    fixture.detectChanges();

    // Assert
    const container = el.querySelector('.setup-account-step') as HTMLElement;
    expect(container.getAttribute('aria-busy')).toBe('true');

    // Cleanup
    httpMock.expectOne('/api/accounts').flush({
      credential: CREATED_ACCOUNT,
      affectedRepositories: [],
    });
  });

  it('should not set aria-busy on the root container when not saving', () => {
    // Arrange / Act
    const { fixture } = setup();
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const container = el.querySelector('.setup-account-step') as HTMLElement;
    expect(container.getAttribute('aria-busy')).toBeNull();
  });

  it('should include a visually-hidden "Error:" prefix in authenticationFailed error message', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const tokenInput = el.querySelector('input[id="setup-token"]') as HTMLInputElement;
    tokenInput.value = 'bad_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    tokenInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
    httpMock.expectOne('/api/accounts/validate-token').flush(AUTH_FAIL_RESULT);
    fixture.detectChanges();

    // Assert — sr-only prefix present
    const statusRegion = el.querySelector('[role="status"]');
    const srOnly = statusRegion?.querySelector('.sr-only');
    expect(srOnly?.textContent?.trim()).toBe('Error:');
  });

  describe('token requirements', () => {
    const GITHUB_REQUIREMENTS: TokenRequirements = {
      providerType: 'github',
      tokenTypeLabel: 'GitHub fine-grained personal access token',
      permissions: [
        { label: 'Contents (read and write)', verification: 'probe' },
        { label: 'Workflows (read and write)', verification: 'unverifiable' },
      ],
      creationUrlTemplate: '{baseUrl}/settings/personal-access-tokens/new?name=Foundry',
      resourceOwnerHint: null,
      closingNote: null,
      creationNote: null,
    };
    const GITLAB_REQUIREMENTS: TokenRequirements = {
      providerType: 'gitlab',
      tokenTypeLabel: 'GitLab personal access token',
      permissions: [
        { label: 'api', verification: 'scope' },
        { label: 'Maintainer role', verification: 'role' },
      ],
      creationUrlTemplate: '{baseUrl}/-/user_settings/personal_access_tokens',
      resourceOwnerHint: null,
      closingNote: null,
      creationNote: null,
    };

    it('should show the classic-token error with a create link and keep Create disabled for wrongTokenKind', async () => {
      // Arrange
      const { fixture, httpMock } = setup();
      fixture.detectChanges();
      httpMock.expectOne('/api/providers/github/token-requirements').flush(GITHUB_REQUIREMENTS);
      await new Promise((resolve) => setTimeout(resolve));
      fixture.detectChanges();
      const el = fixture.nativeElement as HTMLElement;
      const tokenInput = el.querySelector('input[id="setup-token"]') as HTMLInputElement;
      tokenInput.value = 'classic_token';
      tokenInput.dispatchEvent(new Event('input'));
      fixture.detectChanges();

      // Act
      tokenInput.dispatchEvent(new Event('blur'));
      fixture.detectChanges();
      httpMock
        .expectOne('/api/accounts/validate-token')
        .flush({ ...VALID_RESULT, kind: 'wrongTokenKind', wrongKind: 'github-classic' });
      fixture.detectChanges();

      // Assert
      const msg = el.querySelector('[role="status"] .setup-account-step__validation-message--error');
      expect(msg?.querySelector('.sr-only')?.textContent).toBe('Error: ');
      expect(msg?.textContent).toContain(
        'This is a classic token. Foundry needs a fine-grained personal access token (it starts with github_pat_).',
      );
      const link = msg?.querySelector<HTMLAnchorElement>('a.setup-account-step__validation-link');
      expect(link?.getAttribute('href')).toBe(
        GITHUB_REQUIREMENTS.creationUrlTemplate.replace('{baseUrl}', 'https://github.com'),
      );
      expect(link?.getAttribute('target')).toBe('_blank');
      expect(link?.getAttribute('rel')).toBe('noopener noreferrer');
      expect(link?.textContent?.replace(/\s+/g, ' ').trim()).toBe('Create a fine-grained token on github.com ↗(opens in a new tab)');
      expect((el.querySelector('button.setup-account-step__create-btn') as HTMLButtonElement).disabled).toBe(true);
    });

    it('should show the GitHub requirements list between Base URL and Token', async () => {
      // Arrange
      const { fixture, httpMock } = setup();
      fixture.detectChanges();

      // Act
      httpMock.expectOne('/api/providers/github/token-requirements').flush(GITHUB_REQUIREMENTS);
      await new Promise((resolve) => setTimeout(resolve));
      fixture.detectChanges();

      // Assert
      const el = fixture.nativeElement as HTMLElement;
      const list = el.querySelector('fd-token-requirements');
      expect(list?.querySelector('h3')?.id).toBe('setup-req-heading');
      const labels = Array.from(el.querySelectorAll('.token-requirements__label'), (label) => label.textContent);
      expect(labels).toEqual(['Contents (read and write)', 'Workflows (read and write)']);
      const baseUrl = el.querySelector('#setup-base-url')!;
      const token = el.querySelector('#setup-token')!;
      expect(baseUrl.compareDocumentPosition(list!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
      expect(list!.compareDocumentPosition(token) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    });

    it('should load the GitLab requirements when the provider changes to GitLab', async () => {
      // Arrange
      const { fixture, httpMock } = setup();
      fixture.detectChanges();
      httpMock.expectOne('/api/providers/github/token-requirements').flush(GITHUB_REQUIREMENTS);
      await new Promise((resolve) => setTimeout(resolve));
      const el = fixture.nativeElement as HTMLElement;

      // Act
      const radios = el.querySelectorAll('input[type="radio"]') as NodeListOf<HTMLInputElement>;
      Array.from(radios).find((r) => r.value === 'GitLab')!.click();
      fixture.detectChanges();
      httpMock.expectOne('/api/providers/gitlab/token-requirements').flush(GITLAB_REQUIREMENTS);
      await new Promise((resolve) => setTimeout(resolve));
      fixture.detectChanges();

      // Assert
      const labels = Array.from(el.querySelectorAll('.token-requirements__label'), (label) => label.textContent);
      expect(labels).toEqual(['api', 'Maintainer role']);
      expect(el.querySelector('.token-requirements__note')?.textContent).toContain('set on each repository, not on the token');
    });

    it('should show no requirements list when the requirements fail to load', async () => {
      // Arrange
      const { fixture, httpMock } = setup();
      fixture.detectChanges();

      // Act
      httpMock.expectOne('/api/providers/github/token-requirements').flush('', { status: 404, statusText: 'Not Found' });
      await fixture.whenStable();
      fixture.detectChanges();

      // Assert
      expect((fixture.nativeElement as HTMLElement).querySelector('fd-token-requirements')).toBeNull();
    });
  });
});

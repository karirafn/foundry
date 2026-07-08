import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { SetupAccountStepComponent } from './setup-account-step';
import { AccountSummary, TokenValidationResult } from '../../settings/accounts/account.model';

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

describe('SetupAccountStepComponent', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

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
    expect(req.request.body).toEqual({ token: 'ghp_token', baseUrl: 'https://github.com' });

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
    expect(req.request.body).toEqual({ token: 'ghp_pasted_token', baseUrl: 'https://github.com' });

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
    req.flush(CREATED_ACCOUNT);
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
    req.flush({ ...CREATED_ACCOUNT, providerType: 'GitLab' });
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
    httpMock.expectOne('/api/accounts').flush(CREATED_ACCOUNT);
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
    expect(statusRegion?.textContent).toContain('Missing required scopes: repo, workflow');
  });

  // Valid but no identity message
  it('should render "Token is valid, but identity could not be resolved" when accountName is null', () => {
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
    httpMock.expectOne('/api/accounts/validate-token').flush(VALID_NULL_IDENTITY_RESULT);
    fixture.detectChanges();

    // Assert
    const statusRegion = el.querySelector('[role="status"]');
    expect(statusRegion?.textContent).toContain('Token is valid, but the account identity could not be resolved from the provider');
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
});

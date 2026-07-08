import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { SetupAccountStepComponent } from './setup-account-step';
import { AccountSummary } from '../../settings/accounts/account.model';

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
  name: 'My GitHub',
  providerType: 'GitHub',
  baseUrl: 'https://github.com',
  hasToken: true,
};

describe('SetupAccountStepComponent', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  // Cycle 1: renders required form fields and action buttons
  it('should render account name input, token input, and action buttons', () => {
    // Arrange
    const { fixture } = setup();

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('input[id="setup-name"]')).toBeTruthy();
    expect(el.querySelector('input[id="setup-token"]')).toBeTruthy();
    expect(el.querySelector('button.setup-account-step__create-btn')).toBeTruthy();
    expect(el.querySelector('button.setup-account-step__back-btn')).toBeTruthy();
  });

  // Cycle 2: Create button is disabled when required fields are empty
  it('should disable the Create Account button when name and token are empty', () => {
    // Arrange
    const { fixture } = setup();

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const btn = el.querySelector('button.setup-account-step__create-btn') as HTMLButtonElement;
    expect(btn.disabled).toBe(true);
  });

  // Cycle 3: Create button is enabled when name and token are filled
  it('should enable the Create Account button when name and token are provided', () => {
    // Arrange
    const { fixture } = setup();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    // Act
    const nameInput = el.querySelector('input[id="setup-name"]') as HTMLInputElement;
    nameInput.value = 'My GitHub';
    nameInput.dispatchEvent(new Event('input'));

    const tokenInput = el.querySelector('input[id="setup-token"]') as HTMLInputElement;
    tokenInput.value = 'ghp_token';
    tokenInput.dispatchEvent(new Event('input'));

    fixture.detectChanges();

    // Assert
    const btn = el.querySelector('button.setup-account-step__create-btn') as HTMLButtonElement;
    expect(btn.disabled).toBe(false);
  });

  // Cycle 4: clicking Create Account calls AccountService.createAccount()
  it('should call AccountService.createAccount() when the Create Account button is clicked', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    const nameInput = el.querySelector('input[id="setup-name"]') as HTMLInputElement;
    nameInput.value = 'My GitHub';
    nameInput.dispatchEvent(new Event('input'));

    const tokenInput = el.querySelector('input[id="setup-token"]') as HTMLInputElement;
    tokenInput.value = 'ghp_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Act
    const btn = el.querySelector('button.setup-account-step__create-btn') as HTMLButtonElement;
    btn.click();
    fixture.detectChanges();

    // Assert
    const req = httpMock.expectOne('/api/accounts');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      providerType: 'GitHub',
      baseUrl: 'https://github.com',
      token: 'ghp_token',
    });

    // Cleanup
    req.flush(CREATED_ACCOUNT);
  });

  // Cycle 4b: selecting GitLab sends correct providerType and baseUrl
  it('should send GitLab providerType and baseUrl when GitLab provider is selected', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    const radios = el.querySelectorAll('input[type="radio"]') as NodeListOf<HTMLInputElement>;
    const gitlabRadio = Array.from(radios).find((r) => r.value === 'GitLab')!;
    gitlabRadio.click();
    fixture.detectChanges();

    const nameInput = el.querySelector('input[id="setup-name"]') as HTMLInputElement;
    nameInput.value = 'My GitLab';
    nameInput.dispatchEvent(new Event('input'));

    const tokenInput = el.querySelector('input[id="setup-token"]') as HTMLInputElement;
    tokenInput.value = 'glpat_token';
    tokenInput.dispatchEvent(new Event('input'));
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

  // Cycle 5: emits complete with account ID on successful create
  it('should emit the complete event with the account ID after a successful create', () => {
    // Arrange
    const { fixture, component, httpMock } = setup();
    fixture.detectChanges();

    let emittedId: string | undefined;
    component.complete.subscribe((id: string) => (emittedId = id));

    const el = fixture.nativeElement as HTMLElement;
    const nameInput = el.querySelector('input[id="setup-name"]') as HTMLInputElement;
    nameInput.value = 'My GitHub';
    nameInput.dispatchEvent(new Event('input'));

    const tokenInput = el.querySelector('input[id="setup-token"]') as HTMLInputElement;
    tokenInput.value = 'ghp_token';
    tokenInput.dispatchEvent(new Event('input'));
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

  // Cycle 6: back button emits back output
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

  // Cycle 7: shows save error on failure
  it('should display an error message when the create request fails', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    const nameInput = el.querySelector('input[id="setup-name"]') as HTMLInputElement;
    nameInput.value = 'My GitHub';
    nameInput.dispatchEvent(new Event('input'));

    const tokenInput = el.querySelector('input[id="setup-token"]') as HTMLInputElement;
    tokenInput.value = 'ghp_token';
    tokenInput.dispatchEvent(new Event('input'));
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

  // Cycle 8: does not emit complete on failure
  it('should not emit the complete event when the create request fails', () => {
    // Arrange
    const { fixture, component, httpMock } = setup();
    fixture.detectChanges();

    let emitted = false;
    component.complete.subscribe(() => (emitted = true));

    const el = fixture.nativeElement as HTMLElement;
    const nameInput = el.querySelector('input[id="setup-name"]') as HTMLInputElement;
    nameInput.value = 'My GitHub';
    nameInput.dispatchEvent(new Event('input'));

    const tokenInput = el.querySelector('input[id="setup-token"]') as HTMLInputElement;
    tokenInput.value = 'ghp_token';
    tokenInput.dispatchEvent(new Event('input'));
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

  // Cycle 8b: does not emit complete when saveSuccess is already true from a previous unrelated save
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

  // Cycle 9: shows validation result from service
  it('should show a validation success message after a successful token validation', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    const tokenInput = el.querySelector('input[id="setup-token"]') as HTMLInputElement;
    tokenInput.value = 'ghp_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Act
    const validateBtn = el.querySelector('button.setup-account-step__validate-btn') as HTMLButtonElement;
    validateBtn.click();
    fixture.detectChanges();

    httpMock.expectOne('/api/accounts/validate-token').flush({
      isValid: true,
      isAuthFailure: false,
      missingScopes: [],
    });
    fixture.detectChanges();

    // Assert
    const validationResult = el.querySelector('.setup-account-step__validation-result');
    expect(validationResult?.textContent).toContain('valid');
  });

  // Cycle 11: aria-live validation message span is permanently in the DOM
  it('should keep the validation message span in the DOM with empty text when no result is present', () => {
    // Arrange
    const { fixture } = setup();

    // Act
    fixture.detectChanges();

    // Assert — message span is always rendered so screen readers can track text changes
    const el = fixture.nativeElement as HTMLElement;
    const messageSpan = el.querySelector('.setup-account-step__validation-message');
    expect(messageSpan).toBeTruthy();
    expect(messageSpan?.textContent?.trim()).toBe('');
  });

  // Cycle 10: Validate button calls AccountService.validateToken()
  it('should call AccountService.validateToken() when the Validate Token button is clicked', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    const tokenInput = el.querySelector('input[id="setup-token"]') as HTMLInputElement;
    tokenInput.value = 'ghp_token';
    tokenInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Act
    const validateBtn = el.querySelector('button.setup-account-step__validate-btn') as HTMLButtonElement;
    validateBtn.click();
    fixture.detectChanges();

    // Assert
    const req = httpMock.expectOne('/api/accounts/validate-token');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      token: 'ghp_token',
      baseUrl: 'https://github.com',
    });

    // Cleanup
    req.flush({ isValid: true, isAuthFailure: false, missingScopes: [] });
  });

  // Cycle 12: inner radiogroup inside fd-provider-selector is labelled via aria-labelledby
  it('should associate the Provider label with the inner radiogroup via aria-labelledby', () => {
    // Arrange
    const { fixture } = setup();

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const labelSpan = el.querySelector('#setup-provider-label');
    expect(labelSpan).toBeTruthy();
    const radiogroup = el.querySelector('[role="radiogroup"]');
    expect(radiogroup?.getAttribute('aria-labelledby')).toBe('setup-provider-label');
  });

  // Cycle 13: setup-token-validation and setup-save-error ids are present (no collision with account-form)
  it('should have setup-token-validation and setup-save-error element ids', () => {
    // Arrange
    const { fixture } = setup();

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('#setup-token-validation')).toBeTruthy();
    expect(el.querySelector('#setup-save-error')).toBeTruthy();
  });

  it('should reference setup-token-validation and setup-save-error in token input aria-describedby', () => {
    // Arrange
    const { fixture } = setup();

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const tokenInput = el.querySelector('input[id="setup-token"]');
    expect(tokenInput?.getAttribute('aria-describedby')).toBe('setup-token-validation setup-save-error');
  });
});

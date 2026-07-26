import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SettingsAccountsComponent } from './settings-accounts';
import { AccountService } from './account.service';
import { AccountSummary, AffectedRepository, CredentialUpdateResult } from './account.model';

function makeUpdateResult(account: AccountSummary, affected: AffectedRepository[] = []): CredentialUpdateResult {
  return { credential: account, affectedRepositories: affected };
}

function setup() {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    imports: [SettingsAccountsComponent],
    providers: [
      AccountService,
      provideHttpClient(),
      provideHttpClientTesting(),
    ],
  });

  const fixture = TestBed.createComponent(SettingsAccountsComponent);
  const httpMock = TestBed.inject(HttpTestingController);

  return { fixture, httpMock };
}

function flushAccounts(httpMock: HttpTestingController, accounts: object[] = []): void {
  httpMock.expectOne('/api/accounts').flush(accounts);
}

function flushTokenRequirements(httpMock: HttpTestingController, provider: string = 'github'): void {
  const req = httpMock.match(`/api/providers/${provider}/token-requirements`);
  req.forEach(r => r.flush({
    providerType: provider,
    tokenTypeLabel: 'Personal Access Token',
    scopes: ['repo'],
    creationUrlTemplate: 'https://github.com/settings/tokens/new',
  }));
}

describe('SettingsAccountsComponent', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true });
  });

  it('should call loadAccounts on initialization', () => {
    // Arrange
    const { fixture, httpMock } = setup();

    // Act
    fixture.detectChanges();

    // Assert
    const req = httpMock.expectOne('/api/accounts');
    req.flush([]);
  });

  it('should render fd-account-list in list view', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    flushAccounts(httpMock);

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const accountList = el.querySelector('fd-account-list');
    expect(accountList).toBeTruthy();
  });

  it('should render fd-account-form without account input when Add Account is clicked', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    flushAccounts(httpMock);
    fixture.detectChanges();

    // Act
    fixture.componentInstance.onAddAccount();
    fixture.detectChanges();
    flushTokenRequirements(httpMock);

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const form = el.querySelector('fd-account-form');
    expect(form).toBeTruthy();
    const accountList = el.querySelector('fd-account-list');
    expect(accountList).toBeFalsy();
  });

  it('should show fd-account-list after cancelling from the form', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    flushAccounts(httpMock);
    fixture.detectChanges();
    fixture.componentInstance.onAddAccount();
    fixture.detectChanges();
    flushTokenRequirements(httpMock);

    // Act
    fixture.componentInstance.onAccountCancelled();
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const accountList = el.querySelector('fd-account-list');
    expect(accountList).toBeTruthy();
    const form = el.querySelector('fd-account-form');
    expect(form).toBeFalsy();
  });

  it('should display load error in account list when accounts fail to load', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();

    // Act
    httpMock.expectOne('/api/accounts').flush('Unauthorized', {
      status: 401,
      statusText: 'Unauthorized',
    });
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const errorEl = el.querySelector('.account-list__error');
    expect(errorEl).toBeTruthy();
  });

  it('should display delete error in accounts section when deleteAccount fails', () => {
    // Arrange
    const account: AccountSummary = {
      id: '1',
      name: 'My Org',
      providerType: 'GitHub',
      baseUrl: 'https://github.com',
      hasToken: true,
      namespaces: [],
    };
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    flushAccounts(httpMock, [account]);
    fixture.detectChanges();

    // Act
    const accountService = TestBed.inject(AccountService);
    accountService.deleteAccount(account.id);
    httpMock.expectOne(`/api/accounts/${account.id}`).flush('Account is in use.', {
      status: 409,
      statusText: 'Conflict',
    });
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const deleteError = el.querySelector('.accounts-settings__delete-error');
    expect(deleteError).toBeTruthy();
    expect(deleteError?.getAttribute('role')).toBe('alert');
    expect(deleteError?.textContent).toContain('Account is in use.');
  });

  it('should render delete error container even when no delete error is present', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    flushAccounts(httpMock);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const deleteError = el.querySelector('.accounts-settings__delete-error');
    expect(deleteError).toBeTruthy();
    expect(deleteError?.textContent?.trim()).toBeFalsy();
  });

  it('should render fd-account-form with account when Edit is clicked', () => {
    // Arrange
    const account: AccountSummary = {
      id: '1',
      name: 'My Org',
      providerType: 'GitHub',
      baseUrl: 'https://github.com',
      hasToken: true,
      namespaces: [],
    };
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    flushAccounts(httpMock, [account]);
    fixture.detectChanges();

    // Act
    fixture.componentInstance.onEditAccount(account);
    fixture.detectChanges();
    flushTokenRequirements(httpMock);

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const form = el.querySelector('fd-account-form');
    expect(form).toBeTruthy();
    const accountList = el.querySelector('fd-account-list');
    expect(accountList).toBeFalsy();
  });

  it('should render the Accounts section title', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    flushAccounts(httpMock);

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const title = el.querySelector('.accounts-settings__section-title');
    expect(title?.textContent?.trim()).toBe('Accounts');
  });

  it('should call createAccount when saving from add mode', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    flushAccounts(httpMock);
    fixture.detectChanges();
    fixture.componentInstance.onAddAccount();
    fixture.detectChanges();
    flushTokenRequirements(httpMock);

    // Act
    fixture.componentInstance.onSaveNewAccount({
      providerType: 'GitHub',
      baseUrl: 'https://github.com',
      token: 'ghp_test',
    });

    // Assert
    const req = httpMock.expectOne('/api/accounts');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      providerType: 'GitHub',
      baseUrl: 'https://github.com',
      token: 'ghp_test',
    });
    req.flush({
      credential: {
        id: '2',
        name: 'New Account',
        providerType: 'GitHub',
        baseUrl: 'https://github.com',
        hasToken: true,
        namespaces: [],
      },
      affectedRepositories: [],
    });
  });

  it('should call updateAccount when saving from edit mode', () => {
    // Arrange
    const account: AccountSummary = {
      id: '1',
      name: 'My Org',
      providerType: 'GitHub',
      baseUrl: 'https://github.com',
      hasToken: true,
      namespaces: [],
    };
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    flushAccounts(httpMock, [account]);
    fixture.detectChanges();
    fixture.componentInstance.onEditAccount(account);
    fixture.detectChanges();
    flushTokenRequirements(httpMock);

    // Act
    fixture.componentInstance.onSaveExistingAccount({
      baseUrl: 'https://github.com',
    });

    // Assert
    const req = httpMock.expectOne(`/api/accounts/${account.id}`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({
      baseUrl: 'https://github.com',
    });
    req.flush(makeUpdateResult({
      id: '1',
      name: 'Updated Org',
      providerType: 'GitHub',
      baseUrl: 'https://github.com',
      hasToken: true,
      namespaces: [],
    }));
  });

  it('should return to list view and reload accounts after successful save', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    flushAccounts(httpMock);
    fixture.detectChanges();
    fixture.componentInstance.onAddAccount();
    fixture.detectChanges();
    flushTokenRequirements(httpMock);

    // Act
    fixture.componentInstance.onSaveNewAccount({
      providerType: 'GitHub',
      baseUrl: 'https://github.com',
      token: 'ghp_test',
    });
    httpMock.expectOne('/api/accounts').flush({
      credential: {
        id: '2',
        name: 'New Account',
        providerType: 'GitHub',
        baseUrl: 'https://github.com',
        hasToken: true,
        namespaces: [],
      },
      affectedRepositories: [],
    });
    fixture.detectChanges();

    // Assert - should be back in list view and loadAccounts called again
    const el = fixture.nativeElement as HTMLElement;
    const accountList = el.querySelector('fd-account-list');
    expect(accountList).toBeTruthy();
    const form = el.querySelector('fd-account-form');
    expect(form).toBeFalsy();

    // Flush the reload request
    const reloadReq = httpMock.expectOne('/api/accounts');
    reloadReq.flush([]);
  });

  it('should render fd-affected-repositories panel when affected repos are present', () => {
    // Arrange
    const account: AccountSummary = {
      id: '1',
      name: 'My Org',
      providerType: 'GitHub',
      baseUrl: 'https://github.com',
      hasToken: true,
      namespaces: [],
    };
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    flushAccounts(httpMock, [account]);
    fixture.detectChanges();
    fixture.componentInstance.onEditAccount(account);
    fixture.detectChanges();
    flushTokenRequirements(httpMock);
    fixture.componentInstance.onSaveExistingAccount({ baseUrl: 'https://github.com' });
    const affected: AffectedRepository[] = [
      { id: 'repo-1', slug: 'org/api', previousStatus: 'eligible', newStatus: 'ineligible' },
    ];
    httpMock.expectOne(`/api/accounts/${account.id}`).flush(makeUpdateResult(account, affected));
    fixture.detectChanges();
    // flush reload
    httpMock.expectOne('/api/accounts').flush([account]);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const panel = el.querySelector('fd-affected-repositories');
    expect(panel).toBeTruthy();
  });

  it('should not render fd-affected-repositories panel when no repos are affected', () => {
    // Arrange
    const account: AccountSummary = {
      id: '1',
      name: 'My Org',
      providerType: 'GitHub',
      baseUrl: 'https://github.com',
      hasToken: true,
      namespaces: [],
    };
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    flushAccounts(httpMock, [account]);
    fixture.detectChanges();
    fixture.componentInstance.onEditAccount(account);
    fixture.detectChanges();
    flushTokenRequirements(httpMock);
    fixture.componentInstance.onSaveExistingAccount({ baseUrl: 'https://github.com' });
    httpMock.expectOne(`/api/accounts/${account.id}`).flush(makeUpdateResult(account, []));
    fixture.detectChanges();
    // flush reload
    httpMock.expectOne('/api/accounts').flush([account]);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const panel = el.querySelector('fd-affected-repositories');
    expect(panel).toBeFalsy();
  });

  it('should dismiss affected repositories panel when clearAffectedRepositories is called', () => {
    // Arrange
    const account: AccountSummary = {
      id: '1',
      name: 'My Org',
      providerType: 'GitHub',
      baseUrl: 'https://github.com',
      hasToken: true,
      namespaces: [],
    };
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    flushAccounts(httpMock, [account]);
    fixture.detectChanges();
    fixture.componentInstance.onEditAccount(account);
    fixture.detectChanges();
    flushTokenRequirements(httpMock);
    fixture.componentInstance.onSaveExistingAccount({ baseUrl: 'https://github.com' });
    const affected: AffectedRepository[] = [
      { id: 'repo-1', slug: 'org/api', previousStatus: 'eligible', newStatus: 'ineligible' },
    ];
    httpMock.expectOne(`/api/accounts/${account.id}`).flush(makeUpdateResult(account, affected));
    fixture.detectChanges();
    httpMock.expectOne('/api/accounts').flush([account]);
    fixture.detectChanges();

    // Act
    TestBed.inject(AccountService).clearAffectedRepositories();
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const panel = el.querySelector('fd-affected-repositories');
    expect(panel).toBeFalsy();
  });

  it('should move focus to section heading when the affected-repositories panel is dismissed', async () => {
    // Arrange
    const account: AccountSummary = {
      id: '1',
      name: 'My Org',
      providerType: 'GitHub',
      baseUrl: 'https://github.com',
      hasToken: true,
      namespaces: [],
    };
    const { fixture, httpMock } = setup();
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();
    flushAccounts(httpMock, [account]);
    fixture.detectChanges();
    fixture.componentInstance.onEditAccount(account);
    fixture.detectChanges();
    flushTokenRequirements(httpMock);
    fixture.componentInstance.onSaveExistingAccount({ baseUrl: 'https://github.com' });
    const affected: AffectedRepository[] = [
      { id: 'repo-1', slug: 'org/api', previousStatus: 'eligible', newStatus: 'ineligible' },
    ];
    httpMock.expectOne(`/api/accounts/${account.id}`).flush(makeUpdateResult(account, affected));
    fixture.detectChanges();
    httpMock.expectOne('/api/accounts').flush([account]);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const sectionHeading = el.querySelector('.accounts-settings__section-title') as HTMLElement;

    // Act
    fixture.componentInstance.onDismissAffectedRepositories();
    await fixture.whenStable();
    fixture.detectChanges();

    // Assert
    expect(document.activeElement).toBe(sectionHeading);
    document.body.removeChild(fixture.nativeElement);
  });

  it('should call deleteAccount with confirmation when onDeleteAccount is invoked', () => {
    // Arrange
    const account: AccountSummary = {
      id: '1',
      name: 'My Org',
      providerType: 'GitHub',
      baseUrl: 'https://github.com',
      hasToken: true,
      namespaces: [],
    };
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    flushAccounts(httpMock, [account]);
    fixture.detectChanges();
    vi.spyOn(window, 'confirm').mockReturnValue(true);

    // Act
    fixture.componentInstance.onDeleteAccount(account);

    // Assert
    const req = httpMock.expectOne(`/api/accounts/${account.id}`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('should render a persistent sr-only aria-live announcer outside the @if panel', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    flushAccounts(httpMock);

    // Act
    fixture.detectChanges();

    // Assert — announcer must always be present, regardless of affected-repositories panel state
    const el = fixture.nativeElement as HTMLElement;
    const announcer = el.querySelector('.accounts-settings__sr-announcer');
    expect(announcer).toBeTruthy();
    expect(announcer?.getAttribute('aria-live')).toBe('polite');
    expect(announcer?.getAttribute('aria-atomic')).toBe('true');
    expect(announcer?.classList).toContain('sr-only');
  });

  it('should populate the sr-announcer when affected repositories appear', () => {
    // Arrange
    const account: AccountSummary = {
      id: '1',
      name: 'My Org',
      providerType: 'GitHub',
      baseUrl: 'https://github.com',
      hasToken: true,
      namespaces: [],
    };
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    flushAccounts(httpMock, [account]);
    fixture.detectChanges();
    fixture.componentInstance.onEditAccount(account);
    fixture.detectChanges();
    flushTokenRequirements(httpMock);
    fixture.componentInstance.onSaveExistingAccount({ baseUrl: 'https://github.com' });
    const affected: AffectedRepository[] = [
      { id: 'repo-1', slug: 'org/api', previousStatus: 'eligible', newStatus: 'ineligible' },
    ];

    // Act
    httpMock.expectOne(`/api/accounts/${account.id}`).flush(makeUpdateResult(account, affected));
    fixture.detectChanges();
    httpMock.expectOne('/api/accounts').flush([account]);
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const announcer = el.querySelector('.accounts-settings__sr-announcer');
    expect(announcer?.textContent).toContain('1');
    expect(announcer?.textContent).toContain('affected');
  });

  it('should not call deleteAccount when confirmation is declined', () => {
    // Arrange
    const account: AccountSummary = {
      id: '1',
      name: 'My Org',
      providerType: 'GitHub',
      baseUrl: 'https://github.com',
      hasToken: true,
      namespaces: [],
    };
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    flushAccounts(httpMock, [account]);
    fixture.detectChanges();
    vi.spyOn(window, 'confirm').mockReturnValue(false);

    // Act
    fixture.componentInstance.onDeleteAccount(account);

    // Assert - no DELETE request should have been made
    httpMock.expectNone(`/api/accounts/${account.id}`);
  });

  it('should pass loaded accounts to [accounts] input on fd-account-form in add view', () => {
    // Arrange
    const account: AccountSummary = {
      id: '1',
      name: 'My Org',
      providerType: 'GitHub',
      baseUrl: 'https://github.com',
      hasToken: true,
      namespaces: [],
    };
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    flushAccounts(httpMock, [account]);
    fixture.detectChanges();

    // Act
    fixture.componentInstance.onAddAccount();
    fixture.detectChanges();
    flushTokenRequirements(httpMock);

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const form = el.querySelector('fd-account-form');
    expect(form).toBeTruthy();
    // The AccountService loaded one account; the form's [accounts] binding reflects that list
    const accountService = TestBed.inject(AccountService);
    expect(accountService.accounts()).toEqual([account]);
  });

  it('should pass loaded accounts to [accounts] input on fd-account-form in edit view', () => {
    // Arrange
    const account: AccountSummary = {
      id: '1',
      name: 'My Org',
      providerType: 'GitHub',
      baseUrl: 'https://github.com',
      hasToken: true,
      namespaces: [],
    };
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    flushAccounts(httpMock, [account]);
    fixture.detectChanges();

    // Act
    fixture.componentInstance.onEditAccount(account);
    fixture.detectChanges();
    flushTokenRequirements(httpMock);

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    const form = el.querySelector('fd-account-form');
    expect(form).toBeTruthy();
    const accountService = TestBed.inject(AccountService);
    expect(accountService.accounts()).toEqual([account]);
  });
});

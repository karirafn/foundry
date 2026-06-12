import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SettingsAccountsComponent } from './settings-accounts';
import { AccountService } from './account.service';
import { AccountSummary } from './account.model';

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

describe('SettingsAccountsComponent', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true });
  });

  it('should call loadAccounts on initialization', () => {
    // Arrange / Act
    const { fixture, httpMock } = setup();
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
    };
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    flushAccounts(httpMock, [account]);
    fixture.detectChanges();

    // Act
    fixture.componentInstance.onEditAccount(account);
    fixture.detectChanges();

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

    // Act
    fixture.componentInstance.onSaveNewAccount({
      name: 'New Account',
      providerType: 'GitHub',
      baseUrl: 'https://github.com',
      token: 'ghp_test',
    });

    // Assert
    const req = httpMock.expectOne('/api/accounts');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      name: 'New Account',
      providerType: 'GitHub',
      baseUrl: 'https://github.com',
      token: 'ghp_test',
    });
    req.flush({
      id: '2',
      name: 'New Account',
      providerType: 'GitHub',
      baseUrl: 'https://github.com',
      hasToken: true,
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
    };
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    flushAccounts(httpMock, [account]);
    fixture.detectChanges();
    fixture.componentInstance.onEditAccount(account);
    fixture.detectChanges();

    // Act
    fixture.componentInstance.onSaveExistingAccount({
      name: 'Updated Org',
      baseUrl: 'https://github.com',
    });

    // Assert
    const req = httpMock.expectOne(`/api/accounts/${account.id}`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({
      name: 'Updated Org',
      baseUrl: 'https://github.com',
    });
    req.flush({
      id: '1',
      name: 'Updated Org',
      providerType: 'GitHub',
      baseUrl: 'https://github.com',
      hasToken: true,
    });
  });

  it('should return to list view and reload accounts after successful save', () => {
    // Arrange
    const { fixture, httpMock } = setup();
    fixture.detectChanges();
    flushAccounts(httpMock);
    fixture.detectChanges();
    fixture.componentInstance.onAddAccount();
    fixture.detectChanges();

    // Act
    fixture.componentInstance.onSaveNewAccount({
      name: 'New Account',
      providerType: 'GitHub',
      baseUrl: 'https://github.com',
      token: 'ghp_test',
    });
    httpMock.expectOne('/api/accounts').flush({
      id: '2',
      name: 'New Account',
      providerType: 'GitHub',
      baseUrl: 'https://github.com',
      hasToken: true,
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

  it('should call deleteAccount with confirmation when onDeleteAccount is invoked', () => {
    // Arrange
    const account: AccountSummary = {
      id: '1',
      name: 'My Org',
      providerType: 'GitHub',
      baseUrl: 'https://github.com',
      hasToken: true,
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

  it('should not call deleteAccount when confirmation is declined', () => {
    // Arrange
    const account: AccountSummary = {
      id: '1',
      name: 'My Org',
      providerType: 'GitHub',
      baseUrl: 'https://github.com',
      hasToken: true,
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
});

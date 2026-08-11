import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AccountService } from './account.service';
import { AccountSummary, AffectedRepository, CreateAccountRequest, CredentialCreationResult, CredentialUpdateResult, NamespaceConflict, TokenRequirements, UpdateAccountRequest } from './account.model';
import { ToastService } from '../../../core/services/toast.service';
import { AccountPresenceService } from '../../../core/services/account-presence.service';

const MOCK_ACCOUNT: AccountSummary = {
  id: '00000000-0000-0000-0000-000000000001',
  name: 'My GitHub',
  providerType: 'github',
  baseUrl: 'https://api.github.com/',
  hasToken: true,
  namespaces: [],
};

const MOCK_ACCOUNT_2: AccountSummary = {
  id: '00000000-0000-0000-0000-000000000002',
  name: 'Work GitHub',
  providerType: 'github',
  baseUrl: 'https://api.github.com/',
  hasToken: true,
  namespaces: [],
};

function makeUpdateResult(account: AccountSummary, affected: AffectedRepository[] = []): CredentialUpdateResult {
  return { credential: account, affectedRepositories: affected };
}

function setupService() {
  TestBed.configureTestingModule({
    providers: [
      AccountService,
      AccountPresenceService,
      provideHttpClient(),
      provideHttpClientTesting(),
    ],
  });
  return {
    service: TestBed.inject(AccountService),
    httpMock: TestBed.inject(HttpTestingController),
    accountPresence: TestBed.inject(AccountPresenceService),
  };
}

describe('AccountService', () => {
  let service: AccountService;
  let httpMock: HttpTestingController;
  let accountPresence: AccountPresenceService;

  beforeEach(() => {
    const setup = setupService();
    service = setup.service;
    httpMock = setup.httpMock;
    accountPresence = setup.accountPresence;
  });

  afterEach(() => httpMock.verify());

  // Cycle 1: initial signal state
  it('should start with empty accounts, loading false, and no errors', () => {
    // Arrange / Act — no calls yet

    // Assert
    expect(service.accounts()).toEqual([]);
    expect(service.loading()).toBe(false);
    expect(service.saving()).toBe(false);
    expect(service.deleting()).toBe(false);
    expect(service.validating()).toBe(false);
    expect(service.saveSuccess()).toBe(false);
    expect(service.validationResult()).toBeNull();
    expect(service.saveError()).toBeNull();
    expect(service.deleteError()).toBeNull();
    expect(service.loadError()).toBeNull();
    expect(service.conflicts()).toEqual([]);
  });

  // Cycle 2: loadAccounts populates accounts signal
  it('should populate accounts after loadAccounts succeeds', () => {
    // Arrange / Act
    service.loadAccounts();
    const req = httpMock.expectOne('/api/accounts');
    req.flush([MOCK_ACCOUNT]);

    // Assert
    expect(service.accounts()).toEqual([MOCK_ACCOUNT]);
    expect(service.loading()).toBe(false);
  });

  // Cycle 3: loadAccounts sets loading true during request
  it('should set loading to true while loadAccounts is in flight', () => {
    // Arrange / Act
    service.loadAccounts();

    // Assert — before flush
    expect(service.loading()).toBe(true);
    httpMock.expectOne('/api/accounts').flush([]);
  });

  it('should set loading to false after loadAccounts succeeds', () => {
    // Arrange
    service.loadAccounts();
    httpMock.expectOne('/api/accounts').flush([MOCK_ACCOUNT]);

    // Assert
    expect(service.loading()).toBe(false);
  });

  it('should set loading to false when loadAccounts fails', () => {
    // Arrange
    service.loadAccounts();
    httpMock.expectOne('/api/accounts').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });

    // Assert
    expect(service.loading()).toBe(false);
  });

  it('should set loadError when loadAccounts fails with a string body', () => {
    // Arrange
    service.loadAccounts();

    // Act
    httpMock.expectOne('/api/accounts').flush('Forbidden', {
      status: 403,
      statusText: 'Forbidden',
    });

    // Assert
    expect(service.loadError()).toBe('Forbidden');
  });

  it('should clear loadError at start of loadAccounts', () => {
    // Arrange — first call that fails
    service.loadAccounts();
    httpMock.expectOne('/api/accounts').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });

    // Act — second call clears error immediately
    service.loadAccounts();

    // Assert — error is cleared before response
    expect(service.loadError()).toBeNull();
    httpMock.expectOne('/api/accounts').flush([]);
  });

  // Cycle 4: loadAccounts loads multiple accounts
  it('should populate multiple accounts', () => {
    // Arrange / Act
    service.loadAccounts();
    httpMock.expectOne('/api/accounts').flush([MOCK_ACCOUNT, MOCK_ACCOUNT_2]);

    // Assert
    expect(service.accounts().length).toBe(2);
    expect(service.accounts()[0].name).toBe('My GitHub');
    expect(service.accounts()[1].name).toBe('Work GitHub');
  });

  function makeCreationResult(account: AccountSummary, affected: AffectedRepository[] = []): CredentialCreationResult {
    return { credential: account, affectedRepositories: affected };
  }

  // Cycle 5: createAccount calls POST /api/accounts
  it('should POST to /api/accounts when createAccount is called', () => {
    // Arrange
    const request: CreateAccountRequest = {
      providerType: 'github',
      baseUrl: 'https://api.github.com',
      token: 'ghp_test',
    };

    // Act
    service.createAccount(request);
    const req = httpMock.expectOne('/api/accounts');

    // Assert
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(request);
    req.flush(makeCreationResult(MOCK_ACCOUNT), { status: 201, statusText: 'Created' });
  });

  it('should set saving to true while createAccount is in flight', () => {
    // Arrange
    const request: CreateAccountRequest = {
      providerType: 'github',
      baseUrl: 'https://api.github.com',
      token: 'ghp_test',
    };

    // Act
    service.createAccount(request);

    // Assert — before flush
    expect(service.saving()).toBe(true);
    httpMock.expectOne('/api/accounts').flush(makeCreationResult(MOCK_ACCOUNT), { status: 201, statusText: 'Created' });
  });

  it('should set saving to false and saveSuccess to true after createAccount succeeds', () => {
    // Arrange
    const request: CreateAccountRequest = {
      providerType: 'github',
      baseUrl: 'https://api.github.com',
      token: 'ghp_test',
    };
    service.createAccount(request);
    httpMock.expectOne('/api/accounts').flush(makeCreationResult(MOCK_ACCOUNT), { status: 201, statusText: 'Created' });

    // Assert
    expect(service.saving()).toBe(false);
    expect(service.saveSuccess()).toBe(true);
  });

  it('should add the new account to accounts signal after createAccount succeeds', () => {
    // Arrange
    service.loadAccounts();
    httpMock.expectOne('/api/accounts').flush([]);

    const request: CreateAccountRequest = {
      providerType: 'github',
      baseUrl: 'https://api.github.com',
      token: 'ghp_test',
    };

    // Act
    service.createAccount(request);
    httpMock.expectOne('/api/accounts').flush(makeCreationResult(MOCK_ACCOUNT), { status: 201, statusText: 'Created' });

    // Assert
    expect(service.accounts()).toContain(MOCK_ACCOUNT);
  });

  it('should set saving to false when createAccount fails', () => {
    // Arrange
    const request: CreateAccountRequest = {
      providerType: 'github',
      baseUrl: 'https://api.github.com',
      token: 'ghp_test',
    };
    service.createAccount(request);
    httpMock.expectOne('/api/accounts').flush('Bad Request', {
      status: 400,
      statusText: 'Bad Request',
    });

    // Assert
    expect(service.saving()).toBe(false);
    expect(service.saveSuccess()).toBe(false);
  });

  it('should set saveError when createAccount fails with a string body', () => {
    // Arrange
    const request: CreateAccountRequest = {
      providerType: 'github',
      baseUrl: 'https://api.github.com',
      token: 'ghp_test',
    };
    service.createAccount(request);

    // Act
    httpMock.expectOne('/api/accounts').flush('An account with this name already exists.', {
      status: 400,
      statusText: 'Bad Request',
    });

    // Assert
    expect(service.saveError()).toBe('An account with this name already exists.');
  });

  it('should clear saveError at start of createAccount', () => {
    // Arrange — first call that fails
    service.createAccount({ providerType: 'github', baseUrl: 'https://api.github.com', token: 'x' });
    httpMock.expectOne('/api/accounts').flush('Bad Request', { status: 400, statusText: 'Bad Request' });

    // Act — second call clears error immediately
    service.createAccount({ providerType: 'github', baseUrl: 'https://api.github.com', token: 'y' });

    // Assert — error is cleared before response
    expect(service.saveError()).toBeNull();
    httpMock.expectOne('/api/accounts').flush(makeCreationResult(MOCK_ACCOUNT), { status: 201, statusText: 'Created' });
  });

  // Conflict (409) handling
  it('should set conflicts signal when createAccount returns 409 with NamespaceConflictResponse', () => {
    // Arrange
    const conflicts: NamespaceConflict[] = [
      { namespace: 'myorg', holderCredentialId: 'cred-1', holderName: 'Old Account' },
    ];
    service.createAccount({ providerType: 'github', baseUrl: 'https://api.github.com', token: 'ghp_test' });

    // Act
    httpMock.expectOne('/api/accounts').flush({ conflicts }, { status: 409, statusText: 'Conflict' });

    // Assert
    expect(service.conflicts()).toEqual(conflicts);
    expect(service.saveError()).toBeNull();
    expect(service.saving()).toBe(false);
  });

  it('should clear conflicts at start of createAccount', () => {
    // Arrange — first call returns conflicts
    const conflicts: NamespaceConflict[] = [
      { namespace: 'myorg', holderCredentialId: 'cred-1', holderName: 'Old Account' },
    ];
    service.createAccount({ providerType: 'github', baseUrl: 'https://api.github.com', token: 'ghp_x' });
    httpMock.expectOne('/api/accounts').flush({ conflicts }, { status: 409, statusText: 'Conflict' });
    expect(service.conflicts().length).toBe(1);

    // Act — second call should clear conflicts immediately
    service.createAccount({ providerType: 'github', baseUrl: 'https://api.github.com', token: 'ghp_y' });

    // Assert — conflicts cleared before response
    expect(service.conflicts()).toEqual([]);
    httpMock.expectOne('/api/accounts').flush(makeCreationResult(MOCK_ACCOUNT), { status: 201, statusText: 'Created' });
  });

  it('should set saveError for 422 TakeoverValidationResponse listing invalid namespaces', () => {
    // Arrange
    const body = { invalidNamespaces: ['ns-a', 'ns-b'] };
    service.createAccount({ providerType: 'github', baseUrl: 'https://api.github.com', token: 'ghp_test' });

    // Act
    httpMock.expectOne('/api/accounts').flush(body, { status: 422, statusText: 'Unprocessable Entity' });

    // Assert
    expect(service.saveError()).toBe('Invalid namespaces for takeover: ns-a, ns-b.');
    expect(service.conflicts()).toEqual([]);
  });

  it('should set affectedRepositories when createAccount succeeds with affected repos', () => {
    // Arrange
    const affected: AffectedRepository[] = [
      { id: 'repo-1', slug: 'org/repo', previousStatus: 'eligible', newStatus: 'ineligible' },
    ];
    service.createAccount({ providerType: 'github', baseUrl: 'https://api.github.com', token: 'ghp_test' });

    // Act
    httpMock.expectOne('/api/accounts').flush(makeCreationResult(MOCK_ACCOUNT, affected), { status: 201, statusText: 'Created' });

    // Assert
    expect(service.affectedRepositories()).toEqual(affected);
  });

  it('should include takeoverNamespaces in POST body when provided', () => {
    // Arrange
    const request: CreateAccountRequest = {
      providerType: 'github',
      baseUrl: 'https://api.github.com',
      token: 'ghp_test',
      takeoverNamespaces: ['myorg'],
    };

    // Act
    service.createAccount(request);
    const req = httpMock.expectOne('/api/accounts');

    // Assert
    expect(req.request.body.takeoverNamespaces).toEqual(['myorg']);
    req.flush(makeCreationResult(MOCK_ACCOUNT), { status: 201, statusText: 'Created' });
  });

  it('should set saveError when updateAccount fails with a string body', () => {
    // Arrange
    const id = MOCK_ACCOUNT.id;
    const request: UpdateAccountRequest = {
      baseUrl: 'https://api.github.com',
      token: null,
    };
    service.updateAccount(id, request);

    // Act
    httpMock.expectOne(`/api/accounts/${id}`).flush('An account with this name already exists.', {
      status: 409,
      statusText: 'Conflict',
    });

    // Assert
    expect(service.saveError()).toBe('An account with this name already exists.');
  });

  it('should clear saveError at start of updateAccount', () => {
    // Arrange — updateAccount call that fails
    service.updateAccount(MOCK_ACCOUNT.id, { baseUrl: 'https://api.github.com', token: null });
    httpMock.expectOne(`/api/accounts/${MOCK_ACCOUNT.id}`).flush('Conflict', {
      status: 409,
      statusText: 'Conflict',
    });

    // Act — second updateAccount call clears error immediately
    service.updateAccount(MOCK_ACCOUNT.id, { baseUrl: 'https://api.github.com', token: null });

    // Assert — error is cleared before response
    expect(service.saveError()).toBeNull();
    httpMock.expectOne(`/api/accounts/${MOCK_ACCOUNT.id}`).flush(makeUpdateResult(MOCK_ACCOUNT));
  });

  // Cycle 6: updateAccount calls PUT /api/accounts/{id}
  it('should PUT to /api/accounts/{id} when updateAccount is called', () => {
    // Arrange
    const id = '00000000-0000-0000-0000-000000000001';
    const request: UpdateAccountRequest = {
      baseUrl: 'https://api.github.com',
      token: 'ghp_updated',
    };

    // Act
    service.updateAccount(id, request);
    const req = httpMock.expectOne(`/api/accounts/${id}`);

    // Assert
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(request);
    req.flush(makeUpdateResult({ ...MOCK_ACCOUNT, name: 'Updated GitHub' }));
  });

  it('should set saving to true while updateAccount is in flight', () => {
    // Arrange
    const id = MOCK_ACCOUNT.id;
    const request: UpdateAccountRequest = {
      baseUrl: 'https://api.github.com',
      token: null,
    };

    // Act
    service.updateAccount(id, request);

    // Assert — before flush
    expect(service.saving()).toBe(true);
    httpMock.expectOne(`/api/accounts/${id}`).flush(makeUpdateResult(MOCK_ACCOUNT));
  });

  it('should set saving to false and saveSuccess to true after updateAccount succeeds', () => {
    // Arrange
    const id = MOCK_ACCOUNT.id;
    const request: UpdateAccountRequest = {
      baseUrl: 'https://api.github.com',
      token: null,
    };
    service.loadAccounts();
    httpMock.expectOne('/api/accounts').flush([MOCK_ACCOUNT]);

    // Act
    service.updateAccount(id, request);
    httpMock.expectOne(`/api/accounts/${id}`).flush(makeUpdateResult({ ...MOCK_ACCOUNT, name: 'Updated' }));

    // Assert
    expect(service.saving()).toBe(false);
    expect(service.saveSuccess()).toBe(true);
  });

  it('should replace the updated account in accounts signal after updateAccount succeeds', () => {
    // Arrange
    service.loadAccounts();
    httpMock.expectOne('/api/accounts').flush([MOCK_ACCOUNT, MOCK_ACCOUNT_2]);

    const updatedAccount: AccountSummary = { ...MOCK_ACCOUNT, name: 'Updated GitHub' };
    const request: UpdateAccountRequest = {
      baseUrl: 'https://api.github.com',
      token: null,
    };

    // Act
    service.updateAccount(MOCK_ACCOUNT.id, request);
    httpMock.expectOne(`/api/accounts/${MOCK_ACCOUNT.id}`).flush(makeUpdateResult(updatedAccount));

    // Assert
    const accounts = service.accounts();
    expect(accounts.length).toBe(2);
    expect(accounts.find(a => a.id === MOCK_ACCOUNT.id)?.name).toBe('Updated GitHub');
    expect(accounts.find(a => a.id === MOCK_ACCOUNT_2.id)?.name).toBe('Work GitHub');
  });

  // Cycle 7: deleteAccount calls DELETE /api/accounts/{id}
  it('should DELETE /api/accounts/{id} when deleteAccount is called', () => {
    // Arrange
    const id = MOCK_ACCOUNT.id;

    // Act
    service.deleteAccount(id);
    const req = httpMock.expectOne(`/api/accounts/${id}`);

    // Assert
    expect(req.request.method).toBe('DELETE');
    req.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('should set deleting to true while deleteAccount is in flight', () => {
    // Arrange
    const id = MOCK_ACCOUNT.id;

    // Act
    service.deleteAccount(id);

    // Assert — before flush
    expect(service.deleting()).toBe(true);
    httpMock.expectOne(`/api/accounts/${id}`).flush(null, { status: 204, statusText: 'No Content' });
  });

  it('should set deleting to false after deleteAccount succeeds', () => {
    // Arrange
    service.deleteAccount(MOCK_ACCOUNT.id);
    httpMock.expectOne(`/api/accounts/${MOCK_ACCOUNT.id}`).flush(null, {
      status: 204,
      statusText: 'No Content',
    });

    // Assert
    expect(service.deleting()).toBe(false);
  });

  it('should set deleteError when deleteAccount fails with a string body', () => {
    // Arrange
    service.loadAccounts();
    httpMock.expectOne('/api/accounts').flush([MOCK_ACCOUNT]);
    service.deleteAccount(MOCK_ACCOUNT.id);

    // Act
    httpMock.expectOne(`/api/accounts/${MOCK_ACCOUNT.id}`).flush('Account is in use.', {
      status: 409,
      statusText: 'Conflict',
    });

    // Assert
    expect(service.deleteError()).toBe('Account is in use.');
  });

  it('should clear deleteError at start of deleteAccount', () => {
    // Arrange — first call that fails
    service.deleteAccount(MOCK_ACCOUNT.id);
    httpMock.expectOne(`/api/accounts/${MOCK_ACCOUNT.id}`).flush('Conflict', {
      status: 409,
      statusText: 'Conflict',
    });

    // Act — second call clears error immediately
    service.deleteAccount(MOCK_ACCOUNT.id);

    // Assert — error is cleared before response
    expect(service.deleteError()).toBeNull();
    httpMock.expectOne(`/api/accounts/${MOCK_ACCOUNT.id}`).flush(null, {
      status: 204,
      statusText: 'No Content',
    });
  });

  it('should remove the deleted account from accounts signal after deleteAccount succeeds', () => {
    // Arrange
    service.loadAccounts();
    httpMock.expectOne('/api/accounts').flush([MOCK_ACCOUNT, MOCK_ACCOUNT_2]);

    // Act
    service.deleteAccount(MOCK_ACCOUNT.id);
    httpMock.expectOne(`/api/accounts/${MOCK_ACCOUNT.id}`).flush(null, {
      status: 204,
      statusText: 'No Content',
    });

    // Assert
    const accounts = service.accounts();
    expect(accounts.length).toBe(1);
    expect(accounts[0].id).toBe(MOCK_ACCOUNT_2.id);
  });

  // Cycle 7b: affectedRepositories signal
  it('should start with affectedRepositories as null', () => {
    // Arrange / Act — no calls yet

    // Assert
    expect(service.affectedRepositories()).toBeNull();
  });

  it('should set affectedRepositories signal after updateAccount succeeds with affected repos', () => {
    // Arrange
    const affected: AffectedRepository[] = [
      { id: 'repo-1', slug: 'org/repo', previousStatus: 'eligible', newStatus: 'ineligible' },
    ];

    // Act
    service.updateAccount(MOCK_ACCOUNT.id, { baseUrl: 'https://api.github.com', token: null });
    httpMock.expectOne(`/api/accounts/${MOCK_ACCOUNT.id}`).flush(makeUpdateResult(MOCK_ACCOUNT, affected));

    // Assert
    expect(service.affectedRepositories()).toEqual(affected);
  });

  it('should set affectedRepositories to empty array when no repos are affected', () => {
    // Arrange / Act
    service.updateAccount(MOCK_ACCOUNT.id, { baseUrl: 'https://api.github.com', token: null });
    httpMock.expectOne(`/api/accounts/${MOCK_ACCOUNT.id}`).flush(makeUpdateResult(MOCK_ACCOUNT, []));

    // Assert
    expect(service.affectedRepositories()).toEqual([]);
  });

  it('should show confirmation toast when updateAccount succeeds with no affected repos', () => {
    // Arrange
    const toastService = TestBed.inject(ToastService);
    const showSpy = vi.spyOn(toastService, 'show');

    // Act
    service.updateAccount(MOCK_ACCOUNT.id, { baseUrl: 'https://api.github.com', token: null });
    httpMock.expectOne(`/api/accounts/${MOCK_ACCOUNT.id}`).flush(makeUpdateResult(MOCK_ACCOUNT, []));

    // Assert
    expect(showSpy).toHaveBeenCalledWith('Token updated. All repositories retained their access.');
  });

  it('should not show toast when updateAccount succeeds with affected repos', () => {
    // Arrange
    const toastService = TestBed.inject(ToastService);
    const showSpy = vi.spyOn(toastService, 'show');
    const affected: AffectedRepository[] = [
      { id: 'repo-1', slug: 'org/repo', previousStatus: 'eligible', newStatus: 'ineligible' },
    ];

    // Act
    service.updateAccount(MOCK_ACCOUNT.id, { baseUrl: 'https://api.github.com', token: null });
    httpMock.expectOne(`/api/accounts/${MOCK_ACCOUNT.id}`).flush(makeUpdateResult(MOCK_ACCOUNT, affected));

    // Assert
    expect(showSpy).not.toHaveBeenCalled();
  });

  it('should reset affectedRepositories to null at start of updateAccount', () => {
    // Arrange — first call that sets affected repos
    const affected: AffectedRepository[] = [
      { id: 'repo-1', slug: 'org/repo', previousStatus: 'eligible', newStatus: 'ineligible' },
    ];
    service.updateAccount(MOCK_ACCOUNT.id, { baseUrl: 'https://api.github.com', token: null });
    httpMock.expectOne(`/api/accounts/${MOCK_ACCOUNT.id}`).flush(makeUpdateResult(MOCK_ACCOUNT, affected));

    // Act — second call resets signal before response
    service.updateAccount(MOCK_ACCOUNT.id, { baseUrl: 'https://api.github.com', token: null });

    // Assert — signal reset immediately
    expect(service.affectedRepositories()).toBeNull();
    httpMock.expectOne(`/api/accounts/${MOCK_ACCOUNT.id}`).flush(makeUpdateResult(MOCK_ACCOUNT));
  });

  it('should reset affectedRepositories to null at start of createAccount', () => {
    // Arrange — first set affected repos via updateAccount
    const affected: AffectedRepository[] = [
      { id: 'repo-1', slug: 'org/repo', previousStatus: 'eligible', newStatus: 'ineligible' },
    ];
    service.updateAccount(MOCK_ACCOUNT.id, { baseUrl: 'https://api.github.com', token: null });
    httpMock.expectOne(`/api/accounts/${MOCK_ACCOUNT.id}`).flush(makeUpdateResult(MOCK_ACCOUNT, affected));

    // Act — createAccount resets signal before response
    service.createAccount({ providerType: 'github', baseUrl: 'https://api.github.com', token: 'ghp_new' });

    // Assert — signal reset immediately
    expect(service.affectedRepositories()).toBeNull();
    httpMock.expectOne('/api/accounts').flush(makeCreationResult(MOCK_ACCOUNT_2), { status: 201, statusText: 'Created' });
  });

  it('should clear affectedRepositories when clearAffectedRepositories is called', () => {
    // Arrange — set affected repos
    const affected: AffectedRepository[] = [
      { id: 'repo-1', slug: 'org/repo', previousStatus: 'eligible', newStatus: 'ineligible' },
    ];
    service.updateAccount(MOCK_ACCOUNT.id, { baseUrl: 'https://api.github.com', token: null });
    httpMock.expectOne(`/api/accounts/${MOCK_ACCOUNT.id}`).flush(makeUpdateResult(MOCK_ACCOUNT, affected));

    // Act
    service.clearAffectedRepositories();

    // Assert
    expect(service.affectedRepositories()).toBeNull();
  });

  // Cycle 8: validateToken calls POST /api/accounts/validate-token
  it('should POST to /api/accounts/validate-token when validateToken is called', () => {
    // Arrange
    const request = { token: 'ghp_test', baseUrl: 'https://api.github.com', providerType: 'GitHub' };

    // Act
    service.validateToken(request);
    const req = httpMock.expectOne('/api/accounts/validate-token');

    // Assert
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(request);
    req.flush({ isValid: true, isAuthFailure: false, scopesVerified: true, missingScopes: [], accountName: null });
  });

  it('should set validating to true while validateToken is in flight', () => {
    // Arrange
    const request = { token: 'ghp_test', baseUrl: 'https://api.github.com', providerType: 'GitHub' };

    // Act
    service.validateToken(request);

    // Assert — before flush
    expect(service.validating()).toBe(true);
    httpMock.expectOne('/api/accounts/validate-token').flush({
      isValid: true,
      isAuthFailure: false,
      scopesVerified: true,
      missingScopes: [],
      accountName: null,
    });
  });

  it('should set validationResult after validateToken succeeds', () => {
    // Arrange
    const request = { token: 'ghp_test', baseUrl: 'https://api.github.com', providerType: 'GitHub' };
    service.validateToken(request);
    httpMock.expectOne('/api/accounts/validate-token').flush({
      isValid: true,
      isAuthFailure: false,
      scopesVerified: true,
      missingScopes: [],
      accountName: null,
    });

    // Assert
    expect(service.validating()).toBe(false);
    expect(service.validationResult()).toEqual({
      isValid: true,
      isAuthFailure: false,
      scopesVerified: true,
      missingScopes: [],
      accountName: null,
    });
  });

  it('should set validating to false when validateToken fails', () => {
    // Arrange
    const request = { token: 'bad', baseUrl: 'https://api.github.com', providerType: 'GitHub' };
    service.validateToken(request);
    httpMock.expectOne('/api/accounts/validate-token').flush('Bad Request', {
      status: 400,
      statusText: 'Bad Request',
    });

    // Assert
    expect(service.validating()).toBe(false);
  });

  it('should set validationError when validateToken fails', () => {
    // Arrange
    const request = { token: 'bad', baseUrl: 'https://api.github.com', providerType: 'GitHub' };
    service.validateToken(request);

    // Act
    httpMock.expectOne('/api/accounts/validate-token').flush('Bad Request', {
      status: 400,
      statusText: 'Bad Request',
    });

    // Assert
    expect(service.validationError()).not.toBeNull();
  });

  it('should clear validationError at start of validateToken', () => {
    // Arrange — first call that fails
    service.validateToken({ token: 'bad', baseUrl: 'https://api.github.com', providerType: 'GitHub' });
    httpMock.expectOne('/api/accounts/validate-token').flush('Bad Request', {
      status: 400,
      statusText: 'Bad Request',
    });

    // Act — second call clears error immediately
    service.validateToken({ token: 'good', baseUrl: 'https://api.github.com', providerType: 'GitHub' });

    // Assert — error is cleared before response
    expect(service.validationError()).toBeNull();
    httpMock.expectOne('/api/accounts/validate-token').flush({
      isValid: true,
      isAuthFailure: false,
      scopesVerified: true,
      missingScopes: [],
      accountName: null,
    });
  });

  // Cycle 9: getTokenRequirements fetches from correct lowercased URL
  it('should GET /api/providers/github/token-requirements for GitHub provider', async () => {
    // Arrange
    const mockRequirements: TokenRequirements = {
      providerType: 'github',
      tokenTypeLabel: 'GitHub fine-grained personal access token',
      scopes: ['Contents (read and write)', 'Issues (read and write)', 'Pull requests (read and write)', 'Workflows (write)', 'Metadata (read)'],
      creationUrlTemplate: '{baseUrl}/settings/personal-access-tokens/new?name=Foundry&contents=write&issues=write&pull_requests=write&workflows=write',
      resourceOwnerHint: 'Fine-grained tokens are bound to a single resource owner. To reach an organization\'s repositories, choose that organization as the token\'s resource owner when creating the token.',
    };

    // Act
    const promise = service.getTokenRequirements('GitHub');
    const req = httpMock.expectOne('/api/providers/github/token-requirements');

    // Assert request
    expect(req.request.method).toBe('GET');
    req.flush(mockRequirements);

    const result = await promise;
    expect(result).toEqual(mockRequirements);
  });

  // Cycle 10: getTokenRequirements uses correct URL for GitLab
  it('should GET /api/providers/gitlab/token-requirements for GitLab provider', async () => {
    // Arrange
    const mockRequirements: TokenRequirements = {
      providerType: 'gitlab',
      tokenTypeLabel: 'GitLab personal access token',
      scopes: ['api'],
      creationUrlTemplate: '{baseUrl}/-/user_settings/personal_access_tokens',
      resourceOwnerHint: null,
    };

    // Act
    const promise = service.getTokenRequirements('GitLab');
    const req = httpMock.expectOne('/api/providers/gitlab/token-requirements');

    // Assert
    expect(req.request.method).toBe('GET');
    req.flush(mockRequirements);

    const result = await promise;
    expect(result).toEqual(mockRequirements);
  });

  // Cycle 11: second call for same provider serves cache, no new HTTP request
  it('should serve cached TokenRequirements on a second call for the same provider', async () => {
    // Arrange
    const mockRequirements: TokenRequirements = {
      providerType: 'github',
      tokenTypeLabel: 'GitHub fine-grained personal access token',
      scopes: ['Contents (read and write)', 'Issues (read and write)', 'Pull requests (read and write)', 'Workflows (write)', 'Metadata (read)'],
      creationUrlTemplate: '{baseUrl}/settings/personal-access-tokens/new?name=Foundry&contents=write&issues=write&pull_requests=write&workflows=write',
      resourceOwnerHint: 'Fine-grained tokens are bound to a single resource owner. To reach an organization\'s repositories, choose that organization as the token\'s resource owner when creating the token.',
    };

    // Act — first call fetches
    const firstPromise = service.getTokenRequirements('GitHub');
    httpMock.expectOne('/api/providers/github/token-requirements').flush(mockRequirements);
    const firstResult = await firstPromise;

    // Act — second call should resolve from cache
    const secondResult = await service.getTokenRequirements('GitHub');

    // Assert — no second HTTP request was made
    httpMock.expectNone('/api/providers/github/token-requirements');
    expect(secondResult).toEqual(mockRequirements);
    // Assert — same object reference proves the cache returned the stored instance
    expect(secondResult).toBe(firstResult);
  });

  // Presence effect — Cycle 1: loadAccounts with ≥1 account sets hasAccounts to true
  it('should set hasAccounts to true in AccountPresenceService after loadAccounts returns accounts', () => {
    // Arrange / Act
    service.loadAccounts();
    httpMock.expectOne('/api/accounts').flush([MOCK_ACCOUNT]);
    TestBed.flushEffects();

    // Assert
    expect(accountPresence.hasAccounts()).toBe(true);
  });

  // Presence effect — Cycle 2: loadAccounts with empty list sets hasAccounts to false
  it('should set hasAccounts to false in AccountPresenceService after loadAccounts returns empty list', () => {
    // Arrange — first load with an account to get to true
    service.loadAccounts();
    httpMock.expectOne('/api/accounts').flush([MOCK_ACCOUNT]);
    TestBed.flushEffects();

    // Act — reload with empty list
    service.loadAccounts();
    httpMock.expectOne('/api/accounts').flush([]);
    TestBed.flushEffects();

    // Assert
    expect(accountPresence.hasAccounts()).toBe(false);
  });

  // Presence effect — Cycle 3: createAccount adding an account flips hasAccounts to true
  it('should set hasAccounts to true in AccountPresenceService after createAccount adds an account', () => {
    // Arrange — start with empty state (hasAccounts false)
    service.loadAccounts();
    httpMock.expectOne('/api/accounts').flush([]);
    TestBed.flushEffects();
    expect(accountPresence.hasAccounts()).toBe(false);

    // Act
    service.createAccount({ providerType: 'github', baseUrl: 'https://api.github.com', token: 'ghp_test' });
    httpMock.expectOne('/api/accounts').flush(
      { credential: MOCK_ACCOUNT, affectedRepositories: [] },
      { status: 201, statusText: 'Created' }
    );
    TestBed.flushEffects();

    // Assert
    expect(accountPresence.hasAccounts()).toBe(true);
  });

  // Presence effect — Cycle 4: deleting the last account flips hasAccounts to false
  it('should set hasAccounts to false in AccountPresenceService after the last account is deleted', () => {
    // Arrange — load one account so hasAccounts is true
    service.loadAccounts();
    httpMock.expectOne('/api/accounts').flush([MOCK_ACCOUNT]);
    TestBed.flushEffects();
    expect(accountPresence.hasAccounts()).toBe(true);

    // Act — delete the only account
    service.deleteAccount(MOCK_ACCOUNT.id);
    httpMock.expectOne(`/api/accounts/${MOCK_ACCOUNT.id}`).flush(null, { status: 204, statusText: 'No Content' });
    TestBed.flushEffects();

    // Assert
    expect(accountPresence.hasAccounts()).toBe(false);
  });

  // Announcements — createAccount start
  it('should set start announcement when createAccount is called', () => {
    // Arrange / Act
    service.createAccount({ providerType: 'github', baseUrl: 'https://api.github.com', token: 'ghp_test' });

    // Assert — announcement set before response
    expect(service.srAnnouncement()).toBe('Adding account. Contacting the provider — this may take a few seconds.');
    httpMock.expectOne('/api/accounts').flush(
      { credential: MOCK_ACCOUNT, affectedRepositories: [] },
      { status: 201, statusText: 'Created' }
    );
  });

  // Announcements — updateAccount start
  it('should set start announcement when updateAccount is called', () => {
    // Arrange / Act
    service.updateAccount(MOCK_ACCOUNT.id, { baseUrl: 'https://api.github.com', token: null });

    // Assert — announcement set before response
    expect(service.srAnnouncement()).toBe('Updating account. Contacting the provider — this may take a few seconds.');
    httpMock.expectOne(`/api/accounts/${MOCK_ACCOUNT.id}`).flush(makeUpdateResult(MOCK_ACCOUNT));
  });

  // Announcements — deleteAccount start
  it('should set start announcement when deleteAccount is called', () => {
    // Arrange / Act
    service.deleteAccount(MOCK_ACCOUNT.id);

    // Assert — announcement set before response
    expect(service.srAnnouncement()).toBe('Deleting account. Contacting the provider — this may take a few seconds.');
    httpMock.expectOne(`/api/accounts/${MOCK_ACCOUNT.id}`).flush(null, { status: 204, statusText: 'No Content' });
  });

  // Announcements — createAccount terminal success (no repos affected)
  it('should set terminal success announcement when createAccount succeeds with no affected repos', () => {
    // Arrange
    service.createAccount({ providerType: 'github', baseUrl: 'https://api.github.com', token: 'ghp_test' });

    // Act
    httpMock.expectOne('/api/accounts').flush(
      { credential: MOCK_ACCOUNT, affectedRepositories: [] },
      { status: 201, statusText: 'Created' }
    );

    // Assert
    expect(service.srAnnouncement()).toBe('Account added.');
  });

  // Announcements — createAccount terminal success (repos affected — richer message preserved)
  it('should set richer terminal announcement when createAccount succeeds with affected repos', () => {
    // Arrange
    const affected: AffectedRepository[] = [
      { id: 'repo-1', slug: 'org/repo', previousStatus: 'eligible', newStatus: 'ineligible' },
    ];
    service.createAccount({ providerType: 'github', baseUrl: 'https://api.github.com', token: 'ghp_test' });

    // Act
    httpMock.expectOne('/api/accounts').flush(
      { credential: MOCK_ACCOUNT, affectedRepositories: affected },
      { status: 201, statusText: 'Created' }
    );

    // Assert — richer message preserved, not overridden by plain terminal
    expect(service.srAnnouncement()).toBe('Account added. 1 repositories affected — review below.');
  });

  // Announcements — updateAccount terminal success (no repos affected — toast shown, plain terminal set)
  it('should set terminal success announcement when updateAccount succeeds with no affected repos', () => {
    // Arrange
    service.updateAccount(MOCK_ACCOUNT.id, { baseUrl: 'https://api.github.com', token: null });

    // Act
    httpMock.expectOne(`/api/accounts/${MOCK_ACCOUNT.id}`).flush(makeUpdateResult(MOCK_ACCOUNT, []));

    // Assert
    expect(service.srAnnouncement()).toBe('Account updated.');
  });

  // Announcements — updateAccount terminal success (repos affected — richer message preserved)
  it('should set richer terminal announcement when updateAccount succeeds with affected repos', () => {
    // Arrange
    const affected: AffectedRepository[] = [
      { id: 'repo-1', slug: 'org/repo', previousStatus: 'eligible', newStatus: 'ineligible' },
    ];
    service.updateAccount(MOCK_ACCOUNT.id, { baseUrl: 'https://api.github.com', token: null });

    // Act
    httpMock.expectOne(`/api/accounts/${MOCK_ACCOUNT.id}`).flush(makeUpdateResult(MOCK_ACCOUNT, affected));

    // Assert — richer message preserved
    expect(service.srAnnouncement()).toBe('Token updated. 1 repositories affected — review below.');
  });

  // Announcements — deleteAccount terminal success
  it('should set terminal success announcement when deleteAccount succeeds', () => {
    // Arrange
    service.deleteAccount(MOCK_ACCOUNT.id);

    // Act
    httpMock.expectOne(`/api/accounts/${MOCK_ACCOUNT.id}`).flush(null, { status: 204, statusText: 'No Content' });

    // Assert
    expect(service.srAnnouncement()).toBe('Account deleted.');
  });

  // Announcements — createAccount terminal error
  it('should set terminal error announcement when createAccount fails', () => {
    // Arrange
    service.createAccount({ providerType: 'github', baseUrl: 'https://api.github.com', token: 'ghp_test' });

    // Act
    httpMock.expectOne('/api/accounts').flush('Unauthorized.', { status: 401, statusText: 'Unauthorized' });

    // Assert
    expect(service.srAnnouncement()).toBe('Could not add account: Unauthorized.');
  });

  // Announcements — updateAccount terminal error
  it('should set terminal error announcement when updateAccount fails', () => {
    // Arrange
    service.updateAccount(MOCK_ACCOUNT.id, { baseUrl: 'https://api.github.com', token: null });

    // Act
    httpMock.expectOne(`/api/accounts/${MOCK_ACCOUNT.id}`).flush('Token is invalid.', {
      status: 422,
      statusText: 'Unprocessable Entity',
    });

    // Assert
    expect(service.srAnnouncement()).toBe('Could not update account: Token is invalid.');
  });

  // Announcements — deleteAccount terminal error
  it('should set terminal error announcement when deleteAccount fails', () => {
    // Arrange
    service.deleteAccount(MOCK_ACCOUNT.id);

    // Act
    httpMock.expectOne(`/api/accounts/${MOCK_ACCOUNT.id}`).flush('Account is in use.', {
      status: 409,
      statusText: 'Conflict',
    });

    // Assert
    expect(service.srAnnouncement()).toBe('Could not delete account: Account is in use.');
  });

  // Announcements — alternation: two identical creates re-fire start announcement
  it('should re-fire start announcement on second identical createAccount call after terminal', () => {
    // Arrange — first create cycle
    service.createAccount({ providerType: 'github', baseUrl: 'https://api.github.com', token: 'ghp_test' });
    const startAnnouncement = service.srAnnouncement();
    httpMock.expectOne('/api/accounts').flush(
      { credential: MOCK_ACCOUNT, affectedRepositories: [] },
      { status: 201, statusText: 'Created' }
    );
    const terminalAnnouncement = service.srAnnouncement();

    // Precondition — announcements differ
    expect(startAnnouncement).not.toBe(terminalAnnouncement);

    // Act — second identical create
    service.createAccount({ providerType: 'github', baseUrl: 'https://api.github.com', token: 'ghp_test' });

    // Assert — announcement changed back to start text
    expect(service.srAnnouncement()).toBe(startAnnouncement);
    httpMock.expectOne('/api/accounts').flush(
      { credential: MOCK_ACCOUNT_2, affectedRepositories: [] },
      { status: 201, statusText: 'Created' }
    );
  });

  // Timeout — createAccount times out after 60 seconds
  describe('mutation timeout', () => {
    beforeEach(() => {
      vi.useFakeTimers();
    });

    afterEach(() => {
      vi.useRealTimers();
    });

    it('should clear saving and set saveError when createAccount request times out after 60s', () => {
      // Arrange
      service.createAccount({ providerType: 'github', baseUrl: 'https://api.github.com', token: 'ghp_test' });
      expect(service.saving()).toBe(true);

      // Act — advance past timeout; the timeout operator cancels the underlying request
      vi.advanceTimersByTime(60_000);

      // Assert — state reflects timeout error, no response was flushed
      expect(service.saving()).toBe(false);
      expect(service.saveError()).toBe('The request timed out. Please try again.');

      // Drain the cancelled request so httpMock.verify() passes
      httpMock.match('/api/accounts');
    });

    it('should set timeout terminal error announcement when createAccount times out', () => {
      // Arrange
      service.createAccount({ providerType: 'github', baseUrl: 'https://api.github.com', token: 'ghp_test' });

      // Act
      vi.advanceTimersByTime(60_000);

      // Assert
      expect(service.srAnnouncement()).toBe('Could not add account: The request timed out. Please try again.');

      // Drain the cancelled request
      httpMock.match('/api/accounts');
    });

    it('should clear deleting and set deleteError when deleteAccount request times out after 60s', () => {
      // Arrange
      service.deleteAccount(MOCK_ACCOUNT.id);
      expect(service.deleting()).toBe(true);

      // Act — advance past timeout
      vi.advanceTimersByTime(60_000);

      // Assert
      expect(service.deleting()).toBe(false);
      expect(service.deleteError()).toBe('The request timed out. Please try again.');

      // Drain the cancelled request
      httpMock.match(`/api/accounts/${MOCK_ACCOUNT.id}`);
    });

    it('should clear saving and set saveError when updateAccount request times out after 60s', () => {
      // Arrange
      service.updateAccount(MOCK_ACCOUNT.id, { baseUrl: 'https://api.github.com', token: null });
      expect(service.saving()).toBe(true);

      // Act — advance past timeout
      vi.advanceTimersByTime(60_000);

      // Assert
      expect(service.saving()).toBe(false);
      expect(service.saveError()).toBe('The request timed out. Please try again.');

      // Drain the cancelled request
      httpMock.match(`/api/accounts/${MOCK_ACCOUNT.id}`);
    });
  });

  // Cycle 12: different providers each fetch once independently
  it('should fetch each provider independently without cross-provider cache sharing', async () => {
    // Arrange
    const githubRequirements: TokenRequirements = {
      providerType: 'github',
      tokenTypeLabel: 'GitHub fine-grained personal access token',
      scopes: ['Contents (read and write)', 'Issues (read and write)', 'Pull requests (read and write)', 'Workflows (write)', 'Metadata (read)'],
      creationUrlTemplate: '{baseUrl}/settings/personal-access-tokens/new?name=Foundry&contents=write&issues=write&pull_requests=write&workflows=write',
      resourceOwnerHint: 'Fine-grained tokens are bound to a single resource owner. To reach an organization\'s repositories, choose that organization as the token\'s resource owner when creating the token.',
    };
    const gitlabRequirements: TokenRequirements = {
      providerType: 'gitlab',
      tokenTypeLabel: 'GitLab personal access token',
      scopes: ['api'],
      creationUrlTemplate: '{baseUrl}/-/user_settings/personal_access_tokens',
      resourceOwnerHint: null,
    };

    // Act — fetch GitHub
    const githubPromise = service.getTokenRequirements('GitHub');
    httpMock.expectOne('/api/providers/github/token-requirements').flush(githubRequirements);
    await githubPromise;

    // Act — fetch GitLab (must still make its own HTTP request)
    const gitlabPromise = service.getTokenRequirements('GitLab');
    httpMock.expectOne('/api/providers/gitlab/token-requirements').flush(gitlabRequirements);
    const gitlabResult = await gitlabPromise;

    // Assert — GitLab was fetched, not served from GitHub cache
    expect(gitlabResult).toEqual(gitlabRequirements);

    // Assert — subsequent calls are now cached for both
    await service.getTokenRequirements('GitHub');
    await service.getTokenRequirements('GitLab');
    httpMock.expectNone('/api/providers/github/token-requirements');
    httpMock.expectNone('/api/providers/gitlab/token-requirements');
  });
});

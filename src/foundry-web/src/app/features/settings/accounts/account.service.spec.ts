import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AccountService } from './account.service';
import { AccountSummary, AffectedRepository, CreateAccountRequest, CredentialCreationResult, CredentialUpdateResult, NamespaceConflict, TokenRequirements, UpdateAccountRequest } from './account.model';
import { ToastService } from '../../../core/services/toast.service';

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
      provideHttpClient(),
      provideHttpClientTesting(),
    ],
  });
  return {
    service: TestBed.inject(AccountService),
    httpMock: TestBed.inject(HttpTestingController),
  };
}

describe('AccountService', () => {
  let service: AccountService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    const setup = setupService();
    service = setup.service;
    httpMock = setup.httpMock;
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

  it('should set saveError for 400 TakeoverValidationResponse listing invalid namespaces', () => {
    // Arrange
    const body = { invalidNamespaces: ['ns-a', 'ns-b'] };
    service.createAccount({ providerType: 'github', baseUrl: 'https://api.github.com', token: 'ghp_test' });

    // Act
    httpMock.expectOne('/api/accounts').flush(body, { status: 400, statusText: 'Bad Request' });

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
    service.updateAccount(MOCK_ACCOUNT.id, { baseUrl: 'https://api.github.com' });
    httpMock.expectOne(`/api/accounts/${MOCK_ACCOUNT.id}`).flush('Conflict', {
      status: 409,
      statusText: 'Conflict',
    });

    // Act — second updateAccount call clears error immediately
    service.updateAccount(MOCK_ACCOUNT.id, { baseUrl: 'https://api.github.com' });

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
    service.updateAccount(MOCK_ACCOUNT.id, { baseUrl: 'https://api.github.com' });
    httpMock.expectOne(`/api/accounts/${MOCK_ACCOUNT.id}`).flush(makeUpdateResult(MOCK_ACCOUNT, affected));

    // Assert
    expect(service.affectedRepositories()).toEqual(affected);
  });

  it('should set affectedRepositories to empty array when no repos are affected', () => {
    // Arrange / Act
    service.updateAccount(MOCK_ACCOUNT.id, { baseUrl: 'https://api.github.com' });
    httpMock.expectOne(`/api/accounts/${MOCK_ACCOUNT.id}`).flush(makeUpdateResult(MOCK_ACCOUNT, []));

    // Assert
    expect(service.affectedRepositories()).toEqual([]);
  });

  it('should show confirmation toast when updateAccount succeeds with no affected repos', () => {
    // Arrange
    const toastService = TestBed.inject(ToastService);
    const showSpy = vi.spyOn(toastService, 'show');

    // Act
    service.updateAccount(MOCK_ACCOUNT.id, { baseUrl: 'https://api.github.com' });
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
    service.updateAccount(MOCK_ACCOUNT.id, { baseUrl: 'https://api.github.com' });
    httpMock.expectOne(`/api/accounts/${MOCK_ACCOUNT.id}`).flush(makeUpdateResult(MOCK_ACCOUNT, affected));

    // Assert
    expect(showSpy).not.toHaveBeenCalled();
  });

  it('should reset affectedRepositories to null at start of updateAccount', () => {
    // Arrange — first call that sets affected repos
    const affected: AffectedRepository[] = [
      { id: 'repo-1', slug: 'org/repo', previousStatus: 'eligible', newStatus: 'ineligible' },
    ];
    service.updateAccount(MOCK_ACCOUNT.id, { baseUrl: 'https://api.github.com' });
    httpMock.expectOne(`/api/accounts/${MOCK_ACCOUNT.id}`).flush(makeUpdateResult(MOCK_ACCOUNT, affected));

    // Act — second call resets signal before response
    service.updateAccount(MOCK_ACCOUNT.id, { baseUrl: 'https://api.github.com' });

    // Assert — signal reset immediately
    expect(service.affectedRepositories()).toBeNull();
    httpMock.expectOne(`/api/accounts/${MOCK_ACCOUNT.id}`).flush(makeUpdateResult(MOCK_ACCOUNT));
  });

  it('should reset affectedRepositories to null at start of createAccount', () => {
    // Arrange — first set affected repos via updateAccount
    const affected: AffectedRepository[] = [
      { id: 'repo-1', slug: 'org/repo', previousStatus: 'eligible', newStatus: 'ineligible' },
    ];
    service.updateAccount(MOCK_ACCOUNT.id, { baseUrl: 'https://api.github.com' });
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
    service.updateAccount(MOCK_ACCOUNT.id, { baseUrl: 'https://api.github.com' });
    httpMock.expectOne(`/api/accounts/${MOCK_ACCOUNT.id}`).flush(makeUpdateResult(MOCK_ACCOUNT, affected));

    // Act
    service.clearAffectedRepositories();

    // Assert
    expect(service.affectedRepositories()).toBeNull();
  });

  // Cycle 8: validateToken calls POST /api/accounts/validate-token
  it('should POST to /api/accounts/validate-token when validateToken is called', () => {
    // Arrange
    const request = { token: 'ghp_test', baseUrl: 'https://api.github.com' };

    // Act
    service.validateToken(request);
    const req = httpMock.expectOne('/api/accounts/validate-token');

    // Assert
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(request);
    req.flush({ isValid: true, isAuthFailure: false, missingScopes: [] });
  });

  it('should set validating to true while validateToken is in flight', () => {
    // Arrange
    const request = { token: 'ghp_test', baseUrl: 'https://api.github.com' };

    // Act
    service.validateToken(request);

    // Assert — before flush
    expect(service.validating()).toBe(true);
    httpMock.expectOne('/api/accounts/validate-token').flush({
      isValid: true,
      isAuthFailure: false,
      missingScopes: [],
    });
  });

  it('should set validationResult after validateToken succeeds', () => {
    // Arrange
    const request = { token: 'ghp_test', baseUrl: 'https://api.github.com' };
    service.validateToken(request);
    httpMock.expectOne('/api/accounts/validate-token').flush({
      isValid: true,
      isAuthFailure: false,
      missingScopes: [],
    });

    // Assert
    expect(service.validating()).toBe(false);
    expect(service.validationResult()).toEqual({
      isValid: true,
      isAuthFailure: false,
      missingScopes: [],
    });
  });

  it('should set validating to false when validateToken fails', () => {
    // Arrange
    const request = { token: 'bad', baseUrl: 'https://api.github.com' };
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
    const request = { token: 'bad', baseUrl: 'https://api.github.com' };
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
    service.validateToken({ token: 'bad', baseUrl: 'https://api.github.com' });
    httpMock.expectOne('/api/accounts/validate-token').flush('Bad Request', {
      status: 400,
      statusText: 'Bad Request',
    });

    // Act — second call clears error immediately
    service.validateToken({ token: 'good', baseUrl: 'https://api.github.com' });

    // Assert — error is cleared before response
    expect(service.validationError()).toBeNull();
    httpMock.expectOne('/api/accounts/validate-token').flush({
      isValid: true,
      isAuthFailure: false,
      missingScopes: [],
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
      resourceOwnerHint: 'Select a resource owner (your user or an organization) to scope access.',
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
      resourceOwnerHint: 'Select a resource owner (your user or an organization) to scope access.',
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

  // Cycle 12: different providers each fetch once independently
  it('should fetch each provider independently without cross-provider cache sharing', async () => {
    // Arrange
    const githubRequirements: TokenRequirements = {
      providerType: 'github',
      tokenTypeLabel: 'GitHub fine-grained personal access token',
      scopes: ['Contents (read and write)', 'Issues (read and write)', 'Pull requests (read and write)', 'Workflows (write)', 'Metadata (read)'],
      creationUrlTemplate: '{baseUrl}/settings/personal-access-tokens/new?name=Foundry&contents=write&issues=write&pull_requests=write&workflows=write',
      resourceOwnerHint: 'Select a resource owner (your user or an organization) to scope access.',
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

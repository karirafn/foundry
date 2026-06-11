import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AccountService } from './account.service';
import { AccountSummary, CreateAccountRequest, UpdateAccountRequest } from './account.model';

const MOCK_ACCOUNT: AccountSummary = {
  id: '00000000-0000-0000-0000-000000000001',
  name: 'My GitHub',
  providerType: 'github',
  baseUrl: 'https://api.github.com/',
  hasToken: true,
};

const MOCK_ACCOUNT_2: AccountSummary = {
  id: '00000000-0000-0000-0000-000000000002',
  name: 'Work GitHub',
  providerType: 'github',
  baseUrl: 'https://api.github.com/',
  hasToken: true,
};

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

  // Cycle 5: createAccount calls POST /api/accounts
  it('should POST to /api/accounts when createAccount is called', () => {
    // Arrange
    const request: CreateAccountRequest = {
      name: 'My GitHub',
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
    req.flush(MOCK_ACCOUNT, { status: 201, statusText: 'Created' });
  });

  it('should set saving to true while createAccount is in flight', () => {
    // Arrange
    const request: CreateAccountRequest = {
      name: 'My GitHub',
      providerType: 'github',
      baseUrl: 'https://api.github.com',
      token: 'ghp_test',
    };

    // Act
    service.createAccount(request);

    // Assert — before flush
    expect(service.saving()).toBe(true);
    httpMock.expectOne('/api/accounts').flush(MOCK_ACCOUNT, { status: 201, statusText: 'Created' });
  });

  it('should set saving to false and saveSuccess to true after createAccount succeeds', () => {
    // Arrange
    const request: CreateAccountRequest = {
      name: 'My GitHub',
      providerType: 'github',
      baseUrl: 'https://api.github.com',
      token: 'ghp_test',
    };
    service.createAccount(request);
    httpMock.expectOne('/api/accounts').flush(MOCK_ACCOUNT, { status: 201, statusText: 'Created' });

    // Assert
    expect(service.saving()).toBe(false);
    expect(service.saveSuccess()).toBe(true);
  });

  it('should add the new account to accounts signal after createAccount succeeds', () => {
    // Arrange
    service.loadAccounts();
    httpMock.expectOne('/api/accounts').flush([]);

    const request: CreateAccountRequest = {
      name: 'My GitHub',
      providerType: 'github',
      baseUrl: 'https://api.github.com',
      token: 'ghp_test',
    };

    // Act
    service.createAccount(request);
    httpMock.expectOne('/api/accounts').flush(MOCK_ACCOUNT, { status: 201, statusText: 'Created' });

    // Assert
    expect(service.accounts()).toContain(MOCK_ACCOUNT);
  });

  it('should set saving to false when createAccount fails', () => {
    // Arrange
    const request: CreateAccountRequest = {
      name: 'My GitHub',
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

  // Cycle 6: updateAccount calls PUT /api/accounts/{id}
  it('should PUT to /api/accounts/{id} when updateAccount is called', () => {
    // Arrange
    const id = '00000000-0000-0000-0000-000000000001';
    const request: UpdateAccountRequest = {
      name: 'Updated GitHub',
      providerType: 'github',
      baseUrl: 'https://api.github.com',
      token: 'ghp_updated',
    };

    // Act
    service.updateAccount(id, request);
    const req = httpMock.expectOne(`/api/accounts/${id}`);

    // Assert
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(request);
    req.flush({ ...MOCK_ACCOUNT, name: 'Updated GitHub' });
  });

  it('should set saving to true while updateAccount is in flight', () => {
    // Arrange
    const id = MOCK_ACCOUNT.id;
    const request: UpdateAccountRequest = {
      name: 'Updated',
      providerType: 'github',
      baseUrl: 'https://api.github.com',
    };

    // Act
    service.updateAccount(id, request);

    // Assert — before flush
    expect(service.saving()).toBe(true);
    httpMock.expectOne(`/api/accounts/${id}`).flush(MOCK_ACCOUNT);
  });

  it('should set saving to false and saveSuccess to true after updateAccount succeeds', () => {
    // Arrange
    const id = MOCK_ACCOUNT.id;
    const request: UpdateAccountRequest = {
      name: 'Updated',
      providerType: 'github',
      baseUrl: 'https://api.github.com',
    };
    service.loadAccounts();
    httpMock.expectOne('/api/accounts').flush([MOCK_ACCOUNT]);

    // Act
    service.updateAccount(id, request);
    httpMock.expectOne(`/api/accounts/${id}`).flush({ ...MOCK_ACCOUNT, name: 'Updated' });

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
      name: 'Updated GitHub',
      providerType: 'github',
      baseUrl: 'https://api.github.com',
    };

    // Act
    service.updateAccount(MOCK_ACCOUNT.id, request);
    httpMock.expectOne(`/api/accounts/${MOCK_ACCOUNT.id}`).flush(updatedAccount);

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
    req.flush({ isValid: true, scopes: ['repo'], missingScopes: [] });
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
      scopes: ['repo'],
      missingScopes: [],
    });
  });

  it('should set validationResult after validateToken succeeds', () => {
    // Arrange
    const request = { token: 'ghp_test', baseUrl: 'https://api.github.com' };
    service.validateToken(request);
    httpMock.expectOne('/api/accounts/validate-token').flush({
      isValid: true,
      scopes: ['repo'],
      missingScopes: [],
    });

    // Assert
    expect(service.validating()).toBe(false);
    expect(service.validationResult()).toEqual({
      isValid: true,
      scopes: ['repo'],
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
});

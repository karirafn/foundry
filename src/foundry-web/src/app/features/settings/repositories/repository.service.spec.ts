import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { RepositoryService } from './repository.service';
import { AvailableRepository, CreateRepositoryRequest, RepositorySummary, UpdateRepositoryRequest } from './repository.model';

const ACCOUNT_ID = '00000000-0000-0000-0000-000000000001';
const REPO_ID = '00000000-0000-0000-0000-000000000010';
const REPO_ID_2 = '00000000-0000-0000-0000-000000000011';

const MOCK_REPOSITORY: RepositorySummary = {
  id: REPO_ID,
  slug: 'my-org/my-repo',
  accountId: ACCOUNT_ID,
  accountName: 'My GitHub',
  pollIntervalSeconds: 300,
  isActive: true,
  lastPolledAt: '2026-06-15T10:00:00Z',
};

const MOCK_REPOSITORY_2: RepositorySummary = {
  id: REPO_ID_2,
  slug: 'my-org/another-repo',
  accountId: ACCOUNT_ID,
  accountName: 'My GitHub',
  pollIntervalSeconds: null,
  isActive: false,
  lastPolledAt: null,
};

const MOCK_AVAILABLE: AvailableRepository = {
  slug: 'my-org/my-repo',
  isPrivate: false,
};

const MOCK_AVAILABLE_2: AvailableRepository = {
  slug: 'my-org/private-repo',
  isPrivate: true,
};

function setupService() {
  TestBed.configureTestingModule({
    providers: [
      RepositoryService,
      provideHttpClient(),
      provideHttpClientTesting(),
    ],
  });
  return {
    service: TestBed.inject(RepositoryService),
    httpMock: TestBed.inject(HttpTestingController),
  };
}

describe('RepositoryService', () => {
  let service: RepositoryService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    const setup = setupService();
    service = setup.service;
    httpMock = setup.httpMock;
  });

  afterEach(() => httpMock.verify());

  // Cycle 1: initial signal state
  it('should start with empty state and no errors', () => {
    // Arrange
    // (service initialized in beforeEach)

    // Assert
    expect(service.repositories()).toEqual([]);
    expect(service.loading()).toBe(false);
    expect(service.loadError()).toBeNull();
    expect(service.saving()).toBe(false);
    expect(service.saveSuccess()).toBe(false);
    expect(service.saveError()).toBeNull();
    expect(service.deleting()).toBe(false);
    expect(service.deleteError()).toBeNull();
    expect(service.availableRepositories()).toEqual([]);
    expect(service.loadingAvailable()).toBe(false);
    expect(service.loadAvailableError()).toBeNull();
  });

  // Cycle 2: loadRepositories populates repositories signal
  it('should GET /api/accounts/{accountId}/repositories when loadRepositories is called', () => {
    // Arrange

    // Act
    service.loadRepositories(ACCOUNT_ID);
    const req = httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`);

    // Assert
    expect(req.request.method).toBe('GET');
    req.flush([MOCK_REPOSITORY]);
  });

  it('should populate repositories after loadRepositories succeeds', () => {
    // Arrange

    // Act
    service.loadRepositories(ACCOUNT_ID);
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`).flush([MOCK_REPOSITORY]);

    // Assert
    expect(service.repositories()).toEqual([MOCK_REPOSITORY]);
    expect(service.loading()).toBe(false);
  });

  it('should set loading to true while loadRepositories is in flight', () => {
    // Arrange

    // Act
    service.loadRepositories(ACCOUNT_ID);

    // Assert — before flush
    expect(service.loading()).toBe(true);
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`).flush([]);
  });

  it('should set loading to false when loadRepositories fails', () => {
    // Arrange
    service.loadRepositories(ACCOUNT_ID);
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`).flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });

    // Assert
    expect(service.loading()).toBe(false);
  });

  it('should set loadError when loadRepositories fails with a string body', () => {
    // Arrange
    service.loadRepositories(ACCOUNT_ID);

    // Act
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`).flush('Forbidden', {
      status: 403,
      statusText: 'Forbidden',
    });

    // Assert
    expect(service.loadError()).toBe('Forbidden');
  });

  it('should clear loadError at start of loadRepositories', () => {
    // Arrange — first call that fails
    service.loadRepositories(ACCOUNT_ID);
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`).flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });

    // Act — second call clears error immediately
    service.loadRepositories(ACCOUNT_ID);

    // Assert — error is cleared before response
    expect(service.loadError()).toBeNull();
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`).flush([]);
  });

  it('should populate multiple repositories', () => {
    // Arrange

    // Act
    service.loadRepositories(ACCOUNT_ID);
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`).flush([MOCK_REPOSITORY, MOCK_REPOSITORY_2]);

    // Assert
    expect(service.repositories().length).toBe(2);
    expect(service.repositories()[0].slug).toBe('my-org/my-repo');
    expect(service.repositories()[1].slug).toBe('my-org/another-repo');
  });

  // Cycle 3: loadAvailableRepositories
  it('should GET /api/accounts/{accountId}/repositories/available-repositories when loadAvailableRepositories is called', () => {
    // Arrange

    // Act
    service.loadAvailableRepositories(ACCOUNT_ID);
    const req = httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/available-repositories`);

    // Assert
    expect(req.request.method).toBe('GET');
    req.flush([MOCK_AVAILABLE]);
  });

  it('should populate availableRepositories after loadAvailableRepositories succeeds', () => {
    // Arrange

    // Act
    service.loadAvailableRepositories(ACCOUNT_ID);
    httpMock
      .expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/available-repositories`)
      .flush([MOCK_AVAILABLE, MOCK_AVAILABLE_2]);

    // Assert
    expect(service.availableRepositories()).toEqual([MOCK_AVAILABLE, MOCK_AVAILABLE_2]);
    expect(service.loadingAvailable()).toBe(false);
  });

  it('should set loadingAvailable to true while loadAvailableRepositories is in flight', () => {
    // Arrange

    // Act
    service.loadAvailableRepositories(ACCOUNT_ID);

    // Assert — before flush
    expect(service.loadingAvailable()).toBe(true);
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/available-repositories`).flush([]);
  });

  it('should set loadAvailableError when loadAvailableRepositories fails with a string body', () => {
    // Arrange
    service.loadAvailableRepositories(ACCOUNT_ID);

    // Act
    httpMock
      .expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/available-repositories`)
      .flush('Forbidden', { status: 403, statusText: 'Forbidden' });

    // Assert
    expect(service.loadAvailableError()).toBe('Forbidden');
    expect(service.loadingAvailable()).toBe(false);
  });

  it('should clear loadAvailableError at start of loadAvailableRepositories', () => {
    // Arrange — first call that fails
    service.loadAvailableRepositories(ACCOUNT_ID);
    httpMock
      .expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/available-repositories`)
      .flush('Error', { status: 500, statusText: 'Internal Server Error' });

    // Act — second call clears error immediately
    service.loadAvailableRepositories(ACCOUNT_ID);

    // Assert — error is cleared before response
    expect(service.loadAvailableError()).toBeNull();
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/available-repositories`).flush([]);
  });

  // Cycle 4: createRepository calls POST
  it('should POST to /api/accounts/{accountId}/repositories when createRepository is called', () => {
    // Arrange
    const request: CreateRepositoryRequest = {
      slug: 'my-org/my-repo',
      pollIntervalSeconds: 300,
    };

    // Act
    service.createRepository(ACCOUNT_ID, request).subscribe();
    const req = httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`);

    // Assert
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(request);
    req.flush(MOCK_REPOSITORY, { status: 201, statusText: 'Created' });
  });

  it('should set saving to true while createRepository is in flight', () => {
    // Arrange
    const request: CreateRepositoryRequest = { slug: 'my-org/my-repo', pollIntervalSeconds: null };

    // Act
    service.createRepository(ACCOUNT_ID, request).subscribe();

    // Assert — before flush
    expect(service.saving()).toBe(true);
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`).flush(MOCK_REPOSITORY, {
      status: 201,
      statusText: 'Created',
    });
  });

  it('should set saving to false and saveSuccess to true after createRepository succeeds', () => {
    // Arrange
    const request: CreateRepositoryRequest = { slug: 'my-org/my-repo', pollIntervalSeconds: null };
    service.createRepository(ACCOUNT_ID, request).subscribe();
    httpMock
      .expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`)
      .flush(MOCK_REPOSITORY, { status: 201, statusText: 'Created' });

    // Assert
    expect(service.saving()).toBe(false);
    expect(service.saveSuccess()).toBe(true);
  });

  it('should add the new repository to repositories signal after createRepository succeeds', () => {
    // Arrange
    service.loadRepositories(ACCOUNT_ID);
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`).flush([]);

    const request: CreateRepositoryRequest = { slug: 'my-org/my-repo', pollIntervalSeconds: 300 };

    // Act
    service.createRepository(ACCOUNT_ID, request).subscribe();
    httpMock
      .expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`)
      .flush(MOCK_REPOSITORY, { status: 201, statusText: 'Created' });

    // Assert
    expect(service.repositories()).toContain(MOCK_REPOSITORY);
  });

  it('should set saving to false and saveSuccess to false when createRepository fails', () => {
    // Arrange
    const request: CreateRepositoryRequest = { slug: 'my-org/my-repo', pollIntervalSeconds: null };
    service.createRepository(ACCOUNT_ID, request).subscribe({ error: () => {} });
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`).flush('Bad Request', {
      status: 400,
      statusText: 'Bad Request',
    });

    // Assert
    expect(service.saving()).toBe(false);
    expect(service.saveSuccess()).toBe(false);
  });

  it('should set saveError when createRepository fails with a string body', () => {
    // Arrange
    const request: CreateRepositoryRequest = { slug: 'my-org/duplicate', pollIntervalSeconds: null };
    service.createRepository(ACCOUNT_ID, request).subscribe({ error: () => {} });

    // Act
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`).flush('Repository already exists.', {
      status: 409,
      statusText: 'Conflict',
    });

    // Assert
    expect(service.saveError()).toBe('Repository already exists.');
  });

  it('should clear saveError at start of createRepository', () => {
    // Arrange — first call that fails
    service.createRepository(ACCOUNT_ID, { slug: 'x', pollIntervalSeconds: null }).subscribe({ error: () => {} });
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`).flush('Conflict', {
      status: 409,
      statusText: 'Conflict',
    });

    // Act — second call clears error immediately
    service.createRepository(ACCOUNT_ID, { slug: 'y', pollIntervalSeconds: null }).subscribe();

    // Assert — error is cleared before response
    expect(service.saveError()).toBeNull();
    httpMock
      .expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`)
      .flush(MOCK_REPOSITORY, { status: 201, statusText: 'Created' });
  });

  // Cycle 5: updateRepository calls PUT
  it('should PUT to /api/accounts/{accountId}/repositories/{id} when updateRepository is called', () => {
    // Arrange
    const request: UpdateRepositoryRequest = { pollIntervalSeconds: 600, isActive: true };

    // Act
    service.updateRepository(ACCOUNT_ID, REPO_ID, request).subscribe();
    const req = httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/${REPO_ID}`);

    // Assert
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(request);
    req.flush({ ...MOCK_REPOSITORY, pollIntervalSeconds: 600 });
  });

  it('should set saving to true while updateRepository is in flight', () => {
    // Arrange
    const request: UpdateRepositoryRequest = { pollIntervalSeconds: 600, isActive: true };

    // Act
    service.updateRepository(ACCOUNT_ID, REPO_ID, request).subscribe();

    // Assert — before flush
    expect(service.saving()).toBe(true);
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/${REPO_ID}`).flush(MOCK_REPOSITORY);
  });

  it('should set saving to false and saveSuccess to true after updateRepository succeeds', () => {
    // Arrange
    const request: UpdateRepositoryRequest = { pollIntervalSeconds: 600, isActive: true };
    service.updateRepository(ACCOUNT_ID, REPO_ID, request).subscribe();
    httpMock
      .expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/${REPO_ID}`)
      .flush({ ...MOCK_REPOSITORY, pollIntervalSeconds: 600 });

    // Assert
    expect(service.saving()).toBe(false);
    expect(service.saveSuccess()).toBe(true);
  });

  it('should replace the updated repository in repositories signal after updateRepository succeeds', () => {
    // Arrange
    service.loadRepositories(ACCOUNT_ID);
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`).flush([MOCK_REPOSITORY, MOCK_REPOSITORY_2]);

    const updatedRepo: RepositorySummary = { ...MOCK_REPOSITORY, pollIntervalSeconds: 600 };
    const request: UpdateRepositoryRequest = { pollIntervalSeconds: 600, isActive: true };

    // Act
    service.updateRepository(ACCOUNT_ID, REPO_ID, request).subscribe();
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/${REPO_ID}`).flush(updatedRepo);

    // Assert
    const repos = service.repositories();
    expect(repos.length).toBe(2);
    expect(repos.find(r => r.id === REPO_ID)?.pollIntervalSeconds).toBe(600);
    expect(repos.find(r => r.id === REPO_ID_2)?.slug).toBe('my-org/another-repo');
  });

  it('should set saveError when updateRepository fails with a string body', () => {
    // Arrange
    const request: UpdateRepositoryRequest = { pollIntervalSeconds: -1, isActive: true };
    service.updateRepository(ACCOUNT_ID, REPO_ID, request).subscribe({ error: () => {} });

    // Act
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/${REPO_ID}`).flush('Invalid interval.', {
      status: 400,
      statusText: 'Bad Request',
    });

    // Assert
    expect(service.saveError()).toBe('Invalid interval.');
  });

  it('should clear saveError at start of updateRepository', () => {
    // Arrange — first call that fails
    service.updateRepository(ACCOUNT_ID, REPO_ID, { pollIntervalSeconds: -1, isActive: true }).subscribe({ error: () => {} });
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/${REPO_ID}`).flush('Bad Request', {
      status: 400,
      statusText: 'Bad Request',
    });

    // Act — second call clears error immediately
    service.updateRepository(ACCOUNT_ID, REPO_ID, { pollIntervalSeconds: 600, isActive: true }).subscribe();

    // Assert — error is cleared before response
    expect(service.saveError()).toBeNull();
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/${REPO_ID}`).flush(MOCK_REPOSITORY);
  });

  // Cycle 6: deleteRepository calls DELETE
  it('should DELETE /api/accounts/{accountId}/repositories/{id} when deleteRepository is called', () => {
    // Arrange

    // Act
    service.deleteRepository(ACCOUNT_ID, REPO_ID).subscribe();
    const req = httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/${REPO_ID}`);

    // Assert
    expect(req.request.method).toBe('DELETE');
    req.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('should set deleting to true while deleteRepository is in flight', () => {
    // Arrange

    // Act
    service.deleteRepository(ACCOUNT_ID, REPO_ID).subscribe();

    // Assert — before flush
    expect(service.deleting()).toBe(true);
    httpMock
      .expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/${REPO_ID}`)
      .flush(null, { status: 204, statusText: 'No Content' });
  });

  it('should set deleting to false after deleteRepository succeeds', () => {
    // Arrange
    service.deleteRepository(ACCOUNT_ID, REPO_ID).subscribe();
    httpMock
      .expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/${REPO_ID}`)
      .flush(null, { status: 204, statusText: 'No Content' });

    // Assert
    expect(service.deleting()).toBe(false);
  });

  it('should remove the deleted repository from repositories signal after deleteRepository succeeds', () => {
    // Arrange
    service.loadRepositories(ACCOUNT_ID);
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`).flush([MOCK_REPOSITORY, MOCK_REPOSITORY_2]);

    // Act
    service.deleteRepository(ACCOUNT_ID, REPO_ID).subscribe();
    httpMock
      .expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/${REPO_ID}`)
      .flush(null, { status: 204, statusText: 'No Content' });

    // Assert
    const repos = service.repositories();
    expect(repos.length).toBe(1);
    expect(repos[0].id).toBe(REPO_ID_2);
  });

  it('should set deleteError when deleteRepository fails with a string body', () => {
    // Arrange
    service.deleteRepository(ACCOUNT_ID, REPO_ID).subscribe({ error: () => {} });

    // Act
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/${REPO_ID}`).flush('Repository is in use.', {
      status: 409,
      statusText: 'Conflict',
    });

    // Assert
    expect(service.deleteError()).toBe('Repository is in use.');
  });

  it('should clear deleteError at start of deleteRepository', () => {
    // Arrange — first call that fails
    service.deleteRepository(ACCOUNT_ID, REPO_ID).subscribe({ error: () => {} });
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/${REPO_ID}`).flush('Conflict', {
      status: 409,
      statusText: 'Conflict',
    });

    // Act — second call clears error immediately
    service.deleteRepository(ACCOUNT_ID, REPO_ID).subscribe();

    // Assert — error is cleared before response
    expect(service.deleteError()).toBeNull();
    httpMock
      .expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/${REPO_ID}`)
      .flush(null, { status: 204, statusText: 'No Content' });
  });

  // Cycle 7: loadAllRepositories — loads across multiple accounts and combines results
  it('should GET repositories for each accountId when loadAllRepositories is called', () => {
    // Arrange
    const ACCOUNT_ID_2 = '00000000-0000-0000-0000-000000000002';

    // Act
    service.loadAllRepositories([ACCOUNT_ID, ACCOUNT_ID_2]);

    // Assert
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`).flush([MOCK_REPOSITORY]);
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID_2}/repositories`).flush([MOCK_REPOSITORY_2]);
  });

  it('should combine repositories from all accounts into a single signal', () => {
    // Arrange
    const ACCOUNT_ID_2 = '00000000-0000-0000-0000-000000000002';
    const repoForAccount2: RepositorySummary = { ...MOCK_REPOSITORY_2, accountId: ACCOUNT_ID_2 };

    // Act
    service.loadAllRepositories([ACCOUNT_ID, ACCOUNT_ID_2]);
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`).flush([MOCK_REPOSITORY]);
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID_2}/repositories`).flush([repoForAccount2]);

    // Assert
    expect(service.repositories().length).toBe(2);
    expect(service.repositories().some(r => r.id === MOCK_REPOSITORY.id)).toBe(true);
    expect(service.repositories().some(r => r.id === MOCK_REPOSITORY_2.id)).toBe(true);
  });

  it('should set loading to true while loadAllRepositories is in flight', () => {
    // Arrange

    // Act
    service.loadAllRepositories([ACCOUNT_ID]);

    // Assert — before flush
    expect(service.loading()).toBe(true);
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`).flush([]);
  });

  it('should set loading to false after all account repositories have loaded', () => {
    // Arrange
    const ACCOUNT_ID_2 = '00000000-0000-0000-0000-000000000002';
    service.loadAllRepositories([ACCOUNT_ID, ACCOUNT_ID_2]);

    // Act
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`).flush([MOCK_REPOSITORY]);
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID_2}/repositories`).flush([]);

    // Assert
    expect(service.loading()).toBe(false);
  });

  it('should set loadError when any account repository load fails', () => {
    // Arrange
    const ACCOUNT_ID_2 = '00000000-0000-0000-0000-000000000002';
    service.loadAllRepositories([ACCOUNT_ID, ACCOUNT_ID_2]);

    // Act
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`).flush([MOCK_REPOSITORY]);
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID_2}/repositories`).flush('Forbidden', {
      status: 403,
      statusText: 'Forbidden',
    });

    // Assert
    expect(service.loadError()).toBe('Forbidden');
    expect(service.loading()).toBe(false);
  });

  it('should clear loadError at start of loadAllRepositories', () => {
    // Arrange — first call that fails
    service.loadAllRepositories([ACCOUNT_ID]);
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`).flush('Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });

    // Act — second call clears error immediately
    service.loadAllRepositories([ACCOUNT_ID]);

    // Assert — error is cleared before response
    expect(service.loadError()).toBeNull();
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`).flush([]);
  });

  it('should set repositories to empty array when loadAllRepositories is called with no account IDs', () => {
    // Arrange — seed some data first
    service.loadRepositories(ACCOUNT_ID);
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`).flush([MOCK_REPOSITORY]);

    // Act
    service.loadAllRepositories([]);

    // Assert
    expect(service.repositories()).toEqual([]);
    expect(service.loading()).toBe(false);
  });
});

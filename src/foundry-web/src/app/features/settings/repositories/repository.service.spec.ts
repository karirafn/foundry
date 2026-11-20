import { vi } from 'vitest';
import { Subject } from 'rxjs';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { RepositoryService, RepositoryWebhookUpdate } from './repository.service';
import { RepositoryWebhookChange, SystemSignalRService } from '../../../core/services/system-signalr.service';
import { ApplyBranchRulesResponse, AvailableRepositoriesResponse, AvailableRepository, CreateRepositoryRequest, CreateRepositoryResponse, RepositorySummary, RepositoryWebhookInfo, UnclaimedRepository, UpdateRepositoryRequest } from './repository.model';

const ACCOUNT_ID = '00000000-0000-0000-0000-000000000001';
const REPO_ID = '00000000-0000-0000-0000-000000000010';
const REPO_ID_2 = '00000000-0000-0000-0000-000000000011';

const MOCK_REPOSITORY: RepositorySummary = {
  id: REPO_ID,
  slug: 'my-org/my-repo',
  accountId: ACCOUNT_ID,
  accountName: 'My GitHub',
  providerType: 'github',
  position: 0,
  pollIntervalSeconds: 300,
  effectivePollIntervalSeconds: 300,
  pollIntervalIsDefault: false,
  isActive: true,
  maxConcurrentWorkers: 1,
  rebaseAfterRun: true,
  mergeWhenChecksPass: false,
  reviewPullRequests: true,
  lastPolledAt: '2026-06-15T10:00:00Z',
  eligibility: { status: 'eligible', violations: [], reason: null },
};

const MOCK_REPOSITORY_2: RepositorySummary = {
  id: REPO_ID_2,
  slug: 'my-org/another-repo',
  accountId: ACCOUNT_ID,
  accountName: 'My GitHub',
  providerType: 'github',
  position: 1,
  pollIntervalSeconds: null,
  effectivePollIntervalSeconds: 1800,
  pollIntervalIsDefault: true,
  isActive: false,
  maxConcurrentWorkers: 1,
  rebaseAfterRun: true,
  mergeWhenChecksPass: false,
  reviewPullRequests: true,
  lastPolledAt: null,
  eligibility: { status: 'ineligible', violations: [{ rule: 'AllowDirectPushes', description: 'Allow direct pushes is enabled' }], reason: null },
};

const MOCK_CREATE_RESPONSE: CreateRepositoryResponse = {
  repository: MOCK_REPOSITORY,
  branchRules: { kind: 'applied', reason: null, settingsUrl: null, detail: null },
};

const MOCK_AVAILABLE: AvailableRepository = {
  slug: 'my-org/my-repo',
  isPrivate: false,
  canPush: true,
  isMonitored: false,
};

const MOCK_AVAILABLE_2: AvailableRepository = {
  slug: 'my-org/private-repo',
  isPrivate: true,
  canPush: false,
  isMonitored: true,
};

const MOCK_AVAILABLE_RESPONSE: AvailableRepositoriesResponse = {
  hasClaims: true,
  repositories: [MOCK_AVAILABLE, MOCK_AVAILABLE_2],
  noPushAccessExplanation: '',
};

function makeSignalRStub() {
  return {
    reconnected: new Subject<void>(),
    repositoryWebhookChanged: new Subject<RepositoryWebhookChange>(),
    repositoryWebhooksChanged: new Subject<void>(),
  };
}

function setupService() {
  const signalR = makeSignalRStub();
  TestBed.configureTestingModule({
    providers: [
      RepositoryService,
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: SystemSignalRService, useValue: signalR },
    ],
  });
  return {
    service: TestBed.inject(RepositoryService),
    httpMock: TestBed.inject(HttpTestingController),
    signalR,
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
    expect(service.deleteFailure()).toBeNull();
    expect(service.availableRepositories()).toEqual([]);
    expect(service.availableHasClaims()).toBe(false);
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

  it('should set loadError when loadRepositories fails with a ProblemDetails body', () => {
    // Arrange
    service.loadRepositories(ACCOUNT_ID);

    // Act
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`).flush(
      { type: 'tag:foundry,2026:problems/Account.NotFound', title: 'Forbidden', status: 403, detail: 'Forbidden' },
      { status: 403, statusText: 'Forbidden' }
    );

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
    req.flush(MOCK_AVAILABLE_RESPONSE);
  });

  it('should populate availableRepositories from response.repositories after loadAvailableRepositories succeeds', () => {
    // Arrange

    // Act
    service.loadAvailableRepositories(ACCOUNT_ID);
    httpMock
      .expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/available-repositories`)
      .flush(MOCK_AVAILABLE_RESPONSE);

    // Assert
    expect(service.availableRepositories()).toEqual([MOCK_AVAILABLE, MOCK_AVAILABLE_2]);
    expect(service.loadingAvailable()).toBe(false);
  });

  it('should set availableHasClaims to true when response.hasClaims is true', () => {
    // Arrange
    const response: AvailableRepositoriesResponse = { hasClaims: true, repositories: [MOCK_AVAILABLE], noPushAccessExplanation: '' };

    // Act
    service.loadAvailableRepositories(ACCOUNT_ID);
    httpMock
      .expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/available-repositories`)
      .flush(response);

    // Assert
    expect(service.availableHasClaims()).toBe(true);
  });

  it('should set availableHasClaims to false when response.hasClaims is false', () => {
    // Arrange
    const response: AvailableRepositoriesResponse = { hasClaims: false, repositories: [MOCK_AVAILABLE], noPushAccessExplanation: '' };

    // Act
    service.loadAvailableRepositories(ACCOUNT_ID);
    httpMock
      .expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/available-repositories`)
      .flush(response);

    // Assert
    expect(service.availableHasClaims()).toBe(false);
  });

  it('should set loadingAvailable to true while loadAvailableRepositories is in flight', () => {
    // Arrange

    // Act
    service.loadAvailableRepositories(ACCOUNT_ID);

    // Assert — before flush
    expect(service.loadingAvailable()).toBe(true);
    httpMock
      .expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/available-repositories`)
      .flush({ hasClaims: false, repositories: [] });
  });

  it('should set loadAvailableError when loadAvailableRepositories fails with a ProblemDetails body', () => {
    // Arrange
    service.loadAvailableRepositories(ACCOUNT_ID);

    // Act
    httpMock
      .expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/available-repositories`)
      .flush(
        { type: 'tag:foundry,2026:problems/Account.NotFound', title: 'Forbidden', status: 403, detail: 'Forbidden' },
        { status: 403, statusText: 'Forbidden' }
      );

    // Assert
    expect(service.loadAvailableError()).toBe('Forbidden');
    expect(service.loadingAvailable()).toBe(false);
  });

  it('should leave availableHasClaims as false and set loadAvailableError when loadAvailableRepositories fails', () => {
    // Arrange
    service.loadAvailableRepositories(ACCOUNT_ID);

    // Act
    httpMock
      .expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/available-repositories`)
      .flush('Server Error', { status: 500, statusText: 'Internal Server Error' });

    // Assert
    expect(service.availableHasClaims()).toBe(false);
    expect(service.loadAvailableError()).toBeTruthy();
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
    httpMock
      .expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/available-repositories`)
      .flush({ hasClaims: false, repositories: [] });
  });

  // Cycle 4: createRepository calls POST
  it('should POST to /api/accounts/{accountId}/repositories when createRepository is called', () => {
    // Arrange
    const request: CreateRepositoryRequest = {
      slug: 'my-org/my-repo',
      pollIntervalSeconds: 300,
      maxConcurrentWorkers: null,
      applyBranchRules: false,
    };

    // Act
    service.createRepository(ACCOUNT_ID, request).subscribe();
    const req = httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`);

    // Assert
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(request);
    req.flush(MOCK_CREATE_RESPONSE, { status: 201, statusText: 'Created' });
  });

  it('should set saving to true while createRepository is in flight', () => {
    // Arrange
    const request: CreateRepositoryRequest = { slug: 'my-org/my-repo', pollIntervalSeconds: null, maxConcurrentWorkers: null };

    // Act
    service.createRepository(ACCOUNT_ID, request).subscribe();

    // Assert — before flush
    expect(service.saving()).toBe(true);
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`).flush(MOCK_CREATE_RESPONSE, {
      status: 201,
      statusText: 'Created',
    });
  });

  it('should set saving to false and saveSuccess to true after createRepository succeeds', () => {
    // Arrange
    const request: CreateRepositoryRequest = { slug: 'my-org/my-repo', pollIntervalSeconds: null, maxConcurrentWorkers: null };
    service.createRepository(ACCOUNT_ID, request).subscribe();
    httpMock
      .expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`)
      .flush(MOCK_CREATE_RESPONSE, { status: 201, statusText: 'Created' });

    // Assert
    expect(service.saving()).toBe(false);
    expect(service.saveSuccess()).toBe(true);
  });

  it('should add the new repository to repositories signal after createRepository succeeds', () => {
    // Arrange
    service.loadRepositories(ACCOUNT_ID);
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`).flush([]);

    const request: CreateRepositoryRequest = { slug: 'my-org/my-repo', pollIntervalSeconds: 300, maxConcurrentWorkers: null };

    // Act
    service.createRepository(ACCOUNT_ID, request).subscribe();
    httpMock
      .expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`)
      .flush(MOCK_CREATE_RESPONSE, { status: 201, statusText: 'Created' });

    // Assert
    expect(service.repositories()).toContain(MOCK_REPOSITORY);
  });

  it('should emit the full CreateRepositoryResponse (repository + branchRules) from createRepository', () => {
    // Arrange
    const request: CreateRepositoryRequest = { slug: 'my-org/another-repo', pollIntervalSeconds: null, maxConcurrentWorkers: null, applyBranchRules: true };
    let received: CreateRepositoryResponse | undefined;
    const failed: CreateRepositoryResponse = {
      repository: MOCK_REPOSITORY_2,
      branchRules: {
        kind: 'failed',
        reason: 'insufficient-permission',
        settingsUrl: 'https://github.com/my-org/another-repo/settings/rules',
        detail: 'Must have admin rights',
      },
    };

    // Act
    service.createRepository(ACCOUNT_ID, request).subscribe(r => (received = r));
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`).flush(failed, { status: 201, statusText: 'Created' });

    // Assert
    expect(received).toEqual(failed);
    expect(service.repositories()).toEqual([MOCK_REPOSITORY_2]);
  });

  it('should set saving to false and saveSuccess to false when createRepository fails', () => {
    // Arrange
    const request: CreateRepositoryRequest = { slug: 'my-org/my-repo', pollIntervalSeconds: null, maxConcurrentWorkers: null };
    service.createRepository(ACCOUNT_ID, request).subscribe({ error: () => {} });
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`).flush('Bad Request', {
      status: 400,
      statusText: 'Bad Request',
    });

    // Assert
    expect(service.saving()).toBe(false);
    expect(service.saveSuccess()).toBe(false);
  });

  it('should set saveError when createRepository fails with a ProblemDetails body', () => {
    // Arrange
    const request: CreateRepositoryRequest = { slug: 'my-org/duplicate', pollIntervalSeconds: null, maxConcurrentWorkers: null };
    service.createRepository(ACCOUNT_ID, request).subscribe({ error: () => {} });

    // Act
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`).flush(
      { type: 'tag:foundry,2026:problems/Repository.AlreadyExists', title: 'Conflict', status: 409, detail: 'Repository already exists.' },
      { status: 409, statusText: 'Conflict' }
    );

    // Assert
    expect(service.saveError()).toBe('Repository already exists.');
  });

  it('should clear saveError at start of createRepository', () => {
    // Arrange — first call that fails
    service.createRepository(ACCOUNT_ID, { slug: 'x', pollIntervalSeconds: null, maxConcurrentWorkers: null }).subscribe({ error: () => {} });
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`).flush('Conflict', {
      status: 409,
      statusText: 'Conflict',
    });

    // Act — second call clears error immediately
    service.createRepository(ACCOUNT_ID, { slug: 'y', pollIntervalSeconds: null, maxConcurrentWorkers: null }).subscribe();

    // Assert — error is cleared before response
    expect(service.saveError()).toBeNull();
    httpMock
      .expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`)
      .flush(MOCK_CREATE_RESPONSE, { status: 201, statusText: 'Created' });
  });

  // Cycle 5: updateRepository calls PUT
  it('should PUT to /api/accounts/{accountId}/repositories/{id} when updateRepository is called', () => {
    // Arrange
    const request: UpdateRepositoryRequest = { pollIntervalSeconds: 600, isActive: true, maxConcurrentWorkers: 1 };

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
    const request: UpdateRepositoryRequest = { pollIntervalSeconds: 600, isActive: true, maxConcurrentWorkers: 1 };

    // Act
    service.updateRepository(ACCOUNT_ID, REPO_ID, request).subscribe();

    // Assert — before flush
    expect(service.saving()).toBe(true);
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/${REPO_ID}`).flush(MOCK_REPOSITORY);
  });

  it('should set saving to false and saveSuccess to true after updateRepository succeeds', () => {
    // Arrange
    const request: UpdateRepositoryRequest = { pollIntervalSeconds: 600, isActive: true, maxConcurrentWorkers: 1 };
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
    const request: UpdateRepositoryRequest = { pollIntervalSeconds: 600, isActive: true, maxConcurrentWorkers: 1 };

    // Act
    service.updateRepository(ACCOUNT_ID, REPO_ID, request).subscribe();
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/${REPO_ID}`).flush(updatedRepo);

    // Assert
    const repos = service.repositories();
    expect(repos.length).toBe(2);
    expect(repos.find(r => r.id === REPO_ID)?.pollIntervalSeconds).toBe(600);
    expect(repos.find(r => r.id === REPO_ID_2)?.slug).toBe('my-org/another-repo');
  });

  it('should set saveError when updateRepository fails with a ProblemDetails body', () => {
    // Arrange
    const request: UpdateRepositoryRequest = { pollIntervalSeconds: -1, isActive: true, maxConcurrentWorkers: 1 };
    service.updateRepository(ACCOUNT_ID, REPO_ID, request).subscribe({ error: () => {} });

    // Act
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/${REPO_ID}`).flush(
      { type: 'tag:foundry,2026:problems/Repository.InvalidInterval', title: 'Bad Request', status: 400, detail: 'Invalid interval.' },
      { status: 400, statusText: 'Bad Request' }
    );

    // Assert
    expect(service.saveError()).toBe('Invalid interval.');
  });

  it('should clear saveError at start of updateRepository', () => {
    // Arrange — first call that fails
    service.updateRepository(ACCOUNT_ID, REPO_ID, { pollIntervalSeconds: -1, isActive: true, maxConcurrentWorkers: 1 }).subscribe({ error: () => {} });
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/${REPO_ID}`).flush('Bad Request', {
      status: 400,
      statusText: 'Bad Request',
    });

    // Act — second call clears error immediately
    service.updateRepository(ACCOUNT_ID, REPO_ID, { pollIntervalSeconds: 600, isActive: true, maxConcurrentWorkers: 1 }).subscribe();

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

  it('should set a failed deleteFailure when deleteRepository fails with a ProblemDetails body', () => {
    // Arrange
    service.deleteRepository(ACCOUNT_ID, REPO_ID).subscribe({ error: () => {} });

    // Act
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/${REPO_ID}`).flush(
      { type: 'tag:foundry,2026:problems/Repository.InUse', title: 'Conflict', status: 409, detail: 'Repository is in use.' },
      { status: 409, statusText: 'Conflict' }
    );

    // Assert
    expect(service.deleteFailure()).toEqual({ kind: 'failed', message: 'Repository is in use.' });
  });

  it('should set a refused deleteFailure when deleteRepository is refused with blocking issues', () => {
    // Arrange
    service.deleteRepository(ACCOUNT_ID, REPO_ID).subscribe({ error: () => {} });

    // Act
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/${REPO_ID}`).flush(
      {
        type: 'tag:foundry,2026:problems/Repository.RemovalBlocked',
        title: 'Conflict',
        status: 409,
        detail: 'The repository cannot be removed while a worker is still active on: acme/api#42 (in_progress).',
        blockingIssues: [{ repositorySlug: 'acme/api', issueNumber: 42, state: 'in_progress' }],
      },
      { status: 409, statusText: 'Conflict' }
    );

    // Assert
    expect(service.deleteFailure()).toEqual({
      kind: 'refused',
      blockingIssues: [{ repositorySlug: 'acme/api', issueNumber: 42, state: 'in_progress' }],
    });
  });

  it('should clear deleteFailure at start of deleteRepository', () => {
    // Arrange — first call that fails
    service.deleteRepository(ACCOUNT_ID, REPO_ID).subscribe({ error: () => {} });
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/${REPO_ID}`).flush('Conflict', {
      status: 409,
      statusText: 'Conflict',
    });

    // Act — second call clears error immediately
    service.deleteRepository(ACCOUNT_ID, REPO_ID).subscribe();

    // Assert — error is cleared before response
    expect(service.deleteFailure()).toBeNull();
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

  it('should report loadAllRepositories as loaded, or failed when any request fails', async () => {
    // Arrange
    vi.spyOn(console, 'error').mockImplementation(() => {});
    const ACCOUNT_ID_2 = '00000000-0000-0000-0000-000000000002';

    // Act
    const loaded = service.loadAllRepositories([ACCOUNT_ID]);
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`).flush([MOCK_REPOSITORY]);
    const failed = service.loadAllRepositories([ACCOUNT_ID, ACCOUNT_ID_2]);
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`).flush([MOCK_REPOSITORY]);
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID_2}/repositories`).flush('boom', { status: 500, statusText: 'Server Error' });

    // Assert
    expect(await loaded).toBe('loaded');
    expect(await failed).toBe('failed');
    expect(await service.loadAllRepositories([])).toBe('loaded');
  });

  it('should sort combined repositories by position after loadAllRepositories succeeds', () => {
    // Arrange
    const ACCOUNT_ID_2 = '00000000-0000-0000-0000-000000000002';
    const repoA: RepositorySummary = { ...MOCK_REPOSITORY, position: 1 };
    const repoB: RepositorySummary = { ...MOCK_REPOSITORY_2, accountId: ACCOUNT_ID_2, position: 0 };

    // Act — API returns repos in reverse position order across accounts
    service.loadAllRepositories([ACCOUNT_ID, ACCOUNT_ID_2]);
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`).flush([repoA]);
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID_2}/repositories`).flush([repoB]);

    // Assert — result sorted by position ascending
    const repos = service.repositories();
    expect(repos[0].id).toBe(MOCK_REPOSITORY_2.id);
    expect(repos[1].id).toBe(MOCK_REPOSITORY.id);
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
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID_2}/repositories`).flush(
      { type: 'tag:foundry,2026:problems/Account.NotFound', title: 'Forbidden', status: 403, detail: 'Forbidden' },
      { status: 403, statusText: 'Forbidden' }
    );

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

  // Cycle 9: moveRepository calls PATCH /api/repositories/{id}/position
  it('should PATCH /api/repositories/{id}/position when moveRepository is called', () => {
    // Arrange

    // Act
    service.moveRepository(REPO_ID, 2).subscribe();
    const req = httpMock.expectOne(`/api/repositories/${REPO_ID}/position`);

    // Assert
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toEqual({ position: 2 });
    req.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('should reorder the repositories signal to reflect the new position after moveRepository succeeds', () => {
    // Arrange — load two repos in initial order
    service.loadRepositories(ACCOUNT_ID);
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`).flush([MOCK_REPOSITORY, MOCK_REPOSITORY_2]);

    // Act — move repo at index 0 to index 1
    service.moveRepository(REPO_ID, 1).subscribe();
    httpMock.expectOne(`/api/repositories/${REPO_ID}/position`).flush(null, { status: 204, statusText: 'No Content' });

    // Assert — local order updated immediately
    const repos = service.repositories();
    expect(repos[0].id).toBe(REPO_ID_2);
    expect(repos[1].id).toBe(REPO_ID);
  });

  // Fix 3a — signal reverted on PATCH error (WCAG 4.1.3 + status visibility)
  it('should revert the repositories signal to the pre-move order when moveRepository fails', () => {
    // Arrange — load two repos in initial order
    service.loadRepositories(ACCOUNT_ID);
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`).flush([MOCK_REPOSITORY, MOCK_REPOSITORY_2]);

    // Act — attempt to move repo at index 0 to index 1; server returns error
    service.moveRepository(REPO_ID, 1).subscribe({ error: () => {} });
    httpMock.expectOne(`/api/repositories/${REPO_ID}/position`).flush('Server error', {
      status: 500,
      statusText: 'Internal Server Error',
    });

    // Assert — original order preserved
    const repos = service.repositories();
    expect(repos[0].id).toBe(REPO_ID);
    expect(repos[1].id).toBe(REPO_ID_2);
  });

  // Cycle 8: recheckEligibility calls POST to recheck endpoint
  it('should POST to /api/accounts/{accountId}/repositories/{id}/recheck when recheckEligibility is called', () => {
    // Arrange

    // Act
    service.recheckEligibility(ACCOUNT_ID, REPO_ID).subscribe();
    const req = httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/${REPO_ID}/recheck`);

    // Assert
    expect(req.request.method).toBe('POST');
    req.flush(MOCK_REPOSITORY);
  });

  it('should update the repository in repositories signal after recheckEligibility succeeds', () => {
    // Arrange
    service.loadRepositories(ACCOUNT_ID);
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`).flush([MOCK_REPOSITORY_2]);

    const recheckedRepo: RepositorySummary = { ...MOCK_REPOSITORY_2, eligibility: { status: 'eligible', violations: [], reason: null } };

    // Act
    service.recheckEligibility(ACCOUNT_ID, REPO_ID_2).subscribe();
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/${REPO_ID_2}/recheck`).flush(recheckedRepo);

    // Assert
    const repos = service.repositories();
    expect(repos.length).toBe(1);
    expect(repos[0].eligibility?.status).toBe('eligible');
  });

  it('should POST to the apply-branch-rules endpoint and replace the repository from the response', () => {
    // Arrange
    service.loadRepositories(ACCOUNT_ID);
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`).flush([MOCK_REPOSITORY_2]);
    const response: ApplyBranchRulesResponse = {
      repository: { ...MOCK_REPOSITORY_2, eligibility: { status: 'eligible', violations: [], reason: null } },
      branchRules: { kind: 'applied' },
    };
    let received: ApplyBranchRulesResponse | undefined;

    // Act
    service.applyBranchRules(ACCOUNT_ID, REPO_ID_2).subscribe(r => { received = r; });
    const req = httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/${REPO_ID_2}/apply-branch-rules`);
    req.flush(response);

    // Assert
    expect(req.request.method).toBe('POST');
    expect(received).toEqual(response);
    const repos = service.repositories();
    expect(repos.length).toBe(1);
    expect(repos[0].eligibility?.status).toBe('eligible');
  });

  it('should leave the repositories signal unchanged when applyBranchRules fails', () => {
    // Arrange
    service.loadRepositories(ACCOUNT_ID);
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories`).flush([MOCK_REPOSITORY_2]);
    let failed = false;

    // Act
    service.applyBranchRules(ACCOUNT_ID, REPO_ID_2).subscribe({ error: () => { failed = true; } });
    httpMock.expectOne(`/api/accounts/${ACCOUNT_ID}/repositories/${REPO_ID_2}/apply-branch-rules`)
      .flush('nope', { status: 404, statusText: 'Not Found' });

    // Assert
    expect(failed).toBe(true);
    expect(service.repositories()).toEqual([MOCK_REPOSITORY_2]);
  });
});

describe('RepositoryService live webhook updates', () => {
  const LIST_URL = `/api/accounts/${ACCOUNT_ID}/repositories`;
  const WEBHOOK_URL = `${LIST_URL}/${REPO_ID}/webhook`;
  const ACTIVE_INFO: RepositoryWebhookInfo = {
    status: 'active',
    payloadUrl: 'https://foundry.example.com/webhooks/github/abc',
    contentType: 'application/json',
    events: ['issues'],
    backstopIntervalMinutes: 30,
    provider: 'github',
  };

  let service: RepositoryService;
  let httpMock: HttpTestingController;
  let signalR: ReturnType<typeof makeSignalRStub>;
  let updates: RepositoryWebhookUpdate[];

  beforeEach(() => {
    vi.useFakeTimers();
    ({ service, httpMock, signalR } = setupService());
    updates = [];
    service.webhookUpdates.subscribe((update) => updates.push(update));
  });

  afterEach(() => {
    httpMock.verify();
    vi.useRealTimers();
  });

  async function loadList(): Promise<void> {
    const loaded = service.loadAllRepositories([ACCOUNT_ID]);
    httpMock.expectOne(LIST_URL).flush([
      { ...MOCK_REPOSITORY, webhookStatus: 'pending' },
      { ...MOCK_REPOSITORY_2, webhookStatus: 'pending' },
    ]);
    await loaded;
  }

  function statusOf(id: string): string | undefined {
    return service.repositories().find((r) => r.id === id)?.webhookStatus;
  }

  it('ignores a repository change before any list has been loaded', () => {
    signalR.repositoryWebhookChanged.next({ repositoryId: REPO_ID });
    vi.advanceTimersByTime(300);

    httpMock.expectNone(WEBHOOK_URL);
    expect(updates).toEqual([]);
  });

  it('re-fetches a cached repository webhook and updates its row status', async () => {
    await loadList();

    signalR.repositoryWebhookChanged.next({ repositoryId: REPO_ID });
    vi.advanceTimersByTime(300);
    httpMock.expectOne(WEBHOOK_URL).flush(ACTIVE_INFO);

    expect(statusOf(REPO_ID)).toBe('active');
    expect(statusOf(REPO_ID_2)).toBe('pending');
    expect(updates).toEqual([{ kind: 'repository', repositoryId: REPO_ID, info: ACTIVE_INFO }]);
  });

  it('ignores a change for a repository that is not cached', async () => {
    await loadList();

    signalR.repositoryWebhookChanged.next({ repositoryId: 'unknown' });
    vi.advanceTimersByTime(300);

    httpMock.expectNone(`${LIST_URL}/unknown/webhook`);
    expect(updates).toEqual([]);
  });

  it('batches a burst for one repository into one request', async () => {
    await loadList();

    signalR.repositoryWebhookChanged.next({ repositoryId: REPO_ID });
    vi.advanceTimersByTime(100);
    signalR.repositoryWebhookChanged.next({ repositoryId: REPO_ID });
    vi.advanceTimersByTime(300);

    httpMock.expectOne(WEBHOOK_URL).flush(ACTIVE_INFO);
  });

  it('re-fetches each repository of a burst that names two', async () => {
    await loadList();

    signalR.repositoryWebhookChanged.next({ repositoryId: REPO_ID });
    signalR.repositoryWebhookChanged.next({ repositoryId: REPO_ID_2 });
    vi.advanceTimersByTime(300);

    httpMock.expectOne(WEBHOOK_URL).flush(ACTIVE_INFO);
    httpMock.expectOne(`${LIST_URL}/${REPO_ID_2}/webhook`).flush(ACTIVE_INFO);
    expect(statusOf(REPO_ID)).toBe('active');
    expect(statusOf(REPO_ID_2)).toBe('active');
  });

  it('keeps the last known status and keeps listening when a re-fetch fails', async () => {
    await loadList();
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);

    signalR.repositoryWebhookChanged.next({ repositoryId: REPO_ID });
    vi.advanceTimersByTime(300);
    httpMock.expectOne(WEBHOOK_URL).flush('boom', { status: 500, statusText: 'Server Error' });
    signalR.repositoryWebhookChanged.next({ repositoryId: REPO_ID });
    vi.advanceTimersByTime(300);
    httpMock.expectOne(WEBHOOK_URL).flush(ACTIVE_INFO);

    expect(consoleError).toHaveBeenCalled();
    expect(statusOf(REPO_ID)).toBe('active');
    consoleError.mockRestore();
  });

  it('reloads the list in the background when every repository changed', async () => {
    await loadList();

    signalR.repositoryWebhooksChanged.next();
    vi.advanceTimersByTime(300);

    expect(service.loading()).toBe(false);
    httpMock.expectOne(LIST_URL).flush([{ ...MOCK_REPOSITORY, webhookStatus: 'active' }]);
    expect(service.loading()).toBe(false);
    expect(statusOf(REPO_ID)).toBe('active');
    expect(updates).toEqual([{ kind: 'all' }]);
  });

  it('ignores a reconnect before any list has been loaded', () => {
    signalR.reconnected.next();
    vi.advanceTimersByTime(300);

    httpMock.expectNone(LIST_URL);
    expect(updates).toEqual([]);
  });

  it('reloads the list in the background on reconnect after a list was loaded', async () => {
    await loadList();

    signalR.reconnected.next();
    vi.advanceTimersByTime(300);

    httpMock.expectOne(LIST_URL).flush([{ ...MOCK_REPOSITORY, webhookStatus: 'active' }]);
    expect(statusOf(REPO_ID)).toBe('active');
    expect(updates).toEqual([{ kind: 'all' }]);
  });

  it('keeps the list and sets no load error when a background reload fails', async () => {
    await loadList();
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);

    signalR.repositoryWebhooksChanged.next();
    vi.advanceTimersByTime(300);
    httpMock.expectOne(LIST_URL).flush('boom', { status: 500, statusText: 'Server Error' });

    expect(service.repositories().map((r) => r.id)).toEqual([REPO_ID, REPO_ID_2]);
    expect(service.loadError()).toBeNull();
    consoleError.mockRestore();
  });

  it('drops a background reload sent before the operator reordered the list', async () => {
    await loadList();

    signalR.repositoryWebhooksChanged.next();
    vi.advanceTimersByTime(300);
    const reload = httpMock.expectOne(LIST_URL);
    service.moveRepository(REPO_ID, 1).subscribe();
    httpMock.expectOne(`/api/repositories/${REPO_ID}/position`).flush(null);
    reload.flush([{ ...MOCK_REPOSITORY, webhookStatus: 'active' }, MOCK_REPOSITORY_2]);

    expect(service.repositories().map((r) => r.id)).toEqual([REPO_ID_2, REPO_ID]);
    expect(statusOf(REPO_ID)).toBe('pending');
  });
});

describe('RepositoryService unclaimed repositories', () => {
  const UNCLAIMED_URL = '/api/repositories/unclaimed';
  const UNCLAIMED: UnclaimedRepository = {
    id: REPO_ID,
    host: 'gitlab.example.com',
    slug: 'orphan/project',
    violation: 'no-credential:orphan',
    violationDescription: 'No account covers the "orphan" namespace.',
  };
  const UNCLAIMED_2: UnclaimedRepository = { ...UNCLAIMED, id: REPO_ID_2, slug: 'orphan/other' };

  let service: RepositoryService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    const setup = setupService();
    service = setup.service;
    httpMock = setup.httpMock;
  });

  afterEach(() => httpMock.verify());

  it('loads the unclaimed repositories into the signal and resolves loaded', async () => {
    // Act
    const outcome = service.loadUnclaimedRepositories();
    httpMock.expectOne(UNCLAIMED_URL).flush([UNCLAIMED]);

    // Assert
    expect(await outcome).toBe('loaded');
    expect(service.unclaimedRepositories()).toEqual([UNCLAIMED]);
    expect(service.unclaimedLoading()).toBe(false);
    expect(service.unclaimedLoadError()).toBeNull();
  });

  it('keeps the previous list while reloading', () => {
    // Arrange
    service.loadUnclaimedRepositories();
    httpMock.expectOne(UNCLAIMED_URL).flush([UNCLAIMED]);

    // Act
    service.loadUnclaimedRepositories();

    // Assert
    expect(service.unclaimedLoading()).toBe(true);
    expect(service.unclaimedRepositories()).toEqual([UNCLAIMED]);
    httpMock.expectOne(UNCLAIMED_URL).flush([]);
  });

  it('sets the load error from the ProblemDetails body and resolves failed', async () => {
    // Act
    const outcome = service.loadUnclaimedRepositories();
    httpMock.expectOne(UNCLAIMED_URL).flush(
      { title: 'Server Error', status: 500, detail: 'Database unavailable.' },
      { status: 500, statusText: 'Server Error' },
    );

    // Assert
    expect(await outcome).toBe('failed');
    expect(service.unclaimedLoadError()).toBe('Database unavailable.');
    expect(service.unclaimedLoading()).toBe(false);
  });

  it('clears the load error when a reload starts', () => {
    // Arrange
    service.loadUnclaimedRepositories();
    httpMock.expectOne(UNCLAIMED_URL).flush('boom', { status: 500, statusText: 'Server Error' });

    // Act
    service.loadUnclaimedRepositories();

    // Assert
    expect(service.unclaimedLoadError()).toBeNull();
    httpMock.expectOne(UNCLAIMED_URL).flush([]);
  });

  it('deletes through DELETE /api/repositories/{id} and filters the row out', () => {
    // Arrange
    service.loadUnclaimedRepositories();
    httpMock.expectOne(UNCLAIMED_URL).flush([UNCLAIMED, UNCLAIMED_2]);

    // Act
    service.deleteUnclaimedRepository(REPO_ID).subscribe();
    const req = httpMock.expectOne(`/api/repositories/${REPO_ID}`);
    req.flush(null, { status: 204, statusText: 'No Content' });

    // Assert
    expect(req.request.method).toBe('DELETE');
    expect(service.unclaimedRepositories()).toEqual([UNCLAIMED_2]);
  });

  it('sets a refused unclaimedDeleteFailure when the removal is refused', () => {
    // Arrange
    service.deleteUnclaimedRepository(REPO_ID).subscribe({ error: () => {} });

    // Act
    httpMock.expectOne(`/api/repositories/${REPO_ID}`).flush(
      {
        title: 'Conflict',
        status: 409,
        detail: 'The repository cannot be removed while a worker is still active on: orphan/project#4 (in_progress).',
        blockingIssues: [{ repositorySlug: 'orphan/project', issueNumber: 4, state: 'in_progress' }],
      },
      { status: 409, statusText: 'Conflict' },
    );

    // Assert
    expect(service.unclaimedDeleteFailure()).toEqual({
      kind: 'refused',
      blockingIssues: [{ repositorySlug: 'orphan/project', issueNumber: 4, state: 'in_progress' }],
    });
    expect(service.deleteFailure()).toBeNull();
  });

  it('clears unclaimedDeleteFailure when a delete starts', () => {
    // Arrange
    service.deleteUnclaimedRepository(REPO_ID).subscribe({ error: () => {} });
    httpMock.expectOne(`/api/repositories/${REPO_ID}`).flush(
      { status: 404, detail: 'Not found.' },
      { status: 404, statusText: 'Not Found' },
    );

    // Act
    service.deleteUnclaimedRepository(REPO_ID).subscribe();

    // Assert
    expect(service.unclaimedDeleteFailure()).toBeNull();
    httpMock.expectOne(`/api/repositories/${REPO_ID}`).flush(null, { status: 204, statusText: 'No Content' });
  });

  it('recheckClaims posts the host to the unclaimed recheck endpoint and returns the reclaimed count', () => {
    // Arrange
    let reclaimed: number | undefined;

    // Act
    service.recheckClaims('gitlab.example.com').subscribe(r => { reclaimed = r.reclaimedRepositoryCount; });
    const req = httpMock.expectOne(r => r.url === '/api/repositories/unclaimed/recheck');
    req.flush({ reclaimedRepositoryCount: 2 });

    // Assert
    expect(req.request.method).toBe('POST');
    expect(req.request.params.get('host')).toBe('gitlab.example.com');
    expect(reclaimed).toBe(2);
  });

  it('recheckClaims clears unclaimedDeleteFailure when it starts', () => {
    // Arrange
    service.deleteUnclaimedRepository(REPO_ID).subscribe({ error: () => {} });
    httpMock.expectOne(`/api/repositories/${REPO_ID}`).flush(
      { status: 404, detail: 'Not found.' },
      { status: 404, statusText: 'Not Found' },
    );

    // Act
    service.recheckClaims('gitlab.example.com').subscribe();

    // Assert
    expect(service.unclaimedDeleteFailure()).toBeNull();
    httpMock.expectOne(r => r.url === '/api/repositories/unclaimed/recheck').flush({ reclaimedRepositoryCount: 0 });
  });
});

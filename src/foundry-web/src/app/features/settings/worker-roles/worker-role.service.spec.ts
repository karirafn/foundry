import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { WorkerRoleService } from './worker-role.service';
import { buildModelOptions, buildWorkerRole, buildWorkerRoleCatalog } from '../testing/worker-role-catalog.fixture';

function setup() {
  TestBed.configureTestingModule({
    providers: [WorkerRoleService, provideHttpClient(), provideHttpClientTesting()],
  });
  return {
    svc: TestBed.inject(WorkerRoleService),
    httpMock: TestBed.inject(HttpTestingController),
  };
}

describe('WorkerRoleService', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    TestBed.resetTestingModule();
  });

  it('should start idle with no catalog', () => {
    // Arrange / Act
    const { svc } = setup();

    // Assert
    expect(svc.status()).toBe('idle');
    expect(svc.catalog()).toBeNull();
    expect(svc.loadError()).toBeNull();
  });

  it('should GET the catalog from /api/worker-roles and expose it', () => {
    // Arrange
    const { svc, httpMock } = setup();
    const catalog = buildWorkerRoleCatalog();

    // Act
    svc.load();
    const req = httpMock.expectOne('/api/worker-roles');
    expect(svc.status()).toBe('loading');
    req.flush(catalog);

    // Assert
    expect(req.request.method).toBe('GET');
    expect(svc.status()).toBe('loaded');
    expect(svc.catalog()).toEqual(catalog);
  });

  it('should report notReady (not an error) when the catalog is not loaded yet (503)', () => {
    // Arrange
    const { svc, httpMock } = setup();

    // Act
    svc.load();
    httpMock
      .expectOne('/api/worker-roles')
      .flush(
        { title: 'Service Unavailable', status: 503, detail: 'Catalog not loaded' },
        { status: 503, statusText: 'Service Unavailable' },
      );

    // Assert
    expect(svc.status()).toBe('notReady');
    expect(svc.loadError()).toBeNull();
    expect(svc.catalog()).toBeNull();
  });

  it('should surface the ProblemDetails detail on other failures', () => {
    // Arrange
    const { svc, httpMock } = setup();
    vi.spyOn(console, 'error').mockImplementation(() => {});

    // Act
    svc.load();
    httpMock
      .expectOne('/api/worker-roles')
      .flush({ detail: 'Boom' }, { status: 500, statusText: 'Server Error' });

    // Assert
    expect(svc.status()).toBe('error');
    expect(svc.loadError()).toBe('Boom');
  });

  it('should fall back to a generic message when the failure has no detail', () => {
    // Arrange
    const { svc, httpMock } = setup();
    vi.spyOn(console, 'error').mockImplementation(() => {});

    // Act
    svc.load();
    httpMock
      .expectOne('/api/worker-roles')
      .flush(null, { status: 500, statusText: 'Server Error' });

    // Assert
    expect(svc.loadError()).toBe('Failed to load worker roles');
  });

  it('should ignore a stale response that resolves after a newer request', () => {
    // Arrange
    const { svc, httpMock } = setup();
    svc.load();
    svc.load();
    const [stale, latest] = httpMock.match('/api/worker-roles');

    // Act
    latest.flush(buildWorkerRoleCatalog());
    stale.flush(null, { status: 503, statusText: 'Service Unavailable' });

    // Assert
    expect(svc.status()).toBe('loaded');
    expect(svc.catalog()).not.toBeNull();
  });

  describe('model options', () => {
    it('should GET the model options with refresh=true and expose the refresh error', () => {
      // Arrange
      const { svc, httpMock } = setup();
      const options = buildModelOptions({ refreshError: 'The Models API answered HTTP 401.' });

      // Act
      svc.loadModelOptions();
      const req = httpMock.expectOne((r) => r.url === '/api/worker-roles/models');
      req.flush(options);

      // Assert
      expect(req.request.params.get('refresh')).toBe('true');
      expect(svc.modelOptions()).toEqual(options);
      expect(svc.modelOptionsStatus()).toBe('loaded');
      expect(svc.modelOptionsError()).toBe('The Models API answered HTTP 401.');
    });

    it('should keep the previous options when a refresh request fails', () => {
      // Arrange
      const { svc, httpMock } = setup();
      vi.spyOn(console, 'error').mockImplementation(() => {});
      const options = buildModelOptions();
      svc.loadModelOptions();
      httpMock.expectOne((r) => r.url === '/api/worker-roles/models').flush(options);

      // Act
      svc.loadModelOptions();
      httpMock
        .expectOne((r) => r.url === '/api/worker-roles/models')
        .flush(null, { status: 500, statusText: 'Server Error' });

      // Assert
      expect(svc.modelOptions()).toEqual(options);
      expect(svc.modelOptionsStatus()).toBe('error');
      expect(svc.modelOptionsError()).toBe('The model list could not be loaded.');
    });
  });

  describe('saveModel', () => {
    it('should PUT the model and update the role card in the catalog', () => {
      // Arrange
      const { svc, httpMock } = setup();
      svc.load();
      httpMock.expectOne('/api/worker-roles').flush(buildWorkerRoleCatalog());

      // Act
      svc.saveModel('implement', 'sonnet');
      expect(svc.savingModel()).toBe('implement');
      const req = httpMock.expectOne('/api/worker-roles/implement/model');
      req.flush({ role: 'implement', model: 'sonnet' });

      // Assert
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual({ model: 'sonnet' });
      expect(svc.catalog()!.roles[0]).toEqual(buildWorkerRole({ model: 'sonnet' }));
      expect(svc.savingModel()).toBeNull();
      expect(svc.savedModel()).toBe('implement');
    });

    it('should expose a rejected save keyed by role with its code', () => {
      // Arrange
      const { svc, httpMock } = setup();
      vi.spyOn(console, 'error').mockImplementation(() => {});

      // Act
      svc.saveModel('implement', 'claude-typo');
      httpMock.expectOne('/api/worker-roles/implement/model').flush(
        { detail: 'Not listed.', type: 'tag:foundry,2026:problems/Worker.RoleModelNotListed' },
        { status: 400, statusText: 'Bad Request' },
      );

      // Assert
      expect(svc.saveModelError()).toEqual({
        role: 'implement',
        code: 'Worker.RoleModelNotListed',
        message: 'Not listed.',
      });
      expect(svc.savingModel()).toBeNull();
    });
  });
});

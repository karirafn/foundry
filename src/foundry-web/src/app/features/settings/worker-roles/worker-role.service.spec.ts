import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { WorkerRoleService } from './worker-role.service';
import { buildWorkerRoleCatalog } from '../testing/worker-role-catalog.fixture';

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
});

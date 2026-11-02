import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { DispatchCapacityService } from './dispatch-capacity.service';

function setup() {
  TestBed.configureTestingModule({
    providers: [DispatchCapacityService, provideHttpClient(), provideHttpClientTesting()],
  });
  return {
    svc: TestBed.inject(DispatchCapacityService),
    httpMock: TestBed.inject(HttpTestingController),
  };
}

describe('DispatchCapacityService', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    TestBed.resetTestingModule();
  });

  it('should load capacity from the workers capacity endpoint', () => {
    // Arrange
    const { svc, httpMock } = setup();

    // Act
    svc.load();
    httpMock.expectOne('/api/workers/capacity').flush({ maxConcurrent: 3, occupiedSlots: '1', freeSlots: 2 });

    // Assert
    expect(svc.capacity()).toEqual({ maxConcurrent: 3, occupiedSlots: 1, freeSlots: 2 });
    expect(svc.loadError()).toBeNull();
  });

  it('should ignore a stale response that resolves after a newer request', () => {
    // Arrange
    const { svc, httpMock } = setup();
    svc.load();
    svc.load();
    const [stale, latest] = httpMock.match('/api/workers/capacity');

    // Act
    latest.flush({ maxConcurrent: 3, occupiedSlots: 3, freeSlots: 0 });
    stale.flush({ maxConcurrent: 3, occupiedSlots: 0, freeSlots: 3 });

    // Assert
    expect(svc.capacity()?.freeSlots).toBe(0);
  });

  it('should expose an error and keep the last capacity when the request fails', () => {
    // Arrange
    const { svc, httpMock } = setup();
    vi.spyOn(console, 'error').mockImplementation(() => {});
    svc.load();
    httpMock.expectOne('/api/workers/capacity').flush({ maxConcurrent: 2, occupiedSlots: 1, freeSlots: 1 });

    // Act
    svc.load();
    httpMock.expectOne('/api/workers/capacity').flush(null, { status: 500, statusText: 'Server Error' });

    // Assert
    expect(svc.loadError()).toBe('Failed to load dispatch capacity');
    expect(svc.capacity()?.freeSlots).toBe(1);
  });
});

import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { Subject } from 'rxjs';
import { vi } from 'vitest';
import { SystemStatusService } from './system-status.service';
import { SystemSignalRService } from './system-signalr.service';
import { SystemStatusResponse } from '../models/system-status.model';

function createMockSignalRService() {
  const reconnected$ = new Subject<void>();
  return {
    reconnected: reconnected$.asObservable(),
    applyDockerAvailability: vi.fn(),
    notifications: signal([]).asReadonly(),
    _reconnected$: reconnected$,
  };
}

function setup() {
  const mockSignalR = createMockSignalRService();

  TestBed.configureTestingModule({
    providers: [
      SystemStatusService,
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: SystemSignalRService, useValue: mockSignalR },
    ],
  });

  const svc = TestBed.inject(SystemStatusService);
  const http = TestBed.inject(HttpTestingController);

  return { svc, http, mockSignalR };
}

describe('SystemStatusService', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
  });

  // Cycle 1: on construction, seeds via GET /api/system/status
  it('should call applyDockerAvailability(true) when GET returns dockerAvailable: true', () => {
    // Arrange
    const { http, mockSignalR } = setup();
    const response: SystemStatusResponse = { dockerAvailable: true };

    // Act
    const req = http.expectOne('/api/system/status');
    req.flush(response);

    // Assert
    expect(mockSignalR.applyDockerAvailability).toHaveBeenCalledWith(true);
  });

  // Cycle 2: on construction, seeds with false when Docker is unavailable
  it('should call applyDockerAvailability(false) when GET returns dockerAvailable: false', () => {
    // Arrange
    const { http, mockSignalR } = setup();
    const response: SystemStatusResponse = { dockerAvailable: false };

    // Act
    const req = http.expectOne('/api/system/status');
    req.flush(response);

    // Assert
    expect(mockSignalR.applyDockerAvailability).toHaveBeenCalledWith(false);
  });

  // Cycle 3: HTTP error is swallowed — does not call applyDockerAvailability
  it('should swallow HTTP errors and not call applyDockerAvailability', () => {
    // Arrange
    const { http, mockSignalR } = setup();

    // Act
    const req = http.expectOne('/api/system/status');
    req.flush('Server error', { status: 500, statusText: 'Internal Server Error' });

    // Assert
    expect(mockSignalR.applyDockerAvailability).not.toHaveBeenCalled();
  });

  // Cycle 4: reconnect triggers a new GET and re-seeds
  it('should re-seed from GET /api/system/status when SignalR reconnects', () => {
    // Arrange
    const { http, mockSignalR } = setup();
    const initReq = http.expectOne('/api/system/status');
    initReq.flush({ dockerAvailable: true });

    // Act — trigger reconnect
    mockSignalR._reconnected$.next();

    const reconnectReq = http.expectOne('/api/system/status');
    reconnectReq.flush({ dockerAvailable: false });

    // Assert
    expect(mockSignalR.applyDockerAvailability).toHaveBeenCalledTimes(2);
    expect(mockSignalR.applyDockerAvailability).toHaveBeenNthCalledWith(1, true);
    expect(mockSignalR.applyDockerAvailability).toHaveBeenNthCalledWith(2, false);
  });

  // Cycle 5: reconnect resurrection guard — reconnect after recovery clears previously-seeded banner
  it('should clear the docker banner when reconnect reveals Docker has recovered', () => {
    // Arrange
    const { http, mockSignalR } = setup();

    // Initial seed: Docker is down
    const initReq = http.expectOne('/api/system/status');
    initReq.flush({ dockerAvailable: false });
    expect(mockSignalR.applyDockerAvailability).toHaveBeenLastCalledWith(false);

    // Act — reconnect, Docker now available
    mockSignalR._reconnected$.next();
    const reconnectReq = http.expectOne('/api/system/status');
    reconnectReq.flush({ dockerAvailable: true });

    // Assert — true passed (clear the banner)
    expect(mockSignalR.applyDockerAvailability).toHaveBeenLastCalledWith(true);
  });
});

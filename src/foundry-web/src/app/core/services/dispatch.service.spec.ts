import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { DispatchService } from './dispatch.service';
import { GlobalSettingsResponse } from '../../features/settings/settings.model';

function buildResponse(overrides: Partial<GlobalSettingsResponse> = {}): GlobalSettingsResponse {
  return {
    authMode: 'ApiKey',
    maxConcurrent: 3,
    timeoutMinutes: 60,
    accessTokenPresent: false,
    refreshTokenPresent: false,
    expiresAt: null,
    subscriptionType: null,
    systemPromptTemplate: null,
    workerPromptTemplate: null,
    usageLimitResetsAt: null,
    isDispatchPaused: false,
    autoResumeOnUsageReset: true,
    defaultCooldownMinutes: 60,
    installDotnet: false,
    installAngular: false,
    installGlab: false,
    installGh: false,
    imageBuildStatus: 'Idle',
    lastImageBuildError: null,
    ...overrides,
  };
}

function setupService() {
  TestBed.configureTestingModule({
    providers: [
      DispatchService,
      provideHttpClient(),
      provideHttpClientTesting(),
    ],
  });
  return {
    service: TestBed.inject(DispatchService),
    httpMock: TestBed.inject(HttpTestingController),
  };
}

describe('DispatchService', () => {
  let service: DispatchService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    const setup = setupService();
    service = setup.service;
    httpMock = setup.httpMock;
  });

  afterEach(() => httpMock.verify());

  // Cycle 1: initial signal state
  it('should start with default signal values', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
    // (no action — testing initial state)

    // Assert
    expect(service.isDispatchPaused()).toBe(false);
    expect(service.usageLimitResetsAt()).toBeNull();
    expect(service.pausing()).toBe(false);
    expect(service.resuming()).toBe(false);
    expect(service.pauseResumeError()).toBeNull();
  });

  // Cycle 2: updateFromSettings populates isDispatchPaused
  it('should set isDispatchPaused from updateFromSettings response', () => {
    // Arrange
    const response = buildResponse({ isDispatchPaused: true });

    // Act
    service.updateFromSettings(response);

    // Assert
    expect(service.isDispatchPaused()).toBe(true);
  });

  // Cycle 3: updateFromSettings populates usageLimitResetsAt
  it('should set usageLimitResetsAt from updateFromSettings response', () => {
    // Arrange
    const response = buildResponse({ usageLimitResetsAt: '2026-07-01T12:00:00Z' });

    // Act
    service.updateFromSettings(response);

    // Assert
    expect(service.usageLimitResetsAt()).toBe('2026-07-01T12:00:00Z');
  });

  // Cycle 4: updateFromSettings clears usageLimitResetsAt when null
  it('should clear usageLimitResetsAt when updateFromSettings response has null', () => {
    // Arrange
    service.updateFromSettings(buildResponse({ usageLimitResetsAt: '2026-07-01T12:00:00Z' }));
    expect(service.usageLimitResetsAt()).not.toBeNull();

    // Act
    service.updateFromSettings(buildResponse({ usageLimitResetsAt: null }));

    // Assert
    expect(service.usageLimitResetsAt()).toBeNull();
  });

  // Cycle 5: pauseDispatch sends POST to /api/settings/dispatch/pause
  it('should POST to /api/settings/dispatch/pause when pauseDispatch is called', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
    service.pauseDispatch();
    const req = httpMock.expectOne('/api/settings/dispatch/pause');

    // Assert
    expect(req.request.method).toBe('POST');
    req.flush(buildResponse({ isDispatchPaused: true }));
  });

  // Cycle 6: pauseDispatch sets pausing true while in flight
  it('should set pausing to true while pauseDispatch is in flight', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
    service.pauseDispatch();

    // Assert — before flush
    expect(service.pausing()).toBe(true);
    httpMock.expectOne('/api/settings/dispatch/pause').flush(buildResponse({ isDispatchPaused: true }));
  });

  // Cycle 7: pauseDispatch updates signals on success
  it('should update isDispatchPaused and set pausing false after pauseDispatch succeeds', () => {
    // Arrange
    service.pauseDispatch();
    httpMock.expectOne('/api/settings/dispatch/pause').flush(buildResponse({ isDispatchPaused: true }));

    // Assert
    expect(service.pausing()).toBe(false);
    expect(service.isDispatchPaused()).toBe(true);
    expect(service.pauseResumeError()).toBeNull();
  });

  // Cycle 8: pauseDispatch sets pauseResumeError on failure
  it('should set pauseResumeError when pauseDispatch fails', () => {
    // Arrange
    service.pauseDispatch();
    httpMock.expectOne('/api/settings/dispatch/pause').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });

    // Assert
    expect(service.pauseResumeError()).not.toBeNull();
    expect(service.pausing()).toBe(false);
  });

  it('should set pauseResumeError to a fixed user-facing string when pauseDispatch fails', () => {
    // Arrange
    service.pauseDispatch();
    httpMock.expectOne('/api/settings/dispatch/pause').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });

    // Assert
    expect(service.pauseResumeError()).toBe('Failed to pause dispatch');
  });

  // Cycle 9: resumeDispatch sends POST to /api/settings/dispatch/resume
  it('should POST to /api/settings/dispatch/resume when resumeDispatch is called', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
    service.resumeDispatch();
    const req = httpMock.expectOne('/api/settings/dispatch/resume');

    // Assert
    expect(req.request.method).toBe('POST');
    req.flush(buildResponse({ isDispatchPaused: false }));
  });

  // Cycle 10: resumeDispatch sets resuming true while in flight
  it('should set resuming to true while resumeDispatch is in flight', () => {
    // Arrange
    // (service initialized by test setup)

    // Act
    service.resumeDispatch();

    // Assert — before flush
    expect(service.resuming()).toBe(true);
    httpMock.expectOne('/api/settings/dispatch/resume').flush(buildResponse({ isDispatchPaused: false }));
  });

  // Cycle 11: resumeDispatch updates signals on success
  it('should update isDispatchPaused and set resuming false after resumeDispatch succeeds', () => {
    // Arrange
    service.updateFromSettings(buildResponse({ isDispatchPaused: true }));
    service.resumeDispatch();
    httpMock.expectOne('/api/settings/dispatch/resume').flush(buildResponse({ isDispatchPaused: false }));

    // Assert
    expect(service.resuming()).toBe(false);
    expect(service.isDispatchPaused()).toBe(false);
    expect(service.pauseResumeError()).toBeNull();
  });

  // Cycle 12: resumeDispatch sets pauseResumeError on failure
  it('should set pauseResumeError when resumeDispatch fails', () => {
    // Arrange
    service.resumeDispatch();
    httpMock.expectOne('/api/settings/dispatch/resume').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });

    // Assert
    expect(service.pauseResumeError()).not.toBeNull();
    expect(service.resuming()).toBe(false);
  });

  it('should set pauseResumeError to a fixed user-facing string when resumeDispatch fails', () => {
    // Arrange
    service.resumeDispatch();
    httpMock.expectOne('/api/settings/dispatch/resume').flush('Server Error', {
      status: 500,
      statusText: 'Internal Server Error',
    });

    // Assert
    expect(service.pauseResumeError()).toBe('Failed to resume dispatch');
  });
});

/**
 * Shared test harness for settings-page component specs.
 *
 * Import fixtures and helpers from here instead of duplicating them in each
 * page spec.
 */
import { TestBed, ComponentFixture } from '@angular/core/testing';
import { Type } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { NEVER } from 'rxjs';
import { SettingsService } from '../../../core/services/settings.service';
import { SystemSignalRService } from '../../../core/services/system-signalr.service';
import { RateBudgetService } from '../../../core/services/rate-budget.service';

// ---------------------------------------------------------------------------
// Mock service doubles
// ---------------------------------------------------------------------------

export const mockSystemSignalR = {
  reconnected: NEVER,
  reloadTrigger: NEVER,
  loginSessionUpdate: NEVER,
  notifications: signal([]).asReadonly(),
};

export const mockRateBudgetService = {
  snapshot: signal(null).asReadonly(),
};

// ---------------------------------------------------------------------------
// Fixture constants shared across settings page specs
// ---------------------------------------------------------------------------

export const IMAGE_FLAGS_DEFAULTS = {
  installDotnet: false,
  installAngular: false,
  installChromium: false,
  installDocker: false,
  imageBuildStatus: 'Idle',
  lastImageBuildError: null,
  hasUsableImage: false,
  nextRetryAt: null,
  attempt: 0,
};

export const BASE_RESPONSE = {
  usageLimitResetsAt: null,
  isDispatchPaused: false,
  autoResumeOnUsageReset: true,
  ...IMAGE_FLAGS_DEFAULTS,
  systemPromptTemplate: null,
  workerPromptTemplate: null,
  branchNamingInstruction: null,
};

export const API_KEY_RESPONSE = {
  maxConcurrent: 3,
  timeoutMinutes: 60,
  probeIntervalMinutes: 60,
  pollIntervalSeconds: 30,
  ...BASE_RESPONSE,
};

export const CREDENTIALS_API_KEY = {
  accountId: '00000000-0000-0000-0000-000000000001',
  authMode: 'ApiKey',
  apiKeyStatus: 'Present',
  oAuthStatus: 'NotConfigured',
  subscriptionType: null,
  oAuthAccountEmail: null,
  oAuthAccountOrgName: null,
};

export const CREDENTIALS_API_KEY_NOT_CONFIGURED = {
  accountId: '00000000-0000-0000-0000-000000000001',
  authMode: 'ApiKey',
  apiKeyStatus: 'NotConfigured',
  oAuthStatus: 'NotConfigured',
  subscriptionType: null,
  oAuthAccountEmail: null,
  oAuthAccountOrgName: null,
};

export const CREDENTIALS_API_KEY_UNREADABLE = {
  accountId: '00000000-0000-0000-0000-000000000001',
  authMode: 'ApiKey',
  apiKeyStatus: 'Unreadable',
  oAuthStatus: 'NotConfigured',
  subscriptionType: null,
  oAuthAccountEmail: null,
  oAuthAccountOrgName: null,
};

export const CREDENTIALS_OAUTH = {
  accountId: '00000000-0000-0000-0000-000000000001',
  authMode: 'OAuth',
  oAuthStatus: 'Present',
  subscriptionType: 'pro',
  oAuthAccountEmail: 'user@example.com',
  oAuthAccountOrgName: null,
};

export const CREDENTIALS_OAUTH_NOT_CONFIGURED = {
  accountId: '00000000-0000-0000-0000-000000000001',
  authMode: 'OAuth',
  oAuthStatus: 'NotConfigured',
  subscriptionType: null,
  oAuthAccountEmail: null,
  oAuthAccountOrgName: null,
};

export const CREDENTIALS_OAUTH_RELOGIN_NEEDED = {
  accountId: '00000000-0000-0000-0000-000000000001',
  authMode: 'OAuth',
  oAuthStatus: 'ReLoginNeeded',
  subscriptionType: null,
  oAuthAccountEmail: null,
  oAuthAccountOrgName: null,
};

export const OAUTH_RESPONSE = { ...API_KEY_RESPONSE };
export const OAUTH_NOT_CONFIGURED_RESPONSE = { ...API_KEY_RESPONSE };
export const OAUTH_RELOGIN_NEEDED_RESPONSE = { ...API_KEY_RESPONSE };

// ---------------------------------------------------------------------------
// Setup helper
// ---------------------------------------------------------------------------

export interface SettingsPageHarness<T> {
  fixture: ComponentFixture<T>;
  service: SettingsService;
  httpMock: HttpTestingController;
}

/**
 * Configure TestBed for a settings page component and invoke loadSettings().
 *
 * Usage:
 * ```typescript
 * const { fixture, service, httpMock } = setupSettingsPage(MySettingsPageComponent);
 * ```
 */
export function setupSettingsPage<T>(componentType: Type<T>): SettingsPageHarness<T> {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    imports: [componentType],
    providers: [
      SettingsService,
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: SystemSignalRService, useValue: mockSystemSignalR },
      { provide: RateBudgetService, useValue: mockRateBudgetService },
    ],
  });

  const service = TestBed.inject(SettingsService);
  service.loadSettings();
  const httpMock = TestBed.inject(HttpTestingController);
  const fixture = TestBed.createComponent(componentType);

  return { fixture, service, httpMock };
}

// ---------------------------------------------------------------------------
// HTTP flush helper
// ---------------------------------------------------------------------------

export function flushSettings(
  httpMock: HttpTestingController,
  settingsResponse: object = API_KEY_RESPONSE,
  credentialsResponse: object = CREDENTIALS_API_KEY,
): void {
  httpMock.expectOne('/api/settings').flush(settingsResponse);
  httpMock.expectOne('/api/credentials').flush(credentialsResponse);
}

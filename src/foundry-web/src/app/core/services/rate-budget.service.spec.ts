import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { vi } from 'vitest';
import { RateBudgetService, RateBudgetSnapshot, REFRESH_INTERVAL_MS } from './rate-budget.service';

const SNAPSHOT_FIXTURE: RateBudgetSnapshot = {
  budgets: [
    {
      budget: 'GitHubRest',
      displayName: 'GitHub REST',
      remaining: 1000,
      limit: 5000,
      resetAt: null,
      observedAt: '2026-08-31T12:00:00Z',
      floor: 500,
      health: 'Healthy',
    },
    {
      budget: 'GitHubGraphQl',
      displayName: 'GitHub GraphQL',
      remaining: 400,
      limit: 5000,
      resetAt: null,
      observedAt: '2026-08-31T12:00:00Z',
      floor: 500,
      health: 'Low',
    },
    {
      budget: 'GitLabRest',
      displayName: 'GitLab REST',
      remaining: 200,
      limit: 1000,
      resetAt: null,
      observedAt: '2026-08-31T12:00:00Z',
      floor: null,
      health: null,
    },
  ],
};

function setup() {
  TestBed.configureTestingModule({
    providers: [
      RateBudgetService,
      provideHttpClient(),
      provideHttpClientTesting(),
    ],
  });

  const svc = TestBed.inject(RateBudgetService);
  const http = TestBed.inject(HttpTestingController);

  return { svc, http };
}

describe('RateBudgetService', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
  });

  it('should fetch snapshot on construction and expose it via signal', () => {
    // Arrange
    const { svc, http } = setup();

    // Act
    const req = http.expectOne('/api/rate-budget');
    req.flush(SNAPSHOT_FIXTURE);

    // Assert
    const snapshot = svc.snapshot();
    expect(snapshot).not.toBeNull();
    expect(snapshot!.budgets.length).toBe(3);
    expect(snapshot!.budgets[0].budget).toBe('GitHubRest');
  });

  it('should keep prior snapshot when HTTP request fails', () => {
    // Arrange
    vi.useFakeTimers();
    try {
      const { svc, http } = setup();

      // Seed a successful initial response
      const firstReq = http.expectOne('/api/rate-budget');
      firstReq.flush(SNAPSHOT_FIXTURE);

      const snapshotAfterSuccess = svc.snapshot();
      expect(snapshotAfterSuccess).not.toBeNull();

      // Act — advance time to fire the interval, then flush with an error
      vi.advanceTimersByTime(REFRESH_INTERVAL_MS);

      const secondReq = http.expectOne('/api/rate-budget');
      secondReq.flush('Server error', { status: 500, statusText: 'Internal Server Error' });

      // Assert — prior successful snapshot is retained (catchError keeps the old value)
      expect(svc.snapshot()).toBe(snapshotAfterSuccess);
    } finally {
      vi.useRealTimers();
    }
  });

  it('should map null snapshot when initial fetch fails', () => {
    // Arrange
    const { svc, http } = setup();

    // Act — initial request fails
    const req = http.expectOne('/api/rate-budget');
    req.flush('Server error', { status: 500, statusText: 'Internal Server Error' });

    // Assert — snapshot stays null, service does not throw
    expect(svc.snapshot()).toBeNull();
  });

  it('should expose snapshot with correct health values', () => {
    // Arrange
    const { svc, http } = setup();

    // Act
    const req = http.expectOne('/api/rate-budget');
    req.flush(SNAPSHOT_FIXTURE);

    // Assert
    const snapshot = svc.snapshot();
    expect(snapshot).not.toBeNull();

    const restEntry = snapshot!.budgets.find((b) => b.budget === 'GitHubRest');
    expect(restEntry?.health).toBe('Healthy');

    const graphQlEntry = snapshot!.budgets.find((b) => b.budget === 'GitHubGraphQl');
    expect(graphQlEntry?.health).toBe('Low');

    const gitLabEntry = snapshot!.budgets.find((b) => b.budget === 'GitLabRest');
    expect(gitLabEntry?.health).toBeNull();
    expect(gitLabEntry?.floor).toBeNull();
  });
});

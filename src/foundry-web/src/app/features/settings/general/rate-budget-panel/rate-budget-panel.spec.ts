import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { RateBudgetPanelComponent } from './rate-budget-panel';
import { RateBudgetSnapshot } from '../../../../core/services/rate-budget.service';

const SNAPSHOT_ALL_READINGS: RateBudgetSnapshot = {
  budgets: [
    {
      budget: 'GitHubRest',
      displayName: 'GitHub REST',
      remaining: 1000,
      limit: 5000,
      resetAt: '2026-08-31T13:00:00Z',
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

const SNAPSHOT_NO_READINGS: RateBudgetSnapshot = {
  budgets: [
    {
      budget: 'GitHubRest',
      displayName: 'GitHub REST',
      remaining: null,
      limit: null,
      resetAt: null,
      observedAt: null,
      floor: 500,
      health: 'Unknown',
    },
    {
      budget: 'GitHubGraphQl',
      displayName: 'GitHub GraphQL',
      remaining: null,
      limit: null,
      resetAt: null,
      observedAt: null,
      floor: 500,
      health: 'Unknown',
    },
    {
      budget: 'GitLabRest',
      displayName: 'GitLab REST',
      remaining: null,
      limit: null,
      resetAt: null,
      observedAt: null,
      floor: null,
      health: null,
    },
  ],
};

function setup() {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    imports: [RateBudgetPanelComponent],
    providers: [provideHttpClient(), provideHttpClientTesting()],
  });

  const fixture = TestBed.createComponent(RateBudgetPanelComponent);
  const http = TestBed.inject(HttpTestingController);

  return { fixture, http };
}

describe('RateBudgetPanelComponent', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
  });

  it('should render one table row per budget entry', () => {
    // Arrange
    const { fixture, http } = setup();
    fixture.detectChanges();
    http.expectOne('/api/rate-budget').flush(SNAPSHOT_ALL_READINGS);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const rows = el.querySelectorAll('tbody tr');

    // Assert
    expect(rows.length).toBe(3);
  });

  it('should render the display name in each row', () => {
    // Arrange
    const { fixture, http } = setup();
    fixture.detectChanges();
    http.expectOne('/api/rate-budget').flush(SNAPSHOT_ALL_READINGS);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const rows = el.querySelectorAll('tbody tr');

    // Assert
    expect(rows[0].textContent).toContain('GitHub REST');
    expect(rows[1].textContent).toContain('GitHub GraphQL');
    expect(rows[2].textContent).toContain('GitLab REST');
  });

  it('should render Healthy health badge for GitHub REST when health is Healthy', () => {
    // Arrange
    const { fixture, http } = setup();
    fixture.detectChanges();
    http.expectOne('/api/rate-budget').flush(SNAPSHOT_ALL_READINGS);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const badge = el.querySelector('.rbd__badge--healthy');

    // Assert
    expect(badge).not.toBeNull();
    expect(badge!.textContent?.trim()).toBe('Healthy');
  });

  it('should render Low health badge for GitHub GraphQL when health is Low', () => {
    // Arrange
    const { fixture, http } = setup();
    fixture.detectChanges();
    http.expectOne('/api/rate-budget').flush(SNAPSHOT_ALL_READINGS);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const badge = el.querySelector('.rbd__badge--low');

    // Assert
    expect(badge).not.toBeNull();
    expect(badge!.textContent?.trim()).toBe('Low');
  });

  it('should render muted dash for GitLab health (visibility-only, no floor evaluation)', () => {
    // Arrange
    const { fixture, http } = setup();
    fixture.detectChanges();
    http.expectOne('/api/rate-budget').flush(SNAPSHOT_ALL_READINGS);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const rows = el.querySelectorAll('tbody tr');
    const gitLabRow = rows[2];
    const noDataSpans = gitLabRow.querySelectorAll('.rbd__no-data');

    // Assert — at least one "no data" span in the health cell (dash for health=null)
    const healthCell = gitLabRow.querySelectorAll('td')[2];
    expect(healthCell.querySelector('.rbd__no-data')).not.toBeNull();
  });

  it('should render "visibility only" for GitLab floor', () => {
    // Arrange
    const { fixture, http } = setup();
    fixture.detectChanges();
    http.expectOne('/api/rate-budget').flush(SNAPSHOT_ALL_READINGS);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const rows = el.querySelectorAll('tbody tr');
    const gitLabRow = rows[2];
    const floorCell = gitLabRow.querySelectorAll('td')[3];

    // Assert
    expect(floorCell.textContent).toContain('visibility only');
  });

  it('should render "no data yet" in the reset-at cell when reading is absent', () => {
    // Arrange
    const { fixture, http } = setup();
    fixture.detectChanges();
    http.expectOne('/api/rate-budget').flush(SNAPSHOT_NO_READINGS);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const rows = el.querySelectorAll('tbody tr');
    const firstRow = rows[0];

    // Assert — last column shows "no data yet" when observedAt is null
    const resetAtCell = firstRow.querySelectorAll('td')[4];
    expect(resetAtCell.textContent).toContain('no data yet');
  });

  it('should render Unknown badge when no reading is recorded for a GitHub key', () => {
    // Arrange
    const { fixture, http } = setup();
    fixture.detectChanges();
    http.expectOne('/api/rate-budget').flush(SNAPSHOT_NO_READINGS);
    fixture.detectChanges();

    // Act
    const el = fixture.nativeElement as HTMLElement;
    const unknownBadges = el.querySelectorAll('.rbd__badge--unknown');

    // Assert — GitHub REST and GraphQL both show Unknown
    expect(unknownBadges.length).toBe(2);
  });

  it('should render a loading message before the snapshot arrives', () => {
    // Arrange
    const { fixture, http } = setup();
    fixture.detectChanges();

    // Act — do not flush the request yet
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('.rbd__loading')).not.toBeNull();

    // Cleanup
    http.expectOne('/api/rate-budget').flush(SNAPSHOT_ALL_READINGS);
  });
});

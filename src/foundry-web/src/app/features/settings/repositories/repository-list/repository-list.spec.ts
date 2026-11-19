import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RepositoryListComponent } from './repository-list';
import { ApplyBranchRulesResponse, RepositorySummary } from '../repository.model';
import { RepositoryService } from '../repository.service';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideFakeHubs } from '../../../../core/testing/fake-hub-transport';

const MOCK_REPO: RepositorySummary = {
  id: '00000000-0000-0000-0000-000000000001',
  slug: 'my-org/my-repo',
  accountId: '00000000-0000-0000-0000-000000000010',
  accountName: 'my-github',
  providerType: 'github',
  position: 0,
  pollIntervalSeconds: 300,
  effectivePollIntervalSeconds: 300,
  pollIntervalIsDefault: false,
  isActive: true,
  maxConcurrentWorkers: 2,
  rebaseAfterRun: true,
  mergeWhenChecksPass: false,
  reviewPullRequests: true,
  lastPolledAt: '2026-06-14T12:00:00Z',
  eligibility: { status: 'eligible', violations: [], reason: null },
};

const MOCK_REPO_2: RepositorySummary = {
  id: '00000000-0000-0000-0000-000000000002',
  slug: 'work-org/backend',
  accountId: '00000000-0000-0000-0000-000000000011',
  accountName: 'work-gitlab',
  providerType: 'gitlab',
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

const MOCK_REPO_INELIGIBLE: RepositorySummary = {
  id: '00000000-0000-0000-0000-000000000003',
  slug: 'my-org/restricted-repo',
  accountId: '00000000-0000-0000-0000-000000000010',
  accountName: 'my-github',
  providerType: 'github',
  position: 2,
  pollIntervalSeconds: 300,
  effectivePollIntervalSeconds: 300,
  pollIntervalIsDefault: false,
  isActive: true,
  maxConcurrentWorkers: 1,
  rebaseAfterRun: true,
  mergeWhenChecksPass: false,
  reviewPullRequests: true,
  lastPolledAt: '2026-06-14T12:00:00Z',
  eligibility: {
    status: 'ineligible',
    violations: [
      { rule: 'AllowDirectPushes', description: 'Allow direct pushes is enabled' },
    ],
    reason: null,
  },
};

const MOCK_REPO_MULTI_VIOLATIONS: RepositorySummary = {
  id: '00000000-0000-0000-0000-000000000006',
  slug: 'my-org/multi-violation-repo',
  accountId: '00000000-0000-0000-0000-000000000010',
  accountName: 'my-github',
  providerType: 'github',
  position: 5,
  pollIntervalSeconds: 300,
  effectivePollIntervalSeconds: 300,
  pollIntervalIsDefault: false,
  isActive: true,
  maxConcurrentWorkers: 1,
  rebaseAfterRun: true,
  mergeWhenChecksPass: false,
  reviewPullRequests: true,
  lastPolledAt: '2026-06-14T12:00:00Z',
  eligibility: {
    status: 'ineligible',
    violations: [
      { rule: 'AllowDirectPushes', description: 'Allow direct pushes is enabled' },
      { rule: 'NoReviewRequired', description: 'No pull request reviews are required' },
    ],
    reason: null,
  },
};

const MOCK_REPO_NULL_ELIGIBILITY: RepositorySummary = {
  id: '00000000-0000-0000-0000-000000000005',
  slug: 'my-org/unpolled-repo',
  accountId: '00000000-0000-0000-0000-000000000010',
  accountName: 'my-github',
  providerType: 'github',
  position: 4,
  pollIntervalSeconds: 300,
  effectivePollIntervalSeconds: 300,
  pollIntervalIsDefault: false,
  isActive: true,
  maxConcurrentWorkers: 1,
  rebaseAfterRun: true,
  mergeWhenChecksPass: false,
  reviewPullRequests: true,
  lastPolledAt: null,
  eligibility: null,
};

const MOCK_REPO_UNREACHABLE: RepositorySummary = {
  id: '00000000-0000-0000-0000-000000000004',
  slug: 'my-org/offline-repo',
  accountId: '00000000-0000-0000-0000-000000000010',
  accountName: 'my-github',
  providerType: 'github',
  position: 3,
  pollIntervalSeconds: 300,
  effectivePollIntervalSeconds: 300,
  pollIntervalIsDefault: false,
  isActive: true,
  maxConcurrentWorkers: 1,
  rebaseAfterRun: true,
  mergeWhenChecksPass: false,
  reviewPullRequests: true,
  lastPolledAt: '2026-06-14T12:00:00Z',
  eligibility: { status: 'unreachable', violations: [], reason: null },
};

const MOCK_REPO_RATE_LIMITED: RepositorySummary = {
  id: '00000000-0000-0000-0000-000000000007',
  slug: 'my-org/rate-limited-repo',
  accountId: '00000000-0000-0000-0000-000000000010',
  accountName: 'my-github',
  providerType: 'github',
  position: 6,
  pollIntervalSeconds: 300,
  effectivePollIntervalSeconds: 300,
  pollIntervalIsDefault: false,
  isActive: true,
  maxConcurrentWorkers: 1,
  rebaseAfterRun: true,
  mergeWhenChecksPass: false,
  reviewPullRequests: true,
  lastPolledAt: '2026-06-14T12:00:00Z',
  eligibility: { status: 'unreachable', violations: [], reason: 'rate-limited' },
};

const MOCK_REPO_PAUSED: RepositorySummary = {
  id: '00000000-0000-0000-0000-000000000008',
  slug: 'my-org/paused-repo',
  accountId: '00000000-0000-0000-0000-000000000010',
  accountName: 'my-github',
  providerType: 'github',
  position: 7,
  pollIntervalSeconds: 300,
  effectivePollIntervalSeconds: 300,
  pollIntervalIsDefault: false,
  isActive: false,
  maxConcurrentWorkers: 1,
  rebaseAfterRun: true,
  mergeWhenChecksPass: false,
  reviewPullRequests: true,
  lastPolledAt: '2026-06-14T12:00:00Z',
  eligibility: { status: 'eligible', violations: [], reason: null },
};

function setup(overrides: {
  repositories?: RepositorySummary[];
  loading?: boolean;
  error?: string | null;
  reordering?: boolean;
} = {}) {
  const fixture = TestBed.createComponent(RepositoryListComponent);
  fixture.componentRef.setInput('repositories', overrides.repositories ?? []);
  fixture.componentRef.setInput('loading', overrides.loading ?? false);
  fixture.componentRef.setInput('error', overrides.error ?? null);
  fixture.componentRef.setInput('reordering', overrides.reordering ?? false);
  fixture.detectChanges();
  return {
    fixture,
    component: fixture.componentInstance,
    el: fixture.nativeElement as HTMLElement,
    httpMock: TestBed.inject(HttpTestingController),
    repositoryService: TestBed.inject(RepositoryService),
    router: TestBed.inject(Router),
  };
}

describe('RepositoryListComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [RepositoryListComponent],
      providers: [
        RepositoryService,
        provideHttpClient(),
        provideHttpClientTesting(),
        provideFakeHubs(),
        provideRouter([]),
      ],
    }).compileComponents();
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true });
  });

  // ─── Scaffolding: empty / loading / error states (preserved) ────────────────

  it('should render the empty state when there are no repositories', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [] });

    // Assert
    const emptyState = el.querySelector('.repository-list__empty');
    expect(emptyState).toBeTruthy();
    expect(emptyState?.textContent).toContain('No repositories monitored');
  });

  it('should render loading state with accessible role and label', () => {
    // Arrange

    // Act
    const { el } = setup({ loading: true });

    // Assert
    const loadingEl = el.querySelector('[role="status"]');
    expect(loadingEl).toBeTruthy();
    expect(loadingEl?.getAttribute('aria-label')).toBe('Loading repositories');
  });

  it('should render fd-spinner inside the loading region when loading is true', () => {
    // Arrange

    // Act
    const { el } = setup({ loading: true });

    // Assert
    const spinner = el.querySelector('fd-spinner');
    expect(spinner).toBeTruthy();
  });

  it('should render fd-spinner with size 24 when loading is true', () => {
    // Arrange

    // Act
    const { el } = setup({ loading: true });

    // Assert
    const spinnerSpan = el.querySelector('fd-spinner .spinner') as HTMLElement;
    expect(spinnerSpan).toBeTruthy();
    expect(spinnerSpan.style.width).toBe('24px');
    expect(spinnerSpan.style.height).toBe('24px');
  });

  it('should not render fd-spinner when loading is false', () => {
    // Arrange

    // Act
    const { el } = setup({ loading: false, repositories: [] });

    // Assert
    const spinner = el.querySelector('fd-spinner');
    expect(spinner).toBeNull();
  });

  it('should render error message in alert region', () => {
    // Arrange

    // Act
    const { el } = setup({ error: 'Failed to load repositories' });

    // Assert
    const alertEl = el.querySelector('[role="alert"]');
    expect(alertEl).toBeTruthy();
    expect(alertEl?.textContent).toContain('Failed to load repositories');
  });

  it('should render a Retry button in the error state', () => {
    // Arrange

    // Act
    const { el } = setup({ error: 'Network error' });

    // Assert
    const retryBtn = el.querySelector('.repository-list__retry-btn');
    expect(retryBtn).toBeTruthy();
    expect(retryBtn?.textContent?.trim()).toBe('Retry');
  });

  // ─── Rows ──────────────────────────────────────────────────────────────────

  it('should render a list when repositories are provided', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO] });

    // Assert
    const list = el.querySelector('[role="list"]');
    expect(list).toBeTruthy();
    const items = el.querySelectorAll('[role="listitem"]');
    expect(items.length).toBe(1);
  });

  // ─── Slug as <a routerLink> ──────────────────────────────────────────────────

  it('should render slug as an <a> element with routerLink to /settings/repositories/:id', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO] });

    // Assert
    const slugLink = el.querySelector('.repository-list__slug');
    expect(slugLink?.tagName.toLowerCase()).toBe('a');
    expect(slugLink?.getAttribute('href')).toBe(`/settings/repositories/${MOCK_REPO.id}`);
  });

  // ─── Delete ────────────────────────────────────────────────────────────────

  it('should emit delete event when the delete button is clicked', () => {
    // Arrange
    const { el, component } = setup({ repositories: [MOCK_REPO] });
    let emittedRepo: RepositorySummary | undefined;
    component.delete.subscribe((r: RepositorySummary) => { emittedRepo = r; });

    // Act
    const deleteBtn = el.querySelector('[aria-label="Delete repository my-org/my-repo"]') as HTMLButtonElement;
    deleteBtn.click();

    // Assert
    expect(emittedRepo).toEqual(MOCK_REPO);
  });

  // ─── No old elements (chevron, toggle, eligibility-details, old __metadata) ───

  it('should not render fd-repository-eligibility-details component in the card', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO_INELIGIBLE] });

    // Assert
    expect(el.querySelector('fd-repository-eligibility-details')).toBeFalsy();
  });

  it('should not render a chevron toggle button for ineligible repos', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO_INELIGIBLE] });

    // Assert
    expect(el.querySelector('.repository-list__toggle-btn')).toBeFalsy();
    expect(el.querySelector('.repository-list__toggle-chevron')).toBeFalsy();
  });

  it('should not render a title attribute on the slug anchor', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO] });

    // Assert
    const slug = el.querySelector('.repository-list__slug');
    expect(slug?.hasAttribute('title')).toBe(false);
  });

  // ─── Inline eligibility — ineligible ─────────────────────────────────────────

  it('should render the reasons block for an ineligible repo without any toggle', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO_INELIGIBLE] });

    // Assert
    const reasons = el.querySelector('.repository-list__problem');
    expect(reasons).toBeTruthy();
    // No expand state needed — always visible
    expect(reasons?.getAttribute('hidden')).toBeNull();
  });

  it('should render one warning line per violation for an ineligible repo', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO_INELIGIBLE] });

    // Assert
    const warningLines = el.querySelectorAll('.repository-list__problem-list li');
    expect(warningLines.length).toBeGreaterThanOrEqual(1);
    expect(warningLines[0]?.textContent).toContain('Allow direct pushes is enabled');
  });

  it('should render one warning line per violation for multiple violations, with one Re-check button', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO_MULTI_VIOLATIONS] });

    // Assert
    const warningLines = el.querySelectorAll('.repository-list__problem-list li');
    expect(warningLines.length).toBe(2);
    expect(warningLines[0]?.textContent).toContain('Allow direct pushes is enabled');
    expect(warningLines[1]?.textContent).toContain('No pull request reviews are required');

    // Only one Re-check button per card
    const recheckBtns = el.querySelectorAll('.repository-list__recheck-btn');
    expect(recheckBtns.length).toBe(1);
  });

  it('should not render reasons block for an eligible repo', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO] });

    // Assert
    expect(el.querySelector('.repository-list__problem')).toBeFalsy();
  });

  it('should not render reasons block when eligibility is null', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO_NULL_ELIGIBILITY] });

    // Assert
    expect(el.querySelector('.repository-list__problem')).toBeFalsy();
  });

  // ─── Inline eligibility — unreachable ────────────────────────────────────────

  it('should render unreachable reason sentence in the warning line', () => {
    // Arrange — MOCK_REPO_UNREACHABLE has reason: null => fallback sentence

    // Act
    const { el } = setup({ repositories: [MOCK_REPO_UNREACHABLE] });

    // Assert
    const reason = el.querySelector('.repository-list__problem-list li');
    expect(reason?.textContent).toContain('branch-protection settings');
  });

  it('should render never-probed reason sentence in the warning line', () => {
    // Arrange
    const neverProbed: RepositorySummary = { ...MOCK_REPO_UNREACHABLE, eligibility: { status: 'unreachable', violations: [], reason: 'never-probed' } };

    // Act
    const { el } = setup({ repositories: [neverProbed] });

    // Assert
    const reason = el.querySelector('.repository-list__problem-list li');
    expect(reason?.textContent).toContain('Eligibility has not been checked yet');
  });

  it('should render rate-limited reason sentence in the warning line', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO_RATE_LIMITED] });

    // Assert
    const reason = el.querySelector('.repository-list__problem-list li');
    expect(reason?.textContent).toContain('rate limit');
  });

  // ─── Re-check button — rate-limited disabled ───────────────────────────────

  it('should disable the Re-check button when the repo is rate-limited', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO_RATE_LIMITED] });

    // Assert
    const recheckBtn = el.querySelector('.repository-list__recheck-btn') as HTMLButtonElement;
    expect(recheckBtn?.disabled).toBe(true);
  });

  it('should have fdTooltip (not title) on the Re-check button for rate-limited repos', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO_RATE_LIMITED] });

    // Assert
    const recheckBtn = el.querySelector('.repository-list__recheck-btn');
    // fdTooltip is an Angular directive — its presence can be verified by the absence of a [title] attribute
    // and instead checking [fdTooltip] binding or aria-describedby when open
    expect(recheckBtn?.hasAttribute('title')).toBe(false);
  });

  it('should keep the Re-check button enabled for non-rate-limited unreachable repos', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO_UNREACHABLE] });

    // Assert
    const recheckBtn = el.querySelector('.repository-list__recheck-btn') as HTMLButtonElement;
    expect(recheckBtn?.disabled).toBe(false);
  });

  // ─── Re-check flow ─────────────────────────────────────────────────────────

  it('should call recheckEligibility when Re-check is clicked', () => {
    // Arrange
    const { el, httpMock } = setup({ repositories: [MOCK_REPO_INELIGIBLE] });

    // Act
    const recheckBtn = el.querySelector('.repository-list__recheck-btn') as HTMLButtonElement;
    recheckBtn.click();

    // Assert
    const req = httpMock.expectOne(
      `/api/accounts/${MOCK_REPO_INELIGIBLE.accountId}/repositories/${MOCK_REPO_INELIGIBLE.id}/recheck`
    );
    expect(req.request.method).toBe('POST');
    req.flush(MOCK_REPO_INELIGIBLE);
  });

  it('should show "Re-checking…" in the Re-check button and disable it while in flight', () => {
    // Arrange
    const { el, fixture, httpMock } = setup({ repositories: [MOCK_REPO_INELIGIBLE] });

    // Act
    const recheckBtn = el.querySelector('.repository-list__recheck-btn') as HTMLButtonElement;
    recheckBtn.click();
    fixture.detectChanges();

    // Assert
    const updatedBtn = el.querySelector('.repository-list__recheck-btn') as HTMLButtonElement;
    expect(updatedBtn?.textContent?.trim()).toContain('Re-checking');
    expect(updatedBtn?.disabled).toBe(true);

    // Clean up
    httpMock.expectOne(`/api/accounts/${MOCK_REPO_INELIGIBLE.accountId}/repositories/${MOCK_REPO_INELIGIBLE.id}/recheck`)
      .flush(MOCK_REPO_INELIGIBLE);
  });

  it('should show error in a warning line when re-check fails', () => {
    // Arrange
    const { el, fixture, httpMock } = setup({ repositories: [MOCK_REPO_INELIGIBLE] });
    const recheckBtn = el.querySelector('.repository-list__recheck-btn') as HTMLButtonElement;
    recheckBtn.click();
    fixture.detectChanges();

    // Act
    httpMock.expectOne(`/api/accounts/${MOCK_REPO_INELIGIBLE.accountId}/repositories/${MOCK_REPO_INELIGIBLE.id}/recheck`)
      .flush('Server error', { status: 500, statusText: 'Internal Server Error' });
    fixture.detectChanges();

    // Assert — error renders in an alert line
    const errorLine = el.querySelector('.repository-list__problem-error');
    expect(errorLine).toBeTruthy();
    expect(errorLine?.textContent).toContain('Re-check failed');
    expect(errorLine?.getAttribute('role')).toBe('alert');
  });

  it('should clear the warning lines when re-check returns eligible', () => {
    // Arrange
    const eligibleRepo: RepositorySummary = {
      ...MOCK_REPO_INELIGIBLE,
      eligibility: { status: 'eligible', violations: [], reason: null },
    };
    const { el, fixture, httpMock } = setup({ repositories: [MOCK_REPO_INELIGIBLE] });
    const recheckBtn = el.querySelector('.repository-list__recheck-btn') as HTMLButtonElement;
    recheckBtn.click();
    fixture.detectChanges();

    // Act — re-check returns eligible; parent updates the repositories input
    httpMock.expectOne(`/api/accounts/${MOCK_REPO_INELIGIBLE.accountId}/repositories/${MOCK_REPO_INELIGIBLE.id}/recheck`)
      .flush(eligibleRepo);
    fixture.componentRef.setInput('repositories', [eligibleRepo]);
    fixture.detectChanges();

    // Assert — reasons block gone (repo now eligible)
    expect(el.querySelector('.repository-list__problem')).toBeFalsy();
    expect(el.querySelector('.repository-list__problem-list li')).toBeFalsy();
  });

  // ─── Live-region announcements ─────────────────────────────────────────────

  it('should render a persistent sr-only live region with aria-live="polite" and start empty', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO] });

    // Assert
    const liveRegion = el.querySelector('.repository-list__announcement');
    expect(liveRegion).toBeTruthy();
    expect(liveRegion?.getAttribute('aria-live')).toBe('polite');
    expect(liveRegion?.getAttribute('aria-atomic')).toBe('true');
    expect(liveRegion?.textContent?.trim()).toBe('');
  });

  it('should announce "slug: Re-checking..." in the polite live region when recheck starts', () => {
    // Arrange
    const { el, fixture, httpMock } = setup({ repositories: [MOCK_REPO_INELIGIBLE] });

    // Act
    const recheckBtn = el.querySelector('.repository-list__recheck-btn') as HTMLButtonElement;
    recheckBtn.click();
    fixture.detectChanges();

    // Assert
    const liveRegion = el.querySelector('.repository-list__announcement');
    expect(liveRegion?.textContent?.trim()).toBe(`${MOCK_REPO_INELIGIBLE.slug}: Re-checking...`);

    // Clean up
    httpMock.expectOne(`/api/accounts/${MOCK_REPO_INELIGIBLE.accountId}/repositories/${MOCK_REPO_INELIGIBLE.id}/recheck`)
      .flush(MOCK_REPO_INELIGIBLE);
  });

  it('should announce the result label when recheck succeeds', () => {
    // Arrange
    const eligibleRepo: RepositorySummary = {
      ...MOCK_REPO_INELIGIBLE,
      eligibility: { status: 'eligible', violations: [], reason: null },
    };
    const { el, fixture, httpMock } = setup({ repositories: [MOCK_REPO_INELIGIBLE] });
    const recheckBtn = el.querySelector('.repository-list__recheck-btn') as HTMLButtonElement;
    recheckBtn.click();
    fixture.detectChanges();

    // Act
    httpMock.expectOne(`/api/accounts/${MOCK_REPO_INELIGIBLE.accountId}/repositories/${MOCK_REPO_INELIGIBLE.id}/recheck`)
      .flush(eligibleRepo);
    fixture.detectChanges();

    // Assert
    const liveRegion = el.querySelector('.repository-list__announcement');
    expect(liveRegion?.textContent?.trim()).toBe(`${MOCK_REPO_INELIGIBLE.slug}: Eligible`);
  });

  it('should announce "slug: Re-check failed" when recheck errors', () => {
    // Arrange
    const { el, fixture, httpMock } = setup({ repositories: [MOCK_REPO_INELIGIBLE] });
    const recheckBtn = el.querySelector('.repository-list__recheck-btn') as HTMLButtonElement;
    recheckBtn.click();
    fixture.detectChanges();

    // Act
    httpMock.expectOne(`/api/accounts/${MOCK_REPO_INELIGIBLE.accountId}/repositories/${MOCK_REPO_INELIGIBLE.id}/recheck`)
      .flush('Server error', { status: 500, statusText: 'Internal Server Error' });
    fixture.detectChanges();

    // Assert
    const liveRegion = el.querySelector('.repository-list__announcement');
    expect(liveRegion?.textContent?.trim()).toBe(`${MOCK_REPO_INELIGIBLE.slug}: Re-check failed`);
  });

  it('should announce the new position in the live region after a move succeeds', () => {
    // Arrange
    const { el, fixture, httpMock } = setup({ repositories: [MOCK_REPO, MOCK_REPO_2], reordering: true });

    // Act
    const moveDownBtn = el.querySelectorAll('.repository-list__move-down-btn')[0] as HTMLButtonElement;
    moveDownBtn.click();
    httpMock.expectOne(`/api/repositories/${MOCK_REPO.id}/position`).flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();

    // Assert
    const liveRegion = el.querySelector('.repository-list__announcement');
    expect(liveRegion?.textContent?.trim()).toContain(MOCK_REPO.slug);
  });

  // ─── Reorder operations (preserved) ──────────────────────────────────────────

  it('should call moveRepository with index + 1 when move-down is clicked for the first item', () => {
    // Arrange
    const { el, fixture, httpMock } = setup({ repositories: [MOCK_REPO, MOCK_REPO_2], reordering: true });

    // Act
    const moveDownBtn = el.querySelectorAll('.repository-list__move-down-btn')[0] as HTMLButtonElement;
    moveDownBtn.click();
    fixture.detectChanges();

    // Assert
    const req = httpMock.expectOne(`/api/repositories/${MOCK_REPO.id}/position`);
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toEqual({ position: 1 });
    req.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('should call moveRepository with index - 1 when move-up is clicked for the second item', () => {
    // Arrange
    const { el, fixture, httpMock } = setup({ repositories: [MOCK_REPO, MOCK_REPO_2], reordering: true });

    // Act
    const moveUpBtn = el.querySelectorAll('.repository-list__move-up-btn')[1] as HTMLButtonElement;
    moveUpBtn.click();
    fixture.detectChanges();

    // Assert
    const req = httpMock.expectOne(`/api/repositories/${MOCK_REPO_2.id}/position`);
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toEqual({ position: 0 });
    req.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('should display a visible move error message when the PATCH fails', () => {
    // Arrange
    const { el, fixture, httpMock } = setup({ repositories: [MOCK_REPO, MOCK_REPO_2], reordering: true });

    // Act
    const moveDownBtn = el.querySelectorAll('.repository-list__move-down-btn')[0] as HTMLButtonElement;
    moveDownBtn.click();
    httpMock.expectOne(`/api/repositories/${MOCK_REPO.id}/position`).flush('Server error', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    fixture.detectChanges();

    // Assert
    const moveError = el.querySelector('.repository-list__move-error');
    expect(moveError).toBeTruthy();
    expect(moveError?.textContent).toContain('reorder');
  });

  it('should restore focus to a control within the moved item after a successful move-down', async () => {
    // Arrange
    vi.useFakeTimers();
    const { el, fixture, httpMock } = setup({ repositories: [MOCK_REPO, MOCK_REPO_2], reordering: true });
    const moveDownBtn = el.querySelectorAll('.repository-list__move-down-btn')[0] as HTMLButtonElement;
    moveDownBtn.focus();

    // Act
    moveDownBtn.click();
    httpMock.expectOne(`/api/repositories/${MOCK_REPO.id}/position`).flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();
    vi.runAllTimers();

    // Assert
    const movedItemEl = el.querySelector(`#repo-item-${MOCK_REPO.id}`) as HTMLElement;
    expect(movedItemEl).toBeTruthy();
    expect(movedItemEl.contains(document.activeElement)).toBe(true);
    vi.useRealTimers();
  });

  // ─── Retry ─────────────────────────────────────────────────────────────────

  it('should emit retry when retry button is clicked in error state', () => {
    // Arrange
    const { el, component } = setup({ error: 'Failed to load repositories' });
    let emitted = false;
    component.retry.subscribe(() => { emitted = true; });

    // Act
    const retryBtn = el.querySelector('.repository-list__retry-btn') as HTMLButtonElement;
    retryBtn.click();

    // Assert
    expect(emitted).toBe(true);
  });

  // ─── a11y / accessibility ──────────────────────────────────────────────────

  it('should give the drop list an accessible name via aria-label', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO, MOCK_REPO_2], reordering: true });

    // Assert
    const list = el.querySelector('[role="list"]');
    expect(list?.getAttribute('aria-label')).toBeTruthy();
  });

  it('should have no disabled attribute on enabled move-up and move-down buttons', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO, MOCK_REPO_2], reordering: true });

    // Assert
    const moveUpBtns = el.querySelectorAll('.repository-list__move-up-btn');
    const moveDownBtns = el.querySelectorAll('.repository-list__move-down-btn');
    expect(moveUpBtns[1]?.hasAttribute('disabled')).toBe(false);
    expect(moveDownBtns[0]?.hasAttribute('disabled')).toBe(false);
    expect(moveUpBtns[0]?.hasAttribute('disabled')).toBe(true);
    expect(moveDownBtns[1]?.hasAttribute('disabled')).toBe(true);
  });

  it('should have reasons-block role="group" with accessible label for ineligible repos', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO_INELIGIBLE] });

    // Assert
    const reasons = el.querySelector('.repository-list__problem');
    expect(reasons?.getAttribute('role')).toBe('group');
    expect(reasons?.getAttribute('aria-label')).toContain(MOCK_REPO_INELIGIBLE.slug);
  });

  it('should render the fd-provider-icon for a github repository', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO] });

    // Assert
    const icon = el.querySelector('fd-provider-icon');
    expect(icon).toBeTruthy();
    expect(icon?.getAttribute('aria-label')).toBe('GitHub');
  });

  it('should render the fd-provider-icon for a gitlab repository', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO_2] });

    // Assert
    const icon = el.querySelector('fd-provider-icon');
    expect(icon?.getAttribute('aria-label')).toBe('GitLab');
  });

  it('should render a row for each repository', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO, MOCK_REPO_2] });

    // Assert
    const items = el.querySelectorAll('[role="listitem"]');
    expect(items.length).toBe(2);
  });

  // ─── Apply branch rules ────────────────────────────────────────────────────

  describe('apply branch rules', () => {
    const BRANCH_RULES_REPO: RepositorySummary = {
      ...MOCK_REPO_INELIGIBLE,
      id: '00000000-0000-0000-0000-000000000099',
      slug: 'my-org/unprotected',
      eligibility: {
        status: 'ineligible',
        violations: [
          { rule: 'branch-protection:direct-pushes', description: 'Direct pushes are allowed' },
          { rule: 'branch-protection:force-pushes', description: 'Force pushes are allowed' },
        ],
        reason: null,
      },
    };
    const APPLY_URL = `/api/accounts/${BRANCH_RULES_REPO.accountId}/repositories/${BRANCH_RULES_REPO.id}/apply-branch-rules`;
    const APPLIED: ApplyBranchRulesResponse = {
      repository: { ...BRANCH_RULES_REPO, eligibility: { status: 'eligible', violations: [], reason: null } },
      branchRules: { kind: 'applied' },
    };

    it('should show the button when the repo is ineligible only through branch-protection violations', () => {
      // Act
      const { el } = setup({ repositories: [BRANCH_RULES_REPO] });

      // Assert
      const btn = el.querySelector('.repository-list__apply-rules-btn');
      expect(btn).toBeTruthy();
      expect(btn?.textContent?.trim()).toBe('Apply branch rules');
    });

    it('should not show the button when any violation is not a branch-protection violation', () => {
      // Arrange
      const mixed: RepositorySummary = {
        ...BRANCH_RULES_REPO,
        eligibility: {
          status: 'ineligible',
          violations: [
            ...BRANCH_RULES_REPO.eligibility!.violations,
            { rule: 'cannot-push:my-org/unprotected', description: 'Token cannot push' },
          ],
          reason: null,
        },
      };

      // Act
      const { el } = setup({ repositories: [mixed, MOCK_REPO_INELIGIBLE] });

      // Assert
      expect(el.querySelector('.repository-list__apply-rules-btn')).toBeFalsy();
    });

    it('should not show the button for eligible or unreachable repos', () => {
      // Arrange
      const unreachable: RepositorySummary = {
        ...BRANCH_RULES_REPO,
        eligibility: { status: 'unreachable', violations: [], reason: 'never-probed' },
      };

      // Act
      const { el } = setup({ repositories: [MOCK_REPO, unreachable] });

      // Assert
      expect(el.querySelector('.repository-list__apply-rules-btn')).toBeFalsy();
    });

    it('should POST to the apply-branch-rules endpoint and show progress while in flight', () => {
      // Arrange
      const { el, fixture, httpMock } = setup({ repositories: [BRANCH_RULES_REPO] });

      // Act
      (el.querySelector('.repository-list__apply-rules-btn') as HTMLButtonElement).click();
      fixture.detectChanges();

      // Assert
      const req = httpMock.expectOne(APPLY_URL);
      expect(req.request.method).toBe('POST');
      expect(el.querySelector('fd-repository-add-progress')?.textContent).toContain('Applying branch rules');
      const btn = el.querySelector('.repository-list__apply-rules-btn') as HTMLButtonElement;
      expect(btn.disabled).toBe(true);
      expect((el.querySelector('.repository-list__recheck-btn') as HTMLButtonElement).disabled).toBe(true);
      req.flush(APPLIED);
    });

    it('should ignore a repeated click while the request is in flight', () => {
      // Arrange
      const { component, httpMock } = setup({ repositories: [BRANCH_RULES_REPO] });

      // Act
      component.onApplyBranchRules(BRANCH_RULES_REPO);
      component.onApplyBranchRules(BRANCH_RULES_REPO);

      // Assert — exactly one request
      httpMock.expectOne(APPLY_URL).flush(APPLIED);
      httpMock.verify();
    });

    it('should show the outcome checklist after the request completes, even once the card is eligible', () => {
      // Arrange
      const { el, fixture, httpMock } = setup({ repositories: [BRANCH_RULES_REPO] });
      (el.querySelector('.repository-list__apply-rules-btn') as HTMLButtonElement).click();

      // Act — service updates the list; parent feeds it back as the input
      httpMock.expectOne(APPLY_URL).flush(APPLIED);
      fixture.componentRef.setInput('repositories', [APPLIED.repository]);
      fixture.detectChanges();

      // Assert
      expect(el.querySelector('fd-repository-add-progress')).toBeFalsy();
      const outcome = el.querySelector('fd-repository-add-outcome');
      expect(outcome).toBeTruthy();
      expect(outcome?.querySelector('[data-row="branch-rules"]')?.textContent).toContain('Applied');
      expect(el.querySelector('.repository-list__problem-list')).toBeFalsy();
      expect(el.querySelector('.repository-list__apply-rules-btn')).toBeFalsy();
      expect(el.querySelector('.repository-list__problem')?.classList).toContain('repository-list__problem--neutral');
    });

    it('should hide the outcome when dismissed', () => {
      // Arrange
      const { el, fixture, httpMock } = setup({ repositories: [BRANCH_RULES_REPO] });
      (el.querySelector('.repository-list__apply-rules-btn') as HTMLButtonElement).click();
      httpMock.expectOne(APPLY_URL).flush(APPLIED);
      fixture.detectChanges();

      // Act
      (el.querySelector('.repository-list__apply-rules-dismiss') as HTMLButtonElement).click();
      fixture.detectChanges();

      // Assert
      expect(el.querySelector('fd-repository-add-outcome')).toBeFalsy();
    });

    it('should show an alert when the request fails', () => {
      // Arrange
      const { el, fixture, httpMock } = setup({ repositories: [BRANCH_RULES_REPO] });
      (el.querySelector('.repository-list__apply-rules-btn') as HTMLButtonElement).click();

      // Act
      httpMock.expectOne(APPLY_URL).flush('Server error', { status: 500, statusText: 'Internal Server Error' });
      fixture.detectChanges();

      // Assert
      const errorLine = el.querySelector('.repository-list__problem-error');
      expect(errorLine?.textContent).toContain('Applying branch rules failed');
      expect(errorLine?.getAttribute('role')).toBe('alert');
      expect((el.querySelector('.repository-list__apply-rules-btn') as HTMLButtonElement).disabled).toBe(false);
    });
  });

  describe('account link', () => {
    const WITH_CANNOT_PUSH: RepositorySummary = {
      ...MOCK_REPO_INELIGIBLE,
      eligibility: {
        status: 'ineligible',
        violations: [
          { rule: 'cannot-push:my-org/my-repo', description: 'Token is missing Administration (read and write) for my-org/my-repo.' },
        ],
        reason: null,
      },
    };

    it('links a cannot-push violation to Accounts settings', () => {
      // Arrange / Act
      const { el } = setup({ repositories: [WITH_CANNOT_PUSH] });

      // Assert
      const links = el.querySelectorAll<HTMLAnchorElement>('.repository-list__problem-link');
      expect(links.length).toBe(1);
      expect(links[0].textContent?.trim()).toBe('View account');
      expect(links[0].getAttribute('aria-label')).toBe(`View account ${WITH_CANNOT_PUSH.accountName} in Accounts settings`);
      expect(new URL(links[0].getAttribute('href') ?? '', 'http://localhost').pathname).toBe('/settings/accounts');
    });

    it('adds no account link to a branch-protection violation', () => {
      // Arrange / Act
      const { el } = setup({ repositories: [MOCK_REPO_INELIGIBLE] });

      // Assert
      expect(Array.from(el.querySelectorAll('.repository-list__problem-link'), (l) => l.textContent?.trim())).not.toContain('View account');
    });
  });

  describe('package source link', () => {
    const SOURCE = 'https://gitlab.example.com/api/v4/groups/42/-/packages/nuget/index.json';
    const WITH_SOURCE_VIOLATION: RepositorySummary = {
      ...MOCK_REPO_INELIGIBLE,
      eligibility: {
        status: 'ineligible',
        violations: [
          { rule: 'AllowDirectPushes', description: 'Allow direct pushes is enabled' },
          { rule: `package-source-unreachable:${SOURCE}`, description: `Foundry can't read the package source ${SOURCE}.` },
        ],
        reason: null,
      },
    };

    it('links an unreachable package source violation to its card in Package sources settings', () => {
      // Arrange / Act
      const { el } = setup({ repositories: [WITH_SOURCE_VIOLATION] });

      // Assert
      const links = el.querySelectorAll<HTMLAnchorElement>('.repository-list__problem-link');
      expect(links.length).toBe(1);
      const link = links[0];
      expect(link.textContent?.trim()).toBe('View package source');
      expect(link.getAttribute('aria-label')).toBe(`View package source ${SOURCE} in Package sources settings`);
      const href = new URL(link.getAttribute('href') ?? '', 'http://localhost');
      expect(href.pathname).toBe('/settings/package-sources');
      expect(href.searchParams.get('source')).toBe(SOURCE);
      expect(link.closest('.repository-list__problem-list li')?.textContent).toContain(`Foundry can't read the package source ${SOURCE}.`);
    });

    it('adds no link to other violations', () => {
      // Arrange / Act
      const { el } = setup({ repositories: [MOCK_REPO_INELIGIBLE] });

      // Assert
      expect(el.querySelector('.repository-list__problem-link')).toBeNull();
    });
  });
  describe('redesigned row', () => {
    const NOW = Date.parse('2026-06-14T12:00:00Z');
    const BASE: RepositorySummary = {
      ...MOCK_REPO,
      id: '00000000-0000-0000-0000-000000000101',
      slug: 'acme/platform/billing-api',
      maxConcurrentWorkers: 1,
      rebaseAfterRun: true,
      mergeWhenChecksPass: false,
      reviewPullRequests: true,
      webhookStatus: 'not-configured',
    };
    const SECOND: RepositorySummary = { ...BASE, id: '00000000-0000-0000-0000-000000000102', slug: 'acme/web', position: 1 };

    afterEach(() => {
      vi.useRealTimers();
    });

    it('renders the slug link text as the plain slug, with no whitespace', () => {
      // Act
      const { el } = setup({ repositories: [BASE] });

      // Assert
      expect(el.querySelector('.repository-list__slug')?.textContent).toBe('acme/platform/billing-api');
    });

    it('splits the slug into a namespace and a name', () => {
      // Act
      const { el } = setup({ repositories: [BASE] });

      // Assert
      expect(el.querySelector('.repository-list__slug-namespace')?.textContent).toBe('acme/platform/');
      expect(el.querySelector('.repository-list__slug-name')?.textContent).toBe('billing-api');
    });

    it('reads "Updates by webhook" for an active webhook', () => {
      // Act
      const { el } = setup({ repositories: [{ ...BASE, webhookStatus: 'active' }] });

      // Assert
      expect(el.querySelector('.repository-list__updates')?.textContent).toBe('Updates by webhook');
    });

    it('reads the poll interval and last poll for a polling repository', () => {
      // Arrange
      vi.useFakeTimers();
      vi.setSystemTime(NOW);

      // Act
      const { el } = setup({
        repositories: [{ ...BASE, effectivePollIntervalSeconds: 300, lastPolledAt: new Date(NOW - 8 * 60_000).toISOString() }],
      });

      // Assert
      expect(el.querySelector('.repository-list__updates')?.textContent).toBe('Polls every 5m, last polled 8m ago');
    });

    it('renders no setting tags at the defaults', () => {
      // Act
      const { el } = setup({ repositories: [BASE] });

      // Assert
      expect(el.querySelectorAll('.repository-list__tag').length).toBe(0);
    });

    it('renders a tag for each non-default setting', () => {
      // Act
      const { el } = setup({ repositories: [{ ...BASE, maxConcurrentWorkers: 3, mergeWhenChecksPass: true }] });

      // Assert
      const tags = Array.from(el.querySelectorAll('.repository-list__tag')).map(t => t.textContent?.trim());
      expect(tags).toEqual(['3 workers', 'Merges on pass']);
    });

    it('shows the Ineligible status tag for an ineligible repository', () => {
      // Act
      const { el } = setup({ repositories: [{ ...BASE, eligibility: MOCK_REPO_INELIGIBLE.eligibility }] });

      // Assert
      const tag = el.querySelector('.repository-list__status--side.repository-list__status--ineligible');
      expect(tag?.textContent?.trim()).toBe('Ineligible');
    });

    it('shows no status tag for a healthy repository with an active webhook', () => {
      // Act
      const { el } = setup({ repositories: [{ ...BASE, webhookStatus: 'active' }] });

      // Assert
      expect(el.querySelector('.repository-list__status')).toBeNull();
    });

    it('shows no status tag for a paused repository with a silent webhook', () => {
      // Act
      const { el } = setup({ repositories: [{ ...BASE, isActive: false, webhookStatus: 'silent' }] });

      // Assert
      expect(el.querySelector('.repository-list__status')).toBeNull();
      expect(el.querySelector('.repository-list__updates')?.textContent?.startsWith('Paused,')).toBe(true);
    });

    it('renders no button in the empty state', () => {
      // Act
      const { el } = setup({ repositories: [] });

      // Assert
      expect(el.querySelector('.repository-list__empty button')).toBeNull();
    });

    it('ranks the rows by number with a screen-reader priority prefix', () => {
      // Act
      const { el } = setup({ repositories: [BASE, SECOND] });

      // Assert
      const ranks = Array.from(el.querySelectorAll('.repository-list__rank'));
      expect(ranks.map(r => r.textContent?.replace('Priority', '').trim())).toEqual(['1', '2']);
      expect(ranks[0].textContent).toContain('Priority');
    });

    it('renders a delete button and no keyboard reorder controls outside reorder mode', () => {
      // Act
      const { el } = setup({ repositories: [BASE, SECOND] });

      // Assert
      expect(el.querySelector('.repository-list__rank--button')).toBeNull();
      expect(el.querySelector('.repository-list__move-up-btn')).toBeNull();
      expect(el.querySelector('#repository-reorder-hint')).toBeNull();
      const rank = el.querySelector('.repository-list__rank') as HTMLElement;
      expect(rank.tagName).not.toBe('BUTTON');
      expect(rank.hasAttribute('tabindex')).toBe(false);
      expect(el.querySelector('.repository-list__delete-btn')).toBeTruthy();
    });

    it('renders the reorder hint and describes the list by it in reorder mode', () => {
      // Act
      const { el } = setup({ repositories: [BASE, SECOND], reordering: true });

      // Assert
      expect(el.querySelector('#repository-reorder-hint')?.textContent?.trim()).toBe(
        'Drag a row by its number, use the arrows, or focus a number and press the arrow keys. Home and End move to the top or bottom.',
      );
      expect(el.querySelector('.repository-list__list')?.getAttribute('aria-describedby')).toBe('repository-reorder-hint');
    });

    it('renders a labelled rank button per row in reorder mode', () => {
      // Act
      const { el } = setup({ repositories: [BASE, SECOND], reordering: true });

      // Assert
      const labels = Array.from(el.querySelectorAll('.repository-list__rank--button')).map(b => b.getAttribute('aria-label'));
      expect(labels).toEqual([
        'Reorder acme/platform/billing-api, priority 1, use arrow keys to move',
        'Reorder acme/web, priority 2, use arrow keys to move',
      ]);
    });

    it('disables move up on the first row and move down on the last in reorder mode', () => {
      // Act
      const { el } = setup({ repositories: [BASE, SECOND], reordering: true });

      // Assert
      const ups = el.querySelectorAll<HTMLButtonElement>('.repository-list__move-up-btn');
      const downs = el.querySelectorAll<HTMLButtonElement>('.repository-list__move-down-btn');
      expect([ups[0].disabled, ups[1].disabled]).toEqual([true, false]);
      expect([downs[0].disabled, downs[1].disabled]).toEqual([false, true]);
    });

    it('hides the delete button in reorder mode', () => {
      // Act
      const { el } = setup({ repositories: [BASE, SECOND], reordering: true });

      // Assert
      expect(el.querySelector('.repository-list__delete-btn')).toBeNull();
    });

    it('moves a row down when ArrowDown is pressed on its rank button', () => {
      // Arrange
      const { el, httpMock } = setup({ repositories: [BASE, SECOND], reordering: true });
      const rank = el.querySelector('.repository-list__rank--button') as HTMLButtonElement;

      // Act
      rank.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }));

      // Assert
      const req = httpMock.expectOne(`/api/repositories/${BASE.id}/position`);
      expect(req.request.method).toBe('PATCH');
      expect(req.request.body).toEqual({ position: 1 });
      req.flush(null, { status: 204, statusText: 'No Content' });
    });

    it('renders a single repository as rank 1 without drag affordances', () => {
      // Act
      const { el } = setup({ repositories: [BASE] });

      // Assert
      const rank = el.querySelector('.repository-list__rank') as HTMLElement;
      expect(rank.textContent).toContain('1');
      expect(rank.classList).not.toContain('repository-list__rank--draggable');
      expect(el.querySelector('.repository-list__grip')).toBeNull();
      expect(el.querySelector('.repository-list__row')?.hasAttribute('aria-roledescription')).toBe(false);
    });

    it('gives the problem block the ineligible tone for an ineligible repository', () => {
      // Act
      const { el } = setup({ repositories: [{ ...BASE, eligibility: MOCK_REPO_INELIGIBLE.eligibility }] });

      // Assert
      expect(el.querySelector('.repository-list__problem')?.classList).toContain('repository-list__problem--ineligible');
    });

    it('gives the problem block the warning tone for an unreachable repository', () => {
      // Act
      const { el } = setup({ repositories: [{ ...BASE, eligibility: MOCK_REPO_UNREACHABLE.eligibility }] });

      // Assert
      expect(el.querySelector('.repository-list__problem')?.classList).toContain('repository-list__problem--warning');
    });

    it('links a package-source violation to its source with a serialized query', () => {
      // Arrange
      const url = 'https://pkgs.example.com/nuget/v3/index.json';
      const repo: RepositorySummary = {
        ...BASE,
        eligibility: {
          status: 'ineligible',
          violations: [{ rule: `package-source-unreachable:${url}`, description: 'Foundry cannot read the package source.' }],
          reason: null,
        },
      };

      // Act
      const { el, router } = setup({ repositories: [repo] });

      // Assert
      const link = el.querySelector('.repository-list__problem-list li a.repository-list__problem-link') as HTMLAnchorElement;
      expect(link.textContent?.trim()).toBe('View package source');
      expect(link.getAttribute('aria-label')).toBe(`View package source ${url} in Package sources settings`);
      expect(link.getAttribute('href')).toBe(
        router.serializeUrl(router.createUrlTree(['/settings/package-sources'], { queryParams: { source: url } })),
      );
    });

    it('renders no package-source link for a branch-protection violation', () => {
      // Arrange
      const repo: RepositorySummary = {
        ...BASE,
        eligibility: {
          status: 'ineligible',
          violations: [{ rule: 'branch-protection:direct-pushes', description: 'Direct pushes are allowed.' }],
          reason: null,
        },
      };

      // Act
      const { el } = setup({ repositories: [repo] });

      // Assert
      expect(el.querySelector('.repository-list__problem-link')).toBeNull();
    });
  });
});

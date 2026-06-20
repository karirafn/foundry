import { TestBed } from '@angular/core/testing';
import { RepositoryListComponent } from './repository-list';
import { RepositorySummary } from '../repository.model';
import { RepositoryService } from '../repository.service';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';

const MOCK_REPO: RepositorySummary = {
  id: '00000000-0000-0000-0000-000000000001',
  slug: 'my-org/my-repo',
  accountId: '00000000-0000-0000-0000-000000000010',
  accountName: 'my-github',
  pollIntervalSeconds: 300,
  isActive: true,
  lastPolledAt: '2026-06-14T12:00:00Z',
  eligibility: { status: 'eligible', violations: [] },
};

const MOCK_REPO_2: RepositorySummary = {
  id: '00000000-0000-0000-0000-000000000002',
  slug: 'work-org/backend',
  accountId: '00000000-0000-0000-0000-000000000011',
  accountName: 'work-gitlab',
  pollIntervalSeconds: null,
  isActive: false,
  lastPolledAt: null,
  eligibility: { status: 'ineligible', violations: [{ rule: 'AllowDirectPushes', description: 'Allow direct pushes is enabled' }] },
};

const MOCK_REPO_INELIGIBLE: RepositorySummary = {
  id: '00000000-0000-0000-0000-000000000003',
  slug: 'my-org/restricted-repo',
  accountId: '00000000-0000-0000-0000-000000000010',
  accountName: 'my-github',
  pollIntervalSeconds: 300,
  isActive: true,
  lastPolledAt: '2026-06-14T12:00:00Z',
  eligibility: { status: 'ineligible', violations: [{ rule: 'AllowDirectPushes', description: 'Allow direct pushes is enabled' }] },
};

const MOCK_REPO_NULL_ELIGIBILITY: RepositorySummary = {
  id: '00000000-0000-0000-0000-000000000005',
  slug: 'my-org/unpolled-repo',
  accountId: '00000000-0000-0000-0000-000000000010',
  accountName: 'my-github',
  pollIntervalSeconds: 300,
  isActive: true,
  lastPolledAt: null,
  eligibility: null,
};

const MOCK_REPO_UNREACHABLE: RepositorySummary = {
  id: '00000000-0000-0000-0000-000000000004',
  slug: 'my-org/offline-repo',
  accountId: '00000000-0000-0000-0000-000000000010',
  accountName: 'my-github',
  pollIntervalSeconds: 300,
  isActive: true,
  lastPolledAt: '2026-06-14T12:00:00Z',
  eligibility: { status: 'unreachable', violations: [] },
};

function setup(overrides: {
  repositories?: RepositorySummary[];
  loading?: boolean;
  error?: string | null;
} = {}) {
  const fixture = TestBed.createComponent(RepositoryListComponent);
  fixture.componentRef.setInput('repositories', overrides.repositories ?? []);
  fixture.componentRef.setInput('loading', overrides.loading ?? false);
  fixture.componentRef.setInput('error', overrides.error ?? null);
  fixture.detectChanges();
  return {
    fixture,
    component: fixture.componentInstance,
    el: fixture.nativeElement as HTMLElement,
    httpMock: TestBed.inject(HttpTestingController),
    repositoryService: TestBed.inject(RepositoryService),
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
      ],
    }).compileComponents();
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true });
  });

  // Cycle 1: empty state renders when no repositories
  it('should render the empty state when there are no repositories', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [] });

    // Assert
    const emptyState = el.querySelector('.repository-list__empty');
    expect(emptyState).toBeTruthy();
    expect(emptyState?.textContent).toContain('No repositories monitored');
  });

  // Cycle 2: empty state includes description and add button
  it('should render the empty state description and Add Repository button', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [] });

    // Assert
    const description = el.querySelector('.repository-list__empty-description');
    expect(description?.textContent).toContain('Add your first repository to start monitoring for issues');
    const addBtn = el.querySelector('.repository-list__add-btn');
    expect(addBtn?.textContent?.trim()).toContain('Add Repository');
  });

  // Cycle 3: loading state
  it('should render loading state with accessible role and label', () => {
    // Arrange

    // Act
    const { el } = setup({ loading: true });

    // Assert
    const loadingEl = el.querySelector('[role="status"]');
    expect(loadingEl).toBeTruthy();
    expect(loadingEl?.getAttribute('aria-label')).toBe('Loading repositories');
    const srText = el.querySelector('.sr-only');
    expect(srText?.textContent).toContain('Loading repositories');
  });

  // Cycle 4: error state
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

  // Cycle 5: populated list shows list element
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

  // Cycle 6: account badge abbreviation (2-letter)
  it('should render a 2-letter account badge for each repository', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO] });

    // Assert
    const badge = el.querySelector('.repository-list__account-badge');
    expect(badge?.textContent?.trim()).toBe('MY');
    expect(badge?.getAttribute('aria-hidden')).toBe('true');
  });

  // Cycle 7: slug and account name shown
  it('should render slug as primary text and account name as secondary text', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO] });

    // Assert
    const slug = el.querySelector('.repository-list__slug');
    expect(slug?.textContent?.trim()).toBe('my-org/my-repo');
    const accountName = el.querySelector('.repository-list__account-name');
    expect(accountName?.textContent?.trim()).toBe('my-github');
  });

  // Cycle 8: poll interval in minutes
  it('should render poll interval in minutes', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO] });

    // Assert
    const pollInterval = el.querySelector('.repository-list__poll-interval');
    expect(pollInterval?.textContent?.trim()).toBe('5 min');
  });

  // Cycle 9: active status indicator
  it('should render "Active" status with dot for active repositories', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO] });

    // Assert
    const statusLabel = el.querySelector('.repository-list__status-label');
    expect(statusLabel?.textContent?.trim()).toBe('Active');
    const dot = el.querySelector('.repository-list__status-dot');
    expect(dot?.getAttribute('aria-hidden')).toBe('true');
    expect(dot?.classList.contains('repository-list__status-dot--active')).toBe(true);
  });

  it('should render "Paused" status for inactive repositories', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO_2] });

    // Assert
    const statusLabel = el.querySelector('.repository-list__status-label');
    expect(statusLabel?.textContent?.trim()).toBe('Paused');
    const dot = el.querySelector('.repository-list__status-dot');
    expect(dot?.classList.contains('repository-list__status-dot--paused')).toBe(true);
  });

  // Cycle 10: last polled — "Never" when null
  it('should show "Never" for last polled when lastPolledAt is null', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO_2] });

    // Assert
    const lastPolled = el.querySelector('.repository-list__last-polled');
    expect(lastPolled?.textContent?.trim()).toBe('Never');
  });

  // Cycle 11: action buttons with aria-labels
  it('should render edit and delete buttons with accessible labels', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO] });

    // Assert
    const editBtn = el.querySelector('.repository-list__edit-btn');
    expect(editBtn?.getAttribute('aria-label')).toBe('Edit repository my-org/my-repo');
    const deleteBtn = el.querySelector('.repository-list__delete-btn');
    expect(deleteBtn?.getAttribute('aria-label')).toBe('Delete repository my-org/my-repo');
  });

  // Cycle 12: populated state shows Add Repository button in header
  it('should render Add Repository button in header when repositories exist', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO] });

    // Assert
    const headerAddBtn = el.querySelector('.repository-list__header .repository-list__add-btn');
    expect(headerAddBtn).toBeTruthy();
    expect(headerAddBtn?.textContent?.trim()).toContain('Add Repository');
  });

  // Cycle 13: multiple repositories
  it('should render a row for each repository', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO, MOCK_REPO_2] });

    // Assert
    const items = el.querySelectorAll('[role="listitem"]');
    expect(items.length).toBe(2);
  });

  // Cycle 14: add event emitted (empty state)
  it('should emit add event when Add Repository is clicked in empty state', () => {
    // Arrange
    const { el, component } = setup({ repositories: [] });
    let emitted = false;
    component.add.subscribe(() => { emitted = true; });

    // Act
    const addBtn = el.querySelector('.repository-list__add-btn') as HTMLButtonElement;
    addBtn.click();

    // Assert
    expect(emitted).toBe(true);
  });

  // Cycle 15: add event emitted (populated header)
  it('should emit add event when Add Repository is clicked in header', () => {
    // Arrange
    const { el, component } = setup({ repositories: [MOCK_REPO] });
    let emitted = false;
    component.add.subscribe(() => { emitted = true; });

    // Act
    const addBtn = el.querySelector('.repository-list__header .repository-list__add-btn') as HTMLButtonElement;
    addBtn.click();

    // Assert
    expect(emitted).toBe(true);
  });

  // Cycle 16: edit event emitted
  it('should emit the repository when edit is clicked', () => {
    // Arrange
    const { el, component } = setup({ repositories: [MOCK_REPO] });
    let emittedRepo: RepositorySummary | undefined;
    component.edit.subscribe((r: RepositorySummary) => { emittedRepo = r; });

    // Act
    const editBtn = el.querySelector('.repository-list__edit-btn') as HTMLButtonElement;
    editBtn.click();

    // Assert
    expect(emittedRepo).toEqual(MOCK_REPO);
  });

  // Cycle 17: delete event emitted
  it('should emit the repository when delete is clicked', () => {
    // Arrange
    const { el, component } = setup({ repositories: [MOCK_REPO] });
    let emittedRepo: RepositorySummary | undefined;
    component.delete.subscribe((r: RepositorySummary) => { emittedRepo = r; });

    // Act
    const deleteBtn = el.querySelector('.repository-list__delete-btn') as HTMLButtonElement;
    deleteBtn.click();

    // Assert
    expect(emittedRepo).toEqual(MOCK_REPO);
  });

  // Cycle 18: retry event emitted
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

  // Cycle 19: eligibility component rendered for each repository
  it('should render fd-repository-eligibility for each repository item', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO, MOCK_REPO_INELIGIBLE] });

    // Assert
    const eligibilityComponents = el.querySelectorAll('fd-repository-eligibility');
    expect(eligibilityComponents.length).toBe(2);
  });

  // Cycle 20: re-check button calls recheckEligibility service method
  it('should call recheckEligibility when Re-check button is clicked', () => {
    // Arrange
    const { el, httpMock } = setup({ repositories: [MOCK_REPO_INELIGIBLE] });

    // Act
    const recheckBtn = el.querySelector('.repository-list__recheck-btn') as HTMLButtonElement;
    recheckBtn.click();

    // Assert — service POST is issued with correct URL
    const req = httpMock.expectOne(
      `/api/accounts/${MOCK_REPO_INELIGIBLE.accountId}/repositories/${MOCK_REPO_INELIGIBLE.id}/recheck`
    );
    expect(req.request.method).toBe('POST');
    req.flush(MOCK_REPO_INELIGIBLE);
  });

  // Cycle 21: re-check button shows loading state while pending
  it('should show "Re-checking..." on Re-check button while request is in flight', () => {
    // Arrange
    const { el, fixture, httpMock } = setup({ repositories: [MOCK_REPO_INELIGIBLE] });

    // Act
    const recheckBtn = el.querySelector('.repository-list__recheck-btn') as HTMLButtonElement;
    recheckBtn.click();
    fixture.detectChanges();

    // Assert — button text changes while in flight
    const updatedBtn = el.querySelector('.repository-list__recheck-btn') as HTMLButtonElement;
    expect(updatedBtn?.textContent?.trim()).toBe('Re-checking...');

    // Cleanup
    httpMock.expectOne(
      `/api/accounts/${MOCK_REPO_INELIGIBLE.accountId}/repositories/${MOCK_REPO_INELIGIBLE.id}/recheck`
    ).flush(MOCK_REPO_INELIGIBLE);
  });

  // Cycle 22: re-check button shown for ineligible repositories
  it('should render Re-check button for ineligible repositories', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO_INELIGIBLE] });

    // Assert
    const recheckBtn = el.querySelector('.repository-list__recheck-btn');
    expect(recheckBtn).toBeTruthy();
  });

  // Cycle 23: re-check button shown for unreachable repositories
  it('should render Re-check button for unreachable repositories', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO_UNREACHABLE] });

    // Assert
    const recheckBtn = el.querySelector('.repository-list__recheck-btn');
    expect(recheckBtn).toBeTruthy();
  });

  // Cycle 24: eligible repos do not show re-check button
  it('should not render Re-check button for eligible repositories', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO] });

    // Assert
    const recheckBtn = el.querySelector('.repository-list__recheck-btn');
    expect(recheckBtn).toBeFalsy();
  });

  // Cycle 25: null eligibility — no crash, no eligibility component or re-check button
  it('should not render eligibility component when eligibility is null', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO_NULL_ELIGIBILITY] });

    // Assert
    const eligibilityComponent = el.querySelector('fd-repository-eligibility');
    expect(eligibilityComponent).toBeFalsy();
    const recheckBtn = el.querySelector('.repository-list__recheck-btn');
    expect(recheckBtn).toBeFalsy();
  });
});

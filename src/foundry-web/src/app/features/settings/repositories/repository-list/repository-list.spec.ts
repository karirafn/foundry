import { TestBed } from '@angular/core/testing';
import { RepositoryListComponent } from './repository-list';
import { RepositorySummary } from '../repository.model';
import { RepositoryService } from '../repository.service';
import { ProviderIconComponent } from '../../../../shared/components/provider-icon/provider-icon';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';

const MOCK_REPO: RepositorySummary = {
  id: '00000000-0000-0000-0000-000000000001',
  slug: 'my-org/my-repo',
  accountId: '00000000-0000-0000-0000-000000000010',
  accountName: 'my-github',
  providerType: 'github',
  position: 0,
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
  providerType: 'gitlab',
  position: 1,
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
  providerType: 'github',
  position: 2,
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
  providerType: 'github',
  position: 4,
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
  providerType: 'github',
  position: 3,
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

// Cycle 44 — helper text, drag handle, move buttons, single-item suppression

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
    const srText = loadingEl?.querySelector('.sr-only');
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

  // Cycle 6: provider icon renders for each repository
  it('should render fd-provider-icon with the correct providerType for a github repository', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO] });

    // Assert
    const icon = el.querySelector('fd-provider-icon');
    expect(icon).toBeTruthy();
    expect(icon?.getAttribute('aria-label')).toBe('GitHub');
  });

  it('should render fd-provider-icon with the correct providerType for a gitlab repository', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO_2] });

    // Assert
    const icon = el.querySelector('fd-provider-icon');
    expect(icon).toBeTruthy();
    expect(icon?.getAttribute('aria-label')).toBe('GitLab');
  });

  it('should not render the old account-badge element', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO] });

    // Assert
    const badge = el.querySelector('.repository-list__account-badge');
    expect(badge).toBeFalsy();
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

  // Cycle 19: eligibility chip rendered for each repository
  it('should render fd-repository-eligibility for each repository item', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO, MOCK_REPO_INELIGIBLE] });

    // Assert
    const eligibilityComponents = el.querySelectorAll('fd-repository-eligibility');
    expect(eligibilityComponents.length).toBe(2);
  });

  // Cycle 25: null eligibility — no crash, no eligibility component
  it('should not render eligibility component when eligibility is null', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO_NULL_ELIGIBILITY] });

    // Assert
    const eligibilityComponent = el.querySelector('fd-repository-eligibility');
    expect(eligibilityComponent).toBeFalsy();
  });

  // Cycle 28: disclosure toggle shown for ineligible repos
  it('should render a disclosure toggle button for ineligible repos', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO_INELIGIBLE] });

    // Assert
    const toggle = el.querySelector('.repository-list__toggle-btn');
    expect(toggle).toBeTruthy();
  });

  // Cycle 29: disclosure toggle shown for unreachable repos
  it('should render a disclosure toggle button for unreachable repos', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO_UNREACHABLE] });

    // Assert
    const toggle = el.querySelector('.repository-list__toggle-btn');
    expect(toggle).toBeTruthy();
  });

  // Cycle 30: eligible repos do not show disclosure toggle
  it('should not render a disclosure toggle button for eligible repos', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO] });

    // Assert
    const toggle = el.querySelector('.repository-list__toggle-btn');
    expect(toggle).toBeFalsy();
  });

  // Cycle 31: disclosure toggle has aria-expanded=false initially
  it('should have aria-expanded="false" on the disclosure toggle initially', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO_INELIGIBLE] });

    // Assert
    const toggle = el.querySelector('.repository-list__toggle-btn');
    expect(toggle?.getAttribute('aria-expanded')).toBe('false');
  });

  // Cycle 32: clicking the toggle opens the details panel
  it('should set aria-expanded="true" on the toggle after clicking it', () => {
    // Arrange
    const { el, fixture } = setup({ repositories: [MOCK_REPO_INELIGIBLE] });

    // Act
    const toggle = el.querySelector('.repository-list__toggle-btn') as HTMLButtonElement;
    toggle.click();
    fixture.detectChanges();

    // Assert
    const updatedToggle = el.querySelector('.repository-list__toggle-btn');
    expect(updatedToggle?.getAttribute('aria-expanded')).toBe('true');
  });

  // Cycle 33: clicking the toggle again closes the details panel (single-open)
  it('should collapse the panel when the same toggle is clicked again', () => {
    // Arrange
    const { el, fixture } = setup({ repositories: [MOCK_REPO_INELIGIBLE] });
    const toggle = el.querySelector('.repository-list__toggle-btn') as HTMLButtonElement;
    toggle.click();
    fixture.detectChanges();

    // Act
    toggle.click();
    fixture.detectChanges();

    // Assert
    const updatedToggle = el.querySelector('.repository-list__toggle-btn');
    expect(updatedToggle?.getAttribute('aria-expanded')).toBe('false');
  });

  // Cycle 34: details panel is present in DOM (always rendered with [hidden])
  it('should render the details panel element in the DOM for non-eligible repos', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO_INELIGIBLE] });

    // Assert
    const detailsPanel = el.querySelector('fd-repository-eligibility-details');
    expect(detailsPanel).toBeTruthy();
  });

  // Cycle 35: details panel is hidden by default
  it('should hide the details panel by default', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO_INELIGIBLE] });

    // Assert
    const detailsPanel = el.querySelector('fd-repository-eligibility-details');
    expect(detailsPanel?.hasAttribute('hidden')).toBe(true);
  });

  // Cycle 36: details panel becomes visible after toggling
  it('should show the details panel after clicking the toggle', () => {
    // Arrange
    const { el, fixture } = setup({ repositories: [MOCK_REPO_INELIGIBLE] });

    // Act
    const toggle = el.querySelector('.repository-list__toggle-btn') as HTMLButtonElement;
    toggle.click();
    fixture.detectChanges();

    // Assert
    const detailsPanel = el.querySelector('fd-repository-eligibility-details');
    expect(detailsPanel?.hasAttribute('hidden')).toBe(false);
  });

  // Cycle 37: toggle button aria-controls points to the details panel id
  it('should have aria-controls pointing to the details panel id', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO_INELIGIBLE] });

    // Assert
    const toggle = el.querySelector('.repository-list__toggle-btn');
    const controls = toggle?.getAttribute('aria-controls');
    expect(controls).toBe(`eligibility-detail-${MOCK_REPO_INELIGIBLE.id}`);
    const panel = el.querySelector(`#eligibility-detail-${MOCK_REPO_INELIGIBLE.id}`);
    expect(panel).toBeTruthy();
  });

  // Cycle 38: single-open — opening a second repo closes the first
  it('should close the first panel when a second is opened (single-open)', () => {
    // Arrange
    const { el, fixture } = setup({ repositories: [MOCK_REPO_INELIGIBLE, MOCK_REPO_UNREACHABLE] });
    const toggles = el.querySelectorAll('.repository-list__toggle-btn') as NodeListOf<HTMLButtonElement>;

    // Act — open first
    toggles[0].click();
    fixture.detectChanges();
    // Act — open second
    toggles[1].click();
    fixture.detectChanges();

    // Assert — first panel is now hidden, second is visible
    const panels = el.querySelectorAll('fd-repository-eligibility-details');
    expect(panels[0]?.hasAttribute('hidden')).toBe(true);
    expect(panels[1]?.hasAttribute('hidden')).toBe(false);
  });

  // Cycle 20: re-check button in details panel calls recheckEligibility service method
  it('should call recheckEligibility when Re-check is clicked in the details panel', () => {
    // Arrange
    const { el, fixture, httpMock } = setup({ repositories: [MOCK_REPO_INELIGIBLE] });
    // Expand the panel first
    const toggle = el.querySelector('.repository-list__toggle-btn') as HTMLButtonElement;
    toggle.click();
    fixture.detectChanges();

    // Act — click recheck inside details panel
    const recheckBtn = el.querySelector('fd-repository-eligibility-details .repository-eligibility-details__recheck-btn') as HTMLButtonElement;
    recheckBtn.click();

    // Assert — service POST is issued with correct URL
    const req = httpMock.expectOne(
      `/api/accounts/${MOCK_REPO_INELIGIBLE.accountId}/repositories/${MOCK_REPO_INELIGIBLE.id}/recheck`
    );
    expect(req.request.method).toBe('POST');
    req.flush(MOCK_REPO_INELIGIBLE);
  });

  // Cycle 40: live region is persistently present with aria-live="polite" and aria-atomic="true", initially empty
  it('should render a persistent sr-only live region that is always aria-live="polite" and aria-atomic="true" and starts empty', () => {
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

  // Cycle 41: live region announces "Re-checking..." with slug when recheck starts
  it('should announce "slug: Re-checking..." in the polite live region when recheck starts', () => {
    // Arrange
    const { el, fixture, httpMock } = setup({ repositories: [MOCK_REPO_INELIGIBLE] });
    const toggle = el.querySelector('.repository-list__toggle-btn') as HTMLButtonElement;
    toggle.click();
    fixture.detectChanges();

    // Act — click recheck
    const recheckBtn = el.querySelector('fd-repository-eligibility-details .repository-eligibility-details__recheck-btn') as HTMLButtonElement;
    recheckBtn.click();
    fixture.detectChanges();

    // Assert — live region has start message before response; aria-live is always polite
    const liveRegion = el.querySelector('.repository-list__announcement');
    expect(liveRegion?.getAttribute('aria-live')).toBe('polite');
    expect(liveRegion?.textContent?.trim()).toBe(`${MOCK_REPO_INELIGIBLE.slug}: Re-checking...`);

    // Clean up pending request
    const req = httpMock.expectOne(
      `/api/accounts/${MOCK_REPO_INELIGIBLE.accountId}/repositories/${MOCK_REPO_INELIGIBLE.id}/recheck`
    );
    req.flush(MOCK_REPO_INELIGIBLE);
  });

  // Cycle 42: live region announces result label on success
  it('should announce the result label when recheck succeeds with a changed status', () => {
    // Arrange
    const updatedRepo: RepositorySummary = {
      ...MOCK_REPO_INELIGIBLE,
      eligibility: { status: 'eligible', violations: [] },
    };
    const { el, fixture, httpMock } = setup({ repositories: [MOCK_REPO_INELIGIBLE] });
    const toggle = el.querySelector('.repository-list__toggle-btn') as HTMLButtonElement;
    toggle.click();
    fixture.detectChanges();
    const recheckBtn = el.querySelector('fd-repository-eligibility-details .repository-eligibility-details__recheck-btn') as HTMLButtonElement;
    recheckBtn.click();
    fixture.detectChanges();

    // Act — flush with updated result
    const req = httpMock.expectOne(
      `/api/accounts/${MOCK_REPO_INELIGIBLE.accountId}/repositories/${MOCK_REPO_INELIGIBLE.id}/recheck`
    );
    req.flush(updatedRepo);
    fixture.detectChanges();

    // Assert — live region shows the result label; aria-live is always polite
    const liveRegion = el.querySelector('.repository-list__announcement');
    expect(liveRegion?.getAttribute('aria-live')).toBe('polite');
    expect(liveRegion?.textContent?.trim()).toBe(`${MOCK_REPO_INELIGIBLE.slug}: Eligible`);
  });

  // Cycle 44: helper text visible when multiple repositories exist
  it('should render priority helper text when there are multiple repositories', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO, MOCK_REPO_2] });

    // Assert
    const helper = el.querySelector('.repository-list__priority-hint');
    expect(helper).toBeTruthy();
    expect(helper?.textContent).toContain('priority');
  });

  // Fix 5 — helper text clarifies dispatch direction (Low)
  it('should include dispatch direction hint in priority helper text', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO, MOCK_REPO_2] });

    // Assert
    const helper = el.querySelector('.repository-list__priority-hint');
    expect(helper?.textContent).toContain('top');
    expect(helper?.textContent).toContain('first');
  });

  // Cycle 45: drag handle and move buttons are shown with multiple repositories
  it('should render a drag handle for each item when multiple repositories exist', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO, MOCK_REPO_2] });

    // Assert
    const handles = el.querySelectorAll('.repository-list__drag-handle');
    expect(handles.length).toBe(2);
  });

  // Fix 1 — drag handle aria-label conveys keyboard affordance (WCAG 4.1.2)
  it('should set the drag handle aria-label to include keyboard instruction for reordering', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO, MOCK_REPO_2] });

    // Assert
    const handle = el.querySelector('.repository-list__drag-handle') as HTMLElement;
    expect(handle?.getAttribute('aria-label')).toBe(`Reorder ${MOCK_REPO.slug}, use arrow keys to move`);
  });

  it('should set aria-roledescription="reorderable item" on each draggable list item when multiple repositories exist', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO, MOCK_REPO_2] });

    // Assert
    const items = el.querySelectorAll('[role="listitem"]');
    expect(items[0]?.getAttribute('aria-roledescription')).toBe('reorderable item');
    expect(items[1]?.getAttribute('aria-roledescription')).toBe('reorderable item');
  });

  it('should render move-up and move-down buttons for each item when multiple repositories exist', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO, MOCK_REPO_2] });

    // Assert
    const moveUpBtns = el.querySelectorAll('.repository-list__move-up-btn');
    const moveDownBtns = el.querySelectorAll('.repository-list__move-down-btn');
    expect(moveUpBtns.length).toBe(2);
    expect(moveDownBtns.length).toBe(2);
  });

  // Cycle 46: single item — reorder affordances are hidden
  it('should NOT render drag handle, move-up, or move-down buttons when only one repository exists', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO] });

    // Assert
    expect(el.querySelector('.repository-list__drag-handle')).toBeFalsy();
    expect(el.querySelector('.repository-list__move-up-btn')).toBeFalsy();
    expect(el.querySelector('.repository-list__move-down-btn')).toBeFalsy();
  });

  it('should NOT render priority helper text when only one repository exists', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO] });

    // Assert
    const helper = el.querySelector('.repository-list__priority-hint');
    expect(helper).toBeFalsy();
  });

  // Fix 3b — visible inline error when reorder fails (WCAG 4.1.3)
  it('should display a visible move error message when the PATCH fails', () => {
    // Arrange
    const { el, fixture, httpMock } = setup({ repositories: [MOCK_REPO, MOCK_REPO_2] });

    // Act
    const moveDownBtn = el.querySelectorAll('.repository-list__move-down-btn')[0] as HTMLButtonElement;
    moveDownBtn.click();
    httpMock.expectOne(`/api/repositories/${MOCK_REPO.id}/position`).flush('Server error', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    fixture.detectChanges();

    // Assert — visible error element is shown
    const moveError = el.querySelector('.repository-list__move-error');
    expect(moveError).toBeTruthy();
    expect(moveError?.textContent).toContain('reorder');
  });

  it('should clear the visible move error when a subsequent move succeeds', () => {
    // Arrange
    const { el, fixture, httpMock } = setup({ repositories: [MOCK_REPO, MOCK_REPO_2] });
    const moveDownBtns = el.querySelectorAll('.repository-list__move-down-btn');
    (moveDownBtns[0] as HTMLButtonElement).click();
    httpMock.expectOne(`/api/repositories/${MOCK_REPO.id}/position`).flush('Server error', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    fixture.detectChanges();

    // Act — retry the move successfully
    const moveDownBtns2 = el.querySelectorAll('.repository-list__move-down-btn');
    (moveDownBtns2[0] as HTMLButtonElement).click();
    httpMock.expectOne(`/api/repositories/${MOCK_REPO.id}/position`).flush(null, {
      status: 204,
      statusText: 'No Content',
    });
    fixture.detectChanges();

    // Assert — visible error is gone
    const moveError = el.querySelector('.repository-list__move-error');
    expect(moveError).toBeFalsy();
  });

  // Cycle 47: move-down calls moveRepository with new index and announces
  it('should call moveRepository with index + 1 when move-down is clicked for the first item', () => {
    // Arrange
    const { el, fixture, httpMock } = setup({ repositories: [MOCK_REPO, MOCK_REPO_2] });

    // Act
    const moveDownBtn = el.querySelectorAll('.repository-list__move-down-btn')[0] as HTMLButtonElement;
    moveDownBtn.click();
    fixture.detectChanges();

    // Assert — PATCH sent to correct URL
    const req = httpMock.expectOne(`/api/repositories/${MOCK_REPO.id}/position`);
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toEqual({ position: 1 });
    req.flush(null, { status: 204, statusText: 'No Content' });
  });

  // Cycle 48: move-up calls moveRepository with index - 1
  it('should call moveRepository with index - 1 when move-up is clicked for the second item', () => {
    // Arrange
    const { el, fixture, httpMock } = setup({ repositories: [MOCK_REPO, MOCK_REPO_2] });

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

  // Cycle 49: live region announces new position after move
  it('should announce the new position in the live region after a move succeeds', () => {
    // Arrange
    const { el, fixture, httpMock } = setup({ repositories: [MOCK_REPO, MOCK_REPO_2] });

    // Act
    const moveDownBtn = el.querySelectorAll('.repository-list__move-down-btn')[0] as HTMLButtonElement;
    moveDownBtn.click();
    httpMock.expectOne(`/api/repositories/${MOCK_REPO.id}/position`).flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();

    // Assert — live region updated
    const liveRegion = el.querySelector('.repository-list__announcement');
    expect(liveRegion?.textContent?.trim()).toContain(MOCK_REPO.slug);
  });

  // Fix 2 — focus restored to moved item after button-triggered reorder (WCAG 2.4.3)
  it('should restore focus to a control within the moved item after a successful move-down', async () => {
    // Arrange
    vi.useFakeTimers();
    const { el, fixture, httpMock } = setup({ repositories: [MOCK_REPO, MOCK_REPO_2] });
    const moveDownBtn = el.querySelectorAll('.repository-list__move-down-btn')[0] as HTMLButtonElement;
    moveDownBtn.focus();

    // Act
    moveDownBtn.click();
    httpMock.expectOne(`/api/repositories/${MOCK_REPO.id}/position`).flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();
    vi.runAllTimers(); // flush setTimeout(0) for focus restoration

    // Assert — focus lands within the moved repo's list item (looked up by id, stable regardless of render order)
    const movedItemEl = el.querySelector(`#repo-item-${MOCK_REPO.id}`) as HTMLElement;
    expect(movedItemEl).toBeTruthy();
    expect(movedItemEl.contains(document.activeElement)).toBe(true);
    vi.useRealTimers();
  });

  it('should restore focus to a control within the moved item after a successful move-up', async () => {
    // Arrange
    vi.useFakeTimers();
    const { el, fixture, httpMock } = setup({ repositories: [MOCK_REPO, MOCK_REPO_2] });
    const moveUpBtns = el.querySelectorAll('.repository-list__move-up-btn');
    const moveUpBtn = moveUpBtns[1] as HTMLButtonElement;
    moveUpBtn.focus();

    // Act
    moveUpBtn.click();
    httpMock.expectOne(`/api/repositories/${MOCK_REPO_2.id}/position`).flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();
    vi.runAllTimers(); // flush setTimeout(0) for focus restoration

    // Assert — focus lands within the moved repo's list item (looked up by id)
    const movedItemEl = el.querySelector(`#repo-item-${MOCK_REPO_2.id}`) as HTMLElement;
    expect(movedItemEl).toBeTruthy();
    expect(movedItemEl.contains(document.activeElement)).toBe(true);
    vi.useRealTimers();
  });

  // Fix — onDrop restores focus to moved item after pointer drag (WCAG 2.4.3)
  it('should restore focus to a control within the moved item after a pointer drag drop', async () => {
    // Arrange
    vi.useFakeTimers();
    const { el, fixture, httpMock, repositoryService } = setup({ repositories: [MOCK_REPO, MOCK_REPO_2] });
    // Simulate a drop event moving first item to second position
    const component = fixture.componentInstance;
    const dropEvent = {
      previousIndex: 0,
      currentIndex: 1,
      item: {} as never,
      container: {} as never,
      previousContainer: {} as never,
      isPointerOverContainer: true,
      distance: { x: 0, y: 0 },
      dropPoint: { x: 0, y: 0 },
    };

    // Act
    component.onDrop(dropEvent as never);
    httpMock.expectOne(`/api/repositories/${MOCK_REPO.id}/position`).flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();
    vi.runAllTimers();

    // Assert — focus lands within the moved repo's list item
    const movedItemEl = el.querySelector(`#repo-item-${MOCK_REPO.id}`) as HTMLElement;
    expect(movedItemEl).toBeTruthy();
    expect(movedItemEl.contains(document.activeElement)).toBe(true);
    vi.useRealTimers();
  });

  // Fix — announcement resets to empty before each move so identical consecutive announcements are always re-announced
  it('should reset the announcement to empty before calling moveRepository so identical messages always cause a DOM mutation', () => {
    // Arrange
    const { el, fixture, httpMock } = setup({ repositories: [MOCK_REPO, MOCK_REPO_2] });
    const moveDownBtn = el.querySelectorAll('.repository-list__move-down-btn')[0] as HTMLButtonElement;
    // First move — succeed
    moveDownBtn.click();
    httpMock.expectOne(`/api/repositories/${MOCK_REPO.id}/position`).flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();

    // Verify initial announcement is set
    const liveRegion = el.querySelector('.repository-list__announcement');
    expect(liveRegion?.textContent?.trim()).not.toBe('');

    // Act — trigger a move on any button; the announcement should clear before the response arrives
    const moveUpBtn = el.querySelectorAll('.repository-list__move-up-btn')[1] as HTMLButtonElement;
    moveUpBtn.click();
    fixture.detectChanges();

    // Assert — live region is empty while the request is in flight (reset happened)
    expect(liveRegion?.textContent?.trim()).toBe('');

    // Clean up pending request
    httpMock.expectOne(`/api/repositories/${MOCK_REPO_2.id}/position`).flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();
  });

  // Fix — ul has accessible name via aria-label (WCAG 4.1.2 / Medium)
  it('should give the drop list an accessible name via aria-label', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO, MOCK_REPO_2] });

    // Assert
    const list = el.querySelector('[role="list"]');
    expect(list?.getAttribute('aria-label')).toBeTruthy();
  });

  // Fix — priority hint is programmatically associated with the list via aria-describedby (Medium)
  it('should associate the priority hint with the list via aria-describedby', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO, MOCK_REPO_2] });

    // Assert
    const list = el.querySelector('[role="list"]');
    const hintId = list?.getAttribute('aria-describedby');
    expect(hintId).toBeTruthy();
    const hintEl = el.querySelector(`#${hintId}`);
    expect(hintEl).toBeTruthy();
    expect(hintEl?.textContent).toContain('priority');
  });

  // Fix — up/down buttons use attr.disabled so enabled buttons have no disabled attribute (Low)
  it('should have no disabled attribute on enabled move-up and move-down buttons', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO, MOCK_REPO_2] });

    // Assert — first move-up is disabled (index 0), last move-down is disabled (index 1)
    const moveUpBtns = el.querySelectorAll('.repository-list__move-up-btn');
    const moveDownBtns = el.querySelectorAll('.repository-list__move-down-btn');

    // The enabled buttons (not first up, not last down) must NOT have a disabled attribute at all
    expect(moveUpBtns[1]?.hasAttribute('disabled')).toBe(false);
    expect(moveDownBtns[0]?.hasAttribute('disabled')).toBe(false);

    // The disabled buttons should carry the attribute
    expect(moveUpBtns[0]?.hasAttribute('disabled')).toBe(true);
    expect(moveDownBtns[1]?.hasAttribute('disabled')).toBe(true);
  });

  // Cycle 43: live region announces failure when recheck errors
  it('should announce "slug: Re-check failed" when recheck fails', () => {
    // Arrange
    const { el, fixture, httpMock } = setup({ repositories: [MOCK_REPO_INELIGIBLE] });
    const toggle = el.querySelector('.repository-list__toggle-btn') as HTMLButtonElement;
    toggle.click();
    fixture.detectChanges();
    const recheckBtn = el.querySelector('fd-repository-eligibility-details .repository-eligibility-details__recheck-btn') as HTMLButtonElement;
    recheckBtn.click();
    fixture.detectChanges();

    // Act — flush with error
    const req = httpMock.expectOne(
      `/api/accounts/${MOCK_REPO_INELIGIBLE.accountId}/repositories/${MOCK_REPO_INELIGIBLE.id}/recheck`
    );
    req.flush('Server error', { status: 500, statusText: 'Internal Server Error' });
    fixture.detectChanges();

    // Assert — live region announces failure; aria-live is always polite
    const liveRegion = el.querySelector('.repository-list__announcement');
    expect(liveRegion?.getAttribute('aria-live')).toBe('polite');
    expect(liveRegion?.textContent?.trim()).toBe(`${MOCK_REPO_INELIGIBLE.slug}: Re-check failed`);
  });

  // AC-6: reorder controls are visually grouped in __reorder-group, separate from __actions
  it('should render drag-handle and move buttons inside __reorder-group, separate from __actions', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO, MOCK_REPO_2] });

    // Assert
    const reorderGroup = el.querySelector('.repository-list__reorder-group');
    expect(reorderGroup).toBeTruthy();
    expect(reorderGroup?.querySelector('.repository-list__drag-handle')).toBeTruthy();
    expect(reorderGroup?.querySelector('.repository-list__move-up-btn')).toBeTruthy();
    expect(reorderGroup?.querySelector('.repository-list__move-down-btn')).toBeTruthy();

    const actions = el.querySelector('.repository-list__actions');
    expect(actions).toBeTruthy();
    expect(actions?.querySelector('.repository-list__drag-handle')).toBeFalsy();
    expect(actions?.querySelector('.repository-list__move-up-btn')).toBeFalsy();
    expect(actions?.querySelector('.repository-list__move-down-btn')).toBeFalsy();
  });

  // AC-3: slug span carries a title attribute matching the repo slug
  it('should set the title attribute on the slug span to the full repo slug', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO] });

    // Assert
    const slugSpan = el.querySelector('.repository-list__slug');
    expect(slugSpan?.getAttribute('title')).toBe(MOCK_REPO.slug);
  });

  // AC-4: metadata cluster contains all four metadata children
  it('should render all four metadata children inside __metadata', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO] });

    // Assert
    const metadata = el.querySelector('.repository-list__metadata');
    expect(metadata).toBeTruthy();
    expect(metadata?.querySelector('.repository-list__account-name')).toBeTruthy();
    expect(metadata?.querySelector('.repository-list__poll-interval')).toBeTruthy();
    expect(metadata?.querySelector('.repository-list__status')).toBeTruthy();
    expect(metadata?.querySelector('.repository-list__last-polled')).toBeTruthy();
  });
});

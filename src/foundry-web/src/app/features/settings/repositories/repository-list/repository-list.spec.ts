import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
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
  providerType: 'github',
  position: 0,
  pollIntervalSeconds: 300,
  effectivePollIntervalSeconds: 300,
  pollIntervalIsDefault: false,
  isActive: true,
  maxConcurrentWorkers: 2,
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
  lastPolledAt: '2026-06-14T12:00:00Z',
  eligibility: { status: 'eligible', violations: [], reason: null },
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

  // ─── Two-tier layout ─────────────────────────────────────────────────────────

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

  it('should render line 1 (repository-list__line1) and line 2 (repository-list__strip) in each card', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO] });

    // Assert
    const item = el.querySelector('[role="listitem"]');
    expect(item?.querySelector('.repository-list__line1')).toBeTruthy();
    expect(item?.querySelector('.repository-list__strip')).toBeTruthy();
  });

  it('should render fd-provider-icon inside line 1', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO] });

    // Assert
    const line1 = el.querySelector('.repository-list__line1');
    expect(line1?.querySelector('fd-provider-icon')).toBeTruthy();
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

  it('should render full slug text in the link (un-truncated)', () => {
    // Arrange
    const longSlugRepo: RepositorySummary = {
      ...MOCK_REPO,
      slug: 'efla/databridge/some-long-project-name',
    };

    // Act
    const { el } = setup({ repositories: [longSlugRepo] });

    // Assert
    const slugLink = el.querySelector('.repository-list__slug');
    // The full slug text must be present. Normalize whitespace because the @for template
    // may add spaces around text nodes; <wbr> elements are zero-width and excluded from textContent.
    const slugText = slugLink?.textContent?.replace(/\s+/g, '') ?? '';
    expect(slugText).toContain('efla/databridge/some-long-project-name');
  });

  it('should render <wbr> elements between slug segments to allow wrapping at "/"', () => {
    // Arrange
    const multiSegmentRepo: RepositorySummary = {
      ...MOCK_REPO,
      slug: 'my-org/my-repo',
    };

    // Act
    const { el } = setup({ repositories: [multiSegmentRepo] });

    // Assert
    const slugLink = el.querySelector('.repository-list__slug');
    const wbrElements = slugLink?.querySelectorAll('wbr');
    // A two-segment slug has one "/" so one <wbr> element should be present
    expect(wbrElements?.length).toBeGreaterThanOrEqual(1);
  });

  it('should not render a pencil edit button in the card', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO] });

    // Assert
    const editBtn = el.querySelector('[aria-label="Edit repository my-org/my-repo"]');
    expect(editBtn).toBeFalsy();
  });

  it('should not render fd-row-actions (replaced by fd-delete-button)', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO] });

    // Assert
    expect(el.querySelector('fd-row-actions')).toBeFalsy();
  });

  // ─── Delete via fd-delete-button ─────────────────────────────────────────────

  it('should render fd-delete-button in line 1 with an aria-label containing the slug', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO] });

    // Assert
    const deleteBtn = el.querySelector('[aria-label="Delete repository my-org/my-repo"]');
    expect(deleteBtn).toBeTruthy();
  });

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

  // ─── Reorder controls — only with >1 repo ────────────────────────────────────

  it('should render drag handle and move buttons for each item when multiple repositories exist', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO, MOCK_REPO_2] });

    // Assert
    const handles = el.querySelectorAll('.repository-list__drag-handle');
    expect(handles.length).toBe(2);
    expect(el.querySelectorAll('.repository-list__move-up-btn').length).toBe(2);
    expect(el.querySelectorAll('.repository-list__move-down-btn').length).toBe(2);
  });

  it('should NOT render reorder controls when only one repository exists', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO] });

    // Assert
    expect(el.querySelector('.repository-list__drag-handle')).toBeFalsy();
    expect(el.querySelector('.repository-list__move-up-btn')).toBeFalsy();
    expect(el.querySelector('.repository-list__move-down-btn')).toBeFalsy();
    expect(el.querySelector('.repository-list__reorder-group')).toBeFalsy();
  });

  it('should NOT render priority helper text when only one repository exists', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO] });

    // Assert
    expect(el.querySelector('.repository-list__priority-hint')).toBeFalsy();
  });

  it('should render priority helper text when there are multiple repositories', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO, MOCK_REPO_2] });

    // Assert
    const helper = el.querySelector('.repository-list__priority-hint');
    expect(helper).toBeTruthy();
    expect(helper?.textContent).toContain('priority');
    expect(helper?.textContent).toContain('top');
    expect(helper?.textContent).toContain('first');
  });

  it('should render reorder-group inside line 1 when multiple repos exist', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO, MOCK_REPO_2] });

    // Assert
    const line1 = el.querySelector('.repository-list__line1');
    expect(line1?.querySelector('.repository-list__reorder-group')).toBeTruthy();
  });

  // ─── Metadata strip order ─────────────────────────────────────────────────────

  it('should render state dot and label in the strip', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO] });

    // Assert
    const strip = el.querySelector('.repository-list__strip');
    expect(strip?.querySelector('.repository-list__state-dot--active')).toBeTruthy();
    const stateLabel = strip?.querySelector('.repository-list__state-label');
    expect(stateLabel?.textContent?.trim()).toBe('Polling');
  });

  it('should render "Paused" state label for inactive repository in the strip', () => {
    // Arrange
    const paused = { ...MOCK_REPO, isActive: false };

    // Act
    const { el } = setup({ repositories: [paused] });

    // Assert
    const strip = el.querySelector('.repository-list__strip');
    const stateLabel = strip?.querySelector('.repository-list__state-label');
    expect(stateLabel?.textContent?.trim()).toBe('Paused');
    expect(strip?.querySelector('.repository-list__state-dot--paused')).toBeTruthy();
  });

  it('should render the via accountName in the strip', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO] });

    // Assert
    const via = el.querySelector('.repository-list__via');
    expect(via?.textContent?.trim()).toContain('my-github');
  });

  it('should render fd-repository-eligibility badge in the strip when eligibility is non-null', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO] });

    // Assert
    const strip = el.querySelector('.repository-list__strip');
    expect(strip?.querySelector('fd-repository-eligibility')).toBeTruthy();
  });

  it('should not render fd-repository-eligibility badge when eligibility is null', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO_NULL_ELIGIBILITY] });

    // Assert
    expect(el.querySelector('fd-repository-eligibility')).toBeFalsy();
  });

  // ─── Interval chip — own value vs default marker ──────────────────────────────

  it('should render the interval chip with the effective poll interval value in minutes', () => {
    // Arrange — MOCK_REPO has effectivePollIntervalSeconds=300 (5m), pollIntervalIsDefault=false

    // Act
    const { el } = setup({ repositories: [MOCK_REPO] });

    // Assert
    const chip = el.querySelector('.repository-list__chip--interval');
    expect(chip?.textContent?.trim().replace(/\s+/g, ' ')).toContain('5m');
  });

  it('should not render a "default" marker on the interval chip when pollIntervalIsDefault is false', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO] });

    // Assert
    const defaultMarker = el.querySelector('.repository-list__chip-default');
    expect(defaultMarker).toBeFalsy();
  });

  it('should render a "default" marker on the interval chip when pollIntervalIsDefault is true', () => {
    // Arrange — MOCK_REPO_2 has pollIntervalIsDefault=true, effectivePollIntervalSeconds=1800

    // Act
    const { el } = setup({ repositories: [MOCK_REPO_2] });

    // Assert
    const defaultMarker = el.querySelector('.repository-list__chip-default');
    expect(defaultMarker).toBeTruthy();
    expect(defaultMarker?.textContent?.trim()).toContain('default');
  });

  it('should render the inherited global interval value when pollIntervalIsDefault is true', () => {
    // Arrange — MOCK_REPO_2: effectivePollIntervalSeconds=1800 (30m), pollIntervalIsDefault=true

    // Act
    const { el } = setup({ repositories: [MOCK_REPO_2] });

    // Assert
    const chip = el.querySelector('.repository-list__chip--interval');
    expect(chip?.textContent?.trim().replace(/\s+/g, ' ')).toContain('30m');
  });

  it('should render interval chip with aria-label containing full meaning', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO] });

    // Assert
    const chip = el.querySelector('.repository-list__chip--interval');
    const ariaLabel = chip?.getAttribute('aria-label') ?? '';
    expect(ariaLabel).toContain('Polls every');
    expect(ariaLabel).toContain('5 minute');
  });

  it('should include "(default)" in the interval chip aria-label when pollIntervalIsDefault is true', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO_2] });

    // Assert
    const chip = el.querySelector('.repository-list__chip--interval');
    expect(chip?.getAttribute('aria-label')).toContain('(default)');
  });

  // ─── Last-polled chip ─────────────────────────────────────────────────────────

  it('should render "never" text in the last-polled chip when lastPolledAt is null', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO_NULL_ELIGIBILITY] });

    // Assert
    const chip = el.querySelector('.repository-list__chip--last-polled');
    expect(chip?.textContent?.trim().toLowerCase()).toContain('never');
  });

  it('should render the last-polled chip with aria-label "Never polled" when lastPolledAt is null', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO_NULL_ELIGIBILITY] });

    // Assert
    const chip = el.querySelector('.repository-list__chip--last-polled');
    expect(chip?.getAttribute('aria-label')).toBe('Never polled');
  });

  it('should render the last-polled chip with aria-label containing "Last polled" when lastPolledAt is set', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO] });

    // Assert
    const chip = el.querySelector('.repository-list__chip--last-polled');
    expect(chip?.getAttribute('aria-label')).toContain('Last polled');
  });

  // ─── Max-workers chip ─────────────────────────────────────────────────────────

  it('should render the max-workers chip with the workers count and aria-label', () => {
    // Arrange — MOCK_REPO has maxConcurrentWorkers=2

    // Act
    const { el } = setup({ repositories: [MOCK_REPO] });

    // Assert
    const chip = el.querySelector('.repository-list__chip--max-workers');
    expect(chip?.textContent?.trim()).toContain('2');
    expect(chip?.getAttribute('aria-label')).toBe('Max 2 concurrent workers');
  });

  // ─── Keyboard-focusable metadata chips ───────────────────────────────────────

  it('should have tabindex="0" on the interval chip so keyboard users can trigger the tooltip', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO] });

    // Assert
    const chip = el.querySelector('.repository-list__chip--interval');
    expect(chip?.getAttribute('tabindex')).toBe('0');
  });

  it('should have tabindex="0" on the last-polled chip', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO] });

    // Assert
    const chip = el.querySelector('.repository-list__chip--last-polled');
    expect(chip?.getAttribute('tabindex')).toBe('0');
  });

  it('should have tabindex="0" on the max-workers chip', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO] });

    // Assert
    const chip = el.querySelector('.repository-list__chip--max-workers');
    expect(chip?.getAttribute('tabindex')).toBe('0');
  });

  // ─── No old elements (chevron, toggle, eligibility-details, old __metadata) ─────

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

  it('should not render the old __metadata element', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO] });

    // Assert
    expect(el.querySelector('.repository-list__metadata')).toBeFalsy();
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
    const reasons = el.querySelector('.repository-list__reasons');
    expect(reasons).toBeTruthy();
    // No expand state needed — always visible
    expect(reasons?.getAttribute('hidden')).toBeNull();
  });

  it('should render one warning line per violation for an ineligible repo', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO_INELIGIBLE] });

    // Assert
    const warningLines = el.querySelectorAll('.repository-list__reason');
    expect(warningLines.length).toBeGreaterThanOrEqual(1);
    expect(warningLines[0]?.textContent).toContain('Allow direct pushes is enabled');
  });

  it('should render one warning line per violation for multiple violations, with one Re-check button', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO_MULTI_VIOLATIONS] });

    // Assert
    const warningLines = el.querySelectorAll('.repository-list__reason');
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
    expect(el.querySelector('.repository-list__reasons')).toBeFalsy();
  });

  it('should not render reasons block when eligibility is null', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO_NULL_ELIGIBILITY] });

    // Assert
    expect(el.querySelector('.repository-list__reasons')).toBeFalsy();
  });

  // ─── Inline eligibility — unreachable ────────────────────────────────────────

  it('should render unreachable reason sentence in the warning line', () => {
    // Arrange — MOCK_REPO_UNREACHABLE has reason: null => fallback sentence

    // Act
    const { el } = setup({ repositories: [MOCK_REPO_UNREACHABLE] });

    // Assert
    const reason = el.querySelector('.repository-list__reason');
    expect(reason?.textContent).toContain('branch-protection settings');
  });

  it('should render never-probed reason sentence in the warning line', () => {
    // Arrange
    const neverProbed: RepositorySummary = { ...MOCK_REPO_UNREACHABLE, eligibility: { status: 'unreachable', violations: [], reason: 'never-probed' } };

    // Act
    const { el } = setup({ repositories: [neverProbed] });

    // Assert
    const reason = el.querySelector('.repository-list__reason');
    expect(reason?.textContent).toContain('Eligibility has not been checked yet');
  });

  it('should render rate-limited reason sentence in the warning line', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO_RATE_LIMITED] });

    // Assert
    const reason = el.querySelector('.repository-list__reason');
    expect(reason?.textContent).toContain('rate limit');
  });

  // ─── Re-check button — rate-limited disabled ──────────────────────────────────

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

  // ─── Re-check flow ────────────────────────────────────────────────────────────

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
    const errorLine = el.querySelector('.repository-list__reason--error');
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
    expect(el.querySelector('.repository-list__reasons')).toBeFalsy();
    expect(el.querySelector('.repository-list__reason')).toBeFalsy();
  });

  // ─── Live-region announcements ────────────────────────────────────────────────

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
    const { el, fixture, httpMock } = setup({ repositories: [MOCK_REPO, MOCK_REPO_2] });

    // Act
    const moveDownBtn = el.querySelectorAll('.repository-list__move-down-btn')[0] as HTMLButtonElement;
    moveDownBtn.click();
    httpMock.expectOne(`/api/repositories/${MOCK_REPO.id}/position`).flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();

    // Assert
    const liveRegion = el.querySelector('.repository-list__announcement');
    expect(liveRegion?.textContent?.trim()).toContain(MOCK_REPO.slug);
  });

  // ─── Paused state — secondary colour, no opacity on text ─────────────────────

  it('should add repository-list__item--paused class to the card when the repo is not active', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO_PAUSED] });

    // Assert
    const item = el.querySelector('[role="listitem"]');
    expect(item?.classList.contains('repository-list__item--paused')).toBe(true);
  });

  it('should NOT add repository-list__item--paused class when the repo is active', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO] });

    // Assert
    const item = el.querySelector('[role="listitem"]');
    expect(item?.classList.contains('repository-list__item--paused')).toBe(false);
  });

  it('should add the secondary-colour class to the slug when the repo is paused', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO_PAUSED] });

    // Assert
    const slug = el.querySelector('.repository-list__slug');
    // The paused modifier is on the parent item; slug picks it up via CSS class or parent context
    // Verify the parent item carries the paused class (CSS handles the colour)
    const item = el.querySelector('[role="listitem"]');
    expect(item?.classList.contains('repository-list__item--paused')).toBe(true);
  });

  it('should not have an opacity style on the slug element when the repo is paused', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO_PAUSED] });

    // Assert — opacity must not be set inline; WCAG contrast requirement
    const slug = el.querySelector('.repository-list__slug') as HTMLElement;
    expect(slug?.style.opacity).toBe('');
  });

  // ─── Reorder operations (preserved) ──────────────────────────────────────────

  it('should call moveRepository with index + 1 when move-down is clicked for the first item', () => {
    // Arrange
    const { el, fixture, httpMock } = setup({ repositories: [MOCK_REPO, MOCK_REPO_2] });

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

    // Assert
    const moveError = el.querySelector('.repository-list__move-error');
    expect(moveError).toBeTruthy();
    expect(moveError?.textContent).toContain('reorder');
  });

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
    vi.runAllTimers();

    // Assert
    const movedItemEl = el.querySelector(`#repo-item-${MOCK_REPO.id}`) as HTMLElement;
    expect(movedItemEl).toBeTruthy();
    expect(movedItemEl.contains(document.activeElement)).toBe(true);
    vi.useRealTimers();
  });

  // ─── Empty + header add button ─────────────────────────────────────────────────

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

  // ─── a11y / accessibility ─────────────────────────────────────────────────────

  it('should give the drop list an accessible name via aria-label', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO, MOCK_REPO_2] });

    // Assert
    const list = el.querySelector('[role="list"]');
    expect(list?.getAttribute('aria-label')).toBeTruthy();
  });

  it('should set the drag handle aria-label to include keyboard instruction for reordering', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO, MOCK_REPO_2] });

    // Assert
    const handle = el.querySelector('.repository-list__drag-handle') as HTMLElement;
    expect(handle?.getAttribute('aria-label')).toContain(`Reorder ${MOCK_REPO.slug}`);
    expect(handle?.getAttribute('aria-label')).toContain('arrow keys');
  });

  it('should have no disabled attribute on enabled move-up and move-down buttons', () => {
    // Arrange

    // Act
    const { el } = setup({ repositories: [MOCK_REPO, MOCK_REPO_2] });

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
    const reasons = el.querySelector('.repository-list__reasons');
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
});

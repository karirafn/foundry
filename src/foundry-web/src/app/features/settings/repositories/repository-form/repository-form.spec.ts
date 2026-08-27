import { TestBed } from '@angular/core/testing';
import { RepositoryFormComponent } from './repository-form';
import { AccountSummary } from '../../accounts/account.model';
import { AvailableRepository, CreateRepositoryRequest, RepositorySummary, UpdateRepositoryRequest } from '../repository.model';

const MOCK_ACCOUNT: AccountSummary = {
  id: '00000000-0000-0000-0000-000000000001',
  name: 'My GitHub',
  providerType: 'GitHub',
  baseUrl: 'https://github.com',
  hasToken: true,
  namespaces: [],
};

const MOCK_ACCOUNT_2: AccountSummary = {
  id: '00000000-0000-0000-0000-000000000002',
  name: 'Work GitHub',
  providerType: 'GitHub',
  baseUrl: 'https://github.com',
  hasToken: true,
  namespaces: [],
};

const MOCK_REPOSITORY: RepositorySummary = {
  id: '00000000-0000-0000-0000-000000000010',
  slug: 'my-org/my-repo',
  accountId: '00000000-0000-0000-0000-000000000001',
  accountName: 'My GitHub',
  providerType: 'github',
  position: 0,
  pollIntervalSeconds: 300,
  isActive: true,
  lastPolledAt: '2026-06-15T10:00:00Z',
  eligibility: { status: 'eligible', violations: [], reason: null },
};

const MOCK_AVAILABLE: AvailableRepository[] = [
  { slug: 'my-org/my-repo', isPrivate: false, canPush: true, isMonitored: false },
  { slug: 'my-org/other-repo', isPrivate: true, canPush: true, isMonitored: false },
  { slug: 'my-org/third-repo', isPrivate: false, canPush: false, isMonitored: false },
];

const MOCK_AVAILABLE_MIXED: AvailableRepository[] = [
  { slug: 'my-org/writable-repo', isPrivate: false, canPush: true, isMonitored: false },
  { slug: 'my-org/readonly-repo', isPrivate: false, canPush: false, isMonitored: false },
];

const MOCK_AVAILABLE_ALL_READONLY: AvailableRepository[] = [
  { slug: 'my-org/readonly-a', isPrivate: false, canPush: false, isMonitored: false },
  { slug: 'my-org/readonly-b', isPrivate: false, canPush: false, isMonitored: false },
];

const MOCK_AVAILABLE_WITH_MONITORED: AvailableRepository[] = [
  { slug: 'my-org/monitored-repo', isPrivate: false, canPush: true, isMonitored: true },
  { slug: 'my-org/selectable-repo', isPrivate: false, canPush: true, isMonitored: false },
];

const MOCK_AVAILABLE_MONITORED_NO_PUSH: AvailableRepository[] = [
  { slug: 'my-org/monitored-nopush', isPrivate: false, canPush: false, isMonitored: true },
  { slug: 'my-org/selectable-repo', isPrivate: false, canPush: true, isMonitored: false },
];

function setup(overrides: {
  repository?: RepositorySummary | null;
  accounts?: AccountSummary[];
  availableRepositories?: AvailableRepository[];
  loadingAvailable?: boolean;
  loadAvailableError?: string | null;
  saving?: boolean;
  saveError?: string | null;
  hasClaims?: boolean;
} = {}) {
  const fixture = TestBed.createComponent(RepositoryFormComponent);
  fixture.componentRef.setInput('repository', overrides.repository ?? null);
  fixture.componentRef.setInput('accounts', overrides.accounts ?? []);
  fixture.componentRef.setInput('availableRepositories', overrides.availableRepositories ?? []);
  fixture.componentRef.setInput('loadingAvailable', overrides.loadingAvailable ?? false);
  fixture.componentRef.setInput('loadAvailableError', overrides.loadAvailableError ?? null);
  fixture.componentRef.setInput('saving', overrides.saving ?? false);
  fixture.componentRef.setInput('saveError', overrides.saveError ?? null);
  fixture.componentRef.setInput('hasClaims', overrides.hasClaims ?? false);
  fixture.detectChanges();
  return { fixture, component: fixture.componentInstance, el: fixture.nativeElement as HTMLElement };
}

describe('RepositoryFormComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [RepositoryFormComponent],
    }).compileComponents();
  });

  // Cycle 1: add mode renders heading "Add Repository"
  it('should render "Add Repository" heading in add mode', () => {
    // Arrange

    // Act
    const { el } = setup({ repository: null });

    // Assert
    const heading = el.querySelector('.repository-form__heading');
    expect(heading?.textContent?.trim()).toBe('Add Repository');
  });

  // Cycle 2: edit mode renders heading "Edit Repository"
  it('should render "Edit Repository" heading in edit mode', () => {
    // Arrange

    // Act
    const { el } = setup({ repository: MOCK_REPOSITORY });

    // Assert
    const heading = el.querySelector('.repository-form__heading');
    expect(heading?.textContent?.trim()).toBe('Edit Repository');
  });

  // Cycle 3: cancel link renders and emits cancel
  it('should render a cancel link', () => {
    // Arrange

    // Act
    const { el } = setup();

    // Assert
    const cancelLink = el.querySelector('.repository-form__cancel-link');
    expect(cancelLink).toBeTruthy();
    expect(cancelLink?.textContent).toContain('Cancel');
  });

  it('should emit cancel when cancel link is clicked', () => {
    // Arrange
    const { el, component } = setup();
    let emitted = false;
    component.cancel.subscribe(() => { emitted = true; });

    // Act
    const cancelLink = el.querySelector('.repository-form__cancel-link') as HTMLElement;
    cancelLink.click();

    // Assert
    expect(emitted).toBe(true);
  });

  // Cycle 4: add mode shows account dropdown
  it('should render account dropdown in add mode', () => {
    // Arrange

    // Act
    const { el } = setup({
      repository: null,
      accounts: [MOCK_ACCOUNT, MOCK_ACCOUNT_2],
    });

    // Assert
    const label = el.querySelector('label[for="repository-account"]');
    expect(label).toBeTruthy();
    const select = el.querySelector('#repository-account') as HTMLSelectElement;
    expect(select).toBeTruthy();
  });

  it('should populate account dropdown options from accounts input', () => {
    // Arrange

    // Act
    const { el } = setup({
      repository: null,
      accounts: [MOCK_ACCOUNT, MOCK_ACCOUNT_2],
    });

    // Assert
    const select = el.querySelector('#repository-account') as HTMLSelectElement;
    const options = select.querySelectorAll('option:not([disabled])');
    expect(options.length).toBe(2);
    expect(options[0].textContent?.trim()).toBe('My GitHub (github.com)');
    expect(options[1].textContent?.trim()).toBe('Work GitHub (github.com)');
  });

  it('should not render account dropdown in edit mode', () => {
    // Arrange

    // Act
    const { el } = setup({ repository: MOCK_REPOSITORY });

    // Assert
    const select = el.querySelector('#repository-account');
    expect(select).toBeNull();
  });

  // Cycle 5: selecting account emits accountSelected
  it('should emit accountSelected when user picks an account', () => {
    // Arrange
    const { el, component, fixture } = setup({
      repository: null,
      accounts: [MOCK_ACCOUNT, MOCK_ACCOUNT_2],
    });
    let emitted: string | undefined;
    component.accountSelected.subscribe((id: string) => { emitted = id; });

    // Act
    const select = el.querySelector('#repository-account') as HTMLSelectElement;
    select.value = MOCK_ACCOUNT.id;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    // Assert
    expect(emitted).toBe(MOCK_ACCOUNT.id);
  });

  // Cycle 6: repository picker appears after account is selected
  it('should not render repository picker before account is selected', () => {
    // Arrange

    // Act
    const { el } = setup({
      repository: null,
      accounts: [MOCK_ACCOUNT],
    });

    // Assert
    const combobox = el.querySelector('[role="combobox"]');
    expect(combobox).toBeNull();
  });

  it('should render repository picker after account is selected', () => {
    // Arrange
    const { el, fixture } = setup({
      repository: null,
      accounts: [MOCK_ACCOUNT],
      availableRepositories: MOCK_AVAILABLE,
    });

    // Act — select an account
    const select = el.querySelector('#repository-account') as HTMLSelectElement;
    select.value = MOCK_ACCOUNT.id;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    // Assert
    const combobox = el.querySelector('[role="combobox"]');
    expect(combobox).toBeTruthy();
  });

  // Cycle 7: repository picker shows loading state
  it('should show loading state in picker when loadingAvailable is true', () => {
    // Arrange
    const { el, fixture } = setup({
      repository: null,
      accounts: [MOCK_ACCOUNT],
      loadingAvailable: true,
    });

    // Act — select an account to show the picker
    const select = el.querySelector('#repository-account') as HTMLSelectElement;
    select.value = MOCK_ACCOUNT.id;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    // Assert
    const loading = el.querySelector('.repository-form__picker-loading');
    expect(loading).toBeTruthy();
    expect(loading?.textContent).toContain('Loading repositories');
    const spinner = el.querySelector('fd-spinner');
    expect(spinner).toBeTruthy();
  });

  it('should not show fd-spinner when loadingAvailable is false', () => {
    // Arrange
    const { el, fixture } = setup({
      repository: null,
      accounts: [MOCK_ACCOUNT],
      availableRepositories: MOCK_AVAILABLE,
      loadingAvailable: false,
    });

    // Act — select an account to show the picker
    const select = el.querySelector('#repository-account') as HTMLSelectElement;
    select.value = MOCK_ACCOUNT.id;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    // Assert
    const spinner = el.querySelector('fd-spinner');
    expect(spinner).toBeNull();
  });

  // Cycle 8: repository picker shows error state
  it('should show error state in picker when loadAvailableError is set', () => {
    // Arrange
    const { el, fixture } = setup({
      repository: null,
      accounts: [MOCK_ACCOUNT],
      loadAvailableError: 'Failed to load repositories',
    });

    // Act — select an account
    const select = el.querySelector('#repository-account') as HTMLSelectElement;
    select.value = MOCK_ACCOUNT.id;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    // Assert
    const error = el.querySelector('.repository-form__picker-error');
    expect(error).toBeTruthy();
    expect(error?.textContent).toContain('Failed to load repositories');
    const retryBtn = el.querySelector('.repository-form__picker-retry-btn');
    expect(retryBtn).toBeTruthy();
    expect(retryBtn?.textContent).toContain('Retry');
  });

  // Cycle 9: repository picker filters by search text
  it('should filter available repositories by substring match on slug', () => {
    // Arrange
    const { el, fixture } = setup({
      repository: null,
      accounts: [MOCK_ACCOUNT],
      availableRepositories: MOCK_AVAILABLE,
    });

    // Act — select account then open picker
    const select = el.querySelector('#repository-account') as HTMLSelectElement;
    select.value = MOCK_ACCOUNT.id;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const combobox = el.querySelector('[role="combobox"]') as HTMLInputElement;
    combobox.click();
    fixture.detectChanges();

    // Type to filter
    combobox.value = 'other';
    combobox.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Assert
    const options = el.querySelectorAll('[role="option"]');
    expect(options.length).toBe(1);
    expect(options[0].textContent?.trim()).toContain('other-repo');
  });

  // Cycle 10: selecting a repository option sets the slug and closes dropdown
  it('should set slug and close dropdown when a repository option is selected', () => {
    // Arrange
    const { el, fixture } = setup({
      repository: null,
      accounts: [MOCK_ACCOUNT],
      availableRepositories: MOCK_AVAILABLE,
    });

    // Act — select account then open picker and click option
    const select = el.querySelector('#repository-account') as HTMLSelectElement;
    select.value = MOCK_ACCOUNT.id;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const combobox = el.querySelector('[role="combobox"]') as HTMLInputElement;
    combobox.click();
    fixture.detectChanges();

    const option = el.querySelector('[role="option"]') as HTMLElement;
    option.click();
    fixture.detectChanges();

    // Assert
    expect(combobox.value).toBe('my-org/my-repo');
    const listbox = el.querySelector('[role="listbox"]') as HTMLElement;
    expect(listbox).toBeTruthy();
    expect(listbox.hidden).toBe(true);
  });

  // Cycle 11: no matching repos shows empty message
  it('should show empty state when no repositories match filter', () => {
    // Arrange — hasClaims: true so filter-empty message is shown (not the no-claims status)
    const { el, fixture } = setup({
      repository: null,
      accounts: [MOCK_ACCOUNT],
      availableRepositories: MOCK_AVAILABLE,
      hasClaims: true,
    });

    // Act — select account then open picker and type non-matching text
    const select = el.querySelector('#repository-account') as HTMLSelectElement;
    select.value = MOCK_ACCOUNT.id;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const combobox = el.querySelector('[role="combobox"]') as HTMLInputElement;
    combobox.click();
    fixture.detectChanges();

    combobox.value = 'zzz-no-match';
    combobox.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Assert
    const empty = el.querySelector('.repository-form__picker-empty');
    expect(empty).toBeTruthy();
    expect(empty?.textContent).toContain('No matching repositories');
  });

  // Cycle 12: poll interval field
  it('should render poll interval field with label and number input', () => {
    // Arrange

    // Act
    const { el } = setup();

    // Assert
    const label = el.querySelector('label[for="repository-poll-interval"]');
    expect(label).toBeTruthy();
    const input = el.querySelector('#repository-poll-interval') as HTMLInputElement;
    expect(input).toBeTruthy();
    expect(input.type).toBe('number');
    expect(input.min).toBe('1');
    expect(input.max).toBe('1440');
  });

  it('should default poll interval to 5 in add mode', () => {
    // Arrange

    // Act
    const { el } = setup({ repository: null });

    // Assert
    const input = el.querySelector('#repository-poll-interval') as HTMLInputElement;
    expect(input.value).toBe('5');
  });

  // Cycle 13: edit mode shows read-only slug and account
  it('should show read-only repository slug in edit mode', () => {
    // Arrange

    // Act
    const { el } = setup({ repository: MOCK_REPOSITORY });

    // Assert
    const slug = el.querySelector('.repository-form__read-only-slug');
    expect(slug).toBeTruthy();
    expect(slug?.textContent?.trim()).toBe('my-org/my-repo');
  });

  it('should show read-only account name in edit mode', () => {
    // Arrange

    // Act
    const { el } = setup({ repository: MOCK_REPOSITORY });

    // Assert
    const account = el.querySelector('.repository-form__read-only-account');
    expect(account).toBeTruthy();
    expect(account?.textContent?.trim()).toBe('My GitHub');
  });

  // Cycle 14: edit mode pre-populates poll interval
  it('should pre-populate poll interval from repository in edit mode', () => {
    // Arrange

    // Act
    const { el } = setup({ repository: MOCK_REPOSITORY });

    // Assert — MOCK_REPOSITORY has pollIntervalSeconds: 300 = 5 minutes
    const input = el.querySelector('#repository-poll-interval') as HTMLInputElement;
    expect(input.value).toBe('5');
  });

  it('should show empty poll interval when repository has null pollIntervalSeconds', () => {
    // Arrange

    // Act
    const { el } = setup({
      repository: { ...MOCK_REPOSITORY, pollIntervalSeconds: null },
    });

    // Assert
    const input = el.querySelector('#repository-poll-interval') as HTMLInputElement;
    expect(input.value).toBe('');
  });

  // Cycle 15: edit mode active toggle
  it('should render active toggle in edit mode', () => {
    // Arrange

    // Act
    const { el } = setup({ repository: MOCK_REPOSITORY });

    // Assert
    const toggle = el.querySelector('#repository-active') as HTMLInputElement;
    expect(toggle).toBeTruthy();
    expect(toggle.type).toBe('checkbox');
  });

  it('should pre-populate active toggle from repository in edit mode', () => {
    // Arrange

    // Act
    const { el } = setup({ repository: MOCK_REPOSITORY });

    // Assert — MOCK_REPOSITORY.isActive = true
    const toggle = el.querySelector('#repository-active') as HTMLInputElement;
    expect(toggle.checked).toBe(true);
  });

  it('should not render active toggle in add mode', () => {
    // Arrange

    // Act
    const { el } = setup({ repository: null });

    // Assert
    const toggle = el.querySelector('#repository-active');
    expect(toggle).toBeNull();
  });

  // Cycle 16: save error alert
  it('should show save error when saveError is set', () => {
    // Arrange

    // Act
    const { el } = setup({ saveError: 'Something went wrong' });

    // Assert
    const errorEl = el.querySelector('.repository-form__save-error');
    expect(errorEl).toBeTruthy();
    expect(errorEl?.getAttribute('role')).toBe('alert');
    expect(errorEl?.textContent).toContain('Something went wrong');
  });

  it('should not show save error content when saveError is null', () => {
    // Arrange

    // Act
    const { el } = setup({ saveError: null });

    // Assert — wrapper always present, content is empty
    const errorEl = el.querySelector('.repository-form__save-error');
    expect(errorEl?.textContent?.trim()).toBeFalsy();
  });

  // Cycle 17: save button disabled states
  it('should render the save button', () => {
    // Arrange

    // Act
    const { el } = setup();

    // Assert
    const btn = el.querySelector('.repository-form__save-btn');
    expect(btn).toBeTruthy();
    expect(btn?.textContent?.trim()).toBe('Save');
  });

  it('should disable save button in add mode when no account selected', () => {
    // Arrange

    // Act
    const { el } = setup({ repository: null, accounts: [MOCK_ACCOUNT] });

    // Assert
    const btn = el.querySelector('.repository-form__save-btn') as HTMLButtonElement;
    expect(btn.disabled).toBe(true);
  });

  it('should disable save button in add mode when account selected but no repo', () => {
    // Arrange
    const { el, fixture } = setup({
      repository: null,
      accounts: [MOCK_ACCOUNT],
      availableRepositories: MOCK_AVAILABLE,
    });

    // Act — select account but don't pick a repo
    const select = el.querySelector('#repository-account') as HTMLSelectElement;
    select.value = MOCK_ACCOUNT.id;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    // Assert
    const btn = el.querySelector('.repository-form__save-btn') as HTMLButtonElement;
    expect(btn.disabled).toBe(true);
  });

  it('should disable save button when saving is true', () => {
    // Arrange

    // Act
    const { el } = setup({ repository: MOCK_REPOSITORY, saving: true });

    // Assert
    const btn = el.querySelector('.repository-form__save-btn') as HTMLButtonElement;
    expect(btn.disabled).toBe(true);
  });

  it('should enable save button in edit mode when no saving', () => {
    // Arrange

    // Act
    const { el } = setup({ repository: MOCK_REPOSITORY, saving: false });

    // Assert
    const btn = el.querySelector('.repository-form__save-btn') as HTMLButtonElement;
    expect(btn.disabled).toBe(false);
  });

  // Cycle 18: save emits CreateRepositoryRequest in add mode
  it('should emit CreateRepositoryRequest on save in add mode', () => {
    // Arrange
    const { el, component, fixture } = setup({
      repository: null,
      accounts: [MOCK_ACCOUNT],
      availableRepositories: MOCK_AVAILABLE,
    });
    let emitted: CreateRepositoryRequest | UpdateRepositoryRequest | undefined;
    component.save.subscribe((v: CreateRepositoryRequest | UpdateRepositoryRequest) => { emitted = v; });

    // Select account
    const select = el.querySelector('#repository-account') as HTMLSelectElement;
    select.value = MOCK_ACCOUNT.id;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    // Open picker and pick a repo
    const combobox = el.querySelector('[role="combobox"]') as HTMLInputElement;
    combobox.click();
    fixture.detectChanges();
    const option = el.querySelector('[role="option"]') as HTMLElement;
    option.click();
    fixture.detectChanges();

    // Act
    const saveBtn = el.querySelector('.repository-form__save-btn') as HTMLButtonElement;
    saveBtn.click();

    // Assert
    expect(emitted).toEqual({
      slug: 'my-org/my-repo',
      pollIntervalSeconds: 300, // 5 min * 60 sec
    });
  });

  // Cycle 19: save emits UpdateRepositoryRequest in edit mode
  it('should emit UpdateRepositoryRequest on save in edit mode', () => {
    // Arrange
    const { el, component, fixture } = setup({ repository: MOCK_REPOSITORY });
    let emitted: CreateRepositoryRequest | UpdateRepositoryRequest | undefined;
    component.save.subscribe((v: CreateRepositoryRequest | UpdateRepositoryRequest) => { emitted = v; });

    // Change poll interval
    const pollInput = el.querySelector('#repository-poll-interval') as HTMLInputElement;
    pollInput.value = '10';
    pollInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Act
    const saveBtn = el.querySelector('.repository-form__save-btn') as HTMLButtonElement;
    saveBtn.click();

    // Assert
    expect(emitted).toEqual({
      pollIntervalSeconds: 600, // 10 min * 60 sec
      isActive: true,
    });
  });

  it('should include isActive in UpdateRepositoryRequest based on toggle state', () => {
    // Arrange
    const { el, component, fixture } = setup({
      repository: { ...MOCK_REPOSITORY, isActive: true },
    });
    let emitted: CreateRepositoryRequest | UpdateRepositoryRequest | undefined;
    component.save.subscribe((v: CreateRepositoryRequest | UpdateRepositoryRequest) => { emitted = v; });

    // Toggle active off
    const toggle = el.querySelector('#repository-active') as HTMLInputElement;
    toggle.checked = false;
    toggle.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    // Act
    const saveBtn = el.querySelector('.repository-form__save-btn') as HTMLButtonElement;
    saveBtn.click();

    // Assert
    expect((emitted as UpdateRepositoryRequest).isActive).toBe(false);
  });

  // Cycle 20: keyboard navigation — ArrowDown opens dropdown
  it('should open dropdown on ArrowDown key in combobox', () => {
    // Arrange
    const { el, fixture } = setup({
      repository: null,
      accounts: [MOCK_ACCOUNT],
      availableRepositories: MOCK_AVAILABLE,
    });

    // Select account
    const select = el.querySelector('#repository-account') as HTMLSelectElement;
    select.value = MOCK_ACCOUNT.id;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    // Assert dropdown not open initially
    const listbox = el.querySelector('[role="listbox"]') as HTMLElement;
    expect(listbox).toBeTruthy();
    expect(listbox.hidden).toBe(true);

    // Act — press ArrowDown
    const combobox = el.querySelector('[role="combobox"]') as HTMLInputElement;
    combobox.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }));
    fixture.detectChanges();

    // Assert
    expect(listbox.hidden).toBe(false);
  });

  // Cycle 21: keyboard navigation — Escape closes dropdown
  it('should close dropdown on Escape key', () => {
    // Arrange
    const { el, fixture } = setup({
      repository: null,
      accounts: [MOCK_ACCOUNT],
      availableRepositories: MOCK_AVAILABLE,
    });

    // Select account and open dropdown
    const select = el.querySelector('#repository-account') as HTMLSelectElement;
    select.value = MOCK_ACCOUNT.id;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const combobox = el.querySelector('[role="combobox"]') as HTMLInputElement;
    combobox.click();
    fixture.detectChanges();
    const listbox = el.querySelector('[role="listbox"]') as HTMLElement;
    expect(listbox.hidden).toBe(false);

    // Act — press Escape
    combobox.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();

    // Assert
    expect(listbox.hidden).toBe(true);
  });

  // Cycle 22: keyboard navigation — Enter selects highlighted option
  it('should select first option on Enter key when dropdown is open', () => {
    // Arrange
    const { el, fixture } = setup({
      repository: null,
      accounts: [MOCK_ACCOUNT],
      availableRepositories: MOCK_AVAILABLE,
    });

    // Select account and open dropdown
    const select = el.querySelector('#repository-account') as HTMLSelectElement;
    select.value = MOCK_ACCOUNT.id;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const combobox = el.querySelector('[role="combobox"]') as HTMLInputElement;
    // Open with ArrowDown (also moves to first option)
    combobox.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }));
    fixture.detectChanges();

    // Act — press Enter to select
    combobox.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    fixture.detectChanges();

    // Assert
    expect(combobox.value).toBe('my-org/my-repo');
    const listbox = el.querySelector('[role="listbox"]') as HTMLElement;
    expect(listbox.hidden).toBe(true);
  });

  // Cycle 23: disable save when poll interval is invalid
  it('should disable save button when poll interval is out of valid range in edit mode', () => {
    // Arrange
    const { el, fixture } = setup({ repository: MOCK_REPOSITORY });

    // Act — set invalid interval (0)
    const pollInput = el.querySelector('#repository-poll-interval') as HTMLInputElement;
    pollInput.value = '0';
    pollInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Assert
    const btn = el.querySelector('.repository-form__save-btn') as HTMLButtonElement;
    expect(btn.disabled).toBe(true);
  });

  // Cycle 24: null pollIntervalSeconds emits correctly
  it('should emit null pollIntervalSeconds when poll interval input is empty in add mode', () => {
    // Arrange
    const { el, component, fixture } = setup({
      repository: null,
      accounts: [MOCK_ACCOUNT],
      availableRepositories: MOCK_AVAILABLE,
    });
    let emitted: CreateRepositoryRequest | UpdateRepositoryRequest | undefined;
    component.save.subscribe((v: CreateRepositoryRequest | UpdateRepositoryRequest) => { emitted = v; });

    // Select account and pick repo
    const select = el.querySelector('#repository-account') as HTMLSelectElement;
    select.value = MOCK_ACCOUNT.id;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const combobox = el.querySelector('[role="combobox"]') as HTMLInputElement;
    combobox.click();
    fixture.detectChanges();
    const option = el.querySelector('[role="option"]') as HTMLElement;
    option.click();
    fixture.detectChanges();

    // Clear poll interval
    const pollInput = el.querySelector('#repository-poll-interval') as HTMLInputElement;
    pollInput.value = '';
    pollInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Act
    const saveBtn = el.querySelector('.repository-form__save-btn') as HTMLButtonElement;
    saveBtn.click();

    // Assert
    expect((emitted as CreateRepositoryRequest).pollIntervalSeconds).toBeNull();
  });

  // Cycle 25: writable option is selectable
  it('should select a writable repository when clicked', () => {
    // Arrange
    const { el, fixture } = setup({
      repository: null,
      accounts: [MOCK_ACCOUNT],
      availableRepositories: MOCK_AVAILABLE_MIXED,
    });

    const select = el.querySelector('#repository-account') as HTMLSelectElement;
    select.value = MOCK_ACCOUNT.id;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const combobox = el.querySelector('[role="combobox"]') as HTMLInputElement;
    combobox.click();
    fixture.detectChanges();

    // Act — click the writable option (index 0)
    const options = el.querySelectorAll('[role="option"]') as NodeListOf<HTMLElement>;
    options[0].click();
    fixture.detectChanges();

    // Assert
    expect(combobox.value).toBe('my-org/writable-repo');
    const listbox = el.querySelector('[role="listbox"]') as HTMLElement;
    expect(listbox.hidden).toBe(true);
  });

  // Cycle 26: non-writable option is not selectable and shows aria-disabled + reason
  it('should not select a non-writable repository when clicked', () => {
    // Arrange
    const { el, fixture } = setup({
      repository: null,
      accounts: [MOCK_ACCOUNT],
      availableRepositories: MOCK_AVAILABLE_MIXED,
    });

    const select = el.querySelector('#repository-account') as HTMLSelectElement;
    select.value = MOCK_ACCOUNT.id;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const combobox = el.querySelector('[role="combobox"]') as HTMLInputElement;
    combobox.click();
    fixture.detectChanges();

    // Act — click the non-writable option (index 1)
    const options = el.querySelectorAll('[role="option"]') as NodeListOf<HTMLElement>;
    options[1].click();
    fixture.detectChanges();

    // Assert — slug unchanged, picker stays open
    expect(combobox.value).toBe('');
    const listbox = el.querySelector('[role="listbox"]') as HTMLElement;
    expect(listbox.hidden).toBe(false);
  });

  it('should render aria-disabled="true" on a non-writable option', () => {
    // Arrange
    const { el, fixture } = setup({
      repository: null,
      accounts: [MOCK_ACCOUNT],
      availableRepositories: MOCK_AVAILABLE_MIXED,
    });

    const select = el.querySelector('#repository-account') as HTMLSelectElement;
    select.value = MOCK_ACCOUNT.id;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const combobox = el.querySelector('[role="combobox"]') as HTMLInputElement;
    combobox.click();
    fixture.detectChanges();

    // Assert
    const options = el.querySelectorAll('[role="option"]') as NodeListOf<HTMLElement>;
    expect(options[0].getAttribute('aria-disabled')).toBeNull();
    expect(options[1].getAttribute('aria-disabled')).toBe('true');
  });

  it('should render the no-write-access reason text inside a non-writable option', () => {
    // Arrange
    const { el, fixture } = setup({
      repository: null,
      accounts: [MOCK_ACCOUNT],
      availableRepositories: MOCK_AVAILABLE_MIXED,
    });

    const select = el.querySelector('#repository-account') as HTMLSelectElement;
    select.value = MOCK_ACCOUNT.id;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const combobox = el.querySelector('[role="combobox"]') as HTMLInputElement;
    combobox.click();
    fixture.detectChanges();

    // Assert
    const options = el.querySelectorAll('[role="option"]') as NodeListOf<HTMLElement>;
    const reasonEl = options[1].querySelector('.repository-form__picker-option-reason');
    expect(reasonEl).toBeTruthy();
    expect(reasonEl?.textContent).toContain('no write access');
  });

  // Cycle 27: keyboard ArrowDown skips non-selectable options
  it('should skip disabled option and stay on writable when ArrowDown has no next selectable', () => {
    // Arrange — list: [writable@0, readonly@1]; second ArrowDown finds no next selectable
    const { el, fixture } = setup({
      repository: null,
      accounts: [MOCK_ACCOUNT],
      availableRepositories: MOCK_AVAILABLE_MIXED,
    });

    const select = el.querySelector('#repository-account') as HTMLSelectElement;
    select.value = MOCK_ACCOUNT.id;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const combobox = el.querySelector('[role="combobox"]') as HTMLInputElement;
    // First ArrowDown — lands on writable@0
    combobox.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }));
    fixture.detectChanges();
    expect(combobox.getAttribute('aria-activedescendant')).toBe('repo-option-0');

    // Second ArrowDown — no next selectable; active stays at writable@0
    combobox.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }));
    fixture.detectChanges();

    const activeOption = el.querySelector('[role="option"].repository-form__picker-option--active') as HTMLElement;
    expect(activeOption).toBeTruthy();
    expect(activeOption.getAttribute('aria-disabled')).toBeNull();
    expect(combobox.getAttribute('aria-activedescendant')).toBe('repo-option-0');
  });

  it('should skip a monitored row when navigating down with ArrowDown', () => {
    // Arrange — list: [monitored@0, selectable@1]; ArrowDown skips monitored to land on selectable
    const { el, fixture } = setup({
      repository: null,
      accounts: [MOCK_ACCOUNT],
      availableRepositories: MOCK_AVAILABLE_WITH_MONITORED,
      hasClaims: true,
    });

    const select = el.querySelector('#repository-account') as HTMLSelectElement;
    select.value = MOCK_ACCOUNT.id;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const combobox = el.querySelector('[role="combobox"]') as HTMLInputElement;
    // First ArrowDown — skips monitored@0, lands on selectable@1
    combobox.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }));
    fixture.detectChanges();

    // Assert — active is the selectable option (index 1), not the monitored one (index 0)
    expect(combobox.getAttribute('aria-activedescendant')).toBe('repo-option-1');
    const activeOption = el.querySelector('[role="option"].repository-form__picker-option--active') as HTMLElement;
    expect(activeOption).toBeTruthy();
    expect(activeOption.getAttribute('aria-disabled')).toBeNull();
  });

  it('should skip readonly option when navigating with ArrowDown and land on next selectable', () => {
    // Arrange — list: [readonly@0, writable@1]; ArrowDown from -1 skips readonly, lands on writable@1
    const repos: AvailableRepository[] = [
      { slug: 'my-org/readonly-repo', isPrivate: false, canPush: false, isMonitored: false },
      { slug: 'my-org/writable-repo', isPrivate: false, canPush: true, isMonitored: false },
    ];
    const { el, fixture } = setup({
      repository: null,
      accounts: [MOCK_ACCOUNT],
      availableRepositories: repos,
      hasClaims: true,
    });

    const select = el.querySelector('#repository-account') as HTMLSelectElement;
    select.value = MOCK_ACCOUNT.id;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const combobox = el.querySelector('[role="combobox"]') as HTMLInputElement;
    // First ArrowDown — skips readonly@0, lands on writable@1
    combobox.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }));
    fixture.detectChanges();

    expect(combobox.getAttribute('aria-activedescendant')).toBe('repo-option-1');
    const activeOption = el.querySelector('[role="option"].repository-form__picker-option--active') as HTMLElement;
    expect(activeOption).toBeTruthy();
    expect(activeOption.getAttribute('aria-disabled')).toBeNull();
  });

  it('should select writable option when Enter is pressed on a writable active option', () => {
    // Arrange — list: [readonly@0, writable@1]; arrow skips to writable, Enter selects
    const repos: AvailableRepository[] = [
      { slug: 'my-org/readonly-repo', isPrivate: false, canPush: false, isMonitored: false },
      { slug: 'my-org/writable-repo', isPrivate: false, canPush: true, isMonitored: false },
    ];
    const { el, fixture } = setup({
      repository: null,
      accounts: [MOCK_ACCOUNT],
      availableRepositories: repos,
      hasClaims: true,
    });

    const select = el.querySelector('#repository-account') as HTMLSelectElement;
    select.value = MOCK_ACCOUNT.id;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const combobox = el.querySelector('[role="combobox"]') as HTMLInputElement;
    // ArrowDown skips readonly@0, lands on writable@1
    combobox.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }));
    fixture.detectChanges();
    expect(combobox.getAttribute('aria-activedescendant')).toBe('repo-option-1');

    // Enter should select the writable option
    combobox.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    fixture.detectChanges();

    expect(combobox.value).toBe('my-org/writable-repo');
    const listbox = el.querySelector('[role="listbox"]') as HTMLElement;
    expect(listbox.hidden).toBe(true);
  });

  // Cycle 28: Enter key selects active option when selectable; no-op when no active
  it('should be a no-op when Enter is pressed with no active option', () => {
    // Arrange — no ArrowDown pressed; active index is -1
    const repos: AvailableRepository[] = [
      { slug: 'my-org/writable-repo', isPrivate: false, canPush: true, isMonitored: false },
    ];
    const { el, fixture } = setup({
      repository: null,
      accounts: [MOCK_ACCOUNT],
      availableRepositories: repos,
      hasClaims: true,
    });

    const select = el.querySelector('#repository-account') as HTMLSelectElement;
    select.value = MOCK_ACCOUNT.id;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const combobox = el.querySelector('[role="combobox"]') as HTMLInputElement;
    combobox.click();
    fixture.detectChanges();

    // Enter with no active option — should be a no-op
    combobox.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    fixture.detectChanges();

    expect(combobox.value).toBe('');
    const listbox = el.querySelector('[role="listbox"]') as HTMLElement;
    expect(listbox.hidden).toBe(false);
  });

  // Cycle 29: all non-writable — arrows find no selectable option, active stays at -1
  it('should render all entries (not empty state) when all repos are non-writable', () => {
    // Arrange
    const { el, fixture } = setup({
      repository: null,
      accounts: [MOCK_ACCOUNT],
      availableRepositories: MOCK_AVAILABLE_ALL_READONLY,
      hasClaims: true,
    });

    const select = el.querySelector('#repository-account') as HTMLSelectElement;
    select.value = MOCK_ACCOUNT.id;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const combobox = el.querySelector('[role="combobox"]') as HTMLInputElement;
    combobox.click();
    fixture.detectChanges();

    // Assert — all entries shown, no filter-empty state
    const options = el.querySelectorAll('[role="option"]') as NodeListOf<HTMLElement>;
    expect(options.length).toBe(2);
    const emptyState = el.querySelector('.repository-form__picker-empty');
    expect(emptyState).toBeNull();
  });

  it('should not move active descendant when ArrowDown is pressed on an all-disabled list', () => {
    // Arrange
    const { el, fixture } = setup({
      repository: null,
      accounts: [MOCK_ACCOUNT],
      availableRepositories: MOCK_AVAILABLE_ALL_READONLY,
      hasClaims: true,
    });

    const select = el.querySelector('#repository-account') as HTMLSelectElement;
    select.value = MOCK_ACCOUNT.id;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const combobox = el.querySelector('[role="combobox"]') as HTMLInputElement;
    combobox.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }));
    fixture.detectChanges();

    // No selectable option — activeOptionIndex stays -1, no active class on any option
    const activeOption = el.querySelector('[role="option"].repository-form__picker-option--active');
    expect(activeOption).toBeNull();
    expect(combobox.getAttribute('aria-activedescendant')).toBeNull();
  });

  it('should not select any option when Enter is pressed with no active option on all-disabled list', () => {
    // Arrange
    const { el, fixture } = setup({
      repository: null,
      accounts: [MOCK_ACCOUNT],
      availableRepositories: MOCK_AVAILABLE_ALL_READONLY,
      hasClaims: true,
    });

    const select = el.querySelector('#repository-account') as HTMLSelectElement;
    select.value = MOCK_ACCOUNT.id;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const combobox = el.querySelector('[role="combobox"]') as HTMLInputElement;
    combobox.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }));
    fixture.detectChanges();
    combobox.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    fixture.detectChanges();

    // Combobox value unchanged, listbox stays open (active index is -1, Enter no-ops)
    expect(combobox.value).toBe('');
    const listbox = el.querySelector('[role="listbox"]') as HTMLElement;
    expect(listbox.hidden).toBe(false);
  });

  // Cycle 30: add-mode options disambiguate accounts sharing name and host by namespaces
  it('should render distinct option text for accounts with the same name and host but disjoint namespaces', () => {
    // Arrange — two accounts with the same name and baseUrl but different namespaces
    const accountA: AccountSummary = {
      id: '00000000-0000-0000-0000-000000000003',
      name: 'Shared Name',
      providerType: 'GitHub',
      baseUrl: 'https://github.com',
      hasToken: true,
      namespaces: ['team-alpha'],
    };
    const accountB: AccountSummary = {
      id: '00000000-0000-0000-0000-000000000004',
      name: 'Shared Name',
      providerType: 'GitHub',
      baseUrl: 'https://github.com',
      hasToken: true,
      namespaces: ['team-beta'],
    };

    // Act
    const { el } = setup({ repository: null, accounts: [accountA, accountB] });

    // Assert — option text includes namespaces, making them distinct
    const select = el.querySelector('#repository-account') as HTMLSelectElement;
    const options = select.querySelectorAll('option:not([disabled])');
    expect(options.length).toBe(2);
    const textA = options[0].textContent?.trim();
    const textB = options[1].textContent?.trim();
    expect(textA).toBe('Shared Name — team-alpha (github.com)');
    expect(textB).toBe('Shared Name — team-beta (github.com)');
  });

  // Cycle 31: edit mode still shows plain accountName (not the util output)
  it('should show plain accountName in read-only account field in edit mode', () => {
    // Arrange
    const { el } = setup({ repository: MOCK_REPOSITORY });

    // Act
    const accountField = el.querySelector('.repository-form__read-only-account');

    // Assert — plain name from RepositorySummary.accountName, not the util label
    expect(accountField?.textContent?.trim()).toBe('My GitHub');
  });

  // Cycle 32: monitored row — gutter check rendered, sr-only "already monitored" text, non-selectable
  it('should render the gutter check mark for a monitored repository option', () => {
    // Arrange
    const { el, fixture } = setup({
      repository: null,
      accounts: [MOCK_ACCOUNT],
      availableRepositories: MOCK_AVAILABLE_WITH_MONITORED,
      hasClaims: true,
    });

    const select = el.querySelector('#repository-account') as HTMLSelectElement;
    select.value = MOCK_ACCOUNT.id;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const combobox = el.querySelector('[role="combobox"]') as HTMLInputElement;
    combobox.click();
    fixture.detectChanges();

    // Act
    const options = el.querySelectorAll('[role="option"]') as NodeListOf<HTMLElement>;

    // Assert — first option is monitored, has a gutter check element
    const gutter = options[0].querySelector('.repository-form__picker-option-gutter');
    expect(gutter).toBeTruthy();
    const check = options[0].querySelector('.repository-form__picker-check');
    expect(check).toBeTruthy();
    // Second option (selectable) has gutter but no check
    const gutterSelectable = options[1].querySelector('.repository-form__picker-option-gutter');
    expect(gutterSelectable).toBeTruthy();
    const checkSelectable = options[1].querySelector('.repository-form__picker-check');
    expect(checkSelectable).toBeNull();
  });

  it('should render sr-only "already monitored" text on a monitored repository option without a leading comma', () => {
    // Arrange
    const { el, fixture } = setup({
      repository: null,
      accounts: [MOCK_ACCOUNT],
      availableRepositories: MOCK_AVAILABLE_WITH_MONITORED,
      hasClaims: true,
    });

    const select = el.querySelector('#repository-account') as HTMLSelectElement;
    select.value = MOCK_ACCOUNT.id;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const combobox = el.querySelector('[role="combobox"]') as HTMLInputElement;
    combobox.click();
    fixture.detectChanges();

    // Act
    const options = el.querySelectorAll('[role="option"]') as NodeListOf<HTMLElement>;

    // Assert — first (monitored) has sr-only "already monitored" with no leading comma
    const srText = options[0].querySelector('.sr-only');
    expect(srText?.textContent?.trim()).toBe('already monitored');
    // Second (selectable) has no such text
    const srTextSelectable = options[1].querySelector('.sr-only');
    expect(srTextSelectable).toBeNull();
  });

  it('should not select a monitored repository when clicked', () => {
    // Arrange
    const { el, fixture } = setup({
      repository: null,
      accounts: [MOCK_ACCOUNT],
      availableRepositories: MOCK_AVAILABLE_WITH_MONITORED,
      hasClaims: true,
    });

    const select = el.querySelector('#repository-account') as HTMLSelectElement;
    select.value = MOCK_ACCOUNT.id;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const combobox = el.querySelector('[role="combobox"]') as HTMLInputElement;
    combobox.click();
    fixture.detectChanges();

    // Act — click the monitored option (index 0)
    const options = el.querySelectorAll('[role="option"]') as NodeListOf<HTMLElement>;
    options[0].click();
    fixture.detectChanges();

    // Assert — slug unchanged, picker stays open
    expect(combobox.value).toBe('');
    const listbox = el.querySelector('[role="listbox"]') as HTMLElement;
    expect(listbox.hidden).toBe(false);
  });

  it('should render aria-disabled="true" on a monitored repository option', () => {
    // Arrange
    const { el, fixture } = setup({
      repository: null,
      accounts: [MOCK_ACCOUNT],
      availableRepositories: MOCK_AVAILABLE_WITH_MONITORED,
      hasClaims: true,
    });

    const select = el.querySelector('#repository-account') as HTMLSelectElement;
    select.value = MOCK_ACCOUNT.id;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const combobox = el.querySelector('[role="combobox"]') as HTMLInputElement;
    combobox.click();
    fixture.detectChanges();

    // Assert
    const options = el.querySelectorAll('[role="option"]') as NodeListOf<HTMLElement>;
    expect(options[0].getAttribute('aria-disabled')).toBe('true');
    expect(options[1].getAttribute('aria-disabled')).toBeNull();
  });

  // Cycle 33: monitored wins precedence — !canPush && isMonitored shows check, no "no write access" reason
  it('should show monitored check and no "no write access" reason for !canPush && isMonitored repo', () => {
    // Arrange
    const { el, fixture } = setup({
      repository: null,
      accounts: [MOCK_ACCOUNT],
      availableRepositories: MOCK_AVAILABLE_MONITORED_NO_PUSH,
      hasClaims: true,
    });

    const select = el.querySelector('#repository-account') as HTMLSelectElement;
    select.value = MOCK_ACCOUNT.id;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const combobox = el.querySelector('[role="combobox"]') as HTMLInputElement;
    combobox.click();
    fixture.detectChanges();

    // Act
    const options = el.querySelectorAll('[role="option"]') as NodeListOf<HTMLElement>;

    // Assert — first option is monitored+noPush
    const check = options[0].querySelector('.repository-form__picker-check');
    expect(check).toBeTruthy();
    const reason = options[0].querySelector('.repository-form__picker-option-reason');
    expect(reason).toBeNull();
    const srMonitored = options[0].querySelector('.sr-only');
    expect(srMonitored?.textContent).toContain('already monitored');
  });

  // Cycle 34: gutter alignment — both monitored and non-monitored rows render a gutter cell
  it('should render gutter cells on both monitored and selectable option rows', () => {
    // Arrange
    const { el, fixture } = setup({
      repository: null,
      accounts: [MOCK_ACCOUNT],
      availableRepositories: MOCK_AVAILABLE_WITH_MONITORED,
      hasClaims: true,
    });

    const select = el.querySelector('#repository-account') as HTMLSelectElement;
    select.value = MOCK_ACCOUNT.id;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const combobox = el.querySelector('[role="combobox"]') as HTMLInputElement;
    combobox.click();
    fixture.detectChanges();

    // Act
    const options = el.querySelectorAll('[role="option"]') as NodeListOf<HTMLElement>;

    // Assert — all rows have the gutter element for alignment
    options.forEach(option => {
      const gutter = option.querySelector('.repository-form__picker-option-gutter');
      expect(gutter).toBeTruthy();
      expect(gutter?.getAttribute('aria-hidden')).toBe('true');
    });
  });

  // Cycle 35: no-claims empty state — always-in-tree live region, never hidden/display:none
  it('should show "no claimed namespaces" message when picker is open and hasClaims is false', () => {
    // Arrange
    const { el, fixture } = setup({
      repository: null,
      accounts: [MOCK_ACCOUNT],
      availableRepositories: [],
      hasClaims: false,
    });

    const select = el.querySelector('#repository-account') as HTMLSelectElement;
    select.value = MOCK_ACCOUNT.id;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    // Act — open picker
    const combobox = el.querySelector('[role="combobox"]') as HTMLInputElement;
    combobox.click();
    fixture.detectChanges();

    // Assert — persistent role="status" sibling, never hidden, text contains message
    const emptyStatus = el.querySelector('.repository-form__picker-empty-status');
    expect(emptyStatus).toBeTruthy();
    expect(emptyStatus?.getAttribute('role')).toBe('status');
    // Must NOT use hidden attribute (would remove from a11y tree)
    expect(emptyStatus?.hasAttribute('hidden')).toBe(false);
    expect(emptyStatus?.textContent).toContain('no claimed namespaces');
    // Verify it is NOT inside the listbox
    const listbox = el.querySelector('[role="listbox"]');
    expect(listbox?.contains(emptyStatus)).toBe(false);
  });

  it('should have empty text content in the status region when picker is closed', () => {
    // Arrange — picker closed (after account selection, before click)
    const { el, fixture } = setup({
      repository: null,
      accounts: [MOCK_ACCOUNT],
      availableRepositories: [],
      hasClaims: false,
    });

    const select = el.querySelector('#repository-account') as HTMLSelectElement;
    select.value = MOCK_ACCOUNT.id;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    // Act — do NOT open picker

    // Assert — status element always in DOM, but text is empty so it does not announce
    const emptyStatus = el.querySelector('.repository-form__picker-empty-status');
    expect(emptyStatus).toBeTruthy();
    expect(emptyStatus?.hasAttribute('hidden')).toBe(false);
    expect(emptyStatus?.textContent?.trim()).toBe('');
  });

  // Cycle 36: has-claims-but-empty state — persistent live-region, never hidden
  it('should show "no repositories under claimed namespaces" when hasClaims is true but list is empty', () => {
    // Arrange
    const { el, fixture } = setup({
      repository: null,
      accounts: [MOCK_ACCOUNT],
      availableRepositories: [],
      hasClaims: true,
    });

    const select = el.querySelector('#repository-account') as HTMLSelectElement;
    select.value = MOCK_ACCOUNT.id;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    // Act — open picker
    const combobox = el.querySelector('[role="combobox"]') as HTMLInputElement;
    combobox.click();
    fixture.detectChanges();

    // Assert — persistent role="status", never hidden, text contains message, not inside listbox
    const emptyStatus = el.querySelector('.repository-form__picker-empty-status');
    expect(emptyStatus).toBeTruthy();
    expect(emptyStatus?.getAttribute('role')).toBe('status');
    expect(emptyStatus?.hasAttribute('hidden')).toBe(false);
    expect(emptyStatus?.textContent).toContain('No repositories under this account');
    const listbox = el.querySelector('[role="listbox"]');
    expect(listbox?.contains(emptyStatus)).toBe(false);
  });

  // Cycle 37: read-only row still shows reason when !isMonitored
  it('should show "no write access" reason on a read-only (canPush=false, isMonitored=false) option', () => {
    // Arrange
    const repos: AvailableRepository[] = [
      { slug: 'my-org/readonly-repo', isPrivate: false, canPush: false, isMonitored: false },
    ];
    const { el, fixture } = setup({
      repository: null,
      accounts: [MOCK_ACCOUNT],
      availableRepositories: repos,
      hasClaims: true,
    });

    const select = el.querySelector('#repository-account') as HTMLSelectElement;
    select.value = MOCK_ACCOUNT.id;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const combobox = el.querySelector('[role="combobox"]') as HTMLInputElement;
    combobox.click();
    fixture.detectChanges();

    // Act
    const options = el.querySelectorAll('[role="option"]') as NodeListOf<HTMLElement>;

    // Assert — read-only option shows reason text, no check, no "already monitored" sr-text
    const reason = options[0].querySelector('.repository-form__picker-option-reason');
    expect(reason).toBeTruthy();
    expect(reason?.textContent).toContain('no write access');
    const check = options[0].querySelector('.repository-form__picker-check');
    expect(check).toBeNull();
    // The sr-only for "already monitored" must not be present (only the reason sr-only is allowed)
    const srTexts = Array.from(options[0].querySelectorAll('.sr-only'));
    const hasMonitoredSr = srTexts.some(el => el.textContent?.includes('already monitored'));
    expect(hasMonitoredSr).toBe(false);
  });

  // Cycle 38: aria-selected is always a boolean on every role="option" (WCAG 4.1.2)
  it('should render aria-selected as "false" (not absent) on disabled and monitored options', () => {
    // Arrange — list: [monitored@0, selectable@1, readonly@2]
    const repos: AvailableRepository[] = [
      { slug: 'my-org/monitored-repo', isPrivate: false, canPush: true, isMonitored: true },
      { slug: 'my-org/selectable-repo', isPrivate: false, canPush: true, isMonitored: false },
      { slug: 'my-org/readonly-repo', isPrivate: false, canPush: false, isMonitored: false },
    ];
    const { el, fixture } = setup({
      repository: null,
      accounts: [MOCK_ACCOUNT],
      availableRepositories: repos,
      hasClaims: true,
    });

    const select = el.querySelector('#repository-account') as HTMLSelectElement;
    select.value = MOCK_ACCOUNT.id;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const combobox = el.querySelector('[role="combobox"]') as HTMLInputElement;
    combobox.click();
    fixture.detectChanges();

    // Act
    const options = el.querySelectorAll('[role="option"]') as NodeListOf<HTMLElement>;

    // Assert — every option carries aria-selected (never null/absent)
    options.forEach(option => {
      const ariaSelected = option.getAttribute('aria-selected');
      expect(ariaSelected).not.toBeNull();
      expect(ariaSelected === 'true' || ariaSelected === 'false').toBe(true);
    });
    // Monitored option: aria-selected="false" (not selected, not selectable)
    expect(options[0].getAttribute('aria-selected')).toBe('false');
    // Selectable option (not yet selected): aria-selected="false"
    expect(options[1].getAttribute('aria-selected')).toBe('false');
    // Readonly option: aria-selected="false"
    expect(options[2].getAttribute('aria-selected')).toBe('false');
  });
});

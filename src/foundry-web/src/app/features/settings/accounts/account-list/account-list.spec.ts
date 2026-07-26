import { TestBed } from '@angular/core/testing';
import { AccountListComponent } from './account-list';
import { AccountSummary } from '../account.model';

const MOCK_ACCOUNT: AccountSummary = {
  id: '00000000-0000-0000-0000-000000000001',
  name: 'my-github',
  providerType: 'GitHub',
  baseUrl: 'https://github.com',
  hasToken: true,
  namespaces: [],
};

const MOCK_ACCOUNT_2: AccountSummary = {
  id: '00000000-0000-0000-0000-000000000002',
  name: 'work-gitlab',
  providerType: 'GitLab',
  baseUrl: 'https://gitlab.com',
  hasToken: false,
  namespaces: [],
};

const MOCK_ACCOUNT_LOWERCASE_GITHUB: AccountSummary = {
  id: '00000000-0000-0000-0000-000000000003',
  name: 'api-github',
  providerType: 'github',
  baseUrl: 'https://github.com',
  hasToken: true,
  namespaces: [],
};

const MOCK_ACCOUNT_LOWERCASE_GITLAB: AccountSummary = {
  id: '00000000-0000-0000-0000-000000000004',
  name: 'api-gitlab',
  providerType: 'gitlab',
  baseUrl: 'https://gitlab.com',
  hasToken: true,
  namespaces: [],
};

function setup(overrides: {
  accounts?: AccountSummary[];
  loading?: boolean;
  error?: string | null;
} = {}) {
  const fixture = TestBed.createComponent(AccountListComponent);
  fixture.componentRef.setInput('accounts', overrides.accounts ?? []);
  fixture.componentRef.setInput('loading', overrides.loading ?? false);
  fixture.componentRef.setInput('error', overrides.error ?? null);
  fixture.detectChanges();
  return { fixture, component: fixture.componentInstance, el: fixture.nativeElement as HTMLElement };
}

describe('AccountListComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AccountListComponent],
    }).compileComponents();
  });

  // Cycle 1: empty state renders when no accounts
  it('should render the empty state when there are no accounts', () => {
    // Arrange / Act
    const { el } = setup({ accounts: [] });

    // Assert
    const emptyState = el.querySelector('[role="status"]');
    expect(emptyState).toBeTruthy();
    expect(emptyState?.textContent).toContain('No accounts configured');
  });

  // Cycle 2: empty state includes subtitle and add button
  it('should render the empty state description and Add Account button', () => {
    // Arrange / Act
    const { el } = setup({ accounts: [] });

    // Assert
    const description = el.querySelector('.account-list__empty-description');
    expect(description?.textContent).toContain('Add your first provider account to start monitoring repositories');
    const addBtn = el.querySelector('.account-list__add-btn');
    expect(addBtn?.textContent?.trim()).toContain('Add Account');
  });

  // Cycle 3: populated state shows list
  it('should render a list when accounts are provided', () => {
    // Arrange / Act
    const { el } = setup({ accounts: [MOCK_ACCOUNT] });

    // Assert
    const list = el.querySelector('[role="list"]');
    expect(list).toBeTruthy();
    const items = el.querySelectorAll('[role="listitem"]');
    expect(items.length).toBe(1);
  });

  // Cycle 4: account row shows provider badge, name, base URL
  it('should render account name and base URL in each row', () => {
    // Arrange / Act
    const { el } = setup({ accounts: [MOCK_ACCOUNT] });

    // Assert
    const name = el.querySelector('.account-list__name');
    expect(name?.textContent?.trim()).toBe('my-github');
    const url = el.querySelector('.account-list__url');
    expect(url?.textContent?.trim()).toBe('https://github.com');
  });

  // Cycle 5: provider icon renders with correct accessible label
  it('should render fd-provider-icon with aria-label="GitHub" for GitHub accounts', () => {
    // Arrange / Act
    const { el } = setup({ accounts: [MOCK_ACCOUNT] });

    // Assert
    const icon = el.querySelector('fd-provider-icon');
    expect(icon).toBeTruthy();
    expect(icon?.getAttribute('aria-label')).toBe('GitHub');
  });

  it('should render fd-provider-icon with aria-label="GitLab" for GitLab accounts', () => {
    // Arrange / Act
    const { el } = setup({ accounts: [MOCK_ACCOUNT_2] });

    // Assert
    const icon = el.querySelector('fd-provider-icon');
    expect(icon).toBeTruthy();
    expect(icon?.getAttribute('aria-label')).toBe('GitLab');
  });

  it('should not render the old provider-badge element', () => {
    // Arrange / Act
    const { el } = setup({ accounts: [MOCK_ACCOUNT] });

    // Assert
    const badge = el.querySelector('.account-list__provider-badge');
    expect(badge).toBeFalsy();
  });

  // Cycle 6: token status indicator
  it('should show "Configured" token status for accounts with a token', () => {
    // Arrange / Act
    const { el } = setup({ accounts: [MOCK_ACCOUNT] });

    // Assert
    const tokenLabel = el.querySelector('.account-list__token-label');
    expect(tokenLabel?.textContent?.trim()).toBe('Configured');
    const dot = el.querySelector('.account-list__token-dot');
    expect(dot?.classList.contains('account-list__token-dot--configured')).toBe(true);
  });

  it('should show "Not configured" token status for accounts without a token', () => {
    // Arrange / Act
    const { el } = setup({ accounts: [MOCK_ACCOUNT_2] });

    // Assert
    const tokenLabel = el.querySelector('.account-list__token-label');
    expect(tokenLabel?.textContent?.trim()).toBe('Not configured');
    const dot = el.querySelector('.account-list__token-dot');
    expect(dot?.classList.contains('account-list__token-dot--not-configured')).toBe(true);
  });

  // Cycle 7: edit and delete action icon buttons with aria-labels (rendered via fd-row-actions)
  it('should render edit and delete icon buttons with accessible labels', () => {
    // Arrange / Act
    const { el } = setup({ accounts: [MOCK_ACCOUNT] });

    // Assert
    const editBtn = el.querySelector('[aria-label="Edit account my-github"]');
    expect(editBtn).toBeTruthy();
    const deleteBtn = el.querySelector('[aria-label="Delete account my-github"]');
    expect(deleteBtn).toBeTruthy();
  });

  // Cycle 8: populated state shows Add Account button in header
  it('should render Add Account button in header when accounts exist', () => {
    // Arrange / Act
    const { el } = setup({ accounts: [MOCK_ACCOUNT] });

    // Assert
    const headerAddBtn = el.querySelector('.account-list__header .account-list__add-btn');
    expect(headerAddBtn).toBeTruthy();
    expect(headerAddBtn?.textContent?.trim()).toContain('Add Account');
  });

  // Cycle 9: multiple accounts rendered
  it('should render a row for each account', () => {
    // Arrange / Act
    const { el } = setup({ accounts: [MOCK_ACCOUNT, MOCK_ACCOUNT_2] });

    // Assert
    const items = el.querySelectorAll('[role="listitem"]');
    expect(items.length).toBe(2);
  });

  // Cycle 10: add event emitted when Add Account clicked (empty state)
  it('should emit add event when Add Account is clicked in empty state', () => {
    // Arrange
    const { el, component } = setup({ accounts: [] });
    let emitted = false;
    component.add.subscribe(() => { emitted = true; });

    // Act
    const addBtn = el.querySelector('.account-list__add-btn') as HTMLButtonElement;
    addBtn.click();

    // Assert
    expect(emitted).toBe(true);
  });

  // Cycle 11: edit event emitted
  it('should emit the account when edit is clicked', () => {
    // Arrange
    const { el, component } = setup({ accounts: [MOCK_ACCOUNT] });
    let emittedAccount: AccountSummary | undefined;
    component.edit.subscribe((a: AccountSummary) => { emittedAccount = a; });

    // Act
    const editBtn = el.querySelector('[aria-label="Edit account my-github"]') as HTMLButtonElement;
    editBtn.click();

    // Assert
    expect(emittedAccount).toEqual(MOCK_ACCOUNT);
  });

  // Cycle 12: delete event emitted
  it('should emit the account when delete is clicked', () => {
    // Arrange
    const { el, component } = setup({ accounts: [MOCK_ACCOUNT] });
    let emittedAccount: AccountSummary | undefined;
    component.delete.subscribe((a: AccountSummary) => { emittedAccount = a; });

    // Act
    const deleteBtn = el.querySelector('[aria-label="Delete account my-github"]') as HTMLButtonElement;
    deleteBtn.click();

    // Assert
    expect(emittedAccount).toEqual(MOCK_ACCOUNT);
  });

  // Cycle 13: retry event emitted on error
  it('should show an error message when error is set', () => {
    // Arrange / Act
    const { el } = setup({ error: 'Failed to load accounts' });

    // Assert
    const errorEl = el.querySelector('[role="alert"]');
    expect(errorEl).toBeTruthy();
    expect(errorEl?.textContent).toContain('Failed to load accounts');
  });

  it('should emit retry when retry button is clicked on error state', () => {
    // Arrange
    const { el, component } = setup({ error: 'Failed to load accounts' });
    let emitted = false;
    component.retry.subscribe(() => { emitted = true; });

    // Act
    const retryBtn = el.querySelector('.account-list__retry-btn') as HTMLButtonElement;
    retryBtn.click();

    // Assert
    expect(emitted).toBe(true);
  });

  // Cycle 14: lowercase providerType from API renders correct icon and aria-label
  it('should render fd-provider-icon with aria-label="GitHub" for lowercase github providerType', () => {
    // Arrange / Act
    const { el } = setup({ accounts: [MOCK_ACCOUNT_LOWERCASE_GITHUB] });

    // Assert
    const icon = el.querySelector('fd-provider-icon');
    expect(icon?.getAttribute('aria-label')).toBe('GitHub');
  });

  it('should render fd-provider-icon with aria-label="GitLab" for lowercase gitlab providerType', () => {
    // Arrange / Act
    const { el } = setup({ accounts: [MOCK_ACCOUNT_LOWERCASE_GITLAB] });

    // Assert
    const icon = el.querySelector('fd-provider-icon');
    expect(icon?.getAttribute('aria-label')).toBe('GitLab');
  });

  // Cycle 15: namespace chips — zero claims renders nothing
  it('should not render any namespace chips when account has no namespaces', () => {
    // Arrange / Act
    const { el } = setup({ accounts: [MOCK_ACCOUNT] }); // namespaces: []

    // Assert
    const chips = el.querySelectorAll('.account-list__namespace');
    expect(chips.length).toBe(0);
  });

  // Cycle 16: namespace chips — one namespace renders one chip
  it('should render one namespace chip for each claimed namespace', () => {
    // Arrange
    const account = { ...MOCK_ACCOUNT, namespaces: ['myorg'] };

    // Act
    const { el } = setup({ accounts: [account] });

    // Assert
    const chips = el.querySelectorAll('.account-list__namespace');
    expect(chips.length).toBe(1);
    expect(chips[0].textContent?.trim()).toBe('myorg');
  });

  // Cycle 17: namespace chips — up to 4 visible, overflow chip for remainder
  it('should show at most 4 namespace chips and a +N overflow chip for more than 4 namespaces', () => {
    // Arrange
    const account = { ...MOCK_ACCOUNT, namespaces: ['ns1', 'ns2', 'ns3', 'ns4', 'ns5'] };

    // Act
    const { el } = setup({ accounts: [account] });

    // Assert
    const chips = el.querySelectorAll('.account-list__namespace');
    expect(chips.length).toBe(4);

    const overflow = el.querySelector('.account-list__namespace--overflow');
    expect(overflow).toBeTruthy();
    expect(overflow?.textContent?.trim()).toBe('+1');
  });

  // Cycle 18: overflow chip aria-label lists hidden namespaces
  it('should set aria-label on the overflow chip listing the hidden namespaces', () => {
    // Arrange
    const account = { ...MOCK_ACCOUNT, namespaces: ['ns1', 'ns2', 'ns3', 'ns4', 'ns5', 'ns6'] };

    // Act
    const { el } = setup({ accounts: [account] });

    // Assert
    const overflow = el.querySelector('.account-list__namespace--overflow') as HTMLElement;
    expect(overflow?.getAttribute('aria-label')).toBe('2 more namespaces: ns5, ns6');
  });

  // Cycle 19: exactly 4 namespaces — no overflow chip
  it('should not render an overflow chip when there are exactly 4 namespaces', () => {
    // Arrange
    const account = { ...MOCK_ACCOUNT, namespaces: ['ns1', 'ns2', 'ns3', 'ns4'] };

    // Act
    const { el } = setup({ accounts: [account] });

    // Assert
    const chips = el.querySelectorAll('.account-list__namespace');
    expect(chips.length).toBe(4);
    const overflow = el.querySelector('.account-list__namespace--overflow');
    expect(overflow).toBeNull();
  });
});

import { TestBed } from '@angular/core/testing';
import { AccountListComponent } from './account-list';
import { AccountSummary } from '../account.model';

const MOCK_ACCOUNT: AccountSummary = {
  id: '00000000-0000-0000-0000-000000000001',
  name: 'acme-bot',
  providerType: 'GitHub',
  baseUrl: 'https://github.com',
  hasToken: true,
  tokenStatus: 'present',
  deniedRepositories: [],
  tokenInvalidSince: null,
  namespaces: [],
  tokenExpiry: { status: 'notChecked', expiresAt: null },
};

const MOCK_ACCOUNT_2: AccountSummary = {
  id: '00000000-0000-0000-0000-000000000002',
  name: 'octo-org',
  providerType: 'GitLab',
  baseUrl: 'https://gitlab.example.com/',
  hasToken: false,
  tokenStatus: 'absent',
  deniedRepositories: [],
  tokenInvalidSince: null,
  namespaces: [],
  tokenExpiry: { status: 'notChecked', expiresAt: null },
};

const MOCK_ACCOUNT_UNREADABLE: AccountSummary = {
  ...MOCK_ACCOUNT,
  id: '00000000-0000-0000-0000-000000000005',
  name: 'broken-gitlab',
  providerType: 'GitLab',
  baseUrl: 'https://gitlab.example.com',
  tokenStatus: 'unreadable',
};

const MOCK_ACCOUNT_INVALID: AccountSummary = {
  ...MOCK_ACCOUNT,
  id: '00000000-0000-0000-0000-000000000006',
  name: 'rejected-github',
  tokenStatus: 'invalid',
  tokenInvalidSince: '2026-11-12T15:04:05Z',
};

const FIVE_NAMESPACES = ['octo-org', 'acme/web', 'acme', 'acme/platform', 'zeta'];

function setup(overrides: {
  accounts?: AccountSummary[];
  loading?: boolean;
  error?: string | null;
  deletingAccountId?: string | null;
} = {}) {
  const fixture = TestBed.createComponent(AccountListComponent);
  fixture.componentRef.setInput('accounts', overrides.accounts ?? []);
  fixture.componentRef.setInput('loading', overrides.loading ?? false);
  fixture.componentRef.setInput('error', overrides.error ?? null);
  fixture.componentRef.setInput('deletingAccountId', overrides.deletingAccountId ?? null);
  fixture.detectChanges();
  return { fixture, component: fixture.componentInstance, el: fixture.nativeElement as HTMLElement };
}

function text(el: Element | null | undefined): string | undefined {
  return el?.textContent?.replace(/\s+/g, ' ').trim();
}

describe('AccountListComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AccountListComponent],
    }).compileComponents();
  });

  describe('states', () => {
    it('should render the empty state when there are no accounts', () => {
      // Act
      const { el } = setup({ accounts: [] });

      // Assert
      const emptyState = el.querySelector('[role="status"]');
      expect(emptyState?.textContent).toContain('No accounts configured');
      expect(text(el.querySelector('.account-list__empty-description')))
        .toBe('Add your first provider account to start monitoring repositories.');
    });

    it('should render no button in the empty state', () => {
      // Act
      const { el } = setup({ accounts: [] });

      // Assert
      expect(el.querySelector('.account-list__empty button')).toBeNull();
    });

    it('should render a loading status while loading', () => {
      // Act
      const { el } = setup({ loading: true });

      // Assert
      expect(el.querySelector('.account-list__loading[role="status"]')).toBeTruthy();
      expect(el.querySelector('.account-list__list')).toBeNull();
    });

    it('should show an error message when error is set', () => {
      // Act
      const { el } = setup({ error: 'Failed to load accounts' });

      // Assert
      expect(el.querySelector('[role="alert"]')?.textContent).toContain('Failed to load accounts');
    });

    it('should emit retry when retry button is clicked on error state', () => {
      // Arrange
      const { el, component } = setup({ error: 'Failed to load accounts' });
      let emitted = false;
      component.retry.subscribe(() => { emitted = true; });

      // Act
      (el.querySelector('.account-list__retry-btn') as HTMLButtonElement).click();

      // Assert
      expect(emitted).toBe(true);
    });
  });

  describe('list', () => {
    it('should render a labelled list with a row for each account', () => {
      // Act
      const { el } = setup({ accounts: [MOCK_ACCOUNT, MOCK_ACCOUNT_2] });

      // Assert
      const list = el.querySelector('ul[role="list"]');
      expect(list?.getAttribute('aria-label')).toBe('Accounts');
      expect(el.querySelectorAll('li[role="listitem"]').length).toBe(2);
    });

    it.each([
      ['GitHub', 'GitHub'],
      ['GitLab', 'GitLab'],
      ['github', 'GitHub'],
      ['gitlab', 'GitLab'],
    ])('should render the provider icon for providerType %s', (providerType, label) => {
      // Act
      const { el } = setup({ accounts: [{ ...MOCK_ACCOUNT, providerType }] });

      // Assert
      expect(el.querySelector('fd-provider-icon')?.getAttribute('aria-label')).toBe(label);
    });

    it('should render the account name', () => {
      // Act
      const { el } = setup({ accounts: [MOCK_ACCOUNT] });

      // Assert
      expect(text(el.querySelector('.account-list__name'))).toBe('acme-bot');
    });
  });

  describe('meta line', () => {
    it('should show the host only when the account has no namespaces', () => {
      // Act
      const { el } = setup({ accounts: [MOCK_ACCOUNT_2] });

      // Assert
      expect(text(el.querySelector('.account-list__host'))).toBe('gitlab.example.com');
      expect(el.querySelector('.account-list__namespace-text')).toBeNull();
      expect(el.querySelector('.account-list__more-btn')).toBeNull();
    });

    it('should list up to three namespaces alphabetically with no more button', () => {
      // Act
      const { el } = setup({ accounts: [{ ...MOCK_ACCOUNT, namespaces: ['octo-org', 'acme', 'acme/web'] }] });

      // Assert
      expect(text(el.querySelector('.account-list__namespace-text'))).toBe('acme, acme/web, octo-org');
      expect(el.querySelector('.account-list__more-btn')).toBeNull();
    });

    it('should show the first three namespaces and a collapsed more button beyond three', () => {
      // Act
      const { el } = setup({ accounts: [{ ...MOCK_ACCOUNT, namespaces: FIVE_NAMESPACES }] });

      // Assert
      expect(text(el.querySelector('.account-list__namespace-text'))).toBe('acme, acme/platform, acme/web');
      const more = el.querySelector('.account-list__more-btn');
      expect(text(more)).toBe('+2 more');
      expect(more?.getAttribute('aria-expanded')).toBe('false');
      expect(more?.getAttribute('aria-controls')).toBe(`account-namespaces-${MOCK_ACCOUNT.id}`);
      expect(more?.getAttribute('aria-label')).toBe('+2 more namespaces for acme-bot');
      expect(el.querySelector(`#account-namespaces-${MOCK_ACCOUNT.id}`)).toBeTruthy();
    });

    it('should show every namespace when the more button is activated', () => {
      // Arrange
      const { el, fixture } = setup({ accounts: [{ ...MOCK_ACCOUNT, namespaces: FIVE_NAMESPACES }] });

      // Act
      (el.querySelector('.account-list__more-btn') as HTMLButtonElement).click();
      fixture.detectChanges();

      // Assert
      expect(text(el.querySelector('.account-list__namespace-text')))
        .toBe('acme, acme/platform, acme/web, octo-org, zeta');
      const more = el.querySelector('.account-list__more-btn');
      expect(text(more)).toBe('Show fewer');
      expect(more?.getAttribute('aria-expanded')).toBe('true');
      expect(more?.getAttribute('aria-label')).toBe('Show fewer namespaces for acme-bot');
    });

    it('should collapse again when Show fewer is activated', () => {
      // Arrange
      const { el, fixture } = setup({ accounts: [{ ...MOCK_ACCOUNT, namespaces: FIVE_NAMESPACES }] });
      const more = el.querySelector('.account-list__more-btn') as HTMLButtonElement;
      more.click();
      fixture.detectChanges();

      // Act
      more.click();
      fixture.detectChanges();

      // Assert
      expect(text(el.querySelector('.account-list__namespace-text'))).toBe('acme, acme/platform, acme/web');
      expect(more.getAttribute('aria-expanded')).toBe('false');
    });

    it('should expand only the row whose more button was activated', () => {
      // Arrange
      const { el, fixture } = setup({
        accounts: [
          { ...MOCK_ACCOUNT, namespaces: FIVE_NAMESPACES },
          { ...MOCK_ACCOUNT_2, namespaces: FIVE_NAMESPACES },
        ],
      });
      const buttons = el.querySelectorAll<HTMLButtonElement>('.account-list__more-btn');

      // Act
      buttons[0].click();
      fixture.detectChanges();

      // Assert
      expect(buttons[0].getAttribute('aria-expanded')).toBe('true');
      expect(buttons[1].getAttribute('aria-expanded')).toBe('false');
    });

    it('should keep a row expanded when the same account is reloaded', () => {
      // Arrange
      const { el, fixture } = setup({ accounts: [{ ...MOCK_ACCOUNT, namespaces: FIVE_NAMESPACES }] });
      (el.querySelector('.account-list__more-btn') as HTMLButtonElement).click();
      fixture.detectChanges();

      // Act
      fixture.componentRef.setInput('accounts', [{ ...MOCK_ACCOUNT, namespaces: [...FIVE_NAMESPACES] }]);
      fixture.detectChanges();

      // Assert
      expect(el.querySelector('.account-list__more-btn')?.getAttribute('aria-expanded')).toBe('true');
    });

    it('should forget the expansion of an account that left the list', () => {
      // Arrange
      const expandable = { ...MOCK_ACCOUNT, namespaces: FIVE_NAMESPACES };
      const { el, fixture } = setup({ accounts: [expandable, MOCK_ACCOUNT_2] });
      (el.querySelector('.account-list__more-btn') as HTMLButtonElement).click();
      fixture.detectChanges();
      fixture.componentRef.setInput('accounts', [MOCK_ACCOUNT_2]);
      fixture.detectChanges();

      // Act
      fixture.componentRef.setInput('accounts', [expandable, MOCK_ACCOUNT_2]);
      fixture.detectChanges();

      // Assert
      expect(el.querySelector('.account-list__more-btn')?.getAttribute('aria-expanded')).toBe('false');
    });
  });

  describe('token status', () => {
    it('should show no status tag and no problem block for a present token', () => {
      // Act
      const { el } = setup({ accounts: [MOCK_ACCOUNT] });

      // Assert
      expect(el.querySelector('.account-list__status')).toBeNull();
      expect(el.querySelector('.account-list__problem')).toBeNull();
    });

    it('should show a neutral Not configured tag and no problem block for an absent token', () => {
      // Act
      const { el } = setup({ accounts: [MOCK_ACCOUNT_2] });

      // Assert
      const tag = el.querySelector('.account-list__status--side.account-list__status--neutral');
      expect(text(tag)).toBe('Not configured');
      expect(el.querySelector('.account-list__problem')).toBeNull();
    });

    it('should show a warning tag and problem block for an unreadable token', () => {
      // Act
      const { el } = setup({ accounts: [MOCK_ACCOUNT_UNREADABLE] });

      // Assert
      expect(text(el.querySelector('.account-list__status--side.account-list__status--warning'))).toBe('Token unreadable');
      const problem = el.querySelector('.account-list__problem--warning');
      expect(problem?.getAttribute('role')).toBe('group');
      expect(problem?.getAttribute('aria-label')).toBe('Token status for broken-gitlab');
      expect(text(problem?.querySelector('.account-list__problem-text')))
        .toBe("The account token can't be decrypted. Re-enter it on the account to resume.");
      const button = problem?.querySelector('.account-list__problem-btn');
      expect(text(button)).toBe('Re-enter token');
      expect(button?.getAttribute('aria-label')).toBe('Re-enter token for broken-gitlab');
    });

    it('should show an error tag and problem block for an invalid token', () => {
      // Act
      const { el } = setup({ accounts: [MOCK_ACCOUNT_INVALID] });

      // Assert
      expect(text(el.querySelector('.account-list__status--side.account-list__status--error'))).toBe('Token invalid');
      const problem = el.querySelector('.account-list__problem--error');
      expect(text(problem?.querySelector('.account-list__problem-text')))
        .toBe('The provider rejected the account token. Enter a new token on the account to resume.');
      const button = problem?.querySelector('.account-list__problem-btn');
      expect(text(button)).toBe('Enter new token');
      expect(button?.getAttribute('aria-label')).toBe('Enter new token for rejected-github');
    });

    it.each([MOCK_ACCOUNT_UNREADABLE, MOCK_ACCOUNT_INVALID])(
      'should emit edit when the problem button is clicked for $tokenStatus',
      account => {
        // Arrange
        const { el, component } = setup({ accounts: [account] });
        let emitted: AccountSummary | undefined;
        component.edit.subscribe((a: AccountSummary) => { emitted = a; });

        // Act
        (el.querySelector('.account-list__problem-btn') as HTMLButtonElement).click();

        // Assert
        expect(emitted).toEqual(account);
      },
    );
  });

  describe('edit and delete', () => {
    it('should emit the account when edit is clicked', () => {
      // Arrange
      const { el, component } = setup({ accounts: [MOCK_ACCOUNT] });
      let emitted: AccountSummary | undefined;
      component.edit.subscribe((a: AccountSummary) => { emitted = a; });

      // Act
      const editBtn = el.querySelector('.account-list__edit-btn') as HTMLButtonElement;
      editBtn.click();

      // Assert
      expect(editBtn.getAttribute('aria-label')).toBe('Edit account acme-bot');
      expect(emitted).toEqual(MOCK_ACCOUNT);
    });

    it('should emit the account when delete is clicked', () => {
      // Arrange
      const { el, component } = setup({ accounts: [MOCK_ACCOUNT] });
      let emitted: AccountSummary | undefined;
      component.delete.subscribe((a: AccountSummary) => { emitted = a; });

      // Act
      const deleteBtn = el.querySelector('.account-list__delete-btn') as HTMLButtonElement;
      deleteBtn.click();

      // Assert
      expect(deleteBtn.getAttribute('aria-label')).toBe('Delete account acme-bot');
      expect(emitted).toEqual(MOCK_ACCOUNT);
    });

    it('should leave every row idle when deletingAccountId is null', () => {
      // Act
      const { el } = setup({ accounts: [MOCK_ACCOUNT, MOCK_ACCOUNT_2], deletingAccountId: null });

      // Assert
      expect(el.querySelectorAll('fd-spinner').length).toBe(0);
      const deleteButtons = Array.from(el.querySelectorAll<HTMLButtonElement>('.account-list__delete-btn'));
      expect(deleteButtons.map(b => b.disabled)).toEqual([false, false]);
      expect(el.querySelectorAll('.account-list__edit-btn[aria-disabled]').length).toBe(0);
    });

    it('should mark only the deleting row busy', () => {
      // Act
      const { el } = setup({ accounts: [MOCK_ACCOUNT, MOCK_ACCOUNT_2], deletingAccountId: MOCK_ACCOUNT.id });

      // Assert
      const [busyRow, idleRow] = Array.from(el.querySelectorAll('.account-list__row'));
      const busyDelete = busyRow.querySelector('.account-list__delete-btn') as HTMLButtonElement;
      expect(busyDelete.disabled).toBe(true);
      expect(busyDelete.getAttribute('aria-label')).toBe('Deleting account acme-bot…');
      expect(busyDelete.querySelector('fd-spinner')).toBeTruthy();
      expect(busyRow.querySelector('.account-list__edit-btn')?.getAttribute('aria-disabled')).toBe('true');
      const idleDelete = idleRow.querySelector('.account-list__delete-btn') as HTMLButtonElement;
      expect(idleDelete.disabled).toBe(false);
      expect(idleDelete.getAttribute('aria-label')).toBe('Delete account octo-org');
      expect(idleDelete.querySelector('fd-spinner')).toBeNull();
    });

    it('should not emit edit when the busy row is edited', () => {
      // Arrange
      const { el, component } = setup({ accounts: [MOCK_ACCOUNT], deletingAccountId: MOCK_ACCOUNT.id });
      let emitted = false;
      component.edit.subscribe(() => { emitted = true; });

      // Act
      (el.querySelector('.account-list__edit-btn') as HTMLButtonElement).click();

      // Assert
      expect(emitted).toBe(false);
    });
  });
});

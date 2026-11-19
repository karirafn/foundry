import { TestBed } from '@angular/core/testing';
import { AccountSummary } from '../account.model';
import { AccountListComponent } from './account-list';

const NOW = new Date(2026, 10, 17, 12, 0);

const ACCOUNT: AccountSummary = {
  id: '00000000-0000-0000-0000-000000000001',
  name: 'alice',
  providerType: 'gitlab',
  baseUrl: 'https://gitlab.com',
  hasToken: true,
  tokenStatus: 'present',
  deniedRepositories: [],
  tokenInvalidSince: null,
  namespaces: [],
  tokenExpiry: { status: 'notChecked', expiresAt: null },
};

function inDays(days: number): string {
  return new Date(NOW.getFullYear(), NOW.getMonth(), NOW.getDate() + days, 12, 0).toISOString();
}

function account(status: string, expiresAt: string | null, overrides: Partial<AccountSummary> = {}): AccountSummary {
  return { ...ACCOUNT, ...overrides, tokenExpiry: { status, expiresAt } };
}

function setup(accounts: AccountSummary[]) {
  const fixture = TestBed.createComponent(AccountListComponent);
  fixture.componentRef.setInput('accounts', accounts);
  fixture.detectChanges();
  return { fixture, component: fixture.componentInstance, el: fixture.nativeElement as HTMLElement };
}

function text(el: Element | null | undefined): string | undefined {
  return el?.textContent?.replace(/\s+/g, ' ').trim();
}

describe('AccountListComponent token expiry', () => {
  beforeEach(async () => {
    vi.useFakeTimers();
    vi.setSystemTime(NOW);
    await TestBed.configureTestingModule({ imports: [AccountListComponent] }).compileComponents();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('should show the date and days for a token expiring in 30 days, with no warning', () => {
    // Act
    const { el } = setup([account('current', inDays(30))]);

    // Assert
    expect(text(el.querySelector('.account-list__expiry'))).toBe('Expires on Dec 17, 2026 (in 30 days)');
    expect(el.querySelector('.account-list__status')).toBeNull();
    expect(el.querySelector('.account-list__problem')).toBeNull();
  });

  it.each([
    ['notChecked', 'Expiry not checked yet'],
    ['unavailable', 'GitLab reported no expiry for this token'],
    ['never', 'Never expires'],
  ])('should show the %s expiry line', (status, phrase) => {
    // Act
    const { el } = setup([account(status, null)]);

    // Assert
    expect(text(el.querySelector('.account-list__expiry'))).toBe(phrase);
  });

  it('should warn about a token expiring in 10 days with a replace action', () => {
    // Act
    const { el } = setup([account('expiringSoon', inDays(10))]);

    // Assert
    expect(el.querySelector('.account-list__expiry')).toBeNull();
    expect(text(el.querySelector('.account-list__status--side'))).toBe('Token expires soon');
    expect(text(el.querySelector('.account-list__problem-text')))
      .toBe('Warning: Expires on Nov 27, 2026 (in 10 days). Enter a replacement token before it expires.');
    expect(el.querySelector('.account-list__problem-btn')?.getAttribute('aria-label')).toBe('Replace token for alice');
  });

  it('should warn "Expired on" a token whose expiry date has passed', () => {
    // Act
    const { el } = setup([account('expired', inDays(-2))]);

    // Assert
    expect(text(el.querySelector('.account-list__status--side'))).toBe('Token expired');
    expect(text(el.querySelector('.account-list__problem-text')))
      .toBe('Warning: Expired on Nov 15, 2026 (2 days ago). GitLab can reject it at any time, which pauses monitoring.');
  });

  it('should emit edit when "Replace token" is pressed', () => {
    // Arrange
    const expiring = account('expiringSoon', inDays(10));
    const { el, component } = setup([expiring]);
    const emitted: AccountSummary[] = [];
    component.edit.subscribe(a => emitted.push(a));

    // Act
    el.querySelector<HTMLButtonElement>('.account-list__problem-btn')!.click();

    // Assert
    expect(emitted).toEqual([expiring]);
  });

  it.each([
    ['invalid', 'Token invalid'],
    ['unreadable', 'Token unreadable'],
    ['absent', 'Not configured'],
  ])('should show no expiry line, tag or phrase for a %s token', (tokenStatus, label) => {
    // Act
    const { el } = setup([account('expired', inDays(-2), { tokenStatus, hasToken: tokenStatus !== 'absent' })]);

    // Assert
    expect(el.querySelector('.account-list__expiry')).toBeNull();
    expect(text(el.querySelector('.account-list__status--side'))).toBe(label);
    expect(el.querySelector('.account-list__problem fd-token-expiry')).toBeNull();
  });
});

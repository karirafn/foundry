import { Component, input } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AccountSummary } from '../account.model';
import { AccountListComponent } from './account-list';

const BASE: AccountSummary = {
  id: 'account-0',
  name: 'acme-bot',
  providerType: 'GitHub',
  baseUrl: 'https://github.com/',
  hasToken: true,
  tokenStatus: 'present',
  branchRulesTokenStatus: 'absent',
  deniedRepositories: [],
  tokenInvalidSince: null,
  namespaces: [],
  tokenExpiry: { status: 'notChecked', expiresAt: null },
};

const MANY_NAMESPACES: string[] = [
  'acme',
  'acme/archive',
  'acme/archive/client-apps',
  'acme/customer-projects/regional-infrastructure-monitoring-platform',
  ...Array.from({ length: 23 }, (_, i) => `acme/platform/service-${i + 1}`),
];

const ACCOUNTS: AccountSummary[] = [
  {
    ...BASE,
    id: 'account-1',
    providerType: 'GitLab',
    baseUrl: 'https://gitlab.example.com/',
    namespaces: MANY_NAMESPACES,
    tokenExpiry: { status: 'unavailable', expiresAt: null },
  },
  {
    ...BASE,
    id: 'account-2',
    name: 'octo-org-automation-account-with-a-long-name',
    tokenStatus: 'invalid',
    tokenInvalidSince: '2026-11-12T15:04:05Z',
    namespaces: ['octo-org'],
  },
  {
    ...BASE,
    id: 'account-3',
    tokenStatus: 'unreadable',
  },
  {
    ...BASE,
    id: 'account-4',
    name: 'gitlab-release-automation-account',
    providerType: 'GitLab',
    tokenExpiry: { status: 'expiringSoon', expiresAt: '2026-12-28T00:00:00+00:00' },
  },
  {
    ...BASE,
    id: 'account-5',
    tokenExpiry: { status: 'expired', expiresAt: '2026-09-30T00:00:00+00:00' },
  },
];

@Component({
  selector: 'fd-account-list-layout-host',
  imports: [AccountListComponent],
  template: `<div class="host" [style.width.px]="width()"><fd-account-list [accounts]="accounts" /></div>`,
})
class HostComponent {
  readonly width = input.required<number>();
  readonly accounts = ACCOUNTS;
}

async function render(width: number): Promise<{ fixture: ComponentFixture<HostComponent>; host: HTMLElement }> {
  const fixture = TestBed.createComponent(HostComponent);
  fixture.componentRef.setInput('width', width);
  await fixture.whenStable();
  const host = (fixture.nativeElement as HTMLElement).querySelector('.host') as HTMLElement;
  return { fixture, host };
}

// Runs in a real browser (test-layout target); jsdom would measure every box as 0.
describe('account list layout', () => {
  // 332px is the list width inside a 390px viewport after the settings and section padding.
  it.each([390, 332])('has no horizontal overflow at %ipx with 27 namespaces expanded', async width => {
    // Arrange
    const { fixture, host } = await render(width);
    (host.querySelector('.account-list__more-btn') as HTMLButtonElement).click();
    await fixture.whenStable();
    const hostRight = host.getBoundingClientRect().right;

    // Act
    const overflowing = Array.from(host.querySelectorAll('*'))
      .filter(el => el.getBoundingClientRect().right > hostRight + 0.5)
      .map(el => el.className);
    const list = host.querySelector('.account-list__list') as HTMLElement;

    // Assert
    expect(host.querySelector('.account-list__namespace-text')?.textContent).toContain('service-23');
    expect(host.querySelectorAll('.account-list__expiry').length).toBe(1);
    expect(host.querySelectorAll('.account-list__problem fd-token-expiry').length).toBe(2);
    expect(overflowing).toEqual([]);
    expect(list.scrollWidth).toBeLessThanOrEqual(list.clientWidth);
  });

  it('moves the status tag onto the title line at 390px', async () => {
    // Arrange
    const { host } = await render(390);

    // Act
    const side = getComputedStyle(host.querySelector('.account-list__status--side')!);
    const inlineTag = host.querySelector('.account-list__status--inline') as HTMLElement;
    const inline = getComputedStyle(inlineTag);

    // Assert
    expect(side.display).toBe('none');
    // A flex item's inline-flex is blockified to flex, so assert it shows rather than its exact keyword.
    expect(inline.display).not.toBe('none');
    expect(inlineTag.getBoundingClientRect().width).toBeGreaterThan(0);
  });

  it('renders the account name in the UI sans font at weight 600', async () => {
    // Arrange
    const { host } = await render(800);

    // Act
    const name = getComputedStyle(host.querySelector('.account-list__name')!);

    // Assert
    expect(name.fontFamily).not.toContain('JetBrains Mono');
    expect(name.fontWeight).toBe('600');
  });

  it('keeps the status tag in the side column on a wide list', async () => {
    // Arrange
    const { host } = await render(800);

    // Act
    const side = getComputedStyle(host.querySelector('.account-list__status--side')!);
    const inline = getComputedStyle(host.querySelector('.account-list__status--inline')!);

    // Assert
    expect(side.display).not.toBe('none');
    expect(inline.display).toBe('none');
  });
});

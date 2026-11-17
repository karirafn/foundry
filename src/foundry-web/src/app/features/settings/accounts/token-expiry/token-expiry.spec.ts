import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ResolvedTokenExpiry } from '../account.model';
import { TokenExpiryComponent, relativeExpiryText } from './token-expiry';

@Component({
  imports: [TokenExpiryComponent],
  template: `<fd-token-expiry [expiry]="expiry()" providerName="GitLab" />`,
})
class HostComponent {
  readonly expiry = signal<ResolvedTokenExpiry>({ status: 'notChecked' });
}

function render(expiry: ResolvedTokenExpiry): HTMLElement {
  const fixture = TestBed.createComponent(HostComponent);
  fixture.componentInstance.expiry.set(expiry);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('relativeExpiryText', () => {
  const now = new Date(2026, 10, 17, 15, 30);

  it.each([
    [new Date(2026, 10, 17, 23, 0), 'today'],
    [new Date(2026, 10, 18, 1, 0), 'tomorrow'],
    [new Date(2026, 10, 27, 9, 0), 'in 10 days'],
  ])('should describe a future expiry at %s as "%s"', (expiresAt, text) => {
    // Act
    const result = relativeExpiryText('expiringSoon', expiresAt.toISOString(), now);

    // Assert
    expect(result).toBe(text);
  });

  it.each([
    [new Date(2026, 10, 17, 1, 0), 'today'],
    [new Date(2026, 10, 16, 23, 0), 'yesterday'],
    [new Date(2026, 10, 12, 9, 0), '5 days ago'],
  ])('should describe a passed expiry at %s as "%s"', (expiresAt, text) => {
    // Act
    const result = relativeExpiryText('expired', expiresAt.toISOString(), now);

    // Assert
    expect(result).toBe(text);
  });

  it('should clamp a future status whose date has already passed to "today"', () => {
    // Act
    const result = relativeExpiryText('current', new Date(2026, 10, 15).toISOString(), now);

    // Assert
    expect(result).toBe('today');
  });

  it('should clamp an expired status whose date is still ahead to "today"', () => {
    // Act
    const result = relativeExpiryText('expired', new Date(2026, 10, 19).toISOString(), now);

    // Assert
    expect(result).toBe('today');
  });

  it('should count calendar days across a daylight-saving change', () => {
    // Arrange — local midnights a week apart, whatever the zone's offset change in between.
    const before = new Date(2026, 2, 25, 0, 0);
    const after = new Date(2026, 3, 1, 0, 0);

    // Act
    const result = relativeExpiryText('current', after.toISOString(), before);

    // Assert
    expect(result).toBe('in 7 days');
  });
});

describe('TokenExpiryComponent', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date(2026, 10, 17, 12, 0));
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it.each([
    [{ status: 'notChecked' }, 'Expiry not checked yet'],
    [{ status: 'unavailable' }, 'GitLab reported no expiry for this token'],
    [{ status: 'never' }, 'Never expires'],
  ] as const)('should render %o as "%s"', (expiry, text) => {
    // Act
    const element = render(expiry);

    // Assert
    expect(element.textContent?.trim()).toBe(text);
  });

  it('should render a current expiry with its date and days', () => {
    // Act
    const element = render({ status: 'current', expiresAt: new Date(2026, 11, 17, 12, 0).toISOString() });

    // Assert
    expect(element.textContent?.trim()).toBe('Expires on Dec 17, 2026 (in 30 days)');
  });

  it('should render an expired expiry as "Expired on"', () => {
    // Act
    const element = render({ status: 'expired', expiresAt: new Date(2026, 10, 14, 12, 0).toISOString() });

    // Assert
    expect(element.textContent?.trim()).toBe('Expired on Nov 14, 2026 (3 days ago)');
  });

  it('should mark the date up as a machine-readable time', () => {
    // Arrange
    const expiresAt = new Date(2026, 11, 17, 12, 0).toISOString();

    // Act
    const element = render({ status: 'expiringSoon', expiresAt });

    // Assert
    expect(element.querySelector('time')?.getAttribute('datetime')).toBe(expiresAt);
  });
});

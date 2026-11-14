import { TestBed } from '@angular/core/testing';
import { WritableSignal, signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { AccountTokenBannerComponent } from './account-token-banner';
import { SystemSignalRService } from '../../../../../core/services/system-signalr.service';
import { SystemStatusService } from '../../../../../core/services/system-status.service';
import {
  ACCOUNT_TOKEN_NOTIFICATION_CATEGORY,
  SystemNotification,
} from '../../../../../core/models/system-notification.model';

const SINGULAR = '1 account has an invalid token — enter a new one to resume monitoring.';
const PLURAL = '2 accounts have invalid tokens — enter new ones to resume monitoring.';

function accountToken(message: string): SystemNotification {
  return { category: ACCOUNT_TOKEN_NOTIFICATION_CATEGORY, isActive: true, message };
}

function setup(initial: SystemNotification[] = []) {
  const notificationsSignal: WritableSignal<SystemNotification[]> = signal(initial);

  TestBed.configureTestingModule({
    imports: [AccountTokenBannerComponent],
    providers: [
      provideRouter([]),
      { provide: SystemSignalRService, useValue: { notifications: notificationsSignal.asReadonly() } },
      { provide: SystemStatusService, useValue: {} },
    ],
  });

  const fixture = TestBed.createComponent(AccountTokenBannerComponent);
  fixture.detectChanges();
  TestBed.flushEffects();
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;

  const setNotifications = (notifications: SystemNotification[]): void => {
    notificationsSignal.set(notifications);
    fixture.detectChanges();
    TestBed.flushEffects();
    fixture.detectChanges();
  };

  return { fixture, el, setNotifications };
}

function region(el: HTMLElement): HTMLElement {
  return el.querySelector('[aria-label="Account token status"]') as HTMLElement;
}

function announcer(el: HTMLElement): HTMLElement {
  return el.querySelector('.sr-only[role="status"]') as HTMLElement;
}

describe('AccountTokenBannerComponent', () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('should hide the region when there is no account-token notification', () => {
    // Arrange / Act
    const { el } = setup([{ category: 'docker', isActive: true, message: '' }]);

    // Assert
    expect(region(el).hidden).toBe(true);
  });

  it('should show the server-built message', () => {
    // Arrange / Act
    const { el } = setup([accountToken(PLURAL)]);

    // Assert
    expect(region(el).hidden).toBe(false);
    expect(el.querySelector('.system-banner__message')?.textContent?.trim()).toBe(PLURAL);
  });

  it('should link to the Accounts page', () => {
    // Arrange / Act
    const { el } = setup([accountToken(SINGULAR)]);

    // Assert
    const link = el.querySelector('a.system-banner__details-link') as HTMLAnchorElement;
    expect(link.textContent?.trim()).toBe('Open Accounts');
    expect(link.getAttribute('href')).toBe('/settings/accounts');
  });

  it('should not announce the first value', () => {
    // Arrange / Act
    const { el } = setup([accountToken(SINGULAR)]);

    // Assert
    expect(announcer(el).textContent?.trim()).toBe('');
  });

  it('should announce a changed message once, then clear the announcement', () => {
    // Arrange
    const { el, fixture, setNotifications } = setup([accountToken(SINGULAR)]);

    // Act
    setNotifications([accountToken(PLURAL)]);

    // Assert — announced
    expect(announcer(el).textContent?.trim()).toBe(PLURAL);

    // Act — let the clearing timeout run
    vi.advanceTimersByTime(0);
    fixture.detectChanges();

    // Assert — cleared so later renders never re-announce
    expect(announcer(el).textContent?.trim()).toBe('');
  });

  it('should announce that monitoring resumed when the banner clears', () => {
    // Arrange
    const { el, setNotifications } = setup([accountToken(SINGULAR)]);

    // Act
    setNotifications([]);

    // Assert
    expect(region(el).hidden).toBe(true);
    expect(announcer(el).textContent?.trim()).toBe('All account tokens are valid — monitoring resumed.');
  });

  it('should keep the announcer outside the hidden region', () => {
    // Arrange / Act
    const { el } = setup([]);

    // Assert
    expect(region(el).contains(announcer(el))).toBe(false);
    expect(announcer(el).getAttribute('aria-atomic')).toBe('true');
  });
});

import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { SystemNotificationsComponent } from './system-notifications';
import { SystemSignalRService } from '../../../../../core/services/system-signalr.service';
import { SystemNotification } from '../../../../../core/models/system-notification.model';
import { GENERAL_NOTIFICATION_CATEGORIES } from './system-notifications';

function createMockSignalRService(notifications: SystemNotification[]) {
  const notificationsSignal = signal(notifications);
  return {
    notifications: notificationsSignal.asReadonly(),
    _signal: notificationsSignal,
  };
}

function setup(
  notifications: SystemNotification[] = [],
  allowedCategories: ReadonlySet<string> | null = new Set(['auth', 'license'])
) {
  const mockSignalR = createMockSignalRService(notifications);

  TestBed.configureTestingModule({
    imports: [SystemNotificationsComponent],
    providers: [
      { provide: SystemSignalRService, useValue: mockSignalR },
      ...(allowedCategories ? [{ provide: GENERAL_NOTIFICATION_CATEGORIES, useValue: allowedCategories }] : []),
    ],
  });

  const fixture = TestBed.createComponent(SystemNotificationsComponent);
  fixture.detectChanges();
  return { fixture, mockSignalR };
}

describe('SystemNotificationsComponent', () => {
  // Cycle 1: no notifications renders no notification banner
  it('should render no notification banner when there are no active notifications', () => {
    // Arrange / Act
    const { fixture } = setup([]);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const notificationBars = el.querySelectorAll('.system-banner__bar');
    expect(notificationBars.length).toBe(0);
  });

  // Cycle 2: one notification renders one bar with message text
  it('should render one bar with the notification message when one notification is active', () => {
    // Arrange
    const notification: SystemNotification = { category: 'auth', isActive: true, message: 'Claude auth is invalid' };

    // Act
    const { fixture } = setup([notification]);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const bars = el.querySelectorAll('.system-banner__bar');
    expect(bars.length).toBe(1);
    expect(bars[0].textContent?.trim()).toContain('Claude auth is invalid');
  });

  // Cycle 3: multiple notifications render multiple bars
  it('should render multiple bars when multiple notifications are active', () => {
    // Arrange
    const notifications: SystemNotification[] = [
      { category: 'auth', isActive: true, message: 'Auth invalid' },
      { category: 'license', isActive: true, message: 'License expired' },
    ];

    // Act
    const { fixture } = setup(notifications);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const bars = el.querySelectorAll('.system-banner__bar');
    expect(bars.length).toBe(2);
  });

  // Cycle 4: each bar has role="alert"
  it('should have role="alert" on each notification bar', () => {
    // Arrange
    const notification: SystemNotification = { category: 'auth', isActive: true, message: 'Auth invalid' };

    // Act
    const { fixture } = setup([notification]);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const bar = el.querySelector('.system-banner__bar') as HTMLElement;
    expect(bar?.getAttribute('role')).toBe('alert');
  });

  // Cycle 5: notification wrapper has role="region" and aria-label "System notifications"
  it('should have role="region" with aria-label "System notifications" on the notification wrapper', () => {
    // Arrange
    const notification: SystemNotification = { category: 'auth', isActive: true, message: 'Auth invalid' };

    // Act
    const { fixture } = setup([notification]);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const wrapper = el.querySelector('[aria-label="System notifications"]') as HTMLElement;
    expect(wrapper?.getAttribute('role')).toBe('region');
  });

  describe('dispatch notification filtering', () => {
    // Cycle 6: dispatch notification must not render a general alert bar
    it('should not render any general alert bar when the only notification is a dispatch notification', () => {
      // Arrange — dispatch notification with empty message (used as a reload trigger)
      const notification: SystemNotification = { category: 'dispatch', isActive: true, message: '' };

      // Act
      const { fixture } = setup([notification]);
      const el = fixture.nativeElement as HTMLElement;

      // Assert — no general bars rendered (dispatch is excluded from generalNotifications)
      const bars = el.querySelectorAll('.system-banner__bar');
      expect(bars.length).toBe(0);
    });

    // Cycle 7: dispatch notification does not produce an element with role="alert"
    it('should produce zero elements with role="alert" when the only notification is a dispatch notification', () => {
      // Arrange
      const notification: SystemNotification = { category: 'dispatch', isActive: true, message: '' };

      // Act
      const { fixture } = setup([notification]);
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      const alerts = el.querySelectorAll('[role="alert"]');
      expect(alerts.length).toBe(0);
    });

    // Cycle 8: image-build notification must not render a general alert bar
    it('should not render a general alert bar when the only notification is an image-build notification', () => {
      // Arrange
      const notification: SystemNotification = { category: 'image-build', isActive: true, message: 'Building|null' };

      // Act
      const { fixture } = setup([notification]);
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      const bars = el.querySelectorAll('.system-banner__bar');
      expect(bars.length).toBe(0);
    });

    // Cycle 9: docker notification must not render a general alert bar
    it('should not render a general alert bar when the only notification is a docker notification', () => {
      // Arrange
      const notification: SystemNotification = { category: 'docker', isActive: true, message: 'Docker is unavailable' };

      // Act
      const { fixture } = setup([notification]);
      const el = fixture.nativeElement as HTMLElement;

      // Assert — docker notifications are excluded from generalNotifications (rendered by fd-docker-banner instead)
      const bars = el.querySelectorAll('.system-banner__bar');
      expect(bars.length).toBe(0);
    });

    // Cycle 10: docker notification does not produce an element with role="alert"
    it('should produce zero elements with role="alert" when the only notification is a docker notification', () => {
      // Arrange
      const notification: SystemNotification = { category: 'docker', isActive: true, message: 'Docker is unavailable' };

      // Act
      const { fixture } = setup([notification]);
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      const alerts = el.querySelectorAll('[role="alert"]');
      expect(alerts.length).toBe(0);
    });
  });

  describe('category allow-list', () => {
    // Cycle 11: credits reload-trigger notification must not render a generic bar
    it('should not render a general alert bar when the only notification is a credits notification', () => {
      // Arrange — credits notification with empty message (handled by fd-credits-banner)
      const notification: SystemNotification = { category: 'credits', isActive: true, message: '' };

      // Act
      const { fixture } = setup([notification]);
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      expect(el.querySelectorAll('.system-banner__bar').length).toBe(0);
      expect(el.querySelectorAll('[role="alert"]').length).toBe(0);
    });

    // Cycle 12: unknown category is not rendered by default
    it('should not render a general alert bar for a category outside the allow-list', () => {
      // Arrange
      const notification: SystemNotification = { category: 'something-new', isActive: true, message: 'Hello' };

      // Act
      const { fixture } = setup([notification]);
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      expect(el.querySelectorAll('.system-banner__bar').length).toBe(0);
    });

    // Cycle 13: default allow-list renders no backend category generically
    it('should render no general alert bar for any backend category with the default allow-list', () => {
      // Arrange
      const notifications: SystemNotification[] = [
        { category: 'credits', isActive: true, message: '' },
        { category: 'dispatch', isActive: true, message: '' },
        { category: 'docker', isActive: true, message: 'Docker is unavailable' },
        { category: 'image-build', isActive: true, message: 'Building|null' },
        { category: 'auth', isActive: true, message: 'Auth invalid' },
      ];

      // Act
      const { fixture } = setup(notifications, null);
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      expect(el.querySelectorAll('.system-banner__bar').length).toBe(0);
      expect(el.querySelector('[aria-label="System notifications"]')).toBeNull();
    });

    // Cycle 14: allowed category with empty message renders no empty bar
    it('should not render an empty bar for an allowed notification with an empty message', () => {
      // Arrange
      const notifications: SystemNotification[] = [
        { category: 'auth', isActive: true, message: '   ' },
        { category: 'license', isActive: true, message: 'License expired' },
      ];

      // Act
      const { fixture } = setup(notifications);
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      const bars = el.querySelectorAll('.system-banner__bar');
      expect(bars.length).toBe(1);
      expect(bars[0].textContent?.trim()).toContain('License expired');
    });
  });
});

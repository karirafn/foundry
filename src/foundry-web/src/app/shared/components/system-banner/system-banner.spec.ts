import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { SystemBannerComponent } from './system-banner';
import { SystemSignalRService } from '../../../core/services/system-signalr.service';
import { SystemNotification } from '../../../core/models/system-notification.model';

function createMockService(notifications: SystemNotification[]) {
  const notificationsSignal = signal(notifications);
  return {
    notifications: notificationsSignal.asReadonly(),
    _signal: notificationsSignal,
  };
}

function setup(notifications: SystemNotification[] = []) {
  const mockService = createMockService(notifications);

  TestBed.configureTestingModule({
    imports: [SystemBannerComponent],
    providers: [
      { provide: SystemSignalRService, useValue: mockService },
    ],
  });

  const fixture = TestBed.createComponent(SystemBannerComponent);
  fixture.detectChanges();
  return { fixture, mockService };
}

describe('SystemBannerComponent', () => {
  // Cycle 1: no notifications renders nothing
  it('should render nothing when there are no active notifications', () => {
    // Arrange / Act
    const { fixture } = setup([]);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('.system-banner')).toBeNull();
  });

  // Cycle 2: one notification renders one bar with message text
  it('should render one bar with the notification message when one notification is active', () => {
    // Arrange / Act
    const notification: SystemNotification = { category: 'auth', isActive: true, message: 'Claude auth is invalid' };
    const { fixture } = setup([notification]);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const bars = el.querySelectorAll('.system-banner__bar');
    expect(bars.length).toBe(1);
    expect(bars[0].textContent?.trim()).toContain('Claude auth is invalid');
  });

  // Cycle 3: multiple notifications render multiple bars
  it('should render multiple bars when multiple notifications are active', () => {
    // Arrange / Act
    const notifications: SystemNotification[] = [
      { category: 'auth', isActive: true, message: 'Auth invalid' },
      { category: 'license', isActive: true, message: 'License expired' },
    ];
    const { fixture } = setup(notifications);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const bars = el.querySelectorAll('.system-banner__bar');
    expect(bars.length).toBe(2);
  });

  // Cycle 4: each bar has role="alert"
  it('should have role="alert" on each notification bar', () => {
    // Arrange / Act
    const notification: SystemNotification = { category: 'auth', isActive: true, message: 'Auth invalid' };
    const { fixture } = setup([notification]);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const bar = el.querySelector('.system-banner__bar') as HTMLElement;
    expect(bar?.getAttribute('role')).toBe('alert');
  });

  // Cycle 5: wrapper has role="region" and aria-label
  it('should have role="region" with aria-label "System notifications" on the wrapper', () => {
    // Arrange / Act
    const notification: SystemNotification = { category: 'auth', isActive: true, message: 'Auth invalid' };
    const { fixture } = setup([notification]);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const wrapper = el.querySelector('.system-banner') as HTMLElement;
    expect(wrapper?.getAttribute('role')).toBe('region');
    expect(wrapper?.getAttribute('aria-label')).toBe('System notifications');
  });
});

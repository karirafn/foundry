import { TestBed } from '@angular/core/testing';
import { SystemSignalRService, SYSTEM_HUB_FACTORY, SystemHub } from './system-signalr.service';
import { SystemNotification } from '../models/system-notification.model';

interface CapturedHubCallbacks {
  onSystemNotificationReceived: ((notification: SystemNotification) => void) | null;
  onReconnected: (() => void) | null;
}

function buildMockHub(captured: CapturedHubCallbacks): SystemHub {
  return {
    on: (_method: string, cb: (notification: SystemNotification) => void) => {
      captured.onSystemNotificationReceived = cb;
    },
    onReconnected: (cb: () => void) => {
      captured.onReconnected = cb;
    },
    start: () => Promise.resolve(),
  };
}

function setup() {
  TestBed.resetTestingModule();

  const captured: CapturedHubCallbacks = { onSystemNotificationReceived: null, onReconnected: null };
  const mockHubFactory = () => buildMockHub(captured);

  TestBed.configureTestingModule({
    providers: [
      SystemSignalRService,
      { provide: SYSTEM_HUB_FACTORY, useValue: mockHubFactory },
    ],
  });

  return {
    svc: TestBed.inject(SystemSignalRService),
    captured,
  };
}

describe('SystemSignalRService', () => {
  // Cycle 1: receiving an active notification adds it to the signal
  it('should add an active notification to the notifications signal', () => {
    // Arrange
    const { svc, captured } = setup();
    const notification: SystemNotification = {
      category: 'auth',
      isActive: true,
      message: 'Claude auth is invalid',
    };

    // Act
    captured.onSystemNotificationReceived!(notification);

    // Assert
    expect(svc.notifications()).toEqual([notification]);
  });

  // Cycle 2: receiving an inactive notification removes it from the signal
  it('should remove a notification when an inactive notification is received for its category', () => {
    // Arrange
    const { svc, captured } = setup();
    const active: SystemNotification = { category: 'auth', isActive: true, message: 'Auth invalid' };
    captured.onSystemNotificationReceived!(active);
    expect(svc.notifications().length).toBe(1);

    const inactive: SystemNotification = { category: 'auth', isActive: false, message: '' };

    // Act
    captured.onSystemNotificationReceived!(inactive);

    // Assert
    expect(svc.notifications()).toEqual([]);
  });

  // Cycle 3: receiving a new message for an existing category replaces it
  it('should replace an existing notification when upserted by category', () => {
    // Arrange
    const { svc, captured } = setup();
    const first: SystemNotification = { category: 'auth', isActive: true, message: 'First message' };
    captured.onSystemNotificationReceived!(first);

    const updated: SystemNotification = { category: 'auth', isActive: true, message: 'Updated message' };

    // Act
    captured.onSystemNotificationReceived!(updated);

    // Assert
    expect(svc.notifications().length).toBe(1);
    expect(svc.notifications()[0].message).toBe('Updated message');
  });

  // Cycle 4: multiple categories can coexist
  it('should maintain multiple simultaneous notifications for different categories', () => {
    // Arrange
    const { svc, captured } = setup();
    const authNotification: SystemNotification = { category: 'auth', isActive: true, message: 'Auth invalid' };
    const licenseNotification: SystemNotification = { category: 'license', isActive: true, message: 'License expired' };

    // Act
    captured.onSystemNotificationReceived!(authNotification);
    captured.onSystemNotificationReceived!(licenseNotification);

    // Assert
    expect(svc.notifications().length).toBe(2);
    expect(svc.notifications().some((n) => n.category === 'auth')).toBe(true);
    expect(svc.notifications().some((n) => n.category === 'license')).toBe(true);
  });

  // Cycle 5: reconnect callback is registered on the hub
  it('should register an onReconnected callback with the hub', () => {
    // Arrange / Act
    const { captured } = setup();

    // Assert
    expect(captured.onReconnected).not.toBeNull();
  });

  // Cycle 6: firing the reconnect callback emits on reconnected observable
  it('should emit on reconnected observable when the hub reconnects', () => {
    // Arrange
    const { svc, captured } = setup();
    let emitCount = 0;
    svc.reconnected.subscribe(() => emitCount++);

    // Act
    captured.onReconnected!();

    // Assert
    expect(emitCount).toBe(1);
  });

  // Cycle 7: reconnected is an Observable, not a writable Subject
  it('should expose reconnected as an Observable (no next() method)', () => {
    // Arrange / Act
    const { svc } = setup();

    // Assert — Observable does not expose next(), so callers cannot emit spurious events
    expect((svc.reconnected as unknown as { next?: unknown }).next).toBeUndefined();
  });
});

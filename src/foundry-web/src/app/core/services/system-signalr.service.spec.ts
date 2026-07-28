import { TestBed } from '@angular/core/testing';
import { SystemSignalRService, SYSTEM_HUB_FACTORY, SystemHub } from './system-signalr.service';
import { DISPATCH_NOTIFICATION_CATEGORY, DOCKER_NOTIFICATION_CATEGORY, SystemNotification } from '../models/system-notification.model';
import { DOCKER_UNAVAILABLE_MESSAGE } from '../models/system-status.model';
import { LoginSessionUpdate } from '../models/settings.model';

interface CapturedHubCallbacks {
  onSystemNotificationReceived: ((notification: SystemNotification) => void) | null;
  onLoginSessionUpdated: ((update: LoginSessionUpdate) => void) | null;
  onReconnected: (() => void) | null;
}

// eslint-disable-next-line @typescript-eslint/no-explicit-any
function buildMockHub(captured: CapturedHubCallbacks): SystemHub {
  return {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    on: (method: string, cb: (...args: any[]) => void) => {
      if (method === 'SystemNotificationReceived') {
        captured.onSystemNotificationReceived = cb as (notification: SystemNotification) => void;
      } else if (method === 'LoginSessionUpdated') {
        captured.onLoginSessionUpdated = cb as (update: LoginSessionUpdate) => void;
      }
    },
    onReconnected: (cb: () => void) => {
      captured.onReconnected = cb;
    },
    start: () => Promise.resolve(),
  };
}

function setup() {
  TestBed.resetTestingModule();

  const captured: CapturedHubCallbacks = {
    onSystemNotificationReceived: null,
    onLoginSessionUpdated: null,
    onReconnected: null,
  };
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

  // Cycle 8: dispatchStateChanged emits when a dispatch notification arrives with isActive: true
  it('should emit on dispatchStateChanged when a dispatch notification with isActive: true arrives', () => {
    // Arrange
    const { svc, captured } = setup();
    let emitCount = 0;
    svc.dispatchStateChanged.subscribe(() => emitCount++);
    const notification: SystemNotification = {
      category: DISPATCH_NOTIFICATION_CATEGORY,
      isActive: true,
      message: 'Usage limit hit',
    };

    // Act
    captured.onSystemNotificationReceived!(notification);

    // Assert
    expect(emitCount).toBe(1);
  });

  // Cycle 9: dispatchStateChanged emits when a dispatch notification arrives with isActive: false
  it('should emit on dispatchStateChanged when a dispatch notification with isActive: false arrives', () => {
    // Arrange
    const { svc, captured } = setup();
    let emitCount = 0;
    svc.dispatchStateChanged.subscribe(() => emitCount++);
    const notification: SystemNotification = {
      category: DISPATCH_NOTIFICATION_CATEGORY,
      isActive: false,
      message: '',
    };

    // Act
    captured.onSystemNotificationReceived!(notification);

    // Assert
    expect(emitCount).toBe(1);
  });

  // Cycle 10: dispatchStateChanged does NOT emit for a non-dispatch category
  it('should not emit on dispatchStateChanged for a non-dispatch category notification', () => {
    // Arrange
    const { svc, captured } = setup();
    let emitCount = 0;
    svc.dispatchStateChanged.subscribe(() => emitCount++);
    const notification: SystemNotification = {
      category: 'image-build',
      isActive: true,
      message: 'Build in progress',
    };

    // Act
    captured.onSystemNotificationReceived!(notification);

    // Assert
    expect(emitCount).toBe(0);
  });

  // Cycle 11: dispatchStateChanged is an Observable, not a writable Subject
  it('should expose dispatchStateChanged as an Observable (no next() method)', () => {
    // Arrange / Act
    const { svc } = setup();

    // Assert — Observable does not expose next(), so callers cannot emit spurious events
    expect((svc.dispatchStateChanged as unknown as { next?: unknown }).next).toBeUndefined();
  });

  // Cycle 12: LoginSessionUpdated handler is registered on the hub
  it('should register a LoginSessionUpdated handler on the hub', () => {
    // Arrange / Act
    const { captured } = setup();

    // Assert
    expect(captured.onLoginSessionUpdated).not.toBeNull();
  });

  // Cycle 13: receiving a LoginSessionUpdated message emits on loginSessionUpdate
  it('should emit on loginSessionUpdate when a LoginSessionUpdated message arrives', () => {
    // Arrange
    const { svc, captured } = setup();
    const update: LoginSessionUpdate = {
      sessionId: 'session-1',
      phase: 'WaitingForAuthorization',
      authorizationUrl: 'https://claude.ai/oauth/authorize?code=abc',
      failureReason: null,
      failureMessage: null,
    };
    let received: LoginSessionUpdate | null = null;
    svc.loginSessionUpdate.subscribe((u) => (received = u));

    // Act
    captured.onLoginSessionUpdated!(update);

    // Assert
    expect(received).toEqual(update);
  });

  // Cycle 14: loginSessionUpdate is an Observable, not a writable Subject
  it('should expose loginSessionUpdate as an Observable (no next() method)', () => {
    // Arrange / Act
    const { svc } = setup();

    // Assert
    expect((svc.loginSessionUpdate as unknown as { next?: unknown }).next).toBeUndefined();
  });

  // Cycle 15: separate LoginSessionUpdated messages each emit independently
  it('should emit each LoginSessionUpdated message independently', () => {
    // Arrange
    const { svc, captured } = setup();
    const updates: LoginSessionUpdate[] = [];
    svc.loginSessionUpdate.subscribe((u) => updates.push(u));
    const first: LoginSessionUpdate = { sessionId: 'session-1', phase: 'Starting', authorizationUrl: null, failureReason: null, failureMessage: null };
    const second: LoginSessionUpdate = { sessionId: 'session-1', phase: 'WaitingForAuthorization', authorizationUrl: 'https://claude.ai', failureReason: null, failureMessage: null };

    // Act
    captured.onLoginSessionUpdated!(first);
    captured.onLoginSessionUpdated!(second);

    // Assert
    expect(updates.length).toBe(2);
    expect(updates[0].phase).toBe('Starting');
    expect(updates[1].phase).toBe('WaitingForAuthorization');
  });

  // Cycle 16: applyDockerAvailability(false) — Docker down — adds a docker notification
  it('should add a docker notification slot when applyDockerAvailability is called with false', () => {
    // Arrange
    const { svc } = setup();

    // Act
    svc.applyDockerAvailability(false);

    // Assert
    const notifications = svc.notifications();
    expect(notifications.length).toBe(1);
    expect(notifications[0].category).toBe(DOCKER_NOTIFICATION_CATEGORY);
    expect(notifications[0].isActive).toBe(true);
    expect(notifications[0].message).toBe(DOCKER_UNAVAILABLE_MESSAGE);
  });

  // Cycle 17: applyDockerAvailability(true) — Docker recovered — clears the docker notification slot
  it('should clear the docker notification slot when applyDockerAvailability is called with true', () => {
    // Arrange
    const { svc } = setup();
    svc.applyDockerAvailability(false);
    expect(svc.notifications().length).toBe(1);

    // Act
    svc.applyDockerAvailability(true);

    // Assert
    expect(svc.notifications().length).toBe(0);
  });

  // Cycle 18: applyDockerAvailability does not affect other notification categories
  it('should not remove other category notifications when applying docker availability', () => {
    // Arrange
    const { svc, captured } = setup();
    const authNotification: SystemNotification = { category: 'auth', isActive: true, message: 'Auth invalid' };
    captured.onSystemNotificationReceived!(authNotification);
    expect(svc.notifications().length).toBe(1);

    // Act
    svc.applyDockerAvailability(false);

    // Assert
    expect(svc.notifications().length).toBe(2);
    expect(svc.notifications().some((n) => n.category === 'auth')).toBe(true);
    expect(svc.notifications().some((n) => n.category === DOCKER_NOTIFICATION_CATEGORY)).toBe(true);
  });
});

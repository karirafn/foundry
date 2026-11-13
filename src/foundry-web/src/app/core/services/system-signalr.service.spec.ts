import { TestBed } from '@angular/core/testing';
import { SystemSignalRService } from './system-signalr.service';
import { HubConnections } from './hub-connection';
import { FakeHubTransport, provideFakeHubs } from '../testing/fake-hub-transport';
import {
  CREDITS_NOTIFICATION_CATEGORY,
  DISPATCH_NOTIFICATION_CATEGORY,
  DOCKER_NOTIFICATION_CATEGORY,
  IMAGE_BUILD_NOTIFICATION_CATEGORY,
  SystemNotification,
} from '../models/system-notification.model';
import { DOCKER_UNAVAILABLE_MESSAGE } from '../models/system-status.model';
import { LoginSessionUpdate } from '../models/settings.model';
import { UsageSnapshot } from '../models/usage.model';

function setup() {
  TestBed.resetTestingModule();

  const hub = new FakeHubTransport();

  TestBed.configureTestingModule({
    providers: [
      SystemSignalRService,
      provideFakeHubs({ system: hub }),
    ],
  });

  return {
    svc: TestBed.inject(SystemSignalRService),
    hub,
  };
}

describe('SystemSignalRService', () => {
  // Cycle 1: receiving an active notification adds it to the signal
  it('should add an active notification to the notifications signal', () => {
    // Arrange
    const { svc, hub } = setup();
    const notification: SystemNotification = {
      category: 'auth',
      isActive: true,
      message: 'Claude auth is invalid',
    };

    // Act
    hub.emit('SystemNotificationReceived', notification);

    // Assert
    expect(svc.notifications()).toEqual([notification]);
  });

  // Cycle 2: receiving an inactive notification removes it from the signal
  it('should remove a notification when an inactive notification is received for its category', () => {
    // Arrange
    const { svc, hub } = setup();
    const active: SystemNotification = { category: 'auth', isActive: true, message: 'Auth invalid' };
    hub.emit('SystemNotificationReceived', active);
    expect(svc.notifications().length).toBe(1);

    const inactive: SystemNotification = { category: 'auth', isActive: false, message: '' };

    // Act
    hub.emit('SystemNotificationReceived', inactive);

    // Assert
    expect(svc.notifications()).toEqual([]);
  });

  // Cycle 3: receiving a new message for an existing category replaces it
  it('should replace an existing notification when upserted by category', () => {
    // Arrange
    const { svc, hub } = setup();
    const first: SystemNotification = { category: 'auth', isActive: true, message: 'First message' };
    hub.emit('SystemNotificationReceived', first);

    const updated: SystemNotification = { category: 'auth', isActive: true, message: 'Updated message' };

    // Act
    hub.emit('SystemNotificationReceived', updated);

    // Assert
    expect(svc.notifications().length).toBe(1);
    expect(svc.notifications()[0].message).toBe('Updated message');
  });

  // Cycle 4: multiple categories can coexist
  it('should maintain multiple simultaneous notifications for different categories', () => {
    // Arrange
    const { svc, hub } = setup();
    const authNotification: SystemNotification = { category: 'auth', isActive: true, message: 'Auth invalid' };
    const licenseNotification: SystemNotification = { category: 'license', isActive: true, message: 'License expired' };

    // Act
    hub.emit('SystemNotificationReceived', authNotification);
    hub.emit('SystemNotificationReceived', licenseNotification);

    // Assert
    expect(svc.notifications().length).toBe(2);
    expect(svc.notifications().some((n) => n.category === 'auth')).toBe(true);
    expect(svc.notifications().some((n) => n.category === 'license')).toBe(true);
  });

  // Cycle 5: the system hub's connection status is available through the shared connection
  it('should expose the system hub status through the shared connection', async () => {
    // Arrange
    const { hub } = setup();
    const status = TestBed.inject(HubConnections).get('system').status;

    // Act
    await Promise.resolve();
    const afterStart = status();
    hub.reconnecting();

    // Assert
    expect(hub.startCount).toBe(1);
    expect(afterStart).toBe('connected');
    expect(status()).toBe('reconnecting');
  });

  // Cycle 6: firing the reconnect callback emits on reconnected observable
  it('should emit on reconnected observable when the hub reconnects', () => {
    // Arrange
    const { svc, hub } = setup();
    let emitCount = 0;
    svc.reconnected.subscribe(() => emitCount++);

    // Act
    hub.reconnected();

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

  // Cycle 8: reloadTrigger emits when a dispatch notification arrives with isActive: true
  it('should emit on reloadTrigger when a dispatch notification with isActive: true arrives', () => {
    // Arrange
    const { svc, hub } = setup();
    let emitCount = 0;
    svc.reloadTrigger.subscribe(() => emitCount++);
    const notification: SystemNotification = {
      category: DISPATCH_NOTIFICATION_CATEGORY,
      isActive: true,
      message: 'Usage limit hit',
    };

    // Act
    hub.emit('SystemNotificationReceived', notification);

    // Assert
    expect(emitCount).toBe(1);
  });

  // Cycle 9: reloadTrigger emits when a dispatch notification arrives with isActive: false
  it('should emit on reloadTrigger when a dispatch notification with isActive: false arrives', () => {
    // Arrange
    const { svc, hub } = setup();
    let emitCount = 0;
    svc.reloadTrigger.subscribe(() => emitCount++);
    const notification: SystemNotification = {
      category: DISPATCH_NOTIFICATION_CATEGORY,
      isActive: false,
      message: '',
    };

    // Act
    hub.emit('SystemNotificationReceived', notification);

    // Assert
    expect(emitCount).toBe(1);
  });

  // Cycle 10: reloadTrigger emits when an image-build notification arrives
  it('should emit on reloadTrigger when an image-build notification arrives', () => {
    // Arrange
    const { svc, hub } = setup();
    let emitCount = 0;
    svc.reloadTrigger.subscribe(() => emitCount++);
    const notification: SystemNotification = {
      category: IMAGE_BUILD_NOTIFICATION_CATEGORY,
      isActive: false,
      message: '',
    };

    // Act
    hub.emit('SystemNotificationReceived', notification);

    // Assert
    expect(emitCount).toBe(1);
  });

  // Cycle 11: reloadTrigger does NOT emit for an undeclared category (docker)
  it('should not emit on reloadTrigger for an undeclared category (docker)', () => {
    // Arrange
    const { svc, hub } = setup();
    let emitCount = 0;
    svc.reloadTrigger.subscribe(() => emitCount++);
    const notification: SystemNotification = {
      category: DOCKER_NOTIFICATION_CATEGORY,
      isActive: true,
      message: 'Docker unavailable',
    };

    // Act
    hub.emit('SystemNotificationReceived', notification);

    // Assert
    expect(emitCount).toBe(0);
  });

  // Cycle 12: reloadTrigger is an Observable, not a writable Subject
  it('should expose reloadTrigger as an Observable (no next() method)', () => {
    // Arrange / Act
    const { svc } = setup();

    // Assert — Observable does not expose next(), so callers cannot emit spurious events
    expect((svc.reloadTrigger as unknown as { next?: unknown }).next).toBeUndefined();
  });

  // Cycle 13: LoginSessionUpdated handler is registered on the hub
  it('should register a LoginSessionUpdated handler on the hub', () => {
    // Arrange / Act
    const { hub } = setup();

    // Assert
    expect(hub.hasHandler('LoginSessionUpdated')).toBe(true);
  });

  // Cycle 14: receiving a LoginSessionUpdated message emits on loginSessionUpdate
  it('should emit on loginSessionUpdate when a LoginSessionUpdated message arrives', () => {
    // Arrange
    const { svc, hub } = setup();
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
    hub.emit('LoginSessionUpdated', update);

    // Assert
    expect(received).toEqual(update);
  });

  // Cycle 15: loginSessionUpdate is an Observable, not a writable Subject
  it('should expose loginSessionUpdate as an Observable (no next() method)', () => {
    // Arrange / Act
    const { svc } = setup();

    // Assert
    expect((svc.loginSessionUpdate as unknown as { next?: unknown }).next).toBeUndefined();
  });

  // Cycle 16: separate LoginSessionUpdated messages each emit independently
  it('should emit each LoginSessionUpdated message independently', () => {
    // Arrange
    const { svc, hub } = setup();
    const updates: LoginSessionUpdate[] = [];
    svc.loginSessionUpdate.subscribe((u) => updates.push(u));
    const first: LoginSessionUpdate = { sessionId: 'session-1', phase: 'Starting', authorizationUrl: null, failureReason: null, failureMessage: null };
    const second: LoginSessionUpdate = { sessionId: 'session-1', phase: 'WaitingForAuthorization', authorizationUrl: 'https://claude.ai', failureReason: null, failureMessage: null };

    // Act
    hub.emit('LoginSessionUpdated', first);
    hub.emit('LoginSessionUpdated', second);

    // Assert
    expect(updates.length).toBe(2);
    expect(updates[0].phase).toBe('Starting');
    expect(updates[1].phase).toBe('WaitingForAuthorization');
  });

  // Cycle 17: applyDockerAvailability(false) — Docker down — adds a docker notification
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

  // Cycle 18: applyDockerAvailability(true) — Docker recovered — clears the docker notification slot
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

  // Cycle 19: applyDockerAvailability does not affect other notification categories
  it('should not remove other category notifications when applying docker availability', () => {
    // Arrange
    const { svc, hub } = setup();
    const authNotification: SystemNotification = { category: 'auth', isActive: true, message: 'Auth invalid' };
    hub.emit('SystemNotificationReceived', authNotification);
    expect(svc.notifications().length).toBe(1);

    // Act
    svc.applyDockerAvailability(false);

    // Assert
    expect(svc.notifications().length).toBe(2);
    expect(svc.notifications().some((n) => n.category === 'auth')).toBe(true);
    expect(svc.notifications().some((n) => n.category === DOCKER_NOTIFICATION_CATEGORY)).toBe(true);
  });

  // Cycle 20: creditsNotification emits when a credits notification arrives
  it('should emit on creditsNotification when a credits system notification arrives', () => {
    // Arrange
    const { svc, hub } = setup();
    let received: SystemNotification | null = null;
    svc.creditsNotification.subscribe((n) => (received = n));
    const notification: SystemNotification = {
      category: CREDITS_NOTIFICATION_CATEGORY,
      isActive: true,
      message: 'Credits exhausted',
    };

    // Act
    hub.emit('SystemNotificationReceived', notification);

    // Assert
    expect(received).toEqual(notification);
  });

  // Cycle 21: creditsNotification does not emit for non-credits categories
  it('should not emit on creditsNotification for non-credits categories', () => {
    // Arrange
    const { svc, hub } = setup();
    let emitCount = 0;
    svc.creditsNotification.subscribe(() => emitCount++);
    const notification: SystemNotification = {
      category: DISPATCH_NOTIFICATION_CATEGORY,
      isActive: true,
      message: 'Dispatch paused',
    };

    // Act
    hub.emit('SystemNotificationReceived', notification);

    // Assert
    expect(emitCount).toBe(0);
  });

  // Cycle 22: creditsNotification is an Observable (not a writable Subject)
  it('should expose creditsNotification as an Observable (no next() method)', () => {
    // Arrange / Act
    const { svc } = setup();

    // Assert
    expect((svc.creditsNotification as unknown as { next?: unknown }).next).toBeUndefined();
  });

  // Cycle 23: UsageSnapshotUpdated handler is registered on the hub
  it('should register a UsageSnapshotUpdated handler on the hub', () => {
    // Arrange / Act
    const { hub } = setup();

    // Assert
    expect(hub.hasHandler('UsageSnapshotUpdated')).toBe(true);
  });

  // Cycle 24: receiving a UsageSnapshotUpdated message with a snapshot emits on usageSnapshotUpdated
  it('should emit on usageSnapshotUpdated when a UsageSnapshotUpdated message arrives with a snapshot', () => {
    // Arrange
    const { svc, hub } = setup();
    const snapshot: UsageSnapshot = {
      limits: [{ kind: 'session', percent: 55, resetsAt: '2026-10-30T06:00:00Z', modelDisplayName: null, severity: 'normal', isActive: true }],
      extraUsage: null,
      capturedAt: '2026-10-30T01:00:00Z',
      accountEmail: 'user@example.com',
      lastRefreshFailedAt: null,
      source: 'usageReport',
    };
    let received: UsageSnapshot | null | undefined;
    svc.usageSnapshotUpdated.subscribe((s) => (received = s));

    // Act
    hub.emit('UsageSnapshotUpdated', snapshot);

    // Assert
    expect(received).toEqual(snapshot);
  });

  // Cycle 25: receiving a UsageSnapshotUpdated message with null emits null on usageSnapshotUpdated
  it('should emit null on usageSnapshotUpdated when a UsageSnapshotUpdated message arrives with null', () => {
    // Arrange
    const { svc, hub } = setup();
    let received: UsageSnapshot | null | undefined;
    svc.usageSnapshotUpdated.subscribe((s) => (received = s));

    // Act
    hub.emit('UsageSnapshotUpdated', null);

    // Assert
    expect(received).toBeNull();
  });

  // Cycle 26: usageSnapshotUpdated is an Observable (not a writable Subject)
  it('should expose usageSnapshotUpdated as an Observable (no next() method)', () => {
    // Arrange / Act
    const { svc } = setup();

    // Assert
    expect((svc.usageSnapshotUpdated as unknown as { next?: unknown }).next).toBeUndefined();
  });
});

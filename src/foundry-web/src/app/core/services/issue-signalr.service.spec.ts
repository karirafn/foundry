import { TestBed } from '@angular/core/testing';
import { IssueSignalRService } from './issue-signalr.service';
import { FakeHubTransport, provideFakeHubs } from '../testing/fake-hub-transport';

function setup(hub = new FakeHubTransport()) {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({ providers: [provideFakeHubs({ issues: hub })] });
  return { svc: TestBed.inject(IssueSignalRService), hub };
}

describe('IssueSignalRService', () => {
  it('should start the issues hub and report connected', async () => {
    // Arrange
    const { svc, hub } = setup();

    // Act
    await Promise.resolve();

    // Assert
    expect(hub.startCount).toBe(1);
    expect(svc.connectionStatus()).toBe('connected');
  });

  it('should stay disconnected when the first start fails', async () => {
    // Arrange
    const hub = new FakeHubTransport();
    hub.startResult = () => Promise.reject(new Error('refused'));

    // Act
    const { svc } = setup(hub);
    await Promise.resolve();
    await Promise.resolve();

    // Assert
    expect(svc.connectionStatus()).toBe('disconnected');
    expect(hub.startCount).toBe(1);
  });

  it('should track reconnecting, reconnected and close', async () => {
    // Arrange
    const { svc, hub } = setup();
    await Promise.resolve();
    const seen: string[] = [];

    // Act
    hub.reconnecting();
    seen.push(svc.connectionStatus());
    hub.reconnected();
    seen.push(svc.connectionStatus());
    hub.close();
    seen.push(svc.connectionStatus());

    // Assert
    expect(seen).toEqual(['reconnecting', 'connected', 'disconnected']);
  });

  it('should deliver hub messages to handlers registered with on until removed with off', () => {
    // Arrange
    const { svc, hub } = setup();
    const received: string[] = [];
    const handler = (data: string) => received.push(data);
    svc.on<string>('IssueUpdated', handler);

    // Act
    hub.emit('IssueUpdated', 'first');
    svc.off<string>('IssueUpdated', handler);
    hub.emit('IssueUpdated', 'second');

    // Assert
    expect(received).toEqual(['first']);
  });

  it('should run onReconnected callbacks once the status is connected again', () => {
    // Arrange
    const { svc, hub } = setup();
    const seen: string[] = [];
    svc.onReconnected(() => seen.push(svc.connectionStatus()));
    hub.reconnecting();

    // Act
    hub.reconnected();

    // Assert
    expect(seen).toEqual(['connected']);
  });
});

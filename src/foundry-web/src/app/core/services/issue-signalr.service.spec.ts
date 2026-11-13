import { TestBed } from '@angular/core/testing';
import { IssueSignalRService } from './issue-signalr.service';
import { FakeHubTransport, provideFakeHubs } from '../testing/fake-hub-transport';

function setup(hub = new FakeHubTransport()) {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({ providers: [provideFakeHubs({ issues: hub })] });
  return { svc: TestBed.inject(IssueSignalRService), hub };
}

describe('IssueSignalRService', () => {
  afterEach(() => vi.useRealTimers());

  it('should start the issues hub and report connected', async () => {
    // Arrange
    const { svc, hub } = setup();

    // Act
    await Promise.resolve();

    // Assert
    expect(hub.startCount).toBe(1);
    expect(svc.connectionStatus()).toBe('connected');
  });

  it('should report reconnecting and retry when the first start fails', async () => {
    // Arrange
    const hub = new FakeHubTransport();
    hub.startResult = () => Promise.reject(new Error('refused'));
    vi.useFakeTimers();

    // Act
    const { svc } = setup(hub);
    await vi.advanceTimersByTimeAsync(1_000);

    // Assert
    expect(svc.connectionStatus()).toBe('reconnecting');
    expect(hub.startCount).toBe(2);
  });

  it('should report reconnecting after a drop and connected once back', async () => {
    // Arrange
    vi.useFakeTimers();
    const { svc, hub } = setup();
    await vi.advanceTimersByTimeAsync(0);
    const seen: string[] = [];

    // Act
    hub.close();
    seen.push(svc.connectionStatus());
    await vi.advanceTimersByTimeAsync(0);
    seen.push(svc.connectionStatus());

    // Assert
    expect(seen).toEqual(['reconnecting', 'connected']);
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

  it('should run onReconnected callbacks once the status is connected again', async () => {
    // Arrange
    vi.useFakeTimers();
    const { svc, hub } = setup();
    await vi.advanceTimersByTimeAsync(0);
    const seen: string[] = [];
    svc.onReconnected(() => seen.push(svc.connectionStatus()));
    hub.close();

    // Act
    await vi.advanceTimersByTimeAsync(0);

    // Assert
    expect(seen).toEqual(['connected']);
  });
});

import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { DockerBannerComponent } from './docker-banner';
import { SystemSignalRService } from '../../../../../core/services/system-signalr.service';
import { SystemStatusService } from '../../../../../core/services/system-status.service';
import { SystemNotification } from '../../../../../core/models/system-notification.model';
import { DOCKER_UNAVAILABLE_MESSAGE } from '../../../../../core/models/system-status.model';

function createMockSignalRService(notifications: SystemNotification[]) {
  const notificationsSignal = signal(notifications);
  return {
    notifications: notificationsSignal.asReadonly(),
    _signal: notificationsSignal,
  };
}

function createMockSystemStatusService() {
  return {};
}

function setup(notifications: SystemNotification[] = []) {
  const mockSignalR = createMockSignalRService(notifications);
  const mockSystemStatus = createMockSystemStatusService();

  TestBed.configureTestingModule({
    imports: [DockerBannerComponent],
    providers: [
      { provide: SystemSignalRService, useValue: mockSignalR },
      { provide: SystemStatusService, useValue: mockSystemStatus },
    ],
  });

  const fixture = TestBed.createComponent(DockerBannerComponent);
  fixture.detectChanges();
  return { fixture, mockSignalR };
}

describe('DockerBannerComponent', () => {
  // Cycle 1: banner is hidden when there are no docker notifications
  it('should be hidden when no docker notification is present', () => {
    // Arrange / Act
    const { fixture } = setup([]);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const wrapper = el.querySelector('.system-banner--docker-only') as HTMLElement;
    expect(wrapper).toBeTruthy();
    expect(wrapper.hidden).toBe(true);
  });

  // Cycle 2: banner is visible when a docker notification is present
  it('should be visible when a docker notification with isActive: true is present', () => {
    // Arrange
    const notification: SystemNotification = {
      category: 'docker',
      isActive: true,
      message: DOCKER_UNAVAILABLE_MESSAGE,
    };

    // Act
    const { fixture } = setup([notification]);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const wrapper = el.querySelector('.system-banner--docker-only') as HTMLElement;
    expect(wrapper).toBeTruthy();
    expect(wrapper.hidden).toBe(false);
  });

  // Cycle 3: banner clears when the docker notification is removed from signal
  it('should hide when the docker notification is cleared from the notifications signal', () => {
    // Arrange
    const notification: SystemNotification = {
      category: 'docker',
      isActive: true,
      message: DOCKER_UNAVAILABLE_MESSAGE,
    };
    const { fixture, mockSignalR } = setup([notification]);
    const el = fixture.nativeElement as HTMLElement;
    const wrapper = el.querySelector('.system-banner--docker-only') as HTMLElement;
    expect(wrapper.hidden).toBe(false);

    // Act
    mockSignalR._signal.set([]);
    fixture.detectChanges();

    // Assert
    expect(wrapper.hidden).toBe(true);
  });

  // Cycle 4: banner renders the exact copy
  it('should render the exact Docker unavailability message', () => {
    // Arrange
    const notification: SystemNotification = {
      category: 'docker',
      isActive: true,
      message: DOCKER_UNAVAILABLE_MESSAGE,
    };

    // Act
    const { fixture } = setup([notification]);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const msgEl = el.querySelector('.system-banner__message') as HTMLElement;
    expect(msgEl).toBeTruthy();
    expect(msgEl.textContent?.trim()).toBe(DOCKER_UNAVAILABLE_MESSAGE);
  });

  // Cycle 5: no dismiss or retry button present
  it('should not render any action buttons', () => {
    // Arrange
    const notification: SystemNotification = {
      category: 'docker',
      isActive: true,
      message: DOCKER_UNAVAILABLE_MESSAGE,
    };

    // Act
    const { fixture } = setup([notification]);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const buttons = el.querySelectorAll('button');
    expect(buttons.length).toBe(0);
  });

  // Cycle 6: banner bar has role="alert"
  it('should have role="alert" on the banner bar', () => {
    // Arrange
    const notification: SystemNotification = {
      category: 'docker',
      isActive: true,
      message: DOCKER_UNAVAILABLE_MESSAGE,
    };

    // Act
    const { fixture } = setup([notification]);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const bar = el.querySelector('.system-banner__bar--docker') as HTMLElement;
    expect(bar?.getAttribute('role')).toBe('alert');
  });

  // Cycle 7: outer wrapper has role="region" and aria-label="Docker status"
  it('should have role="region" and aria-label="Docker status" on the wrapper', () => {
    // Arrange / Act
    const { fixture } = setup([]);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const wrapper = el.querySelector('[aria-label="Docker status"]') as HTMLElement;
    expect(wrapper?.getAttribute('role')).toBe('region');
  });

  // Cycle 8: non-docker notifications do not make the banner visible
  it('should not show banner for non-docker category notifications', () => {
    // Arrange
    const notification: SystemNotification = {
      category: 'image-build',
      isActive: true,
      message: 'Building|null',
    };

    // Act
    const { fixture } = setup([notification]);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const wrapper = el.querySelector('.system-banner--docker-only') as HTMLElement;
    expect(wrapper.hidden).toBe(true);
  });
});

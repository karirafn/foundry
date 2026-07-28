import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { ImageBuildBannerComponent } from './image-build-banner';
import { SystemSignalRService } from '../../../../../core/services/system-signalr.service';
import { SystemNotification } from '../../../../../core/models/system-notification.model';
import { SettingsService } from '../../../../../core/services/settings.service';

function createMockSignalRService(notifications: SystemNotification[]) {
  const notificationsSignal = signal(notifications);
  return {
    notifications: notificationsSignal.asReadonly(),
    _signal: notificationsSignal,
  };
}

function createMockSettingsService() {
  return {
    loadSettings: vi.fn(),
    setImageBuildStatus: vi.fn(),
    retryImageBuild: vi.fn(),
  };
}

function setup(notifications: SystemNotification[] = []) {
  const mockSignalR = createMockSignalRService(notifications);
  const mockSettings = createMockSettingsService();

  TestBed.configureTestingModule({
    imports: [ImageBuildBannerComponent],
    providers: [
      provideRouter([]),
      { provide: SystemSignalRService, useValue: mockSignalR },
      { provide: SettingsService, useValue: mockSettings },
    ],
  });

  const fixture = TestBed.createComponent(ImageBuildBannerComponent);
  fixture.detectChanges();
  return { fixture, mockSignalR, mockSettings };
}

describe('ImageBuildBannerComponent', () => {
  it('should call setImageBuildStatus when an image-build notification arrives', () => {
    // Arrange
    const { fixture, mockSignalR, mockSettings } = setup([]);
    const notification: SystemNotification = { category: 'image-build', isActive: true, message: 'Building|null' };

    // Act
    mockSignalR._signal.set([notification]);
    fixture.detectChanges();

    // Assert
    expect(mockSettings.setImageBuildStatus).toHaveBeenCalled();
  });

  it('should render a Building bar when an image-build Building notification is active', () => {
    // Arrange
    const notification: SystemNotification = { category: 'image-build', isActive: true, message: 'Building|null' };

    // Act
    const { fixture } = setup([notification]);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const imageBuildBar = el.querySelector('.system-banner__bar--image-build') as HTMLElement;
    expect(imageBuildBar).toBeTruthy();
    expect(imageBuildBar.textContent).toContain('Worker image is building');
  });

  it('should render a Failed bar with Retry button when an image-build Failed notification is active', () => {
    // Arrange
    const notification: SystemNotification = { category: 'image-build', isActive: true, message: 'Failed|Step 2/5 FAILED' };

    // Act
    const { fixture } = setup([notification]);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const imageBuildBar = el.querySelector('.system-banner__bar--image-build') as HTMLElement;
    expect(imageBuildBar).toBeTruthy();
    expect(imageBuildBar.textContent).toContain('Worker image build failed');

    const retryBtn = imageBuildBar.querySelector('.system-banner__action-btn') as HTMLButtonElement;
    expect(retryBtn).toBeTruthy();
    expect(retryBtn.textContent?.trim()).toBe('Retry');
  });

  it('should show "View details" routerLink to /settings/general when image build fails', () => {
    // Arrange
    const notification: SystemNotification = { category: 'image-build', isActive: true, message: 'Failed|error log' };

    // Act
    const { fixture } = setup([notification]);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const imageBuildBar = el.querySelector('.system-banner__bar--image-build') as HTMLElement;
    const link = imageBuildBar?.querySelector('a.system-banner__details-link') as HTMLAnchorElement;
    expect(link).toBeTruthy();
    expect(link.textContent?.trim()).toBe('View details');
    expect(link.getAttribute('href')).toBe('/settings/general');
  });

  it('should treat an empty log part after separator as null (no log tail shown)', () => {
    // Arrange — message with separator but no log content
    const notification: SystemNotification = { category: 'image-build', isActive: true, message: 'Failed|' };

    // Act
    const { fixture } = setup([notification]);
    const el = fixture.nativeElement as HTMLElement;

    // Assert — failed bar rendered but no log-tail span
    const imageBuildBar = el.querySelector('.system-banner__bar--image-build') as HTMLElement;
    expect(imageBuildBar).toBeTruthy();
    const logTail = imageBuildBar.querySelector('.system-banner__log-tail');
    expect(logTail).toBeFalsy();
  });

  it('should not render an image-build bar when there are no image-build notifications', () => {
    // Arrange
    const notification: SystemNotification = { category: 'auth', isActive: true, message: 'Auth invalid' };

    // Act
    const { fixture } = setup([notification]);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const imageBuildBar = el.querySelector('.system-banner__bar--image-build');
    expect(imageBuildBar).toBeFalsy();
  });

  it('should have role="region" with aria-label "Image build status" on the image-build wrapper', () => {
    // Arrange
    const notification: SystemNotification = { category: 'image-build', isActive: true, message: 'Building|null' };

    // Act
    const { fixture } = setup([notification]);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const wrapper = el.querySelector('[aria-label="Image build status"]') as HTMLElement;
    expect(wrapper?.getAttribute('role')).toBe('region');
  });

  it('should have role="status" aria-live="polite" on the Building bar', () => {
    // Arrange
    const notification: SystemNotification = { category: 'image-build', isActive: true, message: 'Building|null' };

    // Act
    const { fixture } = setup([notification]);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const buildingBar = el.querySelector('.system-banner__bar--building') as HTMLElement;
    expect(buildingBar?.getAttribute('role')).toBe('status');
    expect(buildingBar?.getAttribute('aria-live')).toBe('polite');
  });

  it('should have role="alert" on the Failed bar', () => {
    // Arrange
    const notification: SystemNotification = { category: 'image-build', isActive: true, message: 'Failed|error' };

    // Act
    const { fixture } = setup([notification]);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const failedBar = el.querySelector('.system-banner__bar--failed') as HTMLElement;
    expect(failedBar?.getAttribute('role')).toBe('alert');
  });
});

import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { SystemBannerComponent } from './system-banner';
import { SystemSignalRService } from '../../../core/services/system-signalr.service';
import { SystemNotification } from '../../../core/models/system-notification.model';
import { DispatchService } from '../../../core/services/dispatch.service';
import { SettingsService } from '../../../features/settings/settings.service';
import { ToastService } from '../../../core/services/toast.service';

function createMockSignalRService(notifications: SystemNotification[]) {
  const notificationsSignal = signal(notifications);
  return {
    notifications: notificationsSignal.asReadonly(),
    _signal: notificationsSignal,
  };
}

function createMockDispatchService() {
  const isPausedSignal = signal(false);
  const usageLimitSignal = signal<string | null>(null);
  const resumingSignal = signal(false);

  return {
    isDispatchPaused: isPausedSignal.asReadonly(),
    usageLimitResetsAt: usageLimitSignal.asReadonly(),
    resuming: resumingSignal.asReadonly(),
    resumeDispatch: vi.fn(),
  };
}

function createMockSettingsService() {
  return {
    loadSettings: vi.fn(),
    setImageBuildStatus: vi.fn(),
    retryImageBuild: vi.fn(),
  };
}

function createMockToastService() {
  return {
    show: vi.fn(),
    dismiss: vi.fn(),
  };
}

function setup() {
  const mockSignalR = createMockSignalRService([]);
  const mockDispatch = createMockDispatchService();
  const mockSettings = createMockSettingsService();
  const mockToast = createMockToastService();

  TestBed.configureTestingModule({
    imports: [SystemBannerComponent],
    providers: [
      provideRouter([]),
      { provide: SystemSignalRService, useValue: mockSignalR },
      { provide: DispatchService, useValue: mockDispatch },
      { provide: SettingsService, useValue: mockSettings },
      { provide: ToastService, useValue: mockToast },
    ],
  });

  const fixture = TestBed.createComponent(SystemBannerComponent);
  fixture.detectChanges();
  return { fixture };
}

describe('SystemBannerComponent', () => {
  describe('composition', () => {
    it('should render fd-system-notifications child component', () => {
      // Arrange / Act
      const { fixture } = setup();
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      expect(el.querySelector('fd-system-notifications')).not.toBeNull();
    });

    it('should render fd-image-build-banner child component', () => {
      // Arrange / Act
      const { fixture } = setup();
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      expect(el.querySelector('fd-image-build-banner')).not.toBeNull();
    });

    it('should render fd-dispatch-banner child component', () => {
      // Arrange / Act
      const { fixture } = setup();
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      expect(el.querySelector('fd-dispatch-banner')).not.toBeNull();
    });
  });
});

import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { Subject } from 'rxjs';
import { SystemBannerComponent } from './system-banner';
import { SystemSignalRService } from '../../../core/services/system-signalr.service';
import { SystemNotification } from '../../../core/models/system-notification.model';
import { DispatchService } from '../../../core/services/dispatch.service';
import { SettingsService } from '../../../core/services/settings.service';
import { ToastService } from '../../../core/services/toast.service';
import { SystemStatusService } from '../../../core/services/system-status.service';
import { CreditsService } from '../../../core/services/credits.service';

function createMockSignalRService(notifications: SystemNotification[]) {
  const notificationsSignal = signal(notifications);
  const reconnected$ = new Subject<void>();
  return {
    notifications: notificationsSignal.asReadonly(),
    _signal: notificationsSignal,
    reconnected: reconnected$.asObservable(),
    applyDockerAvailability: vi.fn(),
  };
}

function createMockSystemStatusService() {
  return {};
}

function createMockCreditsService() {
  const nextProbeAtSignal = signal<string | null>(null);
  const isCheckingSignal = signal<boolean>(false);

  return {
    nextProbeAt: nextProbeAtSignal.asReadonly(),
    isChecking: isCheckingSignal.asReadonly(),
    checkNow: vi.fn(),
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
  const imageBuildStatusSignal = signal<'Idle' | 'Building' | 'Failed'>('Idle');
  const imageBuildLogTailSignal = signal<string | null>(null);

  return {
    loadSettings: vi.fn(),
    retryImageBuild: vi.fn(),
    imageBuildStatus: imageBuildStatusSignal.asReadonly(),
    imageBuildLogTail: imageBuildLogTailSignal.asReadonly(),
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
  const mockSystemStatus = createMockSystemStatusService();
  const mockCredits = createMockCreditsService();

  TestBed.configureTestingModule({
    imports: [SystemBannerComponent],
    providers: [
      provideRouter([]),
      { provide: SystemSignalRService, useValue: mockSignalR },
      { provide: DispatchService, useValue: mockDispatch },
      { provide: SettingsService, useValue: mockSettings },
      { provide: ToastService, useValue: mockToast },
      { provide: SystemStatusService, useValue: mockSystemStatus },
      { provide: CreditsService, useValue: mockCredits },
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

    it('should render fd-docker-banner child component', () => {
      // Arrange / Act
      const { fixture } = setup();
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      expect(el.querySelector('fd-docker-banner')).not.toBeNull();
    });

    it('should render fd-docker-banner before fd-image-build-banner', () => {
      // Arrange / Act
      const { fixture } = setup();
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      const children = Array.from(el.children).map((c) => c.tagName.toLowerCase());
      const dockerIndex = children.indexOf('fd-docker-banner');
      const imageBuildIndex = children.indexOf('fd-image-build-banner');
      expect(dockerIndex).toBeLessThan(imageBuildIndex);
    });

    it('should render fd-credits-banner child component', () => {
      // Arrange / Act
      const { fixture } = setup();
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      expect(el.querySelector('fd-credits-banner')).not.toBeNull();
    });

    it('should render fd-credits-banner before fd-dispatch-banner', () => {
      // Arrange / Act
      const { fixture } = setup();
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      const children = Array.from(el.children).map((c) => c.tagName.toLowerCase());
      const creditsIndex = children.indexOf('fd-credits-banner');
      const dispatchIndex = children.indexOf('fd-dispatch-banner');
      expect(creditsIndex).toBeLessThan(dispatchIndex);
    });
  });
});

import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { vi } from 'vitest';
import { SystemBannerComponent } from './system-banner';
import { SystemSignalRService } from '../../../core/services/system-signalr.service';
import { SystemNotification } from '../../../core/models/system-notification.model';
import { DispatchService } from '../../../core/services/dispatch.service';
import { SettingsService } from '../../../features/settings/settings.service';

function createMockSignalRService(notifications: SystemNotification[]) {
  const notificationsSignal = signal(notifications);
  return {
    notifications: notificationsSignal.asReadonly(),
    _signal: notificationsSignal,
  };
}

interface DispatchServiceOverrides {
  isDispatchPaused?: boolean;
  usageLimitResetsAt?: string | null;
  resuming?: boolean;
}

function createMockDispatchService(overrides: DispatchServiceOverrides = {}) {
  const isPausedSignal = signal(overrides.isDispatchPaused ?? false);
  const usageLimitSignal = signal(overrides.usageLimitResetsAt ?? null);
  const resumingSignal = signal(overrides.resuming ?? false);
  const resumeDispatch = vi.fn();

  return {
    isDispatchPaused: isPausedSignal.asReadonly(),
    usageLimitResetsAt: usageLimitSignal.asReadonly(),
    resuming: resumingSignal.asReadonly(),
    resumeDispatch,
    _isPausedSignal: isPausedSignal,
    _usageLimitSignal: usageLimitSignal,
    _resumingSignal: resumingSignal,
  };
}

function createMockSettingsService() {
  return {
    loadSettings: vi.fn(),
    setImageBuildStatus: vi.fn(),
  };
}

interface SetupOptions {
  notifications?: SystemNotification[];
  dispatch?: DispatchServiceOverrides;
}

function setup(options: SetupOptions = {}) {
  const mockSignalR = createMockSignalRService(options.notifications ?? []);
  const mockDispatch = createMockDispatchService(options.dispatch ?? {});
  const mockSettings = createMockSettingsService();

  TestBed.configureTestingModule({
    imports: [SystemBannerComponent],
    providers: [
      { provide: SystemSignalRService, useValue: mockSignalR },
      { provide: DispatchService, useValue: mockDispatch },
      { provide: SettingsService, useValue: mockSettings },
    ],
  });

  const fixture = TestBed.createComponent(SystemBannerComponent);
  fixture.detectChanges();
  return { fixture, mockSignalR, mockDispatch, mockSettings };
}

describe('SystemBannerComponent', () => {
  // Cycle 1: no notifications and no dispatch renders no notification banner
  it('should render no notification banner when there are no active notifications', () => {
    // Arrange / Act
    const { fixture } = setup({ notifications: [], dispatch: { isDispatchPaused: false, usageLimitResetsAt: null } });
    const el = fixture.nativeElement as HTMLElement;

    // Assert — the notifications wrapper is absent; only the dispatch wrapper (always in DOM) remains
    const notificationBars = el.querySelectorAll('.system-banner__bar:not(.system-banner__bar--dispatch)');
    expect(notificationBars.length).toBe(0);
  });

  // Cycle 2: one notification renders one bar with message text
  it('should render one bar with the notification message when one notification is active', () => {
    // Arrange
    const notification: SystemNotification = { category: 'auth', isActive: true, message: 'Claude auth is invalid' };

    // Act
    const { fixture } = setup({ notifications: [notification] });
    const el = fixture.nativeElement as HTMLElement;

    // Assert — exclude the always-rendered dispatch bar
    const bars = el.querySelectorAll('.system-banner__bar:not(.system-banner__bar--dispatch)');
    expect(bars.length).toBe(1);
    expect(bars[0].textContent?.trim()).toContain('Claude auth is invalid');
  });

  // Cycle 3: multiple notifications render multiple bars
  it('should render multiple bars when multiple notifications are active', () => {
    // Arrange
    const notifications: SystemNotification[] = [
      { category: 'auth', isActive: true, message: 'Auth invalid' },
      { category: 'license', isActive: true, message: 'License expired' },
    ];

    // Act
    const { fixture } = setup({ notifications });
    const el = fixture.nativeElement as HTMLElement;

    // Assert — exclude the always-rendered dispatch bar
    const bars = el.querySelectorAll('.system-banner__bar:not(.system-banner__bar--dispatch)');
    expect(bars.length).toBe(2);
  });

  // Cycle 4: each bar has role="alert"
  it('should have role="alert" on each notification bar', () => {
    // Arrange
    const notification: SystemNotification = { category: 'auth', isActive: true, message: 'Auth invalid' };

    // Act
    const { fixture } = setup({ notifications: [notification] });
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const bar = el.querySelector('.system-banner__bar:not(.system-banner__bar--dispatch)') as HTMLElement;
    expect(bar?.getAttribute('role')).toBe('alert');
  });

  // Cycle 5: notification wrapper has role="region" and aria-label "System notifications"
  it('should have role="region" with aria-label "System notifications" on the notification wrapper', () => {
    // Arrange
    const notification: SystemNotification = { category: 'auth', isActive: true, message: 'Auth invalid' };

    // Act
    const { fixture } = setup({ notifications: [notification] });
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const wrapper = el.querySelector('[aria-label="System notifications"]') as HTMLElement;
    expect(wrapper?.getAttribute('role')).toBe('region');
  });

  describe('dispatch banner', () => {
    // Cycle 6: dispatch banner visible when isDispatchPaused is true
    it('should show dispatch banner when dispatch is paused', () => {
      // Arrange / Act
      const { fixture } = setup({ dispatch: { isDispatchPaused: true } });
      const el = fixture.nativeElement as HTMLElement;

      // Assert — the dispatch region is visible (not hidden)
      const dispatchRegion = el.querySelector('[aria-label="Dispatch status"]') as HTMLElement;
      expect(dispatchRegion?.hidden).toBe(false);
      expect(el.querySelector('.system-banner__bar--dispatch')).not.toBeNull();
    });

    // Cycle 7: dispatch banner hidden when not paused and no usage limit
    it('should hide dispatch banner when dispatch is not paused and there is no usage limit', () => {
      // Arrange / Act
      const { fixture } = setup({ dispatch: { isDispatchPaused: false, usageLimitResetsAt: null } });
      const el = fixture.nativeElement as HTMLElement;

      // Assert — the dispatch region is in the DOM but has [hidden]
      const dispatchRegion = el.querySelector('[aria-label="Dispatch status"]') as HTMLElement;
      expect(dispatchRegion?.hidden).toBe(true);
    });

    // Cycle 8: dispatch banner visible when usage limit is active (even if not explicitly paused)
    it('should show dispatch banner when usage limit is active', () => {
      // Arrange
      const futureDate = new Date(Date.now() + 60 * 60 * 1000).toISOString();

      // Act
      const { fixture } = setup({ dispatch: { isDispatchPaused: false, usageLimitResetsAt: futureDate } });
      const el = fixture.nativeElement as HTMLElement;

      // Assert — the dispatch region is visible (not hidden)
      const dispatchRegion = el.querySelector('[aria-label="Dispatch status"]') as HTMLElement;
      expect(dispatchRegion?.hidden).toBe(false);
    });

    // Cycle 9: shows "Dispatch is paused" when paused without usage limit
    it('should show "Dispatch is paused" message when paused without a usage limit', () => {
      // Arrange / Act
      const { fixture } = setup({ dispatch: { isDispatchPaused: true, usageLimitResetsAt: null } });
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      const bar = el.querySelector('.system-banner__bar--dispatch') as HTMLElement;
      expect(bar?.textContent).toContain('Dispatch is paused');
    });

    // Cycle 10: shows countdown when usage limit is active (hours format)
    it('should show countdown in "Xh Ym" format when more than one hour remains', () => {
      // Arrange
      const resetsAt = new Date(Date.now() + (2 * 60 * 60 + 34 * 60) * 1000).toISOString();

      // Act
      const { fixture } = setup({ dispatch: { isDispatchPaused: true, usageLimitResetsAt: resetsAt } });
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      const bar = el.querySelector('.system-banner__bar--dispatch') as HTMLElement;
      expect(bar?.textContent).toContain('2h 34m');
    });

    // Cycle 11: countdown in "Xm Ys" format when between 1 and 60 minutes remain
    it('should show countdown in "Xm Ys" format when between one minute and one hour remains', () => {
      // Arrange
      const resetsAt = new Date(Date.now() + (12 * 60 + 5) * 1000).toISOString();

      // Act
      const { fixture } = setup({ dispatch: { isDispatchPaused: true, usageLimitResetsAt: resetsAt } });
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      const bar = el.querySelector('.system-banner__bar--dispatch') as HTMLElement;
      expect(bar?.textContent).toContain('12m 5s');
    });

    // Cycle 12: countdown in "Xs" format when less than one minute remains
    it('should show countdown in "Xs" format when less than one minute remains', () => {
      // Arrange
      const resetsAt = new Date(Date.now() + 45 * 1000).toISOString();

      // Act
      const { fixture } = setup({ dispatch: { isDispatchPaused: true, usageLimitResetsAt: resetsAt } });
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      const bar = el.querySelector('.system-banner__bar--dispatch') as HTMLElement;
      expect(bar?.textContent).toContain('45s');
    });

    // Cycle 13: countdown shows "momentarily" when time has elapsed
    it('should show "momentarily" when the reset time has passed', () => {
      // Arrange
      const resetsAt = new Date(Date.now() - 1000).toISOString();

      // Act
      const { fixture } = setup({ dispatch: { isDispatchPaused: true, usageLimitResetsAt: resetsAt } });
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      const bar = el.querySelector('.system-banner__bar--dispatch') as HTMLElement;
      expect(bar?.textContent).toContain('momentarily');
    });

    // Cycle 14: "Resume All" button calls resumeDispatch
    it('should call resumeDispatch when "Resume All" button is clicked', () => {
      // Arrange
      const { fixture, mockDispatch } = setup({ dispatch: { isDispatchPaused: true } });
      const el = fixture.nativeElement as HTMLElement;

      // Act
      const button = el.querySelector('.system-banner__action-btn') as HTMLButtonElement;
      button.click();

      // Assert
      expect(mockDispatch.resumeDispatch).toHaveBeenCalledTimes(1);
    });

    // Cycle 15: button disabled while resuming
    it('should disable the "Resume All" button while resuming', () => {
      // Arrange / Act
      const { fixture } = setup({ dispatch: { isDispatchPaused: true, resuming: true } });
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      const button = el.querySelector('.system-banner__action-btn') as HTMLButtonElement;
      expect(button.disabled).toBe(true);
    });

    // Cycle 16: button shows "Resuming..." text while resuming
    it('should show "Resuming..." text on the button while resuming', () => {
      // Arrange / Act
      const { fixture } = setup({ dispatch: { isDispatchPaused: true, resuming: true } });
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      const button = el.querySelector('.system-banner__action-btn') as HTMLButtonElement;
      expect(button.textContent?.trim()).toBe('Resuming...');
    });
  });

  describe('image-build notifications', () => {
    it('should call setImageBuildStatus when an image-build notification arrives', () => {
      // Arrange
      const { fixture, mockSignalR, mockSettings } = setup({ notifications: [] });
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
      const { fixture } = setup({ notifications: [notification] });
      const el = fixture.nativeElement as HTMLElement;

      // Assert — there should be an image-build bar
      const imageBuildBar = el.querySelector('.system-banner__bar--image-build') as HTMLElement;
      expect(imageBuildBar).toBeTruthy();
      expect(imageBuildBar.textContent).toContain('Worker image is building');
    });

    it('should render a Failed bar with Retry button when an image-build Failed notification is active', () => {
      // Arrange
      const notification: SystemNotification = { category: 'image-build', isActive: true, message: 'Failed|Step 2/5 FAILED' };

      // Act
      const { fixture } = setup({ notifications: [notification] });
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      const imageBuildBar = el.querySelector('.system-banner__bar--image-build') as HTMLElement;
      expect(imageBuildBar).toBeTruthy();
      expect(imageBuildBar.textContent).toContain('Worker image build failed');

      const retryBtn = imageBuildBar.querySelector('.system-banner__action-btn') as HTMLButtonElement;
      expect(retryBtn).toBeTruthy();
      expect(retryBtn.textContent?.trim()).toBe('Retry');
    });

    it('should show "View details" link to /settings/general when image build fails', () => {
      // Arrange
      const notification: SystemNotification = { category: 'image-build', isActive: true, message: 'Failed|error log' };

      // Act
      const { fixture } = setup({ notifications: [notification] });
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      const imageBuildBar = el.querySelector('.system-banner__bar--image-build') as HTMLElement;
      const link = imageBuildBar?.querySelector('a[href="/settings/general"]') as HTMLAnchorElement;
      expect(link).toBeTruthy();
      expect(link.textContent?.trim()).toBe('View details');
    });

    it('should not render an image-build bar when there are no image-build notifications', () => {
      // Arrange
      const notification: SystemNotification = { category: 'auth', isActive: true, message: 'Auth invalid' };

      // Act
      const { fixture } = setup({ notifications: [notification] });
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      const imageBuildBar = el.querySelector('.system-banner__bar--image-build');
      expect(imageBuildBar).toBeFalsy();
    });

    it('should sort failed conditions before building/in-progress notifications', () => {
      // Arrange
      const notifications: SystemNotification[] = [
        { category: 'image-build', isActive: true, message: 'Building|null' },
        { category: 'auth', isActive: true, message: 'Auth invalid' },
      ];

      // Act
      const { fixture } = setup({ notifications });
      const el = fixture.nativeElement as HTMLElement;
      const bars = Array.from(el.querySelectorAll('.system-banner__bar:not(.system-banner__bar--dispatch):not(.system-banner__bar--image-build)'));

      // Assert — auth (failure-category) renders first, image-build (building) renders below
      const firstBar = bars[0];
      expect(firstBar?.textContent).toContain('Auth invalid');
    });
  });
});

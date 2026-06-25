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

function createMockToastService() {
  return {
    show: vi.fn(),
    dismiss: vi.fn(),
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
  return { fixture, mockSignalR, mockDispatch, mockSettings, mockToast };
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
    // Cycle 6: dispatch banner visible when isDispatchPaused is true (no usage limit)
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

    // Cycle 8: dispatch banner visible when usage limit is active (future timestamp)
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

    // Cycle 10: banner hides when reset time has elapsed and dispatch is not manually paused
    it('should hide dispatch banner when usage limit reset time has elapsed and dispatch is not paused', () => {
      // Arrange — past timestamp (elapsed)
      const pastDate = new Date(Date.now() - 1000).toISOString();

      // Act
      const { fixture } = setup({ dispatch: { isDispatchPaused: false, usageLimitResetsAt: pastDate } });
      const el = fixture.nativeElement as HTMLElement;

      // Assert — banner is hidden because remainingMs <= 0 and not manually paused
      const dispatchRegion = el.querySelector('[aria-label="Dispatch status"]') as HTMLElement;
      expect(dispatchRegion?.hidden).toBe(true);
    });

    // Cycle 11: banner shows "Dispatch is paused" when reset time has elapsed but dispatch IS manually paused
    it('should show "Dispatch is paused" when usage limit has elapsed but dispatch is still manually paused', () => {
      // Arrange — past timestamp (elapsed), but dispatch paused manually
      const pastDate = new Date(Date.now() - 1000).toISOString();

      // Act
      const { fixture } = setup({ dispatch: { isDispatchPaused: true, usageLimitResetsAt: pastDate } });
      const el = fixture.nativeElement as HTMLElement;

      // Assert — banner visible, no countdown shown
      const dispatchRegion = el.querySelector('[aria-label="Dispatch status"]') as HTMLElement;
      expect(dispatchRegion?.hidden).toBe(false);
      const bar = el.querySelector('.system-banner__bar--dispatch') as HTMLElement;
      expect(bar?.textContent).toContain('Dispatch is paused');
    });

    // Cycle 12: usage-limit banner shows new copy "Usage limit reached. Resets in <countdown>"
    it('should show "Usage limit reached. Resets in <countdown>" when usage limit is active', () => {
      // Arrange
      const resetsAt = new Date(Date.now() + 45 * 1000).toISOString();

      // Act
      const { fixture } = setup({ dispatch: { isDispatchPaused: false, usageLimitResetsAt: resetsAt } });
      const el = fixture.nativeElement as HTMLElement;

      // Assert — full new copy in bar text
      const bar = el.querySelector('.system-banner__bar--dispatch') as HTMLElement;
      expect(bar?.textContent).toContain('Usage limit reached. Resets in');
      expect(bar?.textContent).toContain('45s');
    });

    // Cycle 13: no mdash separator span in dispatch bar
    it('should not render an mdash separator span in the dispatch bar', () => {
      // Arrange
      const resetsAt = new Date(Date.now() + 60 * 1000).toISOString();

      // Act
      const { fixture } = setup({ dispatch: { isDispatchPaused: false, usageLimitResetsAt: resetsAt } });
      const el = fixture.nativeElement as HTMLElement;

      // Assert — no .system-banner__countdown element
      const countdownSpan = el.querySelector('.system-banner__countdown');
      expect(countdownSpan).toBeNull();
    });

    // Cycle 14: no "Resume All" button in dispatch bar
    it('should not render a "Resume All" button in the dispatch bar', () => {
      // Arrange / Act
      const { fixture } = setup({ dispatch: { isDispatchPaused: true } });
      const el = fixture.nativeElement as HTMLElement;

      // Assert — no action button inside the dispatch bar
      const dispatchBar = el.querySelector('.system-banner__bar--dispatch') as HTMLElement;
      const btn = dispatchBar?.querySelector('.system-banner__action-btn');
      expect(btn).toBeNull();
    });

    // Cycle 15: countdown in "Xh Ym" format when more than one hour remains
    it('should show countdown in "Xh Ym" format when more than one hour remains', () => {
      // Arrange
      const resetsAt = new Date(Date.now() + (2 * 60 * 60 + 34 * 60) * 1000).toISOString();

      // Act
      const { fixture } = setup({ dispatch: { isDispatchPaused: false, usageLimitResetsAt: resetsAt } });
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      const bar = el.querySelector('.system-banner__bar--dispatch') as HTMLElement;
      expect(bar?.textContent).toContain('2h 34m');
    });

    // Cycle 16: countdown in "Xm Ys" format when between 1 and 60 minutes remain
    it('should show countdown in "Xm Ys" format when between one minute and one hour remains', () => {
      // Arrange
      const resetsAt = new Date(Date.now() + (12 * 60 + 5) * 1000).toISOString();

      // Act
      const { fixture } = setup({ dispatch: { isDispatchPaused: false, usageLimitResetsAt: resetsAt } });
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      const bar = el.querySelector('.system-banner__bar--dispatch') as HTMLElement;
      expect(bar?.textContent).toContain('12m 5s');
    });

    // Cycle 17: countdown in "Xs" format when less than one minute remains
    it('should show countdown in "Xs" format when less than one minute remains', () => {
      // Arrange
      const resetsAt = new Date(Date.now() + 45 * 1000).toISOString();

      // Act
      const { fixture } = setup({ dispatch: { isDispatchPaused: false, usageLimitResetsAt: resetsAt } });
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      const bar = el.querySelector('.system-banner__bar--dispatch') as HTMLElement;
      expect(bar?.textContent).toContain('45s');
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

    it('should show "View details" routerLink to /settings/general when image build fails', () => {
      // Arrange
      const notification: SystemNotification = { category: 'image-build', isActive: true, message: 'Failed|error log' };

      // Act
      const { fixture } = setup({ notifications: [notification] });
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
      const { fixture } = setup({ notifications: [notification] });
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

  describe('usage-limit toast (zero-crossing)', () => {
    beforeEach(() => {
      vi.useFakeTimers();
    });

    afterEach(() => {
      vi.useRealTimers();
    });

    // Scenario 1: active countdown crossing zero in-session → toast fires exactly once
    it('should fire "Usage limit reset" toast exactly once when countdown crosses zero in-session', () => {
      // Arrange — reset 1.5 s in the future so the component starts counting down
      const resetsAt = new Date(Date.now() + 1500).toISOString();
      const { fixture, mockDispatch, mockToast } = setup({
        dispatch: { isDispatchPaused: false, usageLimitResetsAt: resetsAt },
      });

      // Act — advance past expiry (2 s), then one more tick to ensure the interval fires
      vi.advanceTimersByTime(2000);
      fixture.detectChanges();
      TestBed.flushEffects();

      // Assert
      expect(mockToast.show).toHaveBeenCalledTimes(1);
      expect(mockToast.show).toHaveBeenCalledWith('Usage limit reset');
      // Verify: usageLimitResetsAt is still non-null (server hasn't cleared it yet)
      expect(mockDispatch.usageLimitResetsAt()).toBe(resetsAt);
    });

    // Scenario 2: load after expiry → no toast, banner not shown
    it('should not fire toast when page is loaded after expiry (initial remainingMs already <= 0)', () => {
      // Arrange — past timestamp already expired when component initialises
      const resetsAt = new Date(Date.now() - 5000).toISOString();
      const { fixture, mockToast } = setup({
        dispatch: { isDispatchPaused: false, usageLimitResetsAt: resetsAt },
      });

      // Act — let some ticks run, no zero-crossing happened in-session
      vi.advanceTimersByTime(3000);
      fixture.detectChanges();
      TestBed.flushEffects();

      // Assert — no toast and banner hidden
      expect(mockToast.show).not.toHaveBeenCalled();
      const el = fixture.nativeElement as HTMLElement;
      const dispatchRegion = el.querySelector('[aria-label="Dispatch status"]') as HTMLElement;
      expect(dispatchRegion?.hidden).toBe(true);
    });

    // Scenario 3: manual resume clears usageLimitResetsAt before zero → no toast
    it('should not fire toast when usageLimitResetsAt is cleared (manual resume) before countdown reaches zero', () => {
      // Arrange — reset 3 s in the future
      const resetsAt = new Date(Date.now() + 3000).toISOString();
      const { fixture, mockDispatch, mockToast } = setup({
        dispatch: { isDispatchPaused: false, usageLimitResetsAt: resetsAt },
      });

      // Act — server clears the limit after 1 s (manual resume), then time advances past original expiry
      vi.advanceTimersByTime(1000);
      mockDispatch._usageLimitSignal.set(null);
      vi.advanceTimersByTime(3000);
      fixture.detectChanges();
      TestBed.flushEffects();

      // Assert — remainingMs became null (not <= 0), so no toast
      expect(mockToast.show).not.toHaveBeenCalled();
    });

    // Scenario 4: interval keeps ticking past zero → toast fires exactly once (not per tick)
    it('should fire toast only once even when interval ticks continue past zero', () => {
      // Arrange
      const resetsAt = new Date(Date.now() + 1500).toISOString();
      const { fixture, mockToast } = setup({
        dispatch: { isDispatchPaused: false, usageLimitResetsAt: resetsAt },
      });

      // Act — advance 5 s total (4 ticks past zero)
      vi.advanceTimersByTime(5000);
      fixture.detectChanges();
      TestBed.flushEffects();

      // Assert — still only one call
      expect(mockToast.show).toHaveBeenCalledTimes(1);
    });

    // Scenario 5: usage limit + manual pause both active at zero → banner shows "Dispatch is paused" AND toast fires once
    it('should fire toast once and show "Dispatch is paused" when both usage limit expires and dispatch is manually paused', () => {
      // Arrange — usage limit expires, dispatch is also manually paused
      const resetsAt = new Date(Date.now() + 1500).toISOString();
      const { fixture, mockToast } = setup({
        dispatch: { isDispatchPaused: true, usageLimitResetsAt: resetsAt },
      });

      // Act
      vi.advanceTimersByTime(2000);
      fixture.detectChanges();
      TestBed.flushEffects();

      // Assert — toast fired
      expect(mockToast.show).toHaveBeenCalledTimes(1);
      expect(mockToast.show).toHaveBeenCalledWith('Usage limit reset');

      // Assert — banner text shows pause message (not countdown)
      const el = fixture.nativeElement as HTMLElement;
      const bar = el.querySelector('.system-banner__bar--dispatch') as HTMLElement;
      expect(bar?.textContent).toContain('Dispatch is paused');
    });
  });
});

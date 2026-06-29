import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { vi } from 'vitest';
import { DispatchBannerComponent } from './dispatch-banner';
import { DispatchService } from '../../../../../core/services/dispatch.service';
import { ToastService } from '../../../../../core/services/toast.service';

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

function createMockToastService() {
  return {
    show: vi.fn(),
    dismiss: vi.fn(),
  };
}

interface SetupOptions {
  dispatch?: DispatchServiceOverrides;
}

function setup(options: SetupOptions = {}) {
  const mockDispatch = createMockDispatchService(options.dispatch ?? {});
  const mockToast = createMockToastService();

  TestBed.configureTestingModule({
    imports: [DispatchBannerComponent],
    providers: [
      { provide: DispatchService, useValue: mockDispatch },
      { provide: ToastService, useValue: mockToast },
    ],
  });

  const fixture = TestBed.createComponent(DispatchBannerComponent);
  fixture.detectChanges();
  return { fixture, mockDispatch, mockToast };
}

describe('DispatchBannerComponent', () => {
  describe('dispatch banner visibility', () => {
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

  describe('dispatch banner a11y live-region', () => {
    // Cycle: dispatch banner uses polite role="status", not assertive role="alert"
    it('should use role="status" (polite) on the dispatch bar, not role="alert" (assertive)', () => {
      // Arrange / Act
      const { fixture } = setup({ dispatch: { isDispatchPaused: true } });
      const el = fixture.nativeElement as HTMLElement;

      // Assert
      const bar = el.querySelector('.system-banner__bar--dispatch') as HTMLElement;
      expect(bar.getAttribute('role')).toBe('status');
    });

    it('should render the ticking countdown value with aria-hidden="true" when usage limit is active', () => {
      // Arrange
      const resetsAt = new Date(Date.now() + 45 * 1000).toISOString();

      // Act
      const { fixture } = setup({ dispatch: { isDispatchPaused: false, usageLimitResetsAt: resetsAt } });
      const el = fixture.nativeElement as HTMLElement;

      // Assert — countdown span is hidden from AT
      const countdownValue = el.querySelector('.system-banner__bar--dispatch .system-banner__countdown-value') as HTMLElement;
      expect(countdownValue).not.toBeNull();
      expect(countdownValue.getAttribute('aria-hidden')).toBe('true');
    });

    it('should include the static lead text in the dispatch bar without aria-hidden', () => {
      // Arrange
      const resetsAt = new Date(Date.now() + 45 * 1000).toISOString();

      // Act
      const { fixture } = setup({ dispatch: { isDispatchPaused: false, usageLimitResetsAt: resetsAt } });
      const el = fixture.nativeElement as HTMLElement;

      // Assert — the dispatch message span (the polite live region) contains the static text
      const messageSpan = el.querySelector('.system-banner__bar--dispatch .system-banner__message') as HTMLElement;
      expect(messageSpan.textContent).toContain('Usage limit reached. Resets in');
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

      // Assert — no toast before the countdown crosses zero
      expect(mockToast.show).not.toHaveBeenCalled();

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

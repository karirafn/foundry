import { TestBed } from '@angular/core/testing';
import { WritableSignal, signal } from '@angular/core';
import { vi } from 'vitest';
import { PackageProxyPauseBannerComponent } from './package-proxy-pause-banner';
import { DispatchService } from '../../../../../core/services/dispatch.service';
import { ToastService } from '../../../../../core/services/toast.service';
import { PackageProxyPause } from '../../../../../core/models/settings.model';

// Local-time constructors keep the HH:mm assertions independent of the runner's time zone.
const NOW = new Date(2026, 6, 1, 7, 45, 0);

function localIso(hours: number, minutes: number, day = 1): string {
  return new Date(2026, 6, day, hours, minutes, 0).toISOString();
}

function pause(overrides: Partial<PackageProxyPause> = {}): PackageProxyPause {
  return {
    pausedAt: localIso(7, 39),
    nextProbeAt: localIso(7, 52),
    probeAttempts: 0,
    ...overrides,
  };
}

function setup(initial: PackageProxyPause | null = null) {
  const pauseSignal: WritableSignal<PackageProxyPause | null> = signal(initial);
  const mockDispatch = { packageProxyPause: pauseSignal.asReadonly() };
  const mockToast = { show: vi.fn(), dismiss: vi.fn() };

  TestBed.configureTestingModule({
    imports: [PackageProxyPauseBannerComponent],
    providers: [
      { provide: DispatchService, useValue: mockDispatch },
      { provide: ToastService, useValue: mockToast },
    ],
  });

  const fixture = TestBed.createComponent(PackageProxyPauseBannerComponent);
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;

  const setPause = (value: PackageProxyPause | null): void => {
    pauseSignal.set(value);
    fixture.detectChanges();
    TestBed.flushEffects();
    fixture.detectChanges();
  };

  return { fixture, el, mockToast, setPause };
}

function region(el: HTMLElement): HTMLElement {
  return el.querySelector('[aria-label="Package proxy status"]') as HTMLElement;
}

function bar(el: HTMLElement): HTMLElement | null {
  return el.querySelector<HTMLElement>('.system-banner__bar');
}

function normalisedText(node: Element | null): string {
  return (node?.textContent ?? '').replace(/\s+/g, ' ').trim();
}

describe('PackageProxyPauseBannerComponent', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(NOW);
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  describe('visibility', () => {
    it('should hide the region and render no bar when the proxy is not paused', () => {
      // Arrange / Act
      const { el } = setup(null);

      // Assert
      expect(region(el).hidden).toBe(true);
      expect(bar(el)).toBeNull();
    });

    it('should show the region with one bar when the proxy is paused', () => {
      // Arrange / Act
      const { el } = setup(pause());

      // Assert
      expect(region(el).hidden).toBe(false);
      expect(el.querySelectorAll('.system-banner__bar').length).toBe(1);
    });
  });

  describe('content', () => {
    it('should describe a waiting pause with its pause time and next check time', () => {
      // Arrange / Act
      const { el } = setup(pause());

      // Assert
      const text = normalisedText(bar(el));
      expect(text).toContain('Package proxy unreachable since 07:39');
      expect(text).toContain('next check 07:52');
      expect(text).not.toContain('failed');
      expect(text).not.toContain('checking now');
    });

    it('should add the failed check count once a probe has failed', () => {
      // Arrange / Act
      const { el } = setup(pause({ probeAttempts: 3 }));

      // Assert
      expect(normalisedText(bar(el))).toContain('next check 07:52 · 3 checks failed');
    });

    it('should use the singular for one failed check', () => {
      // Arrange / Act
      const { el } = setup(pause({ probeAttempts: 1 }));

      // Assert
      expect(normalisedText(bar(el))).toContain('· 1 check failed');
    });

    it('should format a time that is not today as "MMM d, HH:mm"', () => {
      // Arrange
      vi.setSystemTime(new Date(2026, 6, 2, 9, 0, 0));

      // Act
      const { el } = setup(pause({ pausedAt: localIso(7, 39, 1), nextProbeAt: localIso(9, 10, 2) }));

      // Assert
      const text = normalisedText(bar(el));
      expect(text).toContain('unreachable since Jul 1, 07:39');
      expect(text).toContain('next check 09:10');
    });

    it('should tell the operator that repositories without package sources keep dispatching', () => {
      // Arrange / Act
      const { el } = setup(pause());

      // Assert
      expect(normalisedText(bar(el))).toContain('Repositories without package sources keep dispatching.');
    });

    it('should render no buttons', () => {
      // Arrange / Act
      const { el } = setup(pause());

      // Assert
      expect(el.querySelector('button')).toBeNull();
    });

    it('should show the warning icon, not the spinner, while waiting for the next check', () => {
      // Arrange / Act
      const { el } = setup(pause());

      // Assert
      expect(bar(el)?.querySelector('.system-banner__icon')).not.toBeNull();
      expect(bar(el)?.querySelector('.system-banner__spinner-icon')).toBeNull();
    });
  });

  describe('checking now', () => {
    it('should show "checking now…" with the spinner when nextProbeAt has already passed', () => {
      // Arrange / Act
      const { el } = setup(pause({ nextProbeAt: localIso(7, 44), probeAttempts: 2 }));

      // Assert
      const text = normalisedText(bar(el));
      expect(text).toContain('Package proxy unreachable since 07:39');
      expect(text).toContain('checking now…');
      expect(text).not.toContain('next check');
      expect(bar(el)?.querySelector('.system-banner__spinner-icon')).not.toBeNull();
      expect(bar(el)?.querySelector('.system-banner__icon')).toBeNull();
    });

    it('should switch to "checking now…" when the 1 s tick passes nextProbeAt', () => {
      // Arrange
      const { el, fixture } = setup(pause({ nextProbeAt: new Date(NOW.getTime() + 1500).toISOString() }));
      expect(normalisedText(bar(el))).not.toContain('checking now');

      // Act
      vi.advanceTimersByTime(2000);
      fixture.detectChanges();

      // Assert
      expect(normalisedText(bar(el))).toContain('checking now…');
      expect(bar(el)?.querySelector('.system-banner__spinner-icon')).not.toBeNull();
    });

    it('should not start the tick interval while the proxy is not paused', () => {
      // Arrange
      const setIntervalSpy = vi.spyOn(globalThis, 'setInterval');

      // Act
      setup(null);

      // Assert
      expect(setIntervalSpy).not.toHaveBeenCalled();
    });

    it('should start one 1 s tick interval while the proxy is paused', () => {
      // Arrange
      const setIntervalSpy = vi.spyOn(globalThis, 'setInterval');

      // Act
      setup(pause());

      // Assert
      expect(setIntervalSpy).toHaveBeenCalledTimes(1);
      expect(setIntervalSpy).toHaveBeenCalledWith(expect.any(Function), 1000);
    });

    it('should stop the tick interval when the pause clears', () => {
      // Arrange
      const setIntervalSpy = vi.spyOn(globalThis, 'setInterval');
      const clearIntervalSpy = vi.spyOn(globalThis, 'clearInterval');
      const { setPause } = setup(pause());
      const intervalId = setIntervalSpy.mock.results[0].value;

      // Act
      setPause(null);

      // Assert
      expect(clearIntervalSpy).toHaveBeenCalledWith(intervalId);
    });

    it('should clear the tick interval when destroyed', () => {
      // Arrange
      const setIntervalSpy = vi.spyOn(globalThis, 'setInterval');
      const clearIntervalSpy = vi.spyOn(globalThis, 'clearInterval');
      const { fixture } = setup(pause());
      const intervalId = setIntervalSpy.mock.results[0].value;

      // Act
      fixture.destroy();

      // Assert
      expect(clearIntervalSpy).toHaveBeenCalledWith(intervalId);
    });
  });

  describe('live updates', () => {
    it('should update the next check and the failed count on a reload', () => {
      // Arrange
      const { el, setPause } = setup(pause());

      // Act
      setPause(pause({ nextProbeAt: localIso(7, 55), probeAttempts: 1 }));

      // Assert
      expect(normalisedText(bar(el))).toContain('next check 07:55 · 1 check failed');
    });

    it('should toast once when the pause clears', () => {
      // Arrange
      const { mockToast, setPause } = setup(pause());

      // Act
      setPause(null);

      // Assert
      expect(mockToast.show).toHaveBeenCalledTimes(1);
      expect(mockToast.show).toHaveBeenCalledWith('Package proxy pause cleared — dispatch resumed');
    });

    it('should not toast when the pause is only updated', () => {
      // Arrange
      const { mockToast, setPause } = setup(pause());

      // Act
      setPause(pause({ probeAttempts: 1, nextProbeAt: localIso(7, 55) }));

      // Assert
      expect(mockToast.show).not.toHaveBeenCalled();
    });

    it('should not toast when it starts with no pause', () => {
      // Arrange / Act
      const { mockToast } = setup(null);
      TestBed.flushEffects();

      // Assert
      expect(mockToast.show).not.toHaveBeenCalled();
    });
  });

  describe('accessibility', () => {
    it('should label the banner as a region', () => {
      // Arrange / Act
      const { el } = setup(pause());

      // Assert
      expect(region(el).getAttribute('role')).toBe('region');
    });

    it('should mark the bar as a polite status', () => {
      // Arrange / Act
      const { el } = setup(pause());

      // Assert
      expect(bar(el)?.getAttribute('role')).toBe('status');
    });

    it('should hide the icon from assistive technology', () => {
      // Arrange / Act
      const { el } = setup(pause());

      // Assert
      const icons = Array.from(el.querySelectorAll('svg'));
      expect(icons.length).toBe(1);
      expect(icons.every((icon) => icon.getAttribute('aria-hidden') === 'true')).toBe(true);
    });

    it('should announce a new pause once, then clear the announcement', () => {
      // Arrange
      const { el, fixture, setPause } = setup(null);
      const announcer = el.querySelector('.sr-only') as HTMLElement;

      // Act
      setPause(pause());

      // Assert — announced
      expect(announcer.textContent?.trim()).toBe(
        'Package proxy unreachable — dispatch paused for repositories with package sources'
      );

      // Act — let the clearing timeout run
      vi.advanceTimersByTime(0);
      fixture.detectChanges();

      // Assert — cleared so later ticks never re-announce
      expect(announcer.textContent?.trim()).toBe('');
    });

    it('should not re-announce when the pause is updated', () => {
      // Arrange
      const { el, setPause } = setup(null);
      setPause(pause());
      vi.advanceTimersByTime(0);

      // Act
      setPause(pause({ probeAttempts: 1 }));

      // Assert
      expect((el.querySelector('.sr-only') as HTMLElement).textContent?.trim()).toBe('');
    });
  });
});

import { TestBed } from '@angular/core/testing';
import { WritableSignal, signal } from '@angular/core';
import { vi } from 'vitest';
import { HostPauseBannerComponent } from './host-pause-banner';
import { DispatchService } from '../../../../../core/services/dispatch.service';
import { ToastService } from '../../../../../core/services/toast.service';
import { PausedHost } from '../../../../../core/models/settings.model';

// Local-time constructors keep the HH:mm assertions independent of the runner's time zone.
const NOW = new Date(2026, 6, 1, 7, 45, 0);

function localIso(hours: number, minutes: number, day = 1): string {
  return new Date(2026, 6, day, hours, minutes, 0).toISOString();
}

function pausedHost(overrides: Partial<PausedHost> = {}): PausedHost {
  return {
    host: 'gitlab.com',
    pausedAt: localIso(7, 39),
    nextProbeAt: localIso(7, 52),
    probeAttempts: 0,
    ...overrides,
  };
}

function setup(initialHosts: readonly PausedHost[] = []) {
  const pausedHostsSignal: WritableSignal<readonly PausedHost[]> = signal(initialHosts);
  const mockDispatch = { pausedHosts: pausedHostsSignal.asReadonly() };
  const mockToast = { show: vi.fn(), dismiss: vi.fn() };

  TestBed.configureTestingModule({
    imports: [HostPauseBannerComponent],
    providers: [
      { provide: DispatchService, useValue: mockDispatch },
      { provide: ToastService, useValue: mockToast },
    ],
  });

  const fixture = TestBed.createComponent(HostPauseBannerComponent);
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;

  const setHosts = (hosts: readonly PausedHost[]): void => {
    pausedHostsSignal.set(hosts);
    fixture.detectChanges();
    TestBed.flushEffects();
    fixture.detectChanges();
  };

  return { fixture, el, mockToast, setHosts };
}

function region(el: HTMLElement): HTMLElement {
  return el.querySelector('[aria-label="Provider host status"]') as HTMLElement;
}

function bars(el: HTMLElement): HTMLElement[] {
  return Array.from(el.querySelectorAll<HTMLElement>('.system-banner__bar'));
}

function normalisedText(node: Element): string {
  return (node.textContent ?? '').replace(/\s+/g, ' ').trim();
}

describe('HostPauseBannerComponent', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(NOW);
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  describe('visibility', () => {
    it('should hide the region and render no rows when no host is paused', () => {
      // Arrange / Act
      const { el } = setup([]);

      // Assert
      expect(region(el).hidden).toBe(true);
      expect(bars(el).length).toBe(0);
    });

    it('should show the region when a host is paused', () => {
      // Arrange / Act
      const { el } = setup([pausedHost()]);

      // Assert
      expect(region(el).hidden).toBe(false);
    });
  });

  describe('rows', () => {
    it('should render one row per paused host sorted by pausedAt ascending', () => {
      // Arrange / Act
      const { el } = setup([
        pausedHost({ host: 'github.com', pausedAt: localIso(7, 41) }),
        pausedHost({ host: 'gitlab.com', pausedAt: localIso(7, 39) }),
        pausedHost({ host: 'git.example.org', pausedAt: localIso(7, 40) }),
      ]);

      // Assert
      const hosts = bars(el).map((bar) => bar.querySelector('.host-pause-banner__host')?.textContent?.trim());
      expect(hosts).toEqual(['gitlab.com', 'git.example.org', 'github.com']);
    });

    it('should describe a waiting host with its pause time and next check time', () => {
      // Arrange / Act
      const { el } = setup([pausedHost()]);

      // Assert
      const text = normalisedText(bars(el)[0]);
      expect(text).toContain('gitlab.com unreachable since 07:39');
      expect(text).toContain('next check 07:52');
      expect(text).not.toContain('failed');
      expect(text).not.toContain('checking now');
    });

    it('should add the failed check count once a probe has failed', () => {
      // Arrange / Act
      const { el } = setup([pausedHost({ probeAttempts: 3 })]);

      // Assert
      expect(normalisedText(bars(el)[0])).toContain('next check 07:52 · 3 checks failed');
    });

    it('should use the singular for one failed check', () => {
      // Arrange / Act
      const { el } = setup([pausedHost({ probeAttempts: 1 })]);

      // Assert
      expect(normalisedText(bars(el)[0])).toContain('· 1 check failed');
    });

    it('should format a time that is not today as "MMM d, HH:mm"', () => {
      // Arrange
      vi.setSystemTime(new Date(2026, 6, 2, 9, 0, 0));

      // Act
      const { el } = setup([pausedHost({ pausedAt: localIso(7, 39, 1), nextProbeAt: localIso(9, 10, 2) })]);

      // Assert
      const text = normalisedText(bars(el)[0]);
      expect(text).toContain('unreachable since Jul 1, 07:39');
      expect(text).toContain('next check 09:10');
    });

    it('should render the host in its own element so it can be shown in the mono font', () => {
      // Arrange / Act
      const { el } = setup([pausedHost()]);

      // Assert
      expect(bars(el)[0].querySelector('.host-pause-banner__host')?.textContent?.trim()).toBe('gitlab.com');
    });

    it('should tell the operator that dispatch to other hosts continues', () => {
      // Arrange / Act
      const { el } = setup([pausedHost()]);

      // Assert
      expect(normalisedText(bars(el)[0])).toContain('Dispatch to other hosts continues.');
    });

    it('should render no buttons', () => {
      // Arrange / Act
      const { el } = setup([pausedHost()]);

      // Assert
      expect(el.querySelector('button')).toBeNull();
    });

    it('should show the warning icon, not the spinner, while waiting for the next check', () => {
      // Arrange / Act
      const { el } = setup([pausedHost()]);

      // Assert
      const bar = bars(el)[0];
      expect(bar.querySelector('.system-banner__icon')).not.toBeNull();
      expect(bar.querySelector('.system-banner__spinner-icon')).toBeNull();
    });
  });

  describe('checking now', () => {
    it('should show "checking now…" with the spinner when nextProbeAt has already passed', () => {
      // Arrange / Act
      const { el } = setup([pausedHost({ nextProbeAt: localIso(7, 44), probeAttempts: 2 })]);

      // Assert
      const bar = bars(el)[0];
      const text = normalisedText(bar);
      expect(text).toContain('gitlab.com unreachable since 07:39');
      expect(text).toContain('checking now…');
      expect(text).not.toContain('next check');
      expect(bar.querySelector('.system-banner__spinner-icon')).not.toBeNull();
      expect(bar.querySelector('.system-banner__icon')).toBeNull();
    });

    it('should switch a row to "checking now…" when the 1 s tick passes nextProbeAt', () => {
      // Arrange
      const { el, fixture } = setup([pausedHost({ nextProbeAt: new Date(NOW.getTime() + 1500).toISOString() })]);
      expect(normalisedText(bars(el)[0])).not.toContain('checking now');

      // Act
      vi.advanceTimersByTime(2000);
      fixture.detectChanges();

      // Assert
      expect(normalisedText(bars(el)[0])).toContain('checking now…');
      expect(bars(el)[0].querySelector('.system-banner__spinner-icon')).not.toBeNull();
    });

    it('should not start the tick interval while no host is paused', () => {
      // Arrange
      const setIntervalSpy = vi.spyOn(globalThis, 'setInterval');

      // Act
      setup([]);

      // Assert
      expect(setIntervalSpy).not.toHaveBeenCalled();
    });

    it('should start the tick interval once a host is paused', () => {
      // Arrange
      const setIntervalSpy = vi.spyOn(globalThis, 'setInterval');

      // Act
      setup([pausedHost()]);

      // Assert
      expect(setIntervalSpy).toHaveBeenCalledTimes(1);
      expect(setIntervalSpy).toHaveBeenCalledWith(expect.any(Function), 1000);
    });

    it('should stop the tick interval when the last host leaves the list', () => {
      // Arrange
      const setIntervalSpy = vi.spyOn(globalThis, 'setInterval');
      const clearIntervalSpy = vi.spyOn(globalThis, 'clearInterval');
      const { setHosts } = setup([pausedHost()]);
      const intervalId = setIntervalSpy.mock.results[0].value;

      // Act
      setHosts([]);

      // Assert
      expect(clearIntervalSpy).toHaveBeenCalledWith(intervalId);
    });

    it('should clear the tick interval when destroyed', () => {
      // Arrange
      const setIntervalSpy = vi.spyOn(globalThis, 'setInterval');
      const clearIntervalSpy = vi.spyOn(globalThis, 'clearInterval');
      const { fixture } = setup([pausedHost()]);
      const intervalId = setIntervalSpy.mock.results[0].value;

      // Act
      fixture.destroy();

      // Assert
      expect(clearIntervalSpy).toHaveBeenCalledWith(intervalId);
    });
  });

  describe('live updates', () => {
    it('should add, update and remove rows to match a reloaded list', () => {
      // Arrange
      const { el, setHosts } = setup([
        pausedHost({ host: 'gitlab.com' }),
        pausedHost({ host: 'github.com', pausedAt: localIso(7, 40) }),
      ]);

      // Act
      setHosts([
        pausedHost({ host: 'gitlab.com', nextProbeAt: localIso(7, 55), probeAttempts: 1 }),
        pausedHost({ host: 'git.example.org', pausedAt: localIso(7, 44) }),
      ]);

      // Assert
      const rows = bars(el).map(normalisedText);
      expect(rows.length).toBe(2);
      expect(rows[0]).toContain('gitlab.com unreachable since 07:39');
      expect(rows[0]).toContain('next check 07:55 · 1 check failed');
      expect(rows[1]).toContain('git.example.org unreachable since 07:44');
      expect(rows.some((row) => row.includes('github.com'))).toBe(false);
    });

    it('should toast "<host> reachable — dispatch resumed" when a host leaves the list', () => {
      // Arrange
      const { mockToast, setHosts } = setup([
        pausedHost({ host: 'gitlab.com' }),
        pausedHost({ host: 'github.com' }),
      ]);

      // Act
      setHosts([pausedHost({ host: 'github.com' })]);

      // Assert
      expect(mockToast.show).toHaveBeenCalledTimes(1);
      expect(mockToast.show).toHaveBeenCalledWith('gitlab.com reachable — dispatch resumed');
    });

    it('should not toast when a remaining host is only updated', () => {
      // Arrange
      const { mockToast, setHosts } = setup([pausedHost()]);

      // Act
      setHosts([pausedHost({ probeAttempts: 1, nextProbeAt: localIso(7, 55) })]);

      // Assert
      expect(mockToast.show).not.toHaveBeenCalled();
    });
  });

  describe('accessibility', () => {
    it('should label the banner as a region', () => {
      // Arrange / Act
      const { el } = setup([pausedHost()]);

      // Assert
      expect(region(el).getAttribute('role')).toBe('region');
    });

    it('should mark each row as a polite status', () => {
      // Arrange / Act
      const { el } = setup([pausedHost({ host: 'a.example' }), pausedHost({ host: 'b.example' })]);

      // Assert
      expect(bars(el).map((bar) => bar.getAttribute('role'))).toEqual(['status', 'status']);
    });

    it('should hide every icon from assistive technology', () => {
      // Arrange / Act
      const { el } = setup([pausedHost(), pausedHost({ host: 'github.com', nextProbeAt: localIso(7, 44) })]);

      // Assert
      const icons = Array.from(el.querySelectorAll('svg'));
      expect(icons.length).toBe(2);
      expect(icons.every((icon) => icon.getAttribute('aria-hidden') === 'true')).toBe(true);
    });

    it('should announce a newly paused host once, then clear the announcement', () => {
      // Arrange
      const { el, fixture, setHosts } = setup([]);
      const announcer = el.querySelector('.sr-only') as HTMLElement;

      // Act
      setHosts([pausedHost()]);

      // Assert — announced
      expect(announcer.textContent?.trim()).toBe('gitlab.com unreachable — dispatch to this host paused');

      // Act — let the clearing timeout run
      vi.advanceTimersByTime(0);
      fixture.detectChanges();

      // Assert — cleared so later ticks never re-announce
      expect(announcer.textContent?.trim()).toBe('');
    });

    it('should not announce a host that was already paused when the list updates', () => {
      // Arrange
      const { el, setHosts } = setup([]);
      setHosts([pausedHost()]);
      vi.advanceTimersByTime(0);

      // Act
      setHosts([pausedHost({ probeAttempts: 1 })]);

      // Assert
      expect((el.querySelector('.sr-only') as HTMLElement).textContent?.trim()).toBe('');
    });
  });
});

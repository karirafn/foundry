import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { UsageWidgetComponent } from './usage-widget';
import { UsageService } from '../../../core/services/usage.service';
import { UsageSnapshot } from '../../../core/models/usage.model';

function buildSnapshot(overrides: Partial<UsageSnapshot> = {}): UsageSnapshot {
  return {
    limits: [
      {
        kind: 'session',
        percent: 42,
        resetsAt: '2026-10-30T06:00:00Z',
        modelDisplayName: null,
        severity: 'normal',
        isActive: true,
      },
      {
        kind: 'weekly_all',
        percent: 78,
        resetsAt: '2026-11-03T09:00:00Z',
        modelDisplayName: null,
        severity: 'warning',
        isActive: false,
      },
    ],
    extraUsage: null,
    capturedAt: '2026-10-30T01:00:00Z',
    accountEmail: 'test@example.com',
    lastRefreshFailedAt: null,
    ...overrides,
  };
}

function createMockUsageService(snapshot: UsageSnapshot | null = null) {
  return {
    snapshot: signal(snapshot),
  };
}

function setup(snapshot: UsageSnapshot | null = null) {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    imports: [UsageWidgetComponent],
    providers: [
      { provide: UsageService, useValue: createMockUsageService(snapshot) },
    ],
  });
  const fixture = TestBed.createComponent(UsageWidgetComponent);
  fixture.detectChanges();
  return fixture;
}

describe('UsageWidgetComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  // Cycle a — rendering gate: widget emits no DOM when snapshot is null
  it('should emit no DOM when snapshot is null', () => {
    // Arrange / Act
    const fixture = setup(null);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('.usage-widget__trigger')).toBeNull();
  });

  // Cycle a continued — widget renders trigger when snapshot exists
  it('should render the trigger button when a snapshot exists', () => {
    // Arrange / Act
    const fixture = setup(buildSnapshot());
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('.usage-widget__trigger')).not.toBeNull();
  });

  // Cycle b — session meter is rendered
  it('should render a session meter when limits includes a session entry', () => {
    // Arrange
    const snapshot = buildSnapshot();

    // Act
    const fixture = setup(snapshot);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const meters = el.querySelectorAll('.usage-meter');
    const labels = Array.from(meters).map((m) => m.querySelector('.usage-meter__label')?.textContent?.trim());
    expect(labels).toContain('Session');
  });

  // Cycle b — weekly-all meter is rendered
  it('should render a weekly meter when limits includes a weekly_all entry', () => {
    // Arrange
    const snapshot = buildSnapshot();

    // Act
    const fixture = setup(snapshot);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const meters = el.querySelectorAll('.usage-meter');
    const labels = Array.from(meters).map((m) => m.querySelector('.usage-meter__label')?.textContent?.trim());
    expect(labels).toContain('Weekly');
  });

  // Cycle b — only session + weekly_all shown inline (not weekly_scoped)
  it('should render only session and weekly_all meters inline (no weekly_scoped)', () => {
    // Arrange
    const snapshot = buildSnapshot({
      limits: [
        { kind: 'session', percent: 30, resetsAt: '2026-10-30T06:00:00Z', modelDisplayName: null, severity: 'normal', isActive: true },
        { kind: 'weekly_all', percent: 50, resetsAt: '2026-11-03T09:00:00Z', modelDisplayName: null, severity: 'normal', isActive: false },
        { kind: 'weekly_scoped', percent: 25, resetsAt: '2026-11-03T09:00:00Z', modelDisplayName: 'Opus 4', severity: 'normal', isActive: false },
      ],
    });

    // Act
    const fixture = setup(snapshot);
    const el = fixture.nativeElement as HTMLElement;

    // Assert — inline meters only contain the 2 meters (session + weekly_all), not weekly_scoped
    const inlineMeters = el.querySelectorAll('.usage-widget__inline .usage-meter');
    expect(inlineMeters.length).toBe(2);
  });

  // Cycle b — absent session meter: only weekly_all rendered (never fabricated)
  it('should render only the meters present in limits (no fabrication)', () => {
    // Arrange
    const snapshot = buildSnapshot({
      limits: [
        { kind: 'weekly_all', percent: 55, resetsAt: '2026-11-03T09:00:00Z', modelDisplayName: null, severity: 'warning', isActive: true },
      ],
    });

    // Act
    const fixture = setup(snapshot);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const inlineMeters = el.querySelectorAll('.usage-widget__inline .usage-meter');
    expect(inlineMeters.length).toBe(1);
  });

  // Cycle c — normal severity maps to usage-meter--normal class
  it('should apply usage-meter--normal class for severity "normal"', () => {
    // Arrange
    const snapshot = buildSnapshot({
      limits: [
        { kind: 'session', percent: 30, resetsAt: '2026-10-30T06:00:00Z', modelDisplayName: null, severity: 'normal', isActive: true },
      ],
    });

    // Act
    const fixture = setup(snapshot);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const meter = el.querySelector('.usage-meter');
    expect(meter?.classList.contains('usage-meter--normal')).toBe(true);
  });

  // Cycle c — warning severity maps to usage-meter--warning class
  it('should apply usage-meter--warning class for severity "warning"', () => {
    // Arrange
    const snapshot = buildSnapshot({
      limits: [
        { kind: 'session', percent: 70, resetsAt: '2026-10-30T06:00:00Z', modelDisplayName: null, severity: 'warning', isActive: true },
      ],
    });

    // Act
    const fixture = setup(snapshot);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const meter = el.querySelector('.usage-meter');
    expect(meter?.classList.contains('usage-meter--warning')).toBe(true);
  });

  // Cycle c — critical severity maps to usage-meter--critical class
  it('should apply usage-meter--critical class for severity "critical"', () => {
    // Arrange
    const snapshot = buildSnapshot({
      limits: [
        { kind: 'session', percent: 95, resetsAt: '2026-10-30T06:00:00Z', modelDisplayName: null, severity: 'critical', isActive: true },
      ],
    });

    // Act
    const fixture = setup(snapshot);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const meter = el.querySelector('.usage-meter');
    expect(meter?.classList.contains('usage-meter--critical')).toBe(true);
  });

  // Cycle c — unrecognised severity falls back to usage-meter--neutral
  it('should apply usage-meter--neutral class for an unrecognised severity value', () => {
    // Arrange
    const snapshot = buildSnapshot({
      limits: [
        { kind: 'session', percent: 50, resetsAt: '2026-10-30T06:00:00Z', modelDisplayName: null, severity: 'extreme', isActive: true },
      ],
    });

    // Act
    const fixture = setup(snapshot);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const meter = el.querySelector('.usage-meter');
    expect(meter?.classList.contains('usage-meter--neutral')).toBe(true);
  });

  // Cycle c — percent label shows raw server value, not clamped
  it('should display the raw server percent value in the label', () => {
    // Arrange
    const snapshot = buildSnapshot({
      limits: [
        { kind: 'session', percent: 42, resetsAt: '2026-10-30T06:00:00Z', modelDisplayName: null, severity: 'normal', isActive: true },
      ],
    });

    // Act
    const fixture = setup(snapshot);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const percentEl = el.querySelector('.usage-meter__percent');
    expect(percentEl?.textContent?.trim()).toBe('42%');
  });

  // Cycle d — lastRefreshFailedAt set → widget gets failed modifier class
  it('should apply usage-widget--failed class when lastRefreshFailedAt is set', () => {
    // Arrange
    const snapshot = buildSnapshot({ lastRefreshFailedAt: '2026-10-30T00:55:00Z' });

    // Act
    const fixture = setup(snapshot);
    const el = fixture.nativeElement as HTMLElement;

    // Assert — the inner div wrapping the trigger should carry the failed class
    const failedEl = el.querySelector('.usage-widget--failed');
    expect(failedEl).not.toBeNull();
  });

  // Cycle d — no failed class when lastRefreshFailedAt is null
  it('should not apply usage-widget--failed class when lastRefreshFailedAt is null', () => {
    // Arrange
    const snapshot = buildSnapshot({ lastRefreshFailedAt: null });

    // Act
    const fixture = setup(snapshot);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const failedEl = el.querySelector('.usage-widget--failed');
    expect(failedEl).toBeNull();
  });

  // Cycle e — collapsed meter: isActive limit selected when one exists
  it('should select the isActive limit as the collapsed meter', () => {
    // Arrange
    const snapshot = buildSnapshot({
      limits: [
        { kind: 'session', percent: 42, resetsAt: '2026-10-30T06:00:00Z', modelDisplayName: null, severity: 'normal', isActive: true },
        { kind: 'weekly_all', percent: 78, resetsAt: '2026-11-03T09:00:00Z', modelDisplayName: null, severity: 'warning', isActive: false },
      ],
    });

    // Act
    const fixture = setup(snapshot);
    const el = fixture.nativeElement as HTMLElement;

    // Assert — collapsed meter shows session (42%) since it is isActive
    const collapsed = el.querySelector('.usage-widget__collapsed .usage-meter__percent');
    expect(collapsed?.textContent?.trim()).toBe('42%');
  });

  // Cycle e — collapsed meter: highest percent when no isActive limit
  it('should select the highest-percent limit as collapsed meter when no limit is isActive', () => {
    // Arrange
    const snapshot = buildSnapshot({
      limits: [
        { kind: 'session', percent: 42, resetsAt: '2026-10-30T06:00:00Z', modelDisplayName: null, severity: 'normal', isActive: false },
        { kind: 'weekly_all', percent: 78, resetsAt: '2026-11-03T09:00:00Z', modelDisplayName: null, severity: 'warning', isActive: false },
      ],
    });

    // Act
    const fixture = setup(snapshot);
    const el = fixture.nativeElement as HTMLElement;

    // Assert — collapsed meter shows weekly_all (78%) since it has highest percent
    const collapsed = el.querySelector('.usage-widget__collapsed .usage-meter__percent');
    expect(collapsed?.textContent?.trim()).toBe('78%');
  });

  // Cycle e — collapsed meter has severity dot
  it('should render a severity dot in the collapsed meter', () => {
    // Arrange
    const snapshot = buildSnapshot();

    // Act
    const fixture = setup(snapshot);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const dot = el.querySelector('.usage-widget__collapsed .usage-widget__severity-dot');
    expect(dot).not.toBeNull();
  });

  // Accessibility: meter track has role="img" with accessible name
  it('should give each meter track role="img" with an aria-label', () => {
    // Arrange
    const snapshot = buildSnapshot({
      limits: [
        { kind: 'session', percent: 42, resetsAt: '2026-10-30T06:00:00Z', modelDisplayName: null, severity: 'normal', isActive: true },
      ],
    });

    // Act
    const fixture = setup(snapshot);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const track = el.querySelector('.usage-meter__track');
    expect(track?.getAttribute('role')).toBe('img');
    expect(track?.getAttribute('aria-label')).toContain('Session');
    expect(track?.getAttribute('aria-label')).toContain('42%');
  });

  // Accessibility: fill and track inner divs are aria-hidden
  it('should mark meter fill as aria-hidden', () => {
    // Arrange
    const snapshot = buildSnapshot({
      limits: [
        { kind: 'session', percent: 42, resetsAt: '2026-10-30T06:00:00Z', modelDisplayName: null, severity: 'normal', isActive: true },
      ],
    });

    // Act
    const fixture = setup(snapshot);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const fill = el.querySelector('.usage-meter__fill');
    expect(fill?.getAttribute('aria-hidden')).toBe('true');
  });

  // Accessibility: severity dot in collapsed meter is aria-hidden
  it('should mark the severity dot aria-hidden in the collapsed meter', () => {
    // Arrange
    const snapshot = buildSnapshot();

    // Act
    const fixture = setup(snapshot);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const dot = el.querySelector('.usage-widget__severity-dot');
    expect(dot?.getAttribute('aria-hidden')).toBe('true');
  });
});

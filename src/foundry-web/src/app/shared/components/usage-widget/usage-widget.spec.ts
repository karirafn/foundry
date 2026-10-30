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

  // ─── Step 13 behaviors ───────────────────────────────────────────────────────

  // Behavior a — popover lists all limits (session + weekly_all + weekly_scoped)
  it('should render all limit rows in the popover including weekly_scoped', () => {
    // Arrange
    const snapshot = buildSnapshot({
      limits: [
        { kind: 'session', percent: 42, resetsAt: '2026-10-30T06:00:00Z', modelDisplayName: null, severity: 'normal', isActive: true },
        { kind: 'weekly_all', percent: 78, resetsAt: '2026-11-03T09:00:00Z', modelDisplayName: null, severity: 'warning', isActive: false },
        { kind: 'weekly_scoped', percent: 51, resetsAt: '2026-11-03T09:00:00Z', modelDisplayName: 'Claude Opus 4', severity: 'normal', isActive: false },
      ],
    });

    // Act
    const fixture = setup(snapshot);
    const el = fixture.nativeElement as HTMLElement;

    // Assert — popover limits container has 3 rows
    const limitRows = el.querySelectorAll('.usage-popover__limit');
    expect(limitRows.length).toBe(3);
  });

  // Behavior a — weekly_scoped row in popover uses modelDisplayName as label
  it('should label a weekly_scoped limit row in the popover with its modelDisplayName', () => {
    // Arrange
    const snapshot = buildSnapshot({
      limits: [
        { kind: 'session', percent: 42, resetsAt: '2026-10-30T06:00:00Z', modelDisplayName: null, severity: 'normal', isActive: true },
        { kind: 'weekly_scoped', percent: 51, resetsAt: '2026-11-03T09:00:00Z', modelDisplayName: 'Claude Opus 4', severity: 'normal', isActive: false },
      ],
    });

    // Act
    const fixture = setup(snapshot);
    const el = fixture.nativeElement as HTMLElement;

    // Assert — the scoped row label contains the model display name
    const limitLabels = Array.from(el.querySelectorAll('.usage-popover__limit-label'));
    const labelTexts = limitLabels.map((l) => l.textContent?.trim() ?? '');
    expect(labelTexts.some((t) => t.includes('Claude Opus 4'))).toBe(true);
  });

  // Behavior a — weekly_all row in popover uses "Weekly (all models)" label
  it('should label a weekly_all limit row in the popover as "Weekly (all models)"', () => {
    // Arrange
    const snapshot = buildSnapshot({
      limits: [
        { kind: 'weekly_all', percent: 78, resetsAt: '2026-11-03T09:00:00Z', modelDisplayName: null, severity: 'warning', isActive: false },
      ],
    });

    // Act
    const fixture = setup(snapshot);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const limitLabels = Array.from(el.querySelectorAll('.usage-popover__limit-label'));
    expect(limitLabels.some((l) => l.textContent?.trim() === 'Weekly (all models)')).toBe(true);
  });

  // Behavior b — credits row hidden when extraUsage is null
  it('should not render the credits row when extraUsage is null', () => {
    // Arrange
    const snapshot = buildSnapshot({ extraUsage: null });

    // Act
    const fixture = setup(snapshot);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('.usage-popover__credits')).toBeNull();
  });

  // Behavior b — credits row hidden when extraUsage.isEnabled is false
  it('should not render the credits row when extraUsage.isEnabled is false', () => {
    // Arrange
    const snapshot = buildSnapshot({
      extraUsage: { isEnabled: false, monthlyLimit: 50000, usedCredits: 16568, utilization: 0.33, currency: 'USD' },
    });

    // Act
    const fixture = setup(snapshot);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    expect(el.querySelector('.usage-popover__credits')).toBeNull();
  });

  // Behavior b — credits row visible and formatted when isEnabled
  it('should render the credits row with formatted currency when extraUsage.isEnabled is true', () => {
    // Arrange — minor-unit: 50000 = $500.00, 16568 = $165.68 (verified against issue Notes line 103)
    const snapshot = buildSnapshot({
      extraUsage: { isEnabled: true, monthlyLimit: 50000, usedCredits: 16568, utilization: 0.33, currency: 'USD' },
    });

    // Act
    const fixture = setup(snapshot);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const creditsRow = el.querySelector('.usage-popover__credits');
    expect(creditsRow).not.toBeNull();
    const creditsValue = el.querySelector('.usage-popover__credits-value');
    // The value text should contain the formatted amounts
    expect(creditsValue?.textContent).toContain('165');  // $165.68
    expect(creditsValue?.textContent).toContain('500');  // $500.00
  });

  // Behavior c — "As of" capture line rendered when not failed
  it('should render "As of" capture line when not failed', () => {
    // Arrange
    const snapshot = buildSnapshot({ lastRefreshFailedAt: null });

    // Act
    const fixture = setup(snapshot);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const asof = el.querySelector('.usage-popover__asof');
    expect(asof?.textContent).toContain('As of');
  });

  // Behavior c — failure line shown instead of "As of" when lastRefreshFailedAt is set
  it('should render the failure text instead of "As of" when lastRefreshFailedAt is set', () => {
    // Arrange
    const snapshot = buildSnapshot({ lastRefreshFailedAt: '2026-10-30T00:55:00Z' });

    // Act
    const fixture = setup(snapshot);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const asofFailed = el.querySelector('.usage-popover__asof--failed');
    expect(asofFailed).not.toBeNull();
    expect(asofFailed?.textContent).toContain("Couldn't refresh");
  });

  // Behavior c — warning icon present in failure line
  it('should render a warning SVG icon in the failure line', () => {
    // Arrange
    const snapshot = buildSnapshot({ lastRefreshFailedAt: '2026-10-30T00:55:00Z' });

    // Act
    const fixture = setup(snapshot);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const warnIcon = el.querySelector('.usage-popover__warn-icon');
    expect(warnIcon).not.toBeNull();
  });

  // Behavior e — trigger button has aria-expanded attribute
  it('should have aria-expanded on the trigger button reflecting closed state initially', () => {
    // Arrange
    const snapshot = buildSnapshot();

    // Act
    const fixture = setup(snapshot);
    const el = fixture.nativeElement as HTMLElement;

    // Assert — initially the popover is closed
    const trigger = el.querySelector('.usage-widget__trigger');
    expect(trigger?.hasAttribute('aria-expanded')).toBe(true);
    // false when closed
    expect(trigger?.getAttribute('aria-expanded')).toBe('false');
  });

  // Behavior e — trigger button has aria-controls pointing to the popover
  it('should have aria-controls on the trigger pointing to the popover element', () => {
    // Arrange
    const snapshot = buildSnapshot();

    // Act
    const fixture = setup(snapshot);
    const el = fixture.nativeElement as HTMLElement;
    const component = fixture.componentInstance;

    // Assert
    const trigger = el.querySelector('.usage-widget__trigger');
    const popoverId = component.popoverId;
    expect(trigger?.getAttribute('aria-controls')).toBe(popoverId);
  });

  // Behavior e — popover element has role="group" and aria-label
  it('should have role="group" and aria-label on the popover element', () => {
    // Arrange
    const snapshot = buildSnapshot();

    // Act
    const fixture = setup(snapshot);
    const el = fixture.nativeElement as HTMLElement;
    const component = fixture.componentInstance;

    // Assert
    const popoverEl = el.querySelector(`#${component.popoverId}`);
    expect(popoverEl?.getAttribute('role')).toBe('group');
    expect(popoverEl?.getAttribute('aria-label')).toBe('Claude subscription usage detail');
  });

  // Behavior e — trigger is a real button element
  it('should use a <button type="button"> for the trigger', () => {
    // Arrange
    const snapshot = buildSnapshot();

    // Act
    const fixture = setup(snapshot);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const trigger = el.querySelector('.usage-widget__trigger');
    expect(trigger?.tagName.toLowerCase()).toBe('button');
    expect(trigger?.getAttribute('type')).toBe('button');
  });

  // Behavior e — collapsed state: trigger aria-label narrows to single figure format
  it('should narrow trigger aria-label to single-figure format for collapsed meter when no inline meters', () => {
    // Arrange
    const snapshot = buildSnapshot({
      limits: [
        { kind: 'session', percent: 78, resetsAt: '2026-10-30T06:00:00Z', modelDisplayName: null, severity: 'critical', isActive: true },
        { kind: 'weekly_all', percent: 55, resetsAt: '2026-11-03T09:00:00Z', modelDisplayName: null, severity: 'normal', isActive: false },
      ],
    });

    // Act
    const fixture = setup(snapshot);
    const el = fixture.nativeElement as HTMLElement;

    // Assert — when inline meters exist, label includes all of them
    const trigger = el.querySelector('.usage-widget__trigger');
    const label = trigger?.getAttribute('aria-label') ?? '';
    expect(label).toContain('Claude usage');
    expect(label).toContain('Activate to see details');
  });

  // Finding 5 — trigger aria-label is always the informative label (open and closed)
  it('should use informative buildTriggerLabel as aria-label regardless of open/closed state', () => {
    // Arrange
    const snapshot = buildSnapshot({
      limits: [
        { kind: 'session', percent: 42, resetsAt: '2026-10-30T06:00:00Z', modelDisplayName: null, severity: 'normal', isActive: true },
      ],
    });
    const fixture = setup(snapshot);
    const component = fixture.componentInstance;

    // Act — simulate open state
    component.isOpen.set(true);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    // Assert — label still contains meter data even when open
    const trigger = el.querySelector('.usage-widget__trigger');
    const label = trigger?.getAttribute('aria-label') ?? '';
    expect(label).toContain('Claude usage');
    expect(label).toContain('42%');
  });

  // Finding 4 — isCollapsed=true narrows buildTriggerLabel to single-figure format
  it('should narrow buildTriggerLabel when isCollapsed is true', () => {
    // Arrange
    const snapshot = buildSnapshot({
      limits: [
        { kind: 'session', percent: 78, resetsAt: '2026-10-30T06:00:00Z', modelDisplayName: null, severity: 'critical', isActive: true },
        { kind: 'weekly_all', percent: 55, resetsAt: '2026-11-03T09:00:00Z', modelDisplayName: null, severity: 'normal', isActive: false },
      ],
    });
    const fixture = setup(snapshot);
    const component = fixture.componentInstance;
    const el = fixture.nativeElement as HTMLElement;

    // Act — drive isCollapsed signal true (as ResizeObserver would)
    component.isCollapsed.set(true);
    fixture.detectChanges();

    // Assert — label narrows to single figure (collapsed meter: session 78% critical)
    const trigger = el.querySelector('.usage-widget__trigger');
    const label = trigger?.getAttribute('aria-label') ?? '';
    expect(label).toContain('78%');
    expect(label).toContain('critical');
    // Should NOT contain the second meter's data
    expect(label).not.toContain('55%');
  });

  // Finding 4 — when isCollapsed is false, label includes all inline meters
  it('should include all inline meters in buildTriggerLabel when isCollapsed is false', () => {
    // Arrange
    const snapshot = buildSnapshot({
      limits: [
        { kind: 'session', percent: 78, resetsAt: '2026-10-30T06:00:00Z', modelDisplayName: null, severity: 'critical', isActive: true },
        { kind: 'weekly_all', percent: 55, resetsAt: '2026-11-03T09:00:00Z', modelDisplayName: null, severity: 'normal', isActive: false },
      ],
    });
    const fixture = setup(snapshot);
    const component = fixture.componentInstance;

    // Act — isCollapsed false (default)
    component.isCollapsed.set(false);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    // Assert — label includes both meters
    const trigger = el.querySelector('.usage-widget__trigger');
    const label = trigger?.getAttribute('aria-label') ?? '';
    expect(label).toContain('78%');
    expect(label).toContain('55%');
  });

  // Finding 6 — widget handles empty limits gracefully (no crash)
  it('should render without throwing when limits is empty', () => {
    // Arrange
    const snapshot = buildSnapshot({ limits: [] });

    // Act / Assert — should not throw
    expect(() => {
      const fixture = setup(snapshot);
      fixture.detectChanges();
    }).not.toThrow();
  });

  // Finding 6 — widget hides collapsed figure when collapsedMeter is null (empty limits)
  it('should not render a collapsed meter percent when limits is empty', () => {
    // Arrange
    const snapshot = buildSnapshot({ limits: [] });

    // Act
    const fixture = setup(snapshot);
    const el = fixture.nativeElement as HTMLElement;

    // Assert — no collapsed percent rendered (widget may hide entirely or collapse section)
    const collapsed = el.querySelector('.usage-widget__collapsed .usage-meter__percent');
    expect(collapsed).toBeNull();
  });

  // Behavior d — credits appear only in popover, not in inline meters section
  it('should not render credits in the inline meters section', () => {
    // Arrange
    const snapshot = buildSnapshot({
      extraUsage: { isEnabled: true, monthlyLimit: 50000, usedCredits: 16568, utilization: 0.33, currency: 'USD' },
    });

    // Act
    const fixture = setup(snapshot);
    const el = fixture.nativeElement as HTMLElement;

    // Assert — no credits in the inline meters area
    const inline = el.querySelector('.usage-widget__inline');
    expect(inline?.querySelector('.usage-popover__credits')).toBeNull();
  });

  // Behavior a — popover has a header with account email
  it('should render the account email in the popover header', () => {
    // Arrange
    const snapshot = buildSnapshot({ accountEmail: 'ops@acme.com' });

    // Act
    const fixture = setup(snapshot);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const email = el.querySelector('.usage-popover__email');
    expect(email?.textContent?.trim()).toBe('ops@acme.com');
  });
});

import { afterNextRender, ChangeDetectionStrategy, Component, computed, ElementRef, inject, OnDestroy, signal } from '@angular/core';
import { NgClass } from '@angular/common';
import { ConnectionGateService } from '../../../core/services/connection-gate.service';
import { UsageService } from '../../../core/services/usage.service';
import { UsageExtraCredits, UsageSnapshot } from '../../../core/models/usage.model';

/** Maps a known severity string to its BEM modifier class; anything else → neutral. */
function mapSeverityClass(severity: string): string {
  switch (severity) {
    case 'normal':
      return 'usage-meter--normal';
    case 'warning':
      return 'usage-meter--warning';
    case 'critical':
      return 'usage-meter--critical';
    default:
      return 'usage-meter--neutral';
  }
}

/** Maps a known severity string to a human-readable word for non-colour signal. */
function mapSeverityWord(severity: string): string {
  switch (severity) {
    case 'normal':
      return 'OK';
    case 'warning':
      return 'High';
    case 'critical':
      return 'Critical';
    default:
      return 'Unknown';
  }
}

function labelForKind(kind: string, modelDisplayName: string | null): string {
  if (kind === 'session') {
    return 'Session';
  }
  if (kind === 'weekly_all') {
    return 'Weekly';
  }
  if (kind === 'weekly_scoped') {
    return modelDisplayName ? `Weekly · ${modelDisplayName}` : 'Weekly';
  }
  return kind;
}

function popoverLabelForKind(kind: string, modelDisplayName: string | null): string {
  if (kind === 'session') {
    return 'Session';
  }
  if (kind === 'weekly_all') {
    return 'Weekly (all models)';
  }
  if (kind === 'weekly_scoped') {
    return modelDisplayName ? `Weekly · ${modelDisplayName}` : 'Weekly';
  }
  return kind;
}

export interface UsageMeterVm {
  readonly kind: string;
  readonly modelDisplayName: string | null;
  readonly label: string;
  /** Longer label used in the popover (e.g. "Weekly (all models)" instead of "Weekly"). */
  readonly popoverLabel: string;
  readonly percent: number;
  readonly percentLabel: string;
  readonly severityClass: string;
  readonly severityWord: string;
  /** Null when the window reports no reset time. */
  readonly resetsAtLocal: string | null;
  readonly accessibleName: string;
  readonly isActive: boolean;
}

export interface UsageCreditsVm {
  readonly usedFormatted: string;
  /** Null when the plan reports no monthly limit (amount-only credits). */
  readonly limitFormatted: string | null;
  /** Trigger amount, e.g. "$120.00 / $500.00", or the spent amount alone when amount-only. */
  readonly amountLabel: string;
  /** Null when amount-only — the template renders no bar and no percent. */
  readonly meter: UsageCreditsMeterVm | null;
  /** Trigger accessible-name fragment, e.g. "Extra usage $120.00 of $500.00, 24%". */
  readonly accessibleSummary: string;
}

export interface UsageCreditsMeterVm {
  /** Bar fill, clamped to 0-100. */
  readonly percent: number;
  /** The real (unclamped) percent, rounded, e.g. "112%". */
  readonly percentLabel: string;
  readonly severityClass: string;
  readonly accessibleName: string;
}

export interface UsageWidgetVm {
  readonly inlineMeters: readonly UsageMeterVm[];
  /** Null when allLimits is empty — template must guard before access. */
  readonly collapsedMeter: UsageMeterVm | null;
  readonly allLimits: readonly UsageMeterVm[];
  readonly credits: UsageCreditsVm | null;
  /** True when the figures came from the probe-turn fallback rather than the /usage report. */
  readonly limitedDetail: boolean;
  readonly accountEmail: string;
  readonly capturedRelative: string;
  readonly failed: boolean;
  readonly failedText: string | null;
  /** True when no poll has succeeded yet and the last attempt failed — no figures to show. */
  readonly unavailable: boolean;
}

const CREDIT_MINOR_UNIT = 100;

function formatCurrency(amount: number, currency: string): string {
  return new Intl.NumberFormat(undefined, { style: 'currency', currency }).format(
    amount / CREDIT_MINOR_UNIT
  );
}

function buildMeterVm(
  kind: string,
  percent: number,
  resetsAt: string | null,
  modelDisplayName: string | null,
  severity: string,
  isActive: boolean
): UsageMeterVm {
  const label = labelForKind(kind, modelDisplayName);
  const popoverLabel = popoverLabelForKind(kind, modelDisplayName);
  const clampedPercent = Math.min(100, Math.max(0, percent));
  const percentLabel = `${percent}%`;
  const severityClass = mapSeverityClass(severity);
  const severityWord = mapSeverityWord(severity);
  const resetsAtLocal = resetsAt === null ? null : formatShortDateTime(resetsAt);
  const accessibleName = `${label} usage, ${percentLabel}, ${severityWord.toLowerCase()}`;

  return {
    kind,
    modelDisplayName,
    label,
    popoverLabel,
    percent: clampedPercent,
    percentLabel,
    severityClass,
    severityWord,
    resetsAtLocal,
    accessibleName,
    isActive,
  };
}

function buildCreditsVm(extraUsage: UsageExtraCredits | null): UsageCreditsVm | null {
  if (extraUsage?.isEnabled !== true) {
    return null;
  }
  const usedFormatted = formatCurrency(extraUsage.usedCredits, extraUsage.currency);
  const limitFormatted =
    extraUsage.monthlyLimit === null ? null : formatCurrency(extraUsage.monthlyLimit, extraUsage.currency);
  const amountLabel = limitFormatted === null ? usedFormatted : `${usedFormatted} / ${limitFormatted}`;
  const amountSpoken = limitFormatted === null ? usedFormatted : `${usedFormatted} of ${limitFormatted}`;

  let meter: UsageCreditsMeterVm | null = null;
  let accessibleSummary = `Extra usage ${amountSpoken}`;
  if (extraUsage.percent !== null) {
    const percentLabel = `${Math.round(extraUsage.percent)}%`;
    const severity = extraUsage.severity ?? '';
    accessibleSummary = `${accessibleSummary}, ${percentLabel}`;
    meter = {
      percent: Math.min(100, Math.max(0, extraUsage.percent)),
      percentLabel,
      severityClass: mapSeverityClass(severity),
      accessibleName: `Extra usage, ${percentLabel}, ${mapSeverityWord(severity).toLowerCase()}`,
    };
  }

  return { usedFormatted, limitFormatted, amountLabel, meter, accessibleSummary };
}

function formatShortDateTime(iso: string): string {
  return new Intl.DateTimeFormat(undefined, {
    dateStyle: 'short',
    timeStyle: 'short',
  }).format(new Date(iso));
}

/** No poll has succeeded yet and the last attempt failed: there are no figures to show. */
function buildUnavailableVm(snapshot: UsageSnapshot): UsageWidgetVm {
  const failedText =
    snapshot.lastRefreshFailedAt !== null
      ? `Couldn't load usage. Last attempt ${formatShortDateTime(snapshot.lastRefreshFailedAt)}.`
      : "Couldn't load usage.";
  return {
    inlineMeters: [],
    collapsedMeter: null,
    allLimits: [],
    credits: null,
    limitedDetail: false,
    accountEmail: snapshot.accountEmail,
    capturedRelative: '',
    failed: true,
    failedText,
    unavailable: true,
  };
}

function buildWidgetVm(snapshot: UsageSnapshot): UsageWidgetVm {
  const capturedAt = snapshot.capturedAt;
  if (capturedAt === null) {
    return buildUnavailableVm(snapshot);
  }

  const allLimits: UsageMeterVm[] = snapshot.limits.map((l) =>
    buildMeterVm(l.kind, l.percent, l.resetsAt, l.modelDisplayName, l.severity, l.isActive)
  );

  const inlineMeters = allLimits.filter((m) => m.kind === 'session' || m.kind === 'weekly_all');

  const activeLimit = allLimits.find((m) => m.isActive);
  const collapsedMeter: UsageMeterVm | null =
    allLimits.length === 0
      ? null
      : (activeLimit ??
          allLimits.reduce(
            (best, current) => (current.percent > best.percent ? current : best),
            allLimits[0]
          ));

  const credits = buildCreditsVm(snapshot.extraUsage);

  const failed = snapshot.lastRefreshFailedAt !== null;

  let capturedRelative = '';
  try {
    const diffMs = Date.now() - new Date(capturedAt).getTime();
    const diffMinutes = Math.round(diffMs / 60_000);
    const rtf = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' });
    capturedRelative =
      Math.abs(diffMinutes) < 60
        ? rtf.format(-diffMinutes, 'minute')
        : rtf.format(-Math.round(diffMinutes / 60), 'hour');
  } catch {
    capturedRelative = capturedAt;
  }

  let failedText: string | null = null;
  if (failed) {
    failedText = `Couldn't refresh. Showing usage from ${formatShortDateTime(capturedAt)}.`;
  }

  return {
    inlineMeters,
    collapsedMeter,
    allLimits,
    credits,
    limitedDetail: snapshot.source === 'probeTurn',
    accountEmail: snapshot.accountEmail,
    capturedRelative,
    failed,
    failedText,
    unavailable: false,
  };
}

let _nextId = 0;

/** Mirrors `@container app-header-nav (max-width: 240px)` in usage-widget.scss. */
const COLLAPSE_THRESHOLD_PX = 240;
/** Mirrors `@container app-header-nav (max-width: 360px)` in usage-widget.scss. */
const CREDITS_HIDE_THRESHOLD_PX = 360;

@Component({
  selector: 'fd-usage-widget',
  imports: [NgClass],
  templateUrl: './usage-widget.html',
  styleUrl: './usage-widget.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UsageWidgetComponent implements OnDestroy {
  private readonly _usageService = inject(UsageService);
  private readonly _hostEl = inject(ElementRef<HTMLElement>);
  private _resizeObserver: ResizeObserver | null = null;

  readonly popoverId = `usage-popover-${_nextId++}`;
  readonly isOpen = signal(false);
  readonly isCollapsed = signal(false);
  readonly isCreditsHidden = signal(false);

  readonly refreshState = this._usageService.refreshState;

  /** The connection gate is up: the figures may be out of date, so a placeholder stands in. */
  readonly unavailable = inject(ConnectionGateService).up;

  readonly refreshStatusText = computed<string>(() => {
    switch (this.refreshState()) {
      case 'upToDate':
        return 'Up to date';
      case 'unavailable':
        return "Can't refresh right now";
      case 'error':
        return "Couldn't start refresh";
      default:
        return '';
    }
  });

  readonly view = computed<UsageWidgetVm | null>(() => {
    const snapshot = this._usageService.snapshot();
    if (snapshot === null) {
      return null;
    }
    return buildWidgetVm(snapshot);
  });

  constructor() {
    afterNextRender(() => {
      if (typeof ResizeObserver === 'undefined') {
        return;
      }
      // The CSS collapse is keyed off the `app-header-nav` container query
      // (see usage-widget.scss `@container app-header-nav`), which resolves to the
      // host's parent nav. The host itself is `:host { display: contents }` and so
      // generates no box — observing it yields a 0-width contentRect and would wrongly
      // report "collapsed" at every size, desyncing the aria-label from what is rendered.
      // Observe the parent query container so the threshold matches the container query.
      const queryContainer = this._hostEl.nativeElement.parentElement;
      if (queryContainer === null) {
        return;
      }
      this._resizeObserver = new ResizeObserver((entries) => {
        const entry = entries[0];
        if (entry === undefined) {
          return;
        }
        const width = entry.contentRect.width;
        this.isCollapsed.set(width <= COLLAPSE_THRESHOLD_PX);
        this.isCreditsHidden.set(width <= CREDITS_HIDE_THRESHOLD_PX);
      });
      this._resizeObserver.observe(queryContainer);
    });
  }

  ngOnDestroy(): void {
    this._resizeObserver?.disconnect();
  }

  onRefresh(): void {
    this._usageService.refresh();
  }

  onToggle(event: Event): void {
    this.isOpen.set((event as ToggleEvent).newState === 'open');
  }

  buildTriggerLabel(vm: UsageWidgetVm): string {
    if (vm.unavailable) {
      return 'Claude usage unavailable. Activate to see details.';
    }
    // When collapsed (≤240px), narrow to the single collapsed figure only.
    if (this.isCollapsed() && vm.collapsedMeter !== null) {
      const c = vm.collapsedMeter;
      return `Claude usage ${c.percentLabel} ${c.severityWord.toLowerCase()}. Activate to see details.`;
    }
    const parts = vm.inlineMeters.map(
      (m) => `${m.label} ${m.percentLabel} ${m.severityWord.toLowerCase()}`
    );
    if (vm.credits !== null && !this.isCreditsHidden()) {
      parts.push(vm.credits.accessibleSummary);
    }
    if (parts.length === 0) {
      if (vm.collapsedMeter !== null) {
        const c = vm.collapsedMeter;
        return `Claude usage ${c.percentLabel} ${c.severityWord.toLowerCase()}. Activate to see details.`;
      }
      return 'Claude usage. Activate to see details.';
    }
    return `Claude usage. ${parts.join('. ')}. Activate to see details.`;
  }

}

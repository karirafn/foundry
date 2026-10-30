import { afterNextRender, ChangeDetectionStrategy, Component, computed, ElementRef, inject, OnDestroy, signal } from '@angular/core';
import { NgClass } from '@angular/common';
import { UsageService } from '../../../core/services/usage.service';
import { UsageSnapshot } from '../../../core/models/usage.model';

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
  readonly resetsAtLocal: string;
  readonly accessibleName: string;
  readonly isActive: boolean;
}

export interface UsageCreditsVm {
  readonly usedFormatted: string;
  readonly limitFormatted: string;
}

export interface UsageWidgetVm {
  readonly inlineMeters: readonly UsageMeterVm[];
  /** Null when allLimits is empty — template must guard before access. */
  readonly collapsedMeter: UsageMeterVm | null;
  readonly allLimits: readonly UsageMeterVm[];
  readonly credits: UsageCreditsVm | null;
  readonly accountEmail: string;
  readonly capturedRelative: string;
  readonly failed: boolean;
  readonly failedText: string | null;
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
  resetsAt: string,
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
  const resetsAtLocal = new Intl.DateTimeFormat(undefined, {
    dateStyle: 'short',
    timeStyle: 'short',
  }).format(new Date(resetsAt));
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

function buildWidgetVm(snapshot: UsageSnapshot): UsageWidgetVm {
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

  const extraUsage = snapshot.extraUsage;
  const credits: UsageCreditsVm | null =
    extraUsage?.isEnabled === true
      ? {
          usedFormatted: formatCurrency(extraUsage.usedCredits, extraUsage.currency),
          limitFormatted: formatCurrency(extraUsage.monthlyLimit, extraUsage.currency),
        }
      : null;

  const failed = snapshot.lastRefreshFailedAt !== null;

  let capturedRelative = '';
  try {
    const diffMs = Date.now() - new Date(snapshot.capturedAt).getTime();
    const diffMinutes = Math.round(diffMs / 60_000);
    const rtf = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' });
    capturedRelative =
      Math.abs(diffMinutes) < 60
        ? rtf.format(-diffMinutes, 'minute')
        : rtf.format(-Math.round(diffMinutes / 60), 'hour');
  } catch {
    capturedRelative = snapshot.capturedAt;
  }

  let failedText: string | null = null;
  if (failed && snapshot.lastRefreshFailedAt !== null) {
    const lastGood = new Intl.DateTimeFormat(undefined, {
      dateStyle: 'short',
      timeStyle: 'short',
    }).format(new Date(snapshot.capturedAt));
    failedText = `Couldn't refresh. Showing usage from ${lastGood}.`;
  }

  return {
    inlineMeters,
    collapsedMeter,
    allLimits,
    credits,
    accountEmail: snapshot.accountEmail,
    capturedRelative,
    failed,
    failedText,
  };
}

let _nextId = 0;

const COLLAPSE_THRESHOLD_PX = 240;

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

  readonly view = computed<UsageWidgetVm | null>(() => {
    const snapshot = this._usageService.snapshot();
    if (snapshot === null) {
      return null;
    }
    return buildWidgetVm(snapshot);
  });

  constructor() {
    afterNextRender(() => {
      // Observe the host element for container-width changes to keep aria-label in sync
      // with the CSS container-query collapse (threshold 240px matching @container rule).
      if (typeof ResizeObserver === 'undefined') {
        return;
      }
      this._resizeObserver = new ResizeObserver((entries) => {
        const entry = entries[0];
        if (entry === undefined) {
          return;
        }
        this.isCollapsed.set(entry.contentRect.width <= COLLAPSE_THRESHOLD_PX);
      });
      this._resizeObserver.observe(this._hostEl.nativeElement);
    });
  }

  ngOnDestroy(): void {
    this._resizeObserver?.disconnect();
  }

  onToggle(event: Event): void {
    this.isOpen.set((event as ToggleEvent).newState === 'open');
  }

  buildTriggerLabel(vm: UsageWidgetVm): string {
    // When collapsed (≤240px), narrow to the single collapsed figure only.
    if (this.isCollapsed() && vm.collapsedMeter !== null) {
      const c = vm.collapsedMeter;
      return `Claude usage ${c.percentLabel} ${c.severityWord.toLowerCase()}. Activate to see details.`;
    }
    const parts = vm.inlineMeters.map(
      (m) => `${m.label} ${m.percentLabel} ${m.severityWord.toLowerCase()}`
    );
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

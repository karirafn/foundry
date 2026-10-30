import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
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

const POPOVER_LABEL_FOR_KIND: Record<string, string> = {
  weekly_all: 'Weekly (all models)',
};

function popoverLabelForKind(kind: string, modelDisplayName: string | null): string {
  if (kind === 'session') {
    return 'Session';
  }
  if (kind === 'weekly_all') {
    return POPOVER_LABEL_FOR_KIND['weekly_all'];
  }
  if (kind === 'weekly_scoped') {
    return modelDisplayName ? `Weekly · ${modelDisplayName}` : 'Weekly';
  }
  return kind;
}

export interface UsageMeterVm {
  readonly kind: string;
  readonly label: string;
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
  readonly collapsedMeter: UsageMeterVm;
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
    label,
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
  const collapsedMeter: UsageMeterVm =
    activeLimit ??
    allLimits.reduce(
      (best, current) => (current.percent > best.percent ? current : best),
      allLimits[0]
    );

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

@Component({
  selector: 'fd-usage-widget',
  imports: [NgClass],
  templateUrl: './usage-widget.html',
  styleUrl: './usage-widget.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UsageWidgetComponent {
  private readonly _usageService = inject(UsageService);

  readonly popoverId = `usage-popover-${_nextId++}`;
  readonly isOpen = signal(false);

  readonly view = computed<UsageWidgetVm | null>(() => {
    const snapshot = this._usageService.snapshot();
    if (snapshot === null) {
      return null;
    }
    return buildWidgetVm(snapshot);
  });

  onToggle(event: Event): void {
    this.isOpen.set((event as ToggleEvent).newState === 'open');
  }

  buildTriggerLabel(vm: UsageWidgetVm): string {
    const parts = vm.inlineMeters.map(
      (m) => `${m.label} ${m.percentLabel} ${m.severityWord.toLowerCase()}`
    );
    if (parts.length === 0) {
      const c = vm.collapsedMeter;
      return `Claude usage ${c.percentLabel} ${c.severityWord.toLowerCase()}. Activate to see details.`;
    }
    return `Claude usage. ${parts.join('. ')}. Activate to see details.`;
  }

  // Expose helpers used in the template
  protected readonly popoverLabelForKind = popoverLabelForKind;
}

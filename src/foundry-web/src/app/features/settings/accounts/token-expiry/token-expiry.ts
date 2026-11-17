import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, InputSignal, Signal, computed, input } from '@angular/core';
import { ResolvedTokenExpiry } from '../account.model';

const MS_PER_DAY = 24 * 60 * 60 * 1000;

function localMidnight(date: Date): number {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate()).getTime();
}

/**
 * The distance from `now` to `expiresAt` in whole local calendar days, as text. Rounding the
 * difference between the two local midnights keeps a daylight-saving change from shifting the count,
 * and the count is clamped at 0 so clock skew never shows "in -1 days".
 */
export function relativeExpiryText(
  status: 'current' | 'expiringSoon' | 'expired',
  expiresAt: string,
  now: Date,
): string {
  const days = Math.round((localMidnight(new Date(expiresAt)) - localMidnight(now)) / MS_PER_DAY);

  if (status === 'expired') {
    const ago = Math.max(0, -days);
    if (ago === 0) {
      return 'today';
    }
    return ago === 1 ? 'yesterday' : `${ago} days ago`;
  }

  const ahead = Math.max(0, days);
  if (ahead === 0) {
    return 'today';
  }
  return ahead === 1 ? 'tomorrow' : `in ${ahead} days`;
}

/** The phrase for an account token's expiry. The host decides its colour, prefix and guidance. */
@Component({
  selector: 'fd-token-expiry',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe],
  template: `
    @switch (expiry().status) {
      @case ('notChecked') {Expiry not checked yet}
      @case ('unavailable') {{{ providerName() }} reported no expiry for this token}
      @case ('never') {Never expires}
      @case ('expired') {Expired on <time [attr.datetime]="_expiresAt()">{{ _expiresAt() | date: 'mediumDate' }}</time> ({{ _relative() }})}
      @default {Expires on <time [attr.datetime]="_expiresAt()">{{ _expiresAt() | date: 'mediumDate' }}</time> ({{ _relative() }})}
    }
  `,
})
export class TokenExpiryComponent {
  readonly expiry: InputSignal<ResolvedTokenExpiry> = input.required<ResolvedTokenExpiry>();
  /** 'GitHub' or 'GitLab'. */
  readonly providerName: InputSignal<string> = input.required<string>();

  protected readonly _expiresAt: Signal<string | null> = computed(() => {
    const value = this.expiry();
    return 'expiresAt' in value ? value.expiresAt : null;
  });

  // Read once per input change; there is no ticking timer.
  protected readonly _relative: Signal<string> = computed(() => {
    const value = this.expiry();
    return 'expiresAt' in value ? relativeExpiryText(value.status, value.expiresAt, new Date()) : '';
  });
}

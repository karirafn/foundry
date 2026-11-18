import {
  ChangeDetectionStrategy,
  Component,
  Signal,
  WritableSignal,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { SystemSignalRService } from '../../../../../core/services/system-signalr.service';
import { SystemStatusService } from '../../../../../core/services/system-status.service';
import {
  ACCOUNT_TOKEN_EXPIRY_NOTIFICATION_CATEGORY,
  SystemNotification,
} from '../../../../../core/models/system-notification.model';

// Neutral on purpose: the banner also clears when an expired token turns Invalid, and "no tokens are expiring"
// would then contradict the invalid-token banner.
export const ACCOUNT_TOKEN_EXPIRY_CLEARED_ANNOUNCEMENT = 'Account token expiry warning cleared.';

@Component({
  selector: 'fd-account-token-expiry-banner',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './account-token-expiry-banner.html',
  styleUrl: './account-token-expiry-banner.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AccountTokenExpiryBannerComponent {
  private readonly _systemSignalR = inject(SystemSignalRService);
  // Injected for its construction side effect: seeds the banner from /api/system/status and re-seeds on reconnect. Do not remove.
  private readonly _systemStatusService = inject(SystemStatusService);

  private readonly _notification: Signal<SystemNotification | undefined> = computed(() =>
    this._systemSignalR.notifications().find((n) => n.category === ACCOUNT_TOKEN_EXPIRY_NOTIFICATION_CATEGORY)
  );

  readonly isVisible: Signal<boolean> = computed(() => !!this._notification());

  /** The server-built copy; the client never formats it. */
  readonly message: Signal<string> = computed(() => this._notification()?.message ?? '');

  /** Visually-hidden one-shot announcement of a change in the banner. */
  private readonly _announcementSignal: WritableSignal<string> = signal('');
  readonly announcement: Signal<string> = this._announcementSignal.asReadonly();

  // Effects cannot read their previous run, so whether the first value was seen is kept here.
  private _seenFirstMessage = false;

  constructor() {
    effect(() => {
      const message = this.message();

      // The first value is the page-load state, which the visible banner already shows.
      if (!this._seenFirstMessage) {
        this._seenFirstMessage = true;
        return;
      }

      this._announcementSignal.set(message !== '' ? message : ACCOUNT_TOKEN_EXPIRY_CLEARED_ANNOUNCEMENT);
      // Cleared straight away so a later re-render never re-announces the same message.
      setTimeout(() => this._announcementSignal.set(''), 0);
    });
  }
}

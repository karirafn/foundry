import { ChangeDetectionStrategy, Component, Signal, WritableSignal, effect, inject, signal, untracked } from '@angular/core';
import { IsActiveMatchOptions, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { SystemBannerComponent } from './shared/components/system-banner/system-banner';
import { ForgeOverlayComponent } from './shared/components/forge-overlay/forge-overlay';
import { ToastHostComponent } from './shared/components/toast-host/toast-host';
import { AccountChipComponent } from './shared/components/account-chip/account-chip';
import { UsageWidgetComponent } from './shared/components/usage-widget/usage-widget';
import { AgentsChipComponent, agentsWorkingText } from './shared/components/agents-chip/agents-chip';
import { WorkerSignalRService } from './core/services/worker-signalr.service';
import { SettingsService } from './core/services/settings.service';
import { AccountService } from './features/settings/accounts/account.service';

@Component({
  selector: 'fd-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, SystemBannerComponent, ForgeOverlayComponent, ToastHostComponent, AccountChipComponent, UsageWidgetComponent, AgentsChipComponent],
  templateUrl: './app.html',
  styleUrl: './app.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App {
  /** Board is the root path, so it is active only there; the open-issue query keeps it active. */
  protected readonly boardMatch: IsActiveMatchOptions = {
    paths: 'exact',
    queryParams: 'ignored',
    matrixParams: 'ignored',
    fragment: 'ignored',
  };

  private readonly _settingsService = inject(SettingsService);
  private readonly _accountService = inject(AccountService);
  private readonly _workerSignalR = inject(WorkerSignalRService);

  readonly overlayBlocking: Signal<boolean> = this._settingsService.isColdBuildBlocking;

  // The shell's single polite announcer: the latest of the account announcement and the
  // active-agents count change wins.
  private readonly _srAnnouncementSignal: WritableSignal<string> = signal('');
  protected readonly srAnnouncement: Signal<string> = this._srAnnouncementSignal.asReadonly();
  private _previousAgentCount: number | null = null;

  constructor() {
    effect(() => {
      const announcement = this._accountService.srAnnouncement();
      untracked(() => this._srAnnouncementSignal.set(announcement));
    });

    // Announce agent-count step changes only — never the initial count — so page loads stay quiet.
    effect(() => {
      const count = this._workerSignalR.activeAgentCount();
      const previous = this._previousAgentCount;
      this._previousAgentCount = count;
      if (previous !== null && previous !== count) {
        untracked(() => this._srAnnouncementSignal.set(agentsWorkingText(count)));
      }
    });
  }
}

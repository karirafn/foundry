import { Component, DestroyRef, ElementRef, Signal, ViewChild, computed, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { SettingsService } from '../../../features/settings/settings.service';
import { AccountService } from '../../../features/settings/accounts/account.service';
import { SystemSignalRService } from '../../../core/services/system-signalr.service';
import { ImageBuildStatus } from '../../../features/settings/settings.model';

const STATUS_TEXT_STARTING = 'Starting…';
const STATUS_TEXT_BUILDING = 'Building worker image…';
const STATUS_TEXT_FAILED = 'Worker image build failed';

@Component({
  selector: 'fd-forge-overlay',
  standalone: true,
  templateUrl: './forge-overlay.html',
  styleUrl: './forge-overlay.scss',
})
export class ForgeOverlayComponent {
  private readonly _settingsService = inject(SettingsService);
  private readonly _accountService = inject(AccountService);
  private readonly _signalR = inject(SystemSignalRService);
  private readonly _destroyRef = inject(DestroyRef);

  @ViewChild('retryButton') retryButtonRef?: ElementRef<HTMLButtonElement>;

  readonly isColdBuildBlocking: Signal<boolean> = computed(
    () => this._accountService.accounts().length > 0 && !this._settingsService.hasUsableImage()
  );

  readonly imageBuildStatus: Signal<ImageBuildStatus> = this._settingsService.imageBuildStatus;
  readonly imageBuildLogTail: Signal<string | null> = this._settingsService.imageBuildLogTail;

  readonly liveAnnouncement: Signal<string> = computed(() => {
    const status = this.imageBuildStatus();
    if (status === 'Failed') {
      return STATUS_TEXT_FAILED;
    }
    if (status === 'Building') {
      return STATUS_TEXT_BUILDING;
    }
    return STATUS_TEXT_STARTING;
  });

  readonly statusTextStarting = STATUS_TEXT_STARTING;
  readonly statusTextBuilding = STATUS_TEXT_BUILDING;
  readonly statusTextFailed = STATUS_TEXT_FAILED;

  private readonly _focusRetrySignal = signal(0);

  constructor() {
    this._signalR.reconnected
      .pipe(takeUntilDestroyed(this._destroyRef))
      .subscribe(() => {
        this._settingsService.loadSettings();
      });

    effect(() => {
      const status = this.imageBuildStatus();
      const blocking = this.isColdBuildBlocking();
      if (blocking && status === 'Failed') {
        this._focusRetrySignal.update((n) => n + 1);
      }
    });

    effect(() => {
      this._focusRetrySignal();
      if (this.retryButtonRef?.nativeElement) {
        this.retryButtonRef.nativeElement.focus();
      }
    });
  }

  retryImageBuild(): void {
    this._settingsService.retryImageBuild();
  }
}

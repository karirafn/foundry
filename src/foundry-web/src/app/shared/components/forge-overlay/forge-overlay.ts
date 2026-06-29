import { Component, ElementRef, Signal, ViewChild, afterRenderEffect, computed, inject } from '@angular/core';
import { SettingsService } from '../../../features/settings/settings.service';
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

  @ViewChild('retryButton') retryButtonRef?: ElementRef<HTMLButtonElement>;

  readonly isColdBuildBlocking: Signal<boolean> = this._settingsService.isColdBuildBlocking;

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

  private readonly _focusRetryAfterRender = afterRenderEffect(() => {
    const shouldFocus = this.isColdBuildBlocking() && this.imageBuildStatus() === 'Failed';
    if (shouldFocus) {
      this.retryButtonRef?.nativeElement?.focus();
    }
  });

  retryImageBuild(): void {
    this._settingsService.retryImageBuild();
  }
}

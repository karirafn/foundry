import { ChangeDetectionStrategy, Component, Signal, WritableSignal, computed, effect, inject, signal } from '@angular/core';
import { DatePipe, SlicePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { SettingsService } from '../../../../../core/services/settings.service';
import { ImageBuildStatus } from '../../../../../core/models/settings.model';

@Component({
  selector: 'fd-image-build-banner',
  standalone: true,
  imports: [RouterLink, SlicePipe],
  templateUrl: './image-build-banner.html',
  styleUrl: './image-build-banner.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [DatePipe],
})
export class ImageBuildBannerComponent {
  protected readonly settingsService = inject(SettingsService);
  private readonly _datePipe = inject(DatePipe);

  private _previousStatus: ImageBuildStatus = 'Idle';

  /** Emits failure text once on the Idle/Building → Failed transition; empty otherwise. */
  private readonly _failureAlertTextSignal: WritableSignal<string> = signal('');
  readonly failureAlertText: Signal<string> = this._failureAlertTextSignal.asReadonly();

  protected readonly failedMessage: Signal<string> = computed(() => {
    const retryAt = this.settingsService.imageBuildNextRetryAt();
    const attempt = this.settingsService.imageBuildAttempt();
    const formattedTime = retryAt ? this._datePipe.transform(retryAt, 'HH:mm') : null;
    const hasRetryAt = formattedTime !== null;
    const hasAttempt = attempt >= 1;

    if (hasRetryAt && hasAttempt) {
      return `Worker image build failed — retrying at ${formattedTime} (attempt ${attempt})`;
    }
    if (hasRetryAt) {
      return `Worker image build failed — retrying at ${formattedTime}`;
    }
    if (hasAttempt) {
      return `Worker image build failed (attempt ${attempt})`;
    }
    return 'Worker image build failed.';
  });

  constructor() {
    effect(() => {
      const status = this.settingsService.imageBuildStatus();
      const prevStatus = this._previousStatus;
      this._previousStatus = status;

      if (status === 'Failed' && prevStatus !== 'Failed') {
        this._failureAlertTextSignal.set('Worker image build failed.');
        // Clear after the AT has had a chance to read it; countdown ticks must not re-announce.
        setTimeout(() => this._failureAlertTextSignal.set(''), 0);
      } else if (status !== 'Failed') {
        this._failureAlertTextSignal.set('');
      }
    });
  }

  retryImageBuild(): void {
    this.settingsService.retryImageBuild();
  }
}

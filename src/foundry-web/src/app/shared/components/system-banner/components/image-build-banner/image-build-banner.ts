import { ChangeDetectionStrategy, Component, Signal, computed, inject } from '@angular/core';
import { DatePipe, SlicePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { SettingsService } from '../../../../../core/services/settings.service';

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

  retryImageBuild(): void {
    this.settingsService.retryImageBuild();
  }
}

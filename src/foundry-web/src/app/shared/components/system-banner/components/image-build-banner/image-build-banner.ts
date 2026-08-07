import { Component, inject } from '@angular/core';
import { SlicePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { SettingsService } from '../../../../../core/services/settings.service';

@Component({
  selector: 'fd-image-build-banner',
  standalone: true,
  imports: [RouterLink, SlicePipe],
  templateUrl: './image-build-banner.html',
  styleUrl: './image-build-banner.scss',
})
export class ImageBuildBannerComponent {
  readonly settingsService = inject(SettingsService);

  retryImageBuild(): void {
    this.settingsService.retryImageBuild();
  }
}

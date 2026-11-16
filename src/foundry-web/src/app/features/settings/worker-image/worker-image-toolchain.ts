import { ChangeDetectionStrategy, Component, Signal, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { EMPTY, merge, of } from 'rxjs';
import { catchError, switchMap } from 'rxjs/operators';
import { SystemSignalRService } from '../../../core/services/system-signalr.service';
import { WorkerImageToolchain } from '../../../core/models/toolchain.model';
import { ToolchainListComponent } from '../../../shared/components/toolchain-list/toolchain-list';

export const WORKER_IMAGE_TOOLCHAIN_URL = '/api/workers/image-toolchain';

const IMAGE_ID_PREFIX = 'sha256:';
const SHORT_IMAGE_ID_LENGTH = 12;

/**
 * The toolchain of the image workers run now, read-only (ADR 0109). Re-fetched on the image-build reload trigger, which
 * Foundry also sends once it stores an image's toolchain, so "Toolchain unknown" gives way to the list on its own.
 */
@Component({
  selector: 'fd-worker-image-toolchain',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, ToolchainListComponent],
  template: `
    @if (toolchain(); as toolchain) {
      <h3 class="worker-image-toolchain__title">Installed toolchain</h3>
      @if (toolchain.imageId) {
        <dl class="worker-image-toolchain__meta">
          <div class="worker-image-toolchain__meta-item">
            <dt>Image</dt>
            <dd>
              <code class="worker-image-toolchain__image" [attr.title]="toolchain.imageId">{{ shortImageId() }}</code>
            </dd>
          </div>
          @if (toolchain.status === 'recorded') {
            <div class="worker-image-toolchain__meta-item">
              <dt>Recorded</dt>
              <dd>
                <time class="worker-image-toolchain__recorded-at" [attr.datetime]="toolchain.recordedAt">{{
                  toolchain.recordedAt | date: 'medium'
                }}</time>
              </dd>
            </div>
          }
        </dl>
      }
      @if (toolchain.status === 'recorded') {
        <fd-toolchain-list [tools]="toolchain.tools" />
      } @else {
        <p class="worker-image-toolchain__unknown">
          Toolchain unknown. Foundry reads it from this image after the build and keeps retrying on its own.
        </p>
      }
    }
  `,
  styleUrl: './worker-image-toolchain.scss',
})
export class WorkerImageToolchainComponent {
  private readonly _http = inject(HttpClient);
  private readonly _signalR = inject(SystemSignalRService);

  protected readonly toolchain: Signal<WorkerImageToolchain | undefined> = toSignal(
    merge(of(undefined), this._signalR.reconnected, this._signalR.reloadTrigger).pipe(
      switchMap(() =>
        this._http.get<WorkerImageToolchain>(WORKER_IMAGE_TOOLCHAIN_URL).pipe(
          // Keep showing the last toolchain; the next reload trigger fetches again.
          catchError(() => {
            console.warn('[WorkerImageToolchainComponent] Failed to load the worker image toolchain');
            return EMPTY;
          })
        )
      )
    )
  );

  protected readonly shortImageId: Signal<string> = computed(() => {
    const imageId = this.toolchain()?.imageId ?? '';
    const digest = imageId.startsWith(IMAGE_ID_PREFIX) ? imageId.slice(IMAGE_ID_PREFIX.length) : imageId;
    return digest.slice(0, SHORT_IMAGE_ID_LENGTH);
  });
}

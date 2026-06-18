import { Component, Signal, computed, inject } from '@angular/core';
import { DispatchService } from '../../../../core/services/dispatch.service';

@Component({
  selector: 'fd-dispatch-controls',
  standalone: true,
  template: `
    <div class="dispatch-controls" role="group" aria-label="Dispatch controls">
      <button
        class="dispatch-controls__pause-btn"
        type="button"
        [disabled]="dispatchService.pausing()"
        (click)="dispatchService.pauseDispatch()"
      >
        {{ dispatchService.pausing() ? 'Pausing...' : 'Pause All' }}
      </button>

      @if (showResume()) {
        <button
          class="dispatch-controls__resume-btn"
          type="button"
          [disabled]="dispatchService.resuming()"
          (click)="dispatchService.resumeDispatch()"
        >
          {{ dispatchService.resuming() ? 'Resuming...' : 'Resume All' }}
        </button>
      }

      @if (dispatchService.pauseResumeError()) {
        <span class="dispatch-controls__error" role="alert">
          {{ dispatchService.pauseResumeError() }}
        </span>
      }
    </div>
  `,
  styleUrl: './dispatch-controls.scss',
})
export class DispatchControlsComponent {
  protected readonly dispatchService = inject(DispatchService);

  protected readonly showResume: Signal<boolean> = computed(
    () => this.dispatchService.isDispatchPaused() || this.dispatchService.usageLimitResetsAt() !== null
  );
}

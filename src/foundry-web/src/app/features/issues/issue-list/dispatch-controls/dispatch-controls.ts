import { Component, Signal, computed, inject } from '@angular/core';
import { DispatchService } from '../../../../core/services/dispatch.service';

@Component({
  selector: 'fd-dispatch-controls',
  standalone: true,
  template: `
    <div class="dispatch-controls" role="group" aria-label="Dispatch controls">
      <button
        class="dispatch-controls__toggle-btn"
        [class.dispatch-controls__toggle-btn--resume]="isPaused()"
        type="button"
        [disabled]="dispatchService.pausing() || dispatchService.resuming()"
        (click)="toggle()"
      >
        {{ buttonLabel() }}
      </button>

      <span class="dispatch-controls__status" role="status">{{ dispatchStatusText() }}</span>

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

  protected readonly isPaused: Signal<boolean> = computed(
    () => this.dispatchService.isDispatchPaused() || this.dispatchService.usageLimitResetsAt() !== null
  );

  protected readonly buttonLabel: Signal<string> = computed(() => {
    if (this.dispatchService.pausing()) {
      return 'Pausing...';
    }
    if (this.dispatchService.resuming()) {
      return 'Resuming...';
    }
    return this.isPaused() ? 'Resume All' : 'Pause All';
  });

  protected readonly dispatchStatusText: Signal<string> = computed(() => {
    if (this.dispatchService.usageLimitResetsAt() !== null) {
      return 'Dispatch paused — usage limit';
    }
    if (this.dispatchService.isDispatchPaused()) {
      return 'Dispatch paused';
    }
    return '';
  });

  toggle(): void {
    if (this.isPaused()) {
      this.dispatchService.resumeDispatch();
    } else {
      this.dispatchService.pauseDispatch();
    }
  }
}

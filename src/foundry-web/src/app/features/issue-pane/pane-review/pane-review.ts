import {
  ChangeDetectionStrategy,
  Component,
  InputSignal,
  Signal,
  WritableSignal,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { Subscription } from 'rxjs';
import { WorkerRunDetail } from '../../workers/worker-run.model';
import { WorkerRunService } from '../../workers/worker-run.service';
import { PaneReview, buildReview } from './pane-review.model';

let nextId = 0;

/** The review run fetched for a different id than the pane's current run. */
type Fetched =
  | { readonly status: 'idle' }
  | { readonly status: 'loading'; readonly id: string }
  | { readonly status: 'loaded'; readonly id: string; readonly run: WorkerRunDetail | null };

/**
 * The pane's Review section: the issue's latest review verdict (ADR 0113). During a findings fix the pane's run is
 * the fix run, so the review run is fetched by its own id unless it is the pane's run.
 */
@Component({
  selector: 'fd-pane-review',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './pane-review.html',
  styleUrl: './pane-review.scss',
})
export class PaneReviewComponent {
  /** The run of the issue's latest review verdict; null before any verdict was handled. */
  readonly reviewRunId: InputSignal<string | null> = input.required<string | null>();
  /** The pane's current run, reused when it is the review run. */
  readonly run: InputSignal<WorkerRunDetail | null> = input<WorkerRunDetail | null>(null);

  // The pane can render two issues at once while it swaps, so ids cannot be fixed strings.
  private readonly _id = nextId++;
  protected readonly headingId = `pane-review-heading-${this._id}`;
  protected readonly summaryId = `pane-review-summary-${this._id}`;

  private readonly _workerRunService = inject(WorkerRunService);

  private readonly _fetched: WritableSignal<Fetched> = signal({ status: 'idle' });

  private readonly _paneRunIsReviewRun: Signal<boolean> = computed(() => {
    const id = this.reviewRunId();
    return id !== null && this.run()?.workerRunId === id;
  });

  protected readonly loading: Signal<boolean> = computed(
    () => !this._paneRunIsReviewRun() && this._fetched().status === 'loading',
  );

  protected readonly review: Signal<PaneReview | null> = computed(() => {
    if (this._paneRunIsReviewRun()) {
      return buildReview(this.run()!);
    }
    const fetched = this._fetched();
    return fetched.status === 'loaded' && fetched.id === this.reviewRunId() && fetched.run !== null
      ? buildReview(fetched.run)
      : null;
  });

  private readonly _summaryExpanded: WritableSignal<boolean> = signal(false);
  protected readonly summaryExpanded: Signal<boolean> = this._summaryExpanded.asReadonly();

  constructor() {
    // A review run's verdict never changes once recorded, so each id is fetched once.
    let fetchSub: Subscription | null = null;
    effect(() => {
      const id = this.reviewRunId();
      const reused = this._paneRunIsReviewRun();
      untracked(() => {
        const fetched = this._fetched();
        if (id === null || reused || (fetched.status !== 'idle' && fetched.id === id)) {
          return;
        }
        fetchSub?.unsubscribe();
        this._fetched.set({ status: 'loading', id });
        fetchSub = this._workerRunService.getDetail(id).subscribe((run) => this._fetched.set({ status: 'loaded', id, run }));
      });
    });

    effect(() => {
      this.reviewRunId();
      untracked(() => this._summaryExpanded.set(false));
    });
  }

  protected toggleSummary(): void {
    this._summaryExpanded.update((v) => !v);
  }
}

import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  InputSignal,
  OutputEmitterRef,
  Signal,
  WritableSignal,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { StateBadgeComponent } from '../../../shared/components/state-badge/state-badge';
import { StepChipComponent, StepChipTone } from '../../../shared/components/step-chip/step-chip';
import { SafeHrefPipe } from '../../../shared/pipes/safe-href.pipe';
import { CODE_STEP_LABELS, STAGE_LABELS, codeStepTone } from '../../board/board.model';
import { IssueDetail, IssueSummary } from '../../issues/issue.model';
import { SecretFindingsComponent } from '../../issues/secret-findings/secret-findings';
import { WorkerRunDetail } from '../../workers/worker-run.model';
import { PaneActionsComponent } from '../pane-actions/pane-actions';
import { PaneAction, PaneCallout, buildCallout } from '../pane-callout.model';

let nextId = 0;

interface StatusView {
  readonly detail: IssueDetail;
  readonly callout: PaneCallout;
}

/**
 * Layer 2 of the issue pane: the stage, state badge and step chip, then the one callout chosen by
 * `buildCallout` with its actions. It waits for the detail, showing skeleton bars until it lands.
 */
@Component({
  selector: 'fd-pane-status',
  imports: [StateBadgeComponent, StepChipComponent, SafeHrefPipe, SecretFindingsComponent, PaneActionsComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './pane-status.html',
  styleUrl: './pane-status.scss',
})
export class PaneStatusComponent {
  /** The loaded detail of the pane's issue; null until it lands. */
  readonly detail: InputSignal<IssueDetail | null> = input.required<IssueDetail | null>();
  readonly run: InputSignal<WorkerRunDetail | null> = input.required<WorkerRunDetail | null>();
  readonly summary: InputSignal<IssueSummary | null> = input.required<IssueSummary | null>();
  /** The issue's 1-based place in the dispatch queue, or null. */
  readonly queuePosition: InputSignal<number | null> = input<number | null>(null);
  readonly error: InputSignal<string | null> = input<string | null>(null);
  readonly retry: OutputEmitterRef<void> = output<void>();

  protected readonly listHeadingId = `pane-callout-list-${nextId}`;
  protected readonly findingsHeadingId = `pane-callout-findings-${nextId++}`;

  protected readonly view: Signal<StatusView | null> = computed(() => {
    const detail = this.detail();
    if (detail === null) {
      return null;
    }
    return { detail, callout: buildCallout(detail, this.run(), this.summary(), this.queuePosition()) };
  });

  protected readonly stageLabel: Signal<string | null> = computed(() => {
    const stage = this.summary()?.stage ?? null;
    if (stage === 'done') {
      return 'Done';
    }
    return stage === null ? null : STAGE_LABELS[stage];
  });

  protected readonly stepChip: Signal<{ label: string; tone: StepChipTone } | null> = computed(() => {
    const step = this.summary()?.codeStep ?? null;
    return step === null ? null : { label: CODE_STEP_LABELS[step], tone: codeStepTone(step) };
  });

  private readonly _announcement: WritableSignal<string> = signal('');
  protected readonly announcement: Signal<string> = this._announcement.asReadonly();

  private readonly _title: Signal<string | null> = computed(() => this.view()?.callout.title ?? null);
  private readonly _calloutTitle: Signal<ElementRef<HTMLElement> | undefined> =
    viewChild<ElementRef<HTMLElement>>('calloutTitle');
  private readonly _injector = inject(Injector);

  // Set when an action succeeds: the detail it was taken on, so focus moves once a newer one lands.
  private _focusAfterDetail: IssueDetail | null = null;

  constructor() {
    let previousTitle: string | null = null;
    effect(() => {
      const title = this._title();
      // The first callout of the issue is not a change; the pane heading already announced it.
      if (title !== null && previousTitle !== null && title !== previousTitle) {
        this._announcement.set(`Status: ${title}`);
      }
      if (title !== null) {
        previousTitle = title;
      }
    });

    effect(() => {
      const detail = this.detail();
      const pending = untracked(() => this._focusAfterDetail);
      if (pending !== null && detail !== null && detail !== pending) {
        this._focusAfterDetail = null;
        this._focusTitle();
      }
    });
  }

  /**
   * Focus moves to the callout title once the reloaded detail lands. When the reload already
   * landed and removed the action, it moves now.
   */
  protected onActionSucceeded(action: PaneAction): void {
    const view = this.view();
    if (view === null) {
      return;
    }
    if (!view.callout.actions.includes(action)) {
      this._focusTitle();
      return;
    }
    this._focusAfterDetail = view.detail;
  }

  private _focusTitle(): void {
    afterNextRender(() => this._calloutTitle()?.nativeElement.focus(), { injector: this._injector });
  }
}

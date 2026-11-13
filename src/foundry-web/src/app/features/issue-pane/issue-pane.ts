import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  InputSignal,
  Injector,
  OutputEmitterRef,
  Signal,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  output,
  untracked,
} from '@angular/core';
import { IssueDetailComponent } from '../issues/issue-detail/issue-detail';
import { IssueSummary } from '../issues/issue.model';
import { IssueService } from '../issues/issue.service';

export const ISSUE_PANE_TITLE_ID = 'issue-pane-title';

/**
 * The issue pane's content, rendered inside `fd-side-sheet`: a heading, a close button and the
 * existing `fd-issue-detail` for the selected issue.
 */
@Component({
  selector: 'fd-issue-pane',
  imports: [IssueDetailComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './issue-pane.html',
  styleUrl: './issue-pane.scss',
})
export class IssuePaneComponent {
  readonly issueId: InputSignal<string> = input.required<string>();
  /** The summary the page already holds; null on a deep link the page holds no summary for. */
  readonly summary: InputSignal<IssueSummary | null> = input<IssueSummary | null>(null);
  readonly closed: OutputEmitterRef<void> = output<void>();

  protected readonly issueService = inject(IssueService);
  protected readonly titleId = ISSUE_PANE_TITLE_ID;
  private readonly _host: HTMLElement = inject(ElementRef<HTMLElement>).nativeElement;
  private readonly _injector = inject(Injector);

  /** What the heading names: the page's summary, else the loaded detail (itself a summary). */
  protected readonly heading: Signal<IssueSummary | null> = computed(() => {
    const summary = this.summary();
    if (summary !== null && summary.id === this.issueId()) {
      return summary;
    }
    const detail = this.issueService.issueDetail();
    return detail?.id === this.issueId() ? detail : null;
  });

  protected readonly untracked: Signal<boolean> = computed(
    () => this.issueService.selectedIssueId() === this.issueId() && this.issueService.detailMissing(),
  );

  constructor() {
    // The sheet focuses the heading on open; a swap to another issue does it here.
    let previous: string | null = null;
    effect(() => {
      const id = this.issueId();
      if (previous !== null && previous !== id) {
        untracked(() => this._focusHeading());
      }
      previous = id;
    });
  }

  protected retry(): void {
    this.issueService.loadDetail(this.issueId());
  }

  private _focusHeading(): void {
    afterNextRender(
      () => this._host.querySelector<HTMLElement>(`#${ISSUE_PANE_TITLE_ID}`)?.focus(),
      { injector: this._injector },
    );
  }
}

import {
  ChangeDetectionStrategy,
  Component,
  InputSignal,
  OutputEmitterRef,
  Signal,
  computed,
  inject,
  input,
  output,
} from '@angular/core';
import { SafeHrefPipe } from '../../../../shared/pipes/safe-href.pipe';
import { IssueActionRequest, IssueActionsService } from '../../../issues/issue-actions.service';
import { NeedsItem, RowActionKind, RowActionSpec, rowAction } from '../../board.model';

/**
 * The one primary control of a Needs you row: a provider link, an action run through
 * IssueActionsService (which shares its confirmation and pending state with the pane), or Review,
 * which opens the pane. It sits outside the row's title button, so activating it never opens the
 * pane unless it is Review.
 */
@Component({
  selector: 'fd-row-action',
  imports: [SafeHrefPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './row-action.html',
  styleUrl: './row-action.scss',
})
export class RowActionComponent {
  readonly item: InputSignal<NeedsItem> = input.required<NeedsItem>();
  /** Emits the Review button, so focus returns to it when the pane closes. */
  readonly open: OutputEmitterRef<HTMLElement> = output<HTMLElement>();

  private readonly _issueActions = inject(IssueActionsService);

  protected readonly spec: Signal<RowActionSpec | null> = computed(() => rowAction(this.item()));

  private readonly _issueId: Signal<string> = computed(() => this.item().issue.id);

  /** Any action on the issue is in flight, from this row or the pane: the button ignores clicks. */
  protected readonly busy: Signal<boolean> = computed(() => this._issueActions.pending(this._issueId())());

  /** This row's own action is in flight, so it shows its pending label. */
  protected readonly pending: Signal<boolean> = computed(() => {
    const spec = this.spec();
    return spec?.kind === 'action' && this._issueActions.pendingAction(this._issueId())() === spec.action;
  });

  /** The failure of the action this row offers; another action's error belongs to the pane. */
  protected readonly error: Signal<string | null> = computed(() => {
    const spec = this.spec();
    const failure = this._issueActions.failure(this._issueId())();
    return spec?.kind === 'action' && failure?.action === spec.action ? failure.message : null;
  });

  // aria-label replaces the button text, so it carries the pending label as the text does.
  protected readonly ariaLabel: Signal<string | null> = computed(() => {
    const spec = this.spec();
    if (spec === null) {
      return null;
    }
    if (spec.kind === 'action' && this.pending()) {
      return spec.pendingLabel + spec.ariaLabel.slice(spec.label.length);
    }
    return spec.ariaLabel;
  });

  protected run(action: RowActionKind): void {
    if (this.busy()) {
      return;
    }
    this._issueActions.perform(this._issueId(), this._request(action));
  }

  private _request(action: RowActionKind): IssueActionRequest {
    // The row holds no run: the service reads it from the issue's detail once the retry is confirmed.
    return action === 'retry_publish' ? { kind: action, workerRunId: null, branchName: null } : { kind: action };
  }
}

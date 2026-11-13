import {
  ChangeDetectionStrategy,
  Component,
  InputSignal,
  OutputEmitterRef,
  Signal,
  computed,
  input,
  output,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { StageChipComponent } from '../../../../shared/components/stage-chip/stage-chip';
import { SafeHrefPipe } from '../../../../shared/pipes/safe-href.pipe';
import { formatWaiting } from '../../../../shared/utils/format-waiting';
import { providerDisplayName, providerTerminology } from '../../../../shared/utils/provider.util';
import { ItemView } from '../../board-state.service';
import { NeedsItem, STAGE_LABELS, STAGE_ORDER } from '../../board.model';

interface ReviewLink {
  readonly href: string | null | undefined;
  readonly label: string;
  readonly hint: string;
}

/** An issue that needs a person: why, where it stopped and for how long. It changes no state. */
@Component({
  selector: 'fd-needs-card',
  imports: [DatePipe, SafeHrefPipe, StageChipComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '[attr.data-issue-id]': 'item().issue.id',
    '[style.view-transition-name]': "'fd-card-' + item().issue.id",
    '[style.view-transition-class]': "'fd-card'",
  },
  templateUrl: './needs-card.html',
  styleUrl: './needs-card.scss',
})
export class NeedsCardComponent {
  readonly item: InputSignal<ItemView<NeedsItem>> = input.required<ItemView<NeedsItem>>();
  /** The current time, in epoch ms, from the page's shared tick. */
  readonly now: InputSignal<number> = input.required<number>();
  /** Emits the card's button, so focus can return to it when the pane closes. */
  readonly open: OutputEmitterRef<HTMLElement> = output<HTMLElement>();

  protected readonly stageCount = STAGE_ORDER.length;
  protected readonly stageLabel: Signal<string> = computed(() => STAGE_LABELS[this.item().stage]);
  protected readonly stageStep: Signal<number> = computed(
    () => STAGE_ORDER.indexOf(this.item().stage) + 1,
  );
  protected readonly live: Signal<boolean> = computed(() => this.item().phase === 'live');
  protected readonly tone: Signal<string> = computed(() =>
    this.live() ? this.item().reason.tone : 'completed',
  );
  protected readonly idBase: Signal<string> = computed(() => `needs-card-${this.item().issue.id}`);
  protected readonly label: Signal<string> = computed(
    () => `${this.item().issue.title}, #${this.item().issue.issueNumber}`,
  );
  protected readonly waiting: Signal<string> = computed(() =>
    formatWaiting(this.now() - Date.parse(this.item().issue.enteredStateAt)),
  );

  protected readonly reviewLink: Signal<ReviewLink | null> = computed(() => {
    const { issue, reason } = this.item();
    if (reason.tone !== 'ready' || !issue.pullRequestUrl) {
      return null;
    }
    const provider = issue.providerType ?? 'github';
    return {
      href: issue.pullRequestUrl,
      label: `Review ${providerTerminology(provider).prAbbrev}`,
      hint: ` (opens ${providerDisplayName(provider)} in a new tab)`,
    };
  });
}

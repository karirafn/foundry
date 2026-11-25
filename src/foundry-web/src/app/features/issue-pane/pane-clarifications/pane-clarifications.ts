import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
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
import { DatePipe, NgTemplateOutlet } from '@angular/common';
import { IssueDetail } from '../../issues/issue.model';
import { SafeHrefPipe } from '../../../shared/pipes/safe-href.pipe';
import { ClarificationRoundView, RoundStatus, buildClarifications } from './pane-clarifications.model';

/** The waited time is coarse (minutes at the finest), so a minute's tick keeps it current. */
const CLOCK_TICK_MS = 60_000;

let nextId = 0;

/**
 * The issue's clarification log (ADR 0096, #757): every round's questions, the input that arrived and
 * when, and, for the open round, how its post stands, how long it has waited and its reminder. The
 * round's actions stay with the callout's other actions. Answering happens on the provider thread.
 */
@Component({
  selector: 'fd-pane-clarifications',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, NgTemplateOutlet, SafeHrefPipe],
  templateUrl: './pane-clarifications.html',
  styleUrl: './pane-clarifications.scss',
})
export class PaneClarificationsComponent {
  /** The loaded detail; null while it loads. */
  readonly detail: InputSignal<IssueDetail | null> = input.required<IssueDetail | null>();
  readonly providerType: InputSignal<string> = input<string>('');

  // The pane can render two issues at once while it swaps, so ids cannot be fixed strings.
  protected readonly idPrefix = `pane-clarifications-${nextId++}`;
  protected readonly headingId = `${this.idPrefix}-heading`;

  private readonly _now: WritableSignal<number> = signal(Date.now());

  protected readonly rounds: Signal<readonly ClarificationRoundView[]> = computed(() => {
    const detail = this.detail();
    return detail === null ? [] : buildClarifications(detail, this.providerType(), this._now());
  });

  private readonly _expanded: WritableSignal<ReadonlySet<string>> = signal(new Set<string>());

  /** The last round's number and status: a change in either is a round opening or closing. */
  private readonly _lastRound: Signal<string | null> = computed(() => {
    if (this.detail() === null) {
      return null;
    }
    const rounds = this.rounds();
    const last = rounds.length > 0 ? rounds[rounds.length - 1] : null;
    return last === null ? '' : `${last.number}:${last.status}`;
  });

  private readonly _announcement: WritableSignal<string> = signal('');
  protected readonly announcement: Signal<string> = this._announcement.asReadonly();

  constructor() {
    const timer = setInterval(() => this._now.set(Date.now()), CLOCK_TICK_MS);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));

    // The first loaded log is not a change; the pane heading already announced the issue. A detail
    // reload (null in between) keeps the last round seen.
    let seen: string | null = null;
    effect(() => {
      const last = this._lastRound();
      if (last === null) {
        return;
      }
      untracked(() => {
        if (seen !== null && last !== seen) {
          this._announcement.set(announcementFor(this.rounds()));
        }
        seen = last;
      });
    });
  }

  protected roundId(round: ClarificationRoundView, part: string): string {
    return `${this.idPrefix}-r${round.number}-${part}`;
  }

  protected isExpanded(round: ClarificationRoundView, index: number): boolean {
    return this._expanded().has(`${round.number}-${index}`);
  }

  protected toggle(round: ClarificationRoundView, index: number): void {
    const key = `${round.number}-${index}`;
    this._expanded.update((keys) => {
      const next = new Set(keys);
      if (!next.delete(key)) {
        next.add(key);
      }
      return next;
    });
  }

  protected chipTone(status: RoundStatus): string {
    switch (status) {
      case 'open':
        return 'warning';
      case 'answered':
        return 'success';
      case 'decided':
        return 'neutral';
      case 'unanswered':
        return 'neutral';
    }
  }
}

function announcementFor(rounds: readonly ClarificationRoundView[]): string {
  const last = rounds.length > 0 ? rounds[rounds.length - 1] : null;
  if (last === null) {
    return '';
  }
  switch (last.status) {
    case 'open':
      return `Clarification round ${last.number} opened.`;
    case 'answered':
      return `Clarification round ${last.number} answered.`;
    case 'decided': {
      const count = last.decisions.length;
      return `Triage decided ${count} ${count === 1 ? 'question' : 'questions'} in round ${last.number}.`;
    }
    case 'unanswered':
      return `Clarification round ${last.number} continued without an answer.`;
  }
}

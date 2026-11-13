import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  InputSignal,
  OutputEmitterRef,
  Signal,
  WritableSignal,
  afterNextRender,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { SafeHrefPipe } from '../../../shared/pipes/safe-href.pipe';
import { providerDisplayName, providerTerminology } from '../../../shared/utils/provider.util';
import { IssueSummary } from '../../issues/issue.model';

/**
 * Layer 1 of the issue pane: the issue number, title, repository, links to the issue and its pull
 * request, and the close button. It renders from the summary the page holds, so it shows before
 * the detail loads; with no summary it shows skeleton bars.
 */
@Component({
  selector: 'fd-pane-header',
  imports: [NgTemplateOutlet, SafeHrefPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <header class="pane-header" [class.pane-header--scrolled]="scrolled()">
      <div class="pane-header__main">
        @if (issue(); as i) {
          <h2 [id]="titleId()" class="pane-header__title" tabindex="-1" [attr.title]="i.title">
            <span class="pane-header__number">#{{ i.issueNumber }}</span>&ngsp;<span
              class="pane-header__title-text"
              >{{ i.title }}</span
            >
          </h2>
          <p class="pane-header__repo">{{ i.repositorySlug }}</p>
          <div class="pane-header__links">
            @if (i.url | safeHref; as issueHref) {
              <a
                class="pane-header__link"
                [href]="issueHref"
                target="_blank"
                rel="noopener noreferrer"
                [attr.aria-label]="'View issue #' + i.issueNumber + ' on ' + providerName()"
              >View issue<ng-container [ngTemplateOutlet]="externalIcon" /></a>
            }
            @if (pullRequestUrl() | safeHref; as prHref) {
              <a
                class="pane-header__link"
                [href]="prHref"
                target="_blank"
                rel="noopener noreferrer"
                [attr.aria-label]="'View ' + pullRequestTerm() + ' for issue #' + i.issueNumber"
              >View {{ prAbbrev() }}<ng-container [ngTemplateOutlet]="externalIcon" /></a>
            }
          </div>
        } @else {
          <h2 [id]="titleId()" class="pane-header__title" tabindex="-1" aria-busy="true">
            <span class="sr-only">Loading issue…</span>
            <span class="pane-header__bar pane-header__bar--short" aria-hidden="true"></span>
            <span class="pane-header__bar" aria-hidden="true"></span>
          </h2>
        }
      </div>
      <button type="button" class="pane-header__close" aria-label="Close issue details" (click)="closed.emit()">
        <svg
          xmlns="http://www.w3.org/2000/svg"
          width="16"
          height="16"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="2"
          stroke-linecap="round"
          aria-hidden="true"
          focusable="false"
        >
          <path d="M18 6 6 18M6 6l12 12" />
        </svg>
      </button>
    </header>

    <ng-template #externalIcon>
      <svg
        class="pane-header__icon"
        xmlns="http://www.w3.org/2000/svg"
        width="12"
        height="12"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        stroke-width="2"
        stroke-linecap="round"
        stroke-linejoin="round"
        aria-hidden="true"
        focusable="false"
      >
        <path d="M14 3h7v7M10 14 21 3M21 14v7H3V3h7" />
      </svg>
    </ng-template>
  `,
  styleUrl: './pane-header.scss',
})
export class PaneHeaderComponent {
  readonly issue: InputSignal<IssueSummary | null> = input.required<IssueSummary | null>();
  readonly titleId: InputSignal<string> = input.required<string>();
  /** The pull request, when the issue has one; the summary or the run's publish carries it. */
  readonly pullRequestUrl: InputSignal<string | null> = input<string | null>(null);
  readonly closed: OutputEmitterRef<void> = output<void>();

  private readonly _scrolled: WritableSignal<boolean> = signal(false);
  /** True once the sheet scrolls, so the sticky header gains its bottom border. */
  protected readonly scrolled: Signal<boolean> = this._scrolled.asReadonly();

  protected readonly providerName: Signal<string> = computed(
    () => providerDisplayName(this.issue()?.providerType ?? '') || 'the provider');
  protected readonly pullRequestTerm: Signal<string> = computed(
    () => providerTerminology(this.issue()?.providerType ?? '').pullRequest.toLowerCase());
  protected readonly prAbbrev: Signal<string> = computed(
    () => providerTerminology(this.issue()?.providerType ?? '').prAbbrev);

  constructor() {
    const host: HTMLElement = inject(ElementRef<HTMLElement>).nativeElement;
    const destroyRef = inject(DestroyRef);
    afterNextRender(() => {
      const scroller = scrollParent(host);
      if (scroller === null) {
        return;
      }
      const onScroll = (): void => this._scrolled.set(scroller.scrollTop > 0);
      scroller.addEventListener('scroll', onScroll, { passive: true });
      destroyRef.onDestroy(() => scroller.removeEventListener('scroll', onScroll));
    });
  }
}

function scrollParent(element: HTMLElement): HTMLElement | null {
  let current = element.parentElement;
  while (current !== null) {
    const overflowY = getComputedStyle(current).overflowY;
    if (overflowY === 'auto' || overflowY === 'scroll') {
      return current;
    }
    current = current.parentElement;
  }
  return null;
}

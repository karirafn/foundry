import {
  DestroyRef,
  ElementRef,
  Injectable,
  InjectionToken,
  Provider,
  Signal,
  WritableSignal,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { DOCUMENT, Location } from '@angular/common';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { map } from 'rxjs';
import { IssueSummary } from '../issues/issue.model';
import { IssueService, SAFE_ID_RE } from '../issues/issue.service';

export const ISSUE_PARAM = 'issue';

/**
 * How a page decides whether an `?issue=` it did not open itself names an issue it can show:
 * - `board`: the issue must be on the board (active) once the first load has finished.
 * - `history`: the id must be well formed and its detail must load (a 404 rejects it).
 */
export type IssueSelectionScope = 'board' | 'history';

const ISSUE_SELECTION_SCOPE = new InjectionToken<IssueSelectionScope>('IssueSelectionScope');

/** Provides an `IssueSelection` for one page; put it in the page component's `providers`. */
export function provideIssueSelection(scope: IssueSelectionScope): Provider[] {
  return [{ provide: ISSUE_SELECTION_SCOPE, useValue: scope }, IssueSelection];
}

type Verdict = 'wait' | 'accept' | 'reject';

/**
 * The page's selected issue, kept in the `?issue=<id>` query parameter in both directions.
 *
 * Opening from closed pushes a history entry, so browser Back closes the pane; swapping to another
 * issue replaces it, so quick clicks never stack entries. Every way the parameter can change
 * (open, close, Back, Forward, a pasted URL) runs through one effect, which also returns focus to
 * the card that opened the pane when it closes.
 */
@Injectable()
export class IssueSelection {
  private readonly _router = inject(Router);
  private readonly _route = inject(ActivatedRoute);
  private readonly _location = inject(Location);
  private readonly _issueService = inject(IssueService);
  private readonly _scope = inject(ISSUE_SELECTION_SCOPE);
  private readonly _host: HTMLElement = inject(ElementRef<HTMLElement>).nativeElement;
  private readonly _document = inject(DOCUMENT);

  /** The `issue` query parameter as it stands in the URL. */
  readonly selectedId: Signal<string | null> = toSignal(
    this._route.queryParamMap.pipe(map((params) => params.get(ISSUE_PARAM))),
    { initialValue: this._route.snapshot.queryParamMap.get(ISSUE_PARAM) },
  );

  private readonly _openId: WritableSignal<string | null> = signal(null);
  /** The issue the pane shows: a selection that passed the page's validity check, else null. */
  readonly openId: Signal<string | null> = this._openId.asReadonly();
  readonly isOpen: Signal<boolean> = computed(() => this._openId() !== null);
  /**
   * The issue the pane content renders: the open issue, and after closing the last one, so the content
   * stays in place while the sheet plays its leave animation.
   */
  private readonly _paneId: WritableSignal<string | null> = signal(null);
  readonly paneId: Signal<string | null> = this._paneId.asReadonly();

  /**
   * The pane issue's latest summary. Once it leaves the page's lists (it completed on the board),
   * the last one seen; null when the page never held one (a deep link on `/issues`).
   */
  readonly summary: Signal<IssueSummary | null> = computed(() => {
    const id = this._paneId();
    if (id === null) {
      return null;
    }
    const remembered = this._lastSummary();
    return this._findSummary(id) ?? (remembered?.id === id ? remembered : null);
  });
  private readonly _lastSummary: WritableSignal<IssueSummary | null> = signal(null);

  // A selection the URL names but the page has not yet accepted (a deep link awaiting its check).
  private readonly _pending: WritableSignal<string | null> = signal(null);
  private _lastParam: string | null | undefined = undefined;
  private _pushed = false;
  private _invoker: HTMLElement | null = null;

  constructor() {
    effect(() => {
      const id = this._paneId();
      const found = id === null ? null : this._findSummary(id);
      if (found !== null) {
        untracked(() => this._lastSummary.set(found));
      }
    });
    effect(() => {
      const param = this.selectedId();
      untracked(() => this._onParam(param));
    });
    effect(() => {
      const pending = this._pending();
      if (pending === null) {
        return;
      }
      const verdict = this._verdict(pending);
      untracked(() => this._settle(pending, verdict));
    });
    inject(DestroyRef).onDestroy(() => {
      if (this._issueService.selectedIssueId() !== null) {
        this._issueService.selectIssue(null);
      }
    });
  }

  /** Opens the pane on `id`, or swaps it there when already open. */
  open(id: string, invoker: HTMLElement): void {
    this._invoker = invoker;
    this._pending.set(null);
    this._openId.set(id);
    this._paneId.set(id);
    // Keyed on the URL rather than the pane: a second click before the first navigation lands
    // cancels it, so it must push the entry itself.
    const replace = this.selectedId() !== null;
    this._navigate(id, replace);
    if (!replace) {
      this._pushed = true;
    }
    if (this._issueService.selectedIssueId() !== id) {
      this._issueService.selectIssue(id);
    }
  }

  /** Closes the pane: back through the entry `open` pushed, or by removing the parameter. */
  close(): void {
    if (this._openId() === null) {
      return;
    }
    if (this._pushed) {
      this._pushed = false;
      this._location.back();
      return;
    }
    this._navigate(null, true);
  }

  private _onParam(param: string | null): void {
    if (param === this._lastParam) {
      return;
    }
    this._lastParam = param;

    if (param === null) {
      this._pending.set(null);
      this._pushed = false;
      this._closeNow();
      return;
    }
    if (param === this._openId()) {
      this._pending.set(null);
      return;
    }
    if (this._scope === 'history' && SAFE_ID_RE.test(param)) {
      this._issueService.selectIssue(param);
    }
    this._pending.set(param);
  }

  private _verdict(id: string): Verdict {
    return this._scope === 'board' ? this._boardVerdict(id) : this._historyVerdict(id);
  }

  private _boardVerdict(id: string): Verdict {
    if (this._issueService.initialLoading()) {
      return 'wait';
    }
    return this._issueService.issues().some((i) => i.id === id) ? 'accept' : 'reject';
  }

  private _historyVerdict(id: string): Verdict {
    if (!SAFE_ID_RE.test(id)) {
      return 'reject';
    }
    if (this._issueService.selectedIssueId() !== id) {
      return 'wait';
    }
    if (this._issueService.detailMissing()) {
      return 'reject';
    }
    if (this._issueService.issueDetail()?.id === id || this._issueService.detailError() !== null) {
      return 'accept';
    }
    return 'wait';
  }

  private _settle(id: string, verdict: Verdict): void {
    if (verdict === 'wait') {
      return;
    }
    this._pending.set(null);
    if (verdict === 'reject') {
      if (this._issueService.selectedIssueId() === id) {
        this._issueService.selectIssue(null);
      }
      this._navigate(null, true);
      return;
    }
    this._openId.set(id);
    this._paneId.set(id);
    if (this._issueService.selectedIssueId() !== id) {
      this._issueService.selectIssue(id);
    }
  }

  private _closeNow(): void {
    const id = this._openId();
    if (this._issueService.selectedIssueId() !== null) {
      this._issueService.selectIssue(null);
    }
    if (id === null) {
      return;
    }
    this._openId.set(null);
    this._returnFocus(id);
  }

  private _navigate(id: string | null, replaceUrl: boolean): void {
    void this._router.navigate([], {
      relativeTo: this._route,
      queryParams: { [ISSUE_PARAM]: id },
      queryParamsHandling: 'merge',
      replaceUrl,
    });
  }

  private _findSummary(id: string): IssueSummary | null {
    return (
      this._issueService.issues().find((i) => i.id === id) ??
      this._issueService.resolvedIssues().find((i) => i.id === id) ??
      null
    );
  }

  // The invoker may have re-rendered elsewhere while the pane was open; then its card's button takes
  // focus, and the page heading when the card left the page.
  private _returnFocus(id: string): void {
    const invoker = this._invoker;
    this._invoker = null;
    const target =
      (invoker?.isConnected ? invoker : null) ??
      this._host.querySelector<HTMLElement>(
        `[data-issue-card="${id}"] button, [data-issue-id="${id}"] button`,
      ) ??
      this._host.querySelector<HTMLElement>('h1');
    target?.focus({ preventScroll: this._inView(target) });
  }

  private _inView(element: HTMLElement): boolean {
    const rect = element.getBoundingClientRect();
    const height = this._document.defaultView?.innerHeight ?? 0;
    return rect.top >= 0 && rect.bottom <= height;
  }
}

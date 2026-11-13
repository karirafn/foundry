import { TestBed } from '@angular/core/testing';
import { WritableSignal, signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { ConnectionStatus, IssueSignalRService } from '../../../core/services/issue-signalr.service';
import { WorkerSignalRService } from '../../../core/services/worker-signalr.service';
import { DispatchService } from '../../../core/services/dispatch.service';
import { DispatchCapacityService } from '../../floor/dispatch-capacity.service';
import { BoardPageComponent } from './board-page';

function item(id: string, issueNumber: number, overrides: Record<string, unknown> = {}) {
  return {
    id,
    issueNumber,
    title: `Issue ${issueNumber}`,
    state: 'in_progress',
    repositorySlug: 'acme/api',
    detectedAt: '2026-01-01T00:00:00Z',
    url: `https://github.com/acme/api/issues/${issueNumber}`,
    failureClassification: null,
    repositoryEligibilityStatus: 'eligible',
    runStats: null,
    stage: 'implement',
    needsAction: false,
    awaitsDispatch: false,
    codeStep: null,
    ...overrides,
  };
}

interface Harness {
  el: HTMLElement;
  httpMock: HttpTestingController;
  status: WritableSignal<ConnectionStatus>;
  render: () => Promise<void>;
}

async function setup(): Promise<Harness> {
  const status = signal<ConnectionStatus>('connected');
  TestBed.configureTestingModule({
    imports: [BoardPageComponent],
    providers: [
      provideRouter([]),
      provideHttpClient(),
      provideHttpClientTesting(),
      {
        provide: IssueSignalRService,
        useValue: { on: () => {}, off: () => {}, onReconnected: () => {}, connectionStatus: status },
      },
      {
        provide: WorkerSignalRService,
        useValue: { activeAgentCount: signal(0), activityForIssue: () => null, agentActivityForIssue: () => null },
      },
      {
        provide: DispatchCapacityService,
        useValue: { capacity: signal(null), loadError: signal(null), load: () => {} },
      },
      {
        provide: DispatchService,
        useValue: {
          isDispatchPaused: signal(false),
          usageLimitResetsAt: signal(null),
          pausing: signal(false),
          resuming: signal(false),
          pauseResumeError: signal(null),
        },
      },
    ],
  });
  const fixture = TestBed.createComponent(BoardPageComponent);
  document.body.appendChild(fixture.nativeElement);
  const render = async () => {
    fixture.detectChanges();
    await fixture.whenStable();
  };
  await render();
  return { el: fixture.nativeElement as HTMLElement, httpMock: TestBed.inject(HttpTestingController), status, render };
}

async function loaded(items: unknown[]): Promise<Harness> {
  const h = await setup();
  h.httpMock.expectOne('/api/issues').flush({ items, nextCursor: null });
  await h.render();
  return h;
}

function cardIds(el: HTMLElement, selector: string): string[] {
  return [...el.querySelectorAll(`${selector} fd-board-card`)].map((c) => c.getAttribute('data-issue-card')!);
}

describe('BoardPageComponent', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    document.body.innerHTML = '';
  });

  it('is a section labelled by the Board heading', async () => {
    // Act
    const { el } = await setup();

    // Assert
    const section = el.querySelector('section.board-page')!;
    expect(section.getAttribute('aria-labelledby')).toBe('board-heading');
    expect(el.querySelector('h1#board-heading')?.textContent).toBe('Board');
  });

  it('shows a busy skeleton grid while the issues load', async () => {
    // Act
    const { el } = await setup();

    // Assert
    const board = el.querySelector('.board')!;
    expect(board.getAttribute('role')).toBe('status');
    expect(board.getAttribute('aria-busy')).toBe('true');
    expect(board.textContent).toContain('Loading the board…');
    expect(el.querySelectorAll('.board-column__skeleton').length).toBeGreaterThan(0);
  });

  it('places each card in its stage and cell', async () => {
    // Act
    const { el } = await loaded([
      item('t', 1, { stage: 'triage', state: 'triage_queued', awaitsDispatch: true }),
      item('f', 2),
      item('r', 3, { stage: 'awaiting_review', state: 'review', needsAction: true }),
    ]);

    // Assert
    expect(cardIds(el, '#board-stage-triage ~ .board-column__cell--queue')).toEqual(['t']);
    expect(cardIds(el, '#board-stage-implement ~ .board-column__cell--flow')).toEqual(['f']);
    expect(cardIds(el, '#board-stage-awaiting_review ~ .board-column__cell--needs')).toEqual(['r']);
  });

  it('lists a queue in dispatch order with ineligible and blocked cards after the queued ones', async () => {
    // Act
    const { el } = await loaded([
      item('blocked', 1, { state: 'blocked', awaitsDispatch: true }),
      item('ineligible', 2, { state: 'queued', awaitsDispatch: true, repositoryEligibilityStatus: 'ineligible' }),
      item('second', 3, { state: 'queued', awaitsDispatch: true }),
      item('first', 4, { state: 'revision_queued', awaitsDispatch: true }),
    ]);

    // Assert
    expect(cardIds(el, '#board-stage-implement ~ .board-column__cell--queue')).toEqual([
      'second',
      'first',
      'blocked',
      'ineligible',
    ]);
  });

  it('shows one line naming the trigger label when nothing is in flight', async () => {
    // Act
    const { el } = await loaded([]);

    // Assert
    expect(el.querySelector('.board')).toBeNull();
    expect(el.querySelector('.board-page__empty')?.textContent?.replace(/\s+/g, ' ').trim()).toBe(
      'Nothing is in flight. Add the foundry label to an issue to start work.',
    );
    expect(el.querySelector('.board-page__empty code')?.textContent).toBe('foundry');
  });

  it('mirrors the empty state into a status region', async () => {
    const { el } = await loaded([]);
    const regions = [...el.querySelectorAll('[role="status"]')].map((r) => r.textContent);
    expect(regions).toContain('Nothing is in flight. Add the foundry label to an issue to start work.');
  });

  it('shows an alert with a retry when the issues fail to load', async () => {
    // Act
    const h = await setup();
    vi.spyOn(console, 'error').mockImplementation(() => {});
    h.httpMock.expectOne('/api/issues').flush('boom', { status: 500, statusText: 'Server Error' });
    await h.render();

    // Assert
    expect(h.el.querySelector('[role="alert"]')?.textContent).toContain('Failed to load issues');
  });

  it('reloads and focuses the heading on retry', async () => {
    // Arrange
    const h = await setup();
    vi.spyOn(console, 'error').mockImplementation(() => {});
    h.httpMock.expectOne('/api/issues').flush('boom', { status: 500, statusText: 'Server Error' });
    await h.render();

    // Act
    (h.el.querySelector('.board-page__retry') as HTMLButtonElement).click();

    // Assert
    h.httpMock.expectOne('/api/issues');
    expect(document.activeElement?.id).toBe('board-heading');
  });

  it('keeps its last state, marked stale, while the connection is lost', async () => {
    // Arrange
    const h = await loaded([item('f', 2)]);

    // Act
    h.status.set('reconnecting');
    await h.render();

    // Assert
    expect(h.el.querySelector('.board')?.classList).toContain('board--stale');
    expect(h.el.textContent).toContain('Connection lost — showing last known state');
    expect(cardIds(h.el, '.board')).toEqual(['f']);
  });

  it('moves focus to the first needs-you card from the summary', async () => {
    // Arrange
    const h = await loaded([
      item('r', 3, { stage: 'awaiting_review', state: 'review', needsAction: true }),
      item('t', 1, { stage: 'triage', state: 'triage_failed', needsAction: true }),
    ]);
    const scroll = vi.fn();
    Element.prototype.scrollIntoView = scroll;

    // Act
    (h.el.querySelector('.board-summary__needs') as HTMLButtonElement).click();

    // Assert
    expect(document.activeElement?.closest('[data-issue-card]')?.getAttribute('data-issue-card')).toBe('t');
    expect(scroll).toHaveBeenCalledWith({ block: 'nearest' });
  });

  describe('pane', () => {
    async function openCard(h: Harness, id: string): Promise<HTMLButtonElement> {
      const button = h.el.querySelector(`[data-issue-card="${id}"] button`) as HTMLButtonElement;
      button.click();
      await h.render();
      return button;
    }

    it('opens the issue detail in a dialog headed by the issue', async () => {
      // Arrange
      const h = await loaded([item('f', 142, { title: 'Add retry' })]);

      // Act
      await openCard(h, 'f');

      // Assert
      h.httpMock.expectOne('/api/issues/f');
      const dialog = h.el.querySelector('[role="dialog"]')!;
      expect(dialog.querySelector('h2')?.textContent?.trim()).toBe('#142 Add retry');
      expect(dialog.querySelector('fd-issue-detail')).not.toBeNull();
    });

    it('moves focus into the pane when it opens', async () => {
      // Arrange
      const h = await loaded([item('f', 142)]);

      // Act
      await openCard(h, 'f');

      // Assert
      h.httpMock.expectOne('/api/issues/f');
      expect(document.activeElement?.id).toBe('board-sheet-title');
    });

    it('returns focus to the card when Escape closes it', async () => {
      // Arrange
      const h = await loaded([item('f', 142)]);
      const button = await openCard(h, 'f');
      h.httpMock.expectOne('/api/issues/f');

      // Act
      document.activeElement!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
      await h.render();

      // Assert
      expect(document.activeElement).toBe(button);
    });

    it('closes on a click outside it', async () => {
      // Arrange
      const h = await loaded([item('f', 142)]);
      await openCard(h, 'f');
      h.httpMock.expectOne('/api/issues/f');

      // Act
      h.el.querySelector('h1')!.dispatchEvent(new Event('pointerdown', { bubbles: true }));
      await h.render();
      await new Promise((resolve) => setTimeout(resolve, 0));
      await h.render();

      // Assert
      expect(h.el.querySelector('[role="dialog"].side-sheet:not(.side-sheet--leave)')).toBeNull();
    });

    it('swaps to another card without closing', async () => {
      // Arrange
      const h = await loaded([item('a', 1, { title: 'First' }), item('b', 2, { title: 'Second' })]);
      await openCard(h, 'a');
      h.httpMock.expectOne('/api/issues/a');

      // Act
      h.el.querySelector('[data-issue-card="b"] button')!.dispatchEvent(new Event('pointerdown', { bubbles: true }));
      await openCard(h, 'b');

      // Assert
      h.httpMock.expectOne('/api/issues/b');
      expect(h.el.querySelector('[role="dialog"] h2')?.textContent?.trim()).toBe('#2 Second');
    });
  });
});

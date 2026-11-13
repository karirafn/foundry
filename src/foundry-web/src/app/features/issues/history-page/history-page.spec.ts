import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideLocationMocks } from '@angular/common/testing';
import { provideRouter, Router } from '@angular/router';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { HistoryPageComponent } from './history-page';
import { IssueService } from '../issue.service';
import { IssueSignalRService } from '../../../core/services/issue-signalr.service';
import { WorkerSignalRService, WORKER_HUB_FACTORY } from '../../../core/services/worker-signalr.service';
import { IssueSummary } from '../issue.model';

// Hub handlers register here, so a test can push an IssueUpdated event to every listener.
let hubHandlers: { method: string; handler: (summary: IssueSummary) => void }[] = [];

function pushHubEvent(method: string, summary: IssueSummary): void {
  hubHandlers.filter((h) => h.method === method).forEach((h) => h.handler(summary));
}

const mockIssueSignalRService = {
  on: (method: string, handler: (summary: IssueSummary) => void) => {
    hubHandlers.push({ method, handler });
  },
  onReconnected: () => {},
  connectionStatus: () => 'connected',
};

const mockWorkerHub = {
  on: () => {},
  onReconnected: () => {},
  onReconnecting: () => {},
  onClose: () => {},
  stream: () => ({ subscribe: () => ({ dispose: () => {} }) }),
  start: () => Promise.resolve(),
};

const completed: IssueSummary = {
  id: 'abc123',
  issueNumber: 42,
  title: 'Enable dark mode',
  state: 'completed',
  repositorySlug: 'owner/repo',
  providerType: 'github',
  detectedAt: '2026-01-01T00:00:00Z',
  url: 'https://github.com/owner/repo/issues/42',
  pullRequestUrl: 'https://github.com/owner/repo/pull/7',
  stage: 'done',
  needsAction: false,
  awaitsDispatch: false,
  codeStep: null,
};

const mockRunTotals = {
  runCount: 0,
  durationMs: 0,
  numTurns: 0,
  totalCostUsd: 0,
  inputTokens: { total: 0, knownRunCount: 0, unknownRunCount: 0 },
  outputTokens: { total: 0, knownRunCount: 0, unknownRunCount: 0 },
};

const mockDetail = {
  ...completed,
  body: null,
  author: 'dev',
  labels: [],
  stateDetails: null,
};

function setup() {
  hubHandlers = [];
  TestBed.configureTestingModule({
    imports: [HistoryPageComponent],
    providers: [
      IssueService,
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter([]),
      provideLocationMocks(),
      { provide: IssueSignalRService, useValue: mockIssueSignalRService },
      WorkerSignalRService,
      { provide: WORKER_HUB_FACTORY, useValue: () => mockWorkerHub },
    ],
  });
  const fixture = TestBed.createComponent(HistoryPageComponent);
  const httpMock = TestBed.inject(HttpTestingController);
  document.body.appendChild(fixture.nativeElement);
  return { fixture, httpMock, el: fixture.nativeElement as HTMLElement };
}

function isResolvedPage(req: { url: string; params: { getAll(name: string): string[] | null; has(name: string): boolean } }): boolean {
  return req.url === '/api/issues' && (req.params.getAll('states') ?? []).includes('completed');
}

function expectResolvedPage(httpMock: HttpTestingController): TestRequest {
  return httpMock.expectOne((r) => isResolvedPage(r) && !r.params.has('cursor'));
}

function flushRunTotals(httpMock: HttpTestingController): void {
  httpMock.expectOne((r) => r.url === '/api/workers/run-totals').flush(mockRunTotals);
}

async function render(fixture: { detectChanges: () => void; whenStable: () => Promise<unknown> }): Promise<void> {
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
}

/** Opens the page and answers its first resolved page with `items`. */
async function openWith(items: IssueSummary[], nextCursor: string | null = null) {
  const ctx = setup();
  ctx.fixture.detectChanges();
  expectResolvedPage(ctx.httpMock).flush({ items, nextCursor });
  flushRunTotals(ctx.httpMock);
  await render(ctx.fixture);
  return ctx;
}

function announcer(el: HTMLElement): string {
  return el.querySelector('.history-page__announcer')?.textContent?.trim() ?? '';
}

describe('HistoryPageComponent', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    document.body.innerHTML = '';
  });

  describe('frame', () => {
    it('is a section labelled by the Resolved history heading', async () => {
      // Act
      const { el } = await openWith([]);

      // Assert
      const root = el.querySelector('section.history-page')!;
      expect(root.getAttribute('aria-labelledby')).toBe('history-heading');
      const h1 = el.querySelector('h1#history-heading')!;
      expect(h1.textContent?.trim()).toBe('Resolved history');
      expect(h1.getAttribute('tabindex')).toBe('-1');
      expect(el.querySelector('main')).toBeNull();
    });

    it('describes the page in a lead line', async () => {
      // Act
      const { el } = await openWith([]);

      // Assert
      expect(el.querySelector('.history-page__lead')?.textContent?.trim()).toBe(
        'Issues Foundry has completed, most recent first.',
      );
    });

    it('links back to the Board', async () => {
      // Act
      const { el } = await openWith([]);

      // Assert
      const link = el.querySelector<HTMLAnchorElement>('.history-page__board-link')!;
      expect(link.textContent?.trim()).toBe('Board');
      expect(link.getAttribute('href')).toBe('/');
    });

    it('shows the run statistics with their time range', async () => {
      // Act
      const { el } = await openWith([]);

      // Assert
      expect(el.querySelector('fd-run-stats-bar')).not.toBeNull();
      expect(el.querySelector('fd-run-stats-bar [role="radiogroup"][aria-label="Time range"]')).not.toBeNull();
    });

    it('heads the resolved list with an h2 below the h1', async () => {
      // Act
      const { el } = await openWith([]);

      // Assert
      const region = el.querySelector('section.history-page__resolved')!;
      expect(region.getAttribute('aria-labelledby')).toBe('resolved-heading');
      const h2 = region.querySelector('h2#resolved-heading')!;
      expect(h2.textContent?.trim()).toBe('Completed issues');
      expect(h2.getAttribute('tabindex')).toBe('-1');
    });

    it('shows no active groups, group filter or dispatch controls', async () => {
      // Act
      const { el } = await openWith([completed]);

      // Assert
      expect(el.querySelector('fd-issue-group-filter')).toBeNull();
      expect(el.querySelector('fd-dispatch-controls')).toBeNull();
      expect(el.querySelector('[data-group]')).toBeNull();
      expect(el.querySelectorAll('h2').length).toBe(2); // Completed issues + Run statistics
    });

    it('does not load the active issues', async () => {
      // Act
      const { httpMock } = await openWith([]);

      // Assert
      httpMock.expectNone((r) => r.url === '/api/issues' && !r.params.has('states'));
      httpMock.expectNone('/api/issues/counts');
    });
  });

  describe('loading', () => {
    it('shows four card skeletons in a busy status region while the first page loads', () => {
      // Arrange
      const { fixture, httpMock, el } = setup();

      // Act
      fixture.detectChanges();

      // Assert
      const wrapper = el.querySelector('.history-page__skeletons')!;
      expect(wrapper.getAttribute('role')).toBe('status');
      expect(wrapper.getAttribute('aria-busy')).toBe('true');
      expect(wrapper.querySelector('.sr-only')?.textContent?.trim()).toBe('Loading resolved issues…');
      expect(wrapper.querySelectorAll('fd-issue-card-skeleton').length).toBe(4);
      expect(announcer(el)).toBe('Loading resolved issues…');

      expectResolvedPage(httpMock).flush({ items: [], nextCursor: null });
      flushRunTotals(httpMock);
    });

    it('removes the skeletons once the first page arrives', async () => {
      // Act
      const { el } = await openWith([completed]);

      // Assert
      expect(el.querySelector('.history-page__skeletons')).toBeNull();
    });
  });

  describe('populated', () => {
    it('renders one card per resolved issue, newest first as served', async () => {
      // Arrange
      const older: IssueSummary = { ...completed, id: 'older', issueNumber: 41, title: 'Older' };

      // Act
      const { el } = await openWith([completed, older]);

      // Assert
      const ids = Array.from(el.querySelectorAll('.history-page__item')).map((i) => i.getAttribute('data-issue-id'));
      expect(ids).toEqual(['abc123', 'older']);
      expect(el.querySelectorAll('fd-issue-card').length).toBe(2);
    });

    it('announces how many resolved issues loaded', async () => {
      // Act
      const { el } = await openWith([completed]);

      // Assert
      expect(announcer(el)).toBe('Loaded 1 resolved issue');
    });

    it('puts an issue that completes while the page is open at the top', async () => {
      // Arrange
      const { fixture, el } = await openWith([completed]);

      // Act
      pushHubEvent('IssueUpdated', { ...completed, id: 'fresh', issueNumber: 50, title: 'Just finished' });
      await render(fixture);

      // Assert
      const ids = Array.from(el.querySelectorAll('.history-page__item')).map((i) => i.getAttribute('data-issue-id'));
      expect(ids).toEqual(['fresh', 'abc123']);
    });
  });

  describe('empty', () => {
    it('says no issue has resolved yet, and announces it', async () => {
      // Act
      const { el } = await openWith([]);

      // Assert
      expect(el.querySelector('.history-page__empty')?.textContent?.trim()).toBe(
        'No resolved issues yet. Completed issues appear here.',
      );
      expect(announcer(el)).toBe('No resolved issues yet. Completed issues appear here.');
      expect(el.querySelector('.history-page__grid')).toBeNull();
    });
  });

  describe('error', () => {
    async function failedFirstPage() {
      const ctx = setup();
      ctx.fixture.detectChanges();
      expectResolvedPage(ctx.httpMock).flush('boom', { status: 500, statusText: 'Server Error' });
      flushRunTotals(ctx.httpMock);
      await render(ctx.fixture);
      return ctx;
    }

    it('shows an alert with a Retry button when the first page fails', async () => {
      // Act
      const { el } = await failedFirstPage();

      // Assert
      const alert = el.querySelector('[role="alert"]')!;
      expect(alert.textContent).toContain('Failed to load resolved issues');
      expect(alert.querySelector('button')?.textContent?.trim()).toBe('Retry');
      expect(el.querySelector('.history-page__empty')).toBeNull();
      expect(announcer(el)).toBe('');
    });

    it('fetches the first page again and focuses the list heading on Retry', async () => {
      // Arrange
      const { fixture, httpMock, el } = await failedFirstPage();

      // Act
      el.querySelector<HTMLButtonElement>('[role="alert"] button')!.click();
      await render(fixture);

      // Assert
      expect(document.activeElement).toBe(el.querySelector('#resolved-heading'));
      expectResolvedPage(httpMock).flush({ items: [completed], nextCursor: null });
      await render(fixture);
      expect(el.querySelectorAll('fd-issue-card').length).toBe(1);
    });
  });

  describe('load more', () => {
    it('offers Load more while another page exists', async () => {
      // Act
      const { el } = await openWith([completed], 'cursor-1');

      // Assert
      expect(el.querySelector('.history-page__load-more')?.textContent?.trim()).toBe('Load more');
    });

    it('hides Load more on the last page', async () => {
      // Act
      const { el } = await openWith([completed], null);

      // Assert
      expect(el.querySelector('.history-page__load-more')).toBeNull();
    });

    it('disables the button while the next page loads, and announces the added issues', async () => {
      // Arrange
      const { fixture, httpMock, el } = await openWith([completed], 'cursor-1');

      // Act
      el.querySelector<HTMLButtonElement>('.history-page__load-more')!.click();
      await render(fixture);

      // Assert
      const button = el.querySelector<HTMLButtonElement>('.history-page__load-more')!;
      expect(button.disabled).toBe(true);
      expect(button.querySelector('.sr-only')?.textContent?.trim()).toBe('Loading more resolved issues…');

      httpMock
        .expectOne((r) => isResolvedPage(r) && r.params.get('cursor') === 'cursor-1')
        .flush({ items: [{ ...completed, id: 'p2a' }, { ...completed, id: 'p2b' }], nextCursor: null });
      await render(fixture);
      expect(el.querySelectorAll('fd-issue-card').length).toBe(3);
      expect(announcer(el)).toBe('Loaded 2 more resolved issues (3 total)');
    });

    it('keeps the loaded cards and offers Retry when the next page fails, which focuses the list heading', async () => {
      // Arrange
      const { fixture, httpMock, el } = await openWith([completed], 'cursor-1');
      el.querySelector<HTMLButtonElement>('.history-page__load-more')!.click();
      httpMock
        .expectOne((r) => isResolvedPage(r) && r.params.get('cursor') === 'cursor-1')
        .flush('boom', { status: 500, statusText: 'Server Error' });
      await render(fixture);

      // Act
      const row = el.querySelector('.history-page__load-more-error')!;
      expect(row.getAttribute('role')).toBe('alert');
      expect(el.querySelectorAll('fd-issue-card').length).toBe(1);
      row.querySelector<HTMLButtonElement>('button')!.click();
      await render(fixture);

      // Assert
      expect(document.activeElement).toBe(el.querySelector('#resolved-heading'));
      httpMock
        .expectOne((r) => isResolvedPage(r) && r.params.get('cursor') === 'cursor-1')
        .flush({ items: [], nextCursor: null });
    });
  });

  describe('issue pane', () => {
    it('opens the shared pane for a resolved card, with the issue in the URL', async () => {
      // Arrange
      const { fixture, httpMock, el } = await openWith([completed]);

      // Act
      const toggle = el.querySelector<HTMLButtonElement>('[data-issue-id="abc123"] .issue-card__toggle')!;
      expect(toggle.getAttribute('aria-haspopup')).toBe('dialog');
      toggle.click();
      await render(fixture);
      httpMock.expectOne('/api/issues/abc123').flush(mockDetail);
      await render(fixture);

      // Assert
      const dialog = el.querySelector('[role="dialog"]')!;
      expect(dialog.getAttribute('aria-labelledby')).toBe('issue-pane-title');
      expect(dialog.querySelector('h2#issue-pane-title')?.textContent?.trim()).toBe('#42 Enable dark mode');
      expect(dialog.querySelector('fd-issue-detail')).not.toBeNull();
      expect(TestBed.inject(Router).url).toBe('/?issue=abc123');
    });

    it('opens the pane from a deep link once the detail loads', async () => {
      // Arrange
      const { fixture, httpMock, el } = setup();
      await TestBed.inject(Router).navigateByUrl('/?issue=abc123');

      // Act
      fixture.detectChanges();
      expectResolvedPage(httpMock).flush({ items: [completed], nextCursor: null });
      flushRunTotals(httpMock);
      await render(fixture);
      httpMock.expectOne('/api/issues/abc123').flush(mockDetail);
      await render(fixture);

      // Assert
      expect(el.querySelector('[role="dialog"] h2#issue-pane-title')?.textContent?.trim()).toBe(
        '#42 Enable dark mode',
      );
    });

    it('removes a deep-linked id whose detail is not found and opens nothing', async () => {
      // Arrange
      const { fixture, httpMock, el } = setup();
      const router = TestBed.inject(Router);
      await router.navigateByUrl('/?issue=gone');

      // Act
      fixture.detectChanges();
      expectResolvedPage(httpMock).flush({ items: [], nextCursor: null });
      flushRunTotals(httpMock);
      httpMock.expectOne('/api/issues/gone').flush('Not Found', { status: 404, statusText: 'Not Found' });
      await render(fixture);

      // Assert
      expect(el.querySelector('[role="dialog"]')).toBeNull();
      expect(router.url).toBe('/');
    });
  });
});

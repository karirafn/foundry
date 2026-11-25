import { Component, WritableSignal, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { WorkerSignalRService } from '../../../core/services/worker-signalr.service';
import { IssueActionsService } from '../../issues/issue-actions.service';
import { IssueSummary } from '../../issues/issue.model';
import { ItemView } from '../board-state.service';
import { CardOpened, NeedsItem, buildBoardBands } from '../board.model';
import { NeedsBandComponent } from './needs-band';

const NOW = Date.parse('2026-01-01T12:00:00Z');
const PR = 'https://github.com/acme/api/pull/9';

function issue(id: string, overrides: Partial<IssueSummary> = {}): IssueSummary {
  return {
    id,
    issueNumber: Number(id.replace(/\D/g, '')),
    title: `Issue ${id}`,
    state: 'review',
    repositorySlug: 'acme/api',
    detectedAt: '2026-01-01T00:00:00Z',
    url: `https://github.com/acme/api/issues/${id}`,
    stage: 'review',
    needsAction: true,
    awaitsDispatch: false,
    waitsOnBlockers: false,
    runKind: null,
    repositoryId: 'repo-1',
    enteredStateAt: '2026-01-01T10:00:00Z',
    round: 1,
    providerType: 'github',
    pullRequestUrl: PR,
    lifecycle: [
      { step: 'checks', actor: 'code', status: 'done', phase: null },
      { step: 'your_review', actor: 'person', status: 'current', phase: 'needs_you' },
    ],
    ...overrides,
  };
}

function view(issues: readonly IssueSummary[]): ItemView<NeedsItem>[] {
  return buildBoardBands(issues, [], []).needs.map((item) => ({
    ...item,
    phase: 'live',
    arrived: false,
    moved: false,
  }));
}

@Component({
  selector: 'fd-needs-band-host',
  imports: [NeedsBandComponent],
  template: `<fd-needs-band
    [alerts]="[]"
    [items]="items()"
    [now]="now"
    (open)="opened.push($event)"
  />`,
})
class HostComponent {
  readonly items: WritableSignal<readonly ItemView<NeedsItem>[]> = signal([]);
  readonly now = NOW;
  readonly opened: CardOpened[] = [];
}

function render(issues: readonly IssueSummary[]): ComponentFixture<HostComponent> {
  TestBed.configureTestingModule({
    providers: [
      {
        provide: WorkerSignalRService,
        useValue: { activeAgentCount: signal(0), agentActivityForIssue: () => null },
      },
      {
        provide: IssueActionsService,
        useValue: {
          pending: () => signal(false),
          pendingAction: () => signal(null),
          failure: () => signal(null),
        },
      },
    ],
  });
  const fixture = TestBed.createComponent(HostComponent);
  fixture.componentInstance.items.set(view(issues));
  document.body.appendChild(fixture.nativeElement);
  fixture.detectChanges();
  return fixture;
}

function root(fixture: ComponentFixture<HostComponent>): HTMLElement {
  return fixture.nativeElement as HTMLElement;
}

function cardIds(fixture: ComponentFixture<HostComponent>): (string | null)[] {
  return [...root(fixture).querySelectorAll('.needs-band__rows fd-lifecycle-row')].map((row) =>
    row.getAttribute('data-issue-id'),
  );
}

function reviewRowIds(fixture: ComponentFixture<HostComponent>): (string | null)[] {
  return [...root(fixture).querySelectorAll('.needs-band__review-rows fd-board-row')].map((row) =>
    row.getAttribute('data-issue-id'),
  );
}

function reviewRow(fixture: ComponentFixture<HostComponent>): HTMLElement {
  return root(fixture).querySelector<HTMLElement>('.needs-band__review-rows fd-board-row')!;
}

describe('NeedsBandComponent', () => {
  afterEach(() => document.body.replaceChildren());

  it('keeps issues that are not awaiting review as full cards, in their order', () => {
    // Act
    const fixture = render([
      issue('i1', { state: 'failed', stage: 'implement' }),
      issue('i2', { checksOutcome: 'still_failing' }),
      issue('i3'),
    ]);

    // Assert
    expect(cardIds(fixture)).toEqual(['i1', 'i2']);
  });

  it('lists issues awaiting review as rows under the cards, oldest first, then by number', () => {
    // Act
    const fixture = render([
      issue('i4', { enteredStateAt: '2026-01-01T11:00:00Z' }),
      issue('i3', { enteredStateAt: '2026-01-01T09:00:00Z', mergeOutcome: 'skipped' }),
      issue('i2', { enteredStateAt: '2026-01-01T11:00:00Z', reviewOutcome: 'skipped' }),
      issue('i1', { state: 'failed', stage: 'implement' }),
    ]);

    // Assert
    const list = root(fixture).querySelector('ul.needs-band__review-rows');
    expect(list?.getAttribute('aria-label')).toBe('Issues awaiting your review');
    expect(reviewRowIds(fixture)).toEqual(['i3', 'i2', 'i4']);
    expect(cardIds(fixture)).toEqual(['i1']);
  });

  it('counts the cards and the review rows together', () => {
    // Act
    const fixture = render([issue('i1', { state: 'failed', stage: 'implement' }), issue('i2')]);

    // Assert
    expect(root(fixture).querySelector('.needs-band__count')?.textContent).toBe('2');
  });

  it('shows the reason, repository, title, bar and waiting time with the review link, in order', () => {
    // Act
    const fixture = render([issue('i7')]);

    // Assert
    const row = reviewRow(fixture);
    const reason = row.querySelector('.needs-band__reason');
    expect([reason?.textContent?.trim(), reason?.getAttribute('data-tone')]).toEqual([
      'PR ready',
      'ready',
    ]);
    const cells = [
      row.querySelector('.needs-band__reason'),
      row.querySelector('.board-row__repo'),
      row.querySelector('.board-row__title'),
      row.querySelector('fd-lifecycle-bar'),
      row.querySelector('.needs-band__marker'),
    ];
    expect(cells.every((cell) => cell !== null)).toBe(true);
    for (let i = 1; i < cells.length; i++) {
      expect(
        cells[i - 1]!.compareDocumentPosition(cells[i]!) & Node.DOCUMENT_POSITION_FOLLOWING,
      ).toBeTruthy();
    }
    expect(row.querySelector('.needs-band__marker')?.textContent?.replace(/\s+/g, ' ').trim()).toBe(
      'waiting 2h·Review PR ↗ (opens GitHub in a new tab)',
    );
  });

  it("names the provider's term on the review link and reason", () => {
    // Act
    const fixture = render([
      issue('i7', {
        providerType: 'gitlab',
        pullRequestUrl: 'https://gitlab.com/acme/api/-/merge_requests/9',
      }),
    ]);

    // Assert
    const row = reviewRow(fixture);
    expect(row.querySelector('.needs-band__reason')?.textContent?.trim()).toBe('MR ready');
    expect(row.querySelector('.needs-band__link')?.textContent).toContain('Review MR');
  });

  it('gives the explanation in the marker title and the row description', () => {
    // Act
    const fixture = render([issue('i7', { reviewOutcome: 'skipped' })]);

    // Assert
    const row = reviewRow(fixture);
    const explanation = "Checks passed; the review didn't finish";
    expect(row.querySelector('.needs-band__marker')?.getAttribute('title')).toBe(explanation);
    const ids = row
      .querySelector('.board-row__title')!
      .getAttribute('aria-describedby')!
      .split(' ');
    const described = ids.map((id) => document.getElementById(id)?.textContent?.trim());
    expect(described).toContain(explanation);
  });

  it('opens the pull request in a new tab without opening the pane', () => {
    // Arrange
    const fixture = render([issue('i7')]);
    const link = reviewRow(fixture).querySelector<HTMLAnchorElement>('.needs-band__link')!;
    document.addEventListener('click', (event) => event.preventDefault(), { once: true });

    // Act
    link.click();

    // Assert
    expect([link.href, link.target]).toEqual([PR, '_blank']);
    expect(fixture.componentInstance.opened).toEqual([]);
  });

  it('opens the pane when the row is clicked elsewhere', () => {
    // Arrange
    const fixture = render([issue('i7')]);
    const title = reviewRow(fixture).querySelector<HTMLButtonElement>('.board-row__title')!;

    // Act
    title.click();

    // Assert
    expect(fixture.componentInstance.opened).toEqual([{ id: 'i7', invoker: title }]);
  });

  it('shows only the waiting time when there is no pull request link', () => {
    // Act
    const fixture = render([issue('i7', { pullRequestUrl: null })]);

    // Assert
    const marker = reviewRow(fixture).querySelector('.needs-band__marker');
    expect(marker?.querySelector('a')).toBeNull();
    expect(marker?.textContent?.trim()).toBe('waiting 2h');
  });

  it('beacons the bar of a review row that just arrived', () => {
    // Arrange
    const fixture = render([]);

    // Act
    fixture.componentInstance.items.set(view([issue('i7')]).map((i) => ({ ...i, arrived: true })));
    fixture.detectChanges();

    // Assert
    expect(reviewRow(fixture).querySelector('.bar')?.classList).toContain('bar--arriving');
  });

  it('moves a review row whose checks turn red to the cards, under the same transition name', () => {
    // Arrange
    const fixture = render([issue('i7')]);
    const before = reviewRow(fixture).style.getPropertyValue('view-transition-name');

    // Act
    fixture.componentInstance.items.set(view([issue('i7', { checksOutcome: 'still_failing' })]));
    fixture.detectChanges();

    // Assert
    expect(reviewRowIds(fixture)).toEqual([]);
    const card = root(fixture).querySelector<HTMLElement>('.needs-band__rows fd-lifecycle-row')!;
    expect([
      card.getAttribute('data-issue-id'),
      card.style.getPropertyValue('view-transition-name'),
    ]).toEqual(['i7', before]);
  });
});

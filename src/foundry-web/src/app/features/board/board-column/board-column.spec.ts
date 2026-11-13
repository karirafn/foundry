import { TestBed } from '@angular/core/testing';
import { IssueSummary } from '../../issues/issue.model';
import { STAGES, buildBoardColumns } from '../board.model';
import { BoardColumnView } from '../board-state.service';
import { BoardColumnComponent, CardOpened } from './board-column';

function buildIssue(overrides: Partial<IssueSummary> = {}): IssueSummary {
  return {
    id: 'issue-1',
    issueNumber: 1,
    title: 'An issue',
    state: 'in_progress',
    repositorySlug: 'acme/api',
    detectedAt: '2026-01-01T00:00:00Z',
    url: 'https://github.com/acme/api/issues/1',
    stage: 'implement',
    needsAction: false,
    awaitsDispatch: false,
    codeStep: null,
    ...overrides,
  };
}

function columnOf(issues: IssueSummary[], stageIndex = 1, queueIds: string[] = []): BoardColumnView {
  const column = buildBoardColumns(issues, queueIds)[stageIndex];
  const view = (cards: typeof column.queue) => cards.map((c) => ({ ...c, phase: 'live' as const, arrived: false, moved: false }));
  return { ...column, queue: view(column.queue), flow: view(column.flow), needs: view(column.needs) };
}

function render(column: BoardColumnView, loading = false) {
  const fixture = TestBed.createComponent(BoardColumnComponent);
  fixture.componentRef.setInput('column', column);
  fixture.componentRef.setInput('position', 2);
  fixture.componentRef.setInput('loading', loading);
  fixture.detectChanges();
  return { fixture, el: fixture.nativeElement as HTMLElement };
}

describe('BoardColumnComponent', () => {
  it('is a region labelled by its stage heading', () => {
    // Act
    const { el } = render(columnOf([]));

    // Assert
    expect(el.getAttribute('role')).toBe('region');
    expect(el.getAttribute('aria-labelledby')).toBe('board-stage-implement');
    expect(el.querySelector('h2#board-stage-implement')?.textContent).toContain('Implement');
  });

  it('gives the heading its count, spelled out for screen readers', () => {
    // Act
    const { el } = render(columnOf([buildIssue({ id: 'a' }), buildIssue({ id: 'b' })]));

    // Assert
    const heading = el.querySelector('h2')!;
    expect(heading.querySelector('.board-column__count')?.textContent).toBe('2');
    expect(heading.querySelector('.sr-only')?.textContent).toBe(', 2 issues');
  });

  it('lists queued cards in an ordered, labelled list', () => {
    // Act
    const { el } = render(
      columnOf([buildIssue({ id: 'q', state: 'queued', awaitsDispatch: true })], 1, ['q']),
    );

    // Assert
    expect(el.querySelector('ol')?.getAttribute('aria-label')).toBe('Implement queue');
    expect(el.querySelectorAll('ol fd-board-card')).toHaveLength(1);
  });

  it('lists in-flow and needs-you cards in labelled lists', () => {
    // Act
    const { el } = render(columnOf([buildIssue({ id: 'f' }), buildIssue({ id: 'n', needsAction: true, state: 'failed' })]));

    // Assert
    const labels = [...el.querySelectorAll('ul')].map((ul) => ul.getAttribute('aria-label'));
    expect(labels).toEqual(['Implement in flow', 'Implement: needs you']);
  });

  it('renders no caption for an empty cell', () => {
    // Act
    const { el } = render(columnOf([buildIssue({ id: 'f' })]));

    // Assert
    const captions = [...el.querySelectorAll('.board-column__caption')].map((c) => c.textContent);
    expect(captions).toEqual(['In flow']);
  });

  it('has no queue cell for a stage without a queue', () => {
    // Arrange
    const issues = [buildIssue({ id: 'r', stage: 'awaiting_review', state: 'review', awaitsDispatch: true })];

    // Act
    const { el } = render(columnOf(issues, STAGES.findIndex((s) => s.id === 'awaiting_review')));

    // Assert
    expect(el.querySelector('ol')).toBeNull();
  });

  it('emits the opened card with its button', () => {
    // Arrange
    const { fixture, el } = render(columnOf([buildIssue({ id: 'f' })]));
    let opened: CardOpened | null = null;
    fixture.componentInstance.open.subscribe((e) => (opened = e));
    const button = el.querySelector('fd-board-card button') as HTMLButtonElement;

    // Act
    button.click();

    // Assert
    expect(opened).toEqual({ id: 'f', invoker: button });
  });

  it('shows skeletons and hides the count while loading', () => {
    // Act
    const { el } = render(columnOf([]), true);

    // Assert
    expect(el.querySelectorAll('.board-column__skeleton')).toHaveLength(2);
    expect(el.querySelector('.board-column__count')).toBeNull();
  });

  it('marks a collapsing card for its collapse animation', () => {
    // Arrange
    const column = columnOf([buildIssue({ id: 'f' })]);
    const collapsing = { ...column, flow: [{ ...column.flow[0], phase: 'collapse' as const }] };

    // Act
    const { el } = render(collapsing);

    // Assert
    expect(el.querySelector('li')?.classList).toContain('board-column__item--collapse');
  });
});

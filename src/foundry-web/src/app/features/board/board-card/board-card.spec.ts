import { TestBed } from '@angular/core/testing';
import { IssueSummary } from '../../issues/issue.model';
import { BoardCardView } from '../board-state.service';
import { BoardCardComponent } from './board-card';

function buildCard(overrides: Partial<BoardCardView> = {}, issue: Partial<IssueSummary> = {}): BoardCardView {
  return {
    issue: {
      id: 'issue-1',
      issueNumber: 142,
      title: 'Add retry to webhook relay',
      state: 'in_progress',
      repositorySlug: 'acme/api',
      detectedAt: '2026-01-01T00:00:00Z',
      url: 'https://github.com/acme/api/issues/142',
      stage: 'implement',
      needsAction: false,
      awaitsDispatch: false,
      codeStep: null,
      ...issue,
    },
    stage: 'implement',
    cell: 'flow',
    position: null,
    ineligible: false,
    nextRole: null,
    queueMarker: null,
    reason: null,
    running: false,
    phase: 'live',
    arrived: false,
    moved: false,
    ...overrides,
  };
}

function render(card: BoardCardView) {
  const fixture = TestBed.createComponent(BoardCardComponent);
  fixture.componentRef.setInput('card', card);
  fixture.detectChanges();
  const host = fixture.nativeElement as HTMLElement;
  return { fixture, host, button: host.querySelector('button') as HTMLButtonElement };
}

describe('BoardCardComponent', () => {
  it('is a button that opens a dialog', () => {
    const { button } = render(buildCard());
    expect(button.getAttribute('type')).toBe('button');
    expect(button.getAttribute('aria-haspopup')).toBe('dialog');
  });

  it('shows the repository slug and issue number', () => {
    const { button } = render(buildCard());
    expect(button.querySelector('.board-card__slug')?.textContent).toBe('acme/api');
    expect(button.querySelector('.board-card__number')?.textContent).toBe('#142');
  });

  it('shows the title with the full title in its title attribute', () => {
    const { button } = render(buildCard());
    const title = button.querySelector('.board-card__title')!;
    expect(title.textContent).toBe('Add retry to webhook relay');
    expect(title.getAttribute('title')).toBe('Add retry to webhook relay');
  });

  it('shows its queue position', () => {
    const { button } = render(buildCard({ cell: 'queue', position: 3 }));
    expect(button.querySelector('.board-card__position')?.textContent).toBe(', queue position #3 next');
  });

  it('highlights the next card in the queue', () => {
    const { button } = render(buildCard({ cell: 'queue', position: 1 }));
    expect(button.querySelector('.board-card__position--next')).not.toBeNull();
  });

  it('shows a dash and the ineligible marker for an ineligible repository', () => {
    // Act
    const { button } = render(buildCard({ cell: 'queue', ineligible: true }));

    // Assert
    const position = button.querySelector('.board-card__position')!;
    expect(position.textContent).toBe('—');
    expect(position.getAttribute('aria-hidden')).toBe('true');
    expect(button.querySelector('.board-card__ineligible')?.textContent?.trim()).toBe('Repo ineligible');
  });

  it('shows the next role of a queued card', () => {
    const { button } = render(buildCard({ cell: 'queue', nextRole: 'triage' }));
    expect(button.querySelector('fd-role-tag')?.textContent).toContain('triage');
  });

  it('shows the step chip for a code step', () => {
    const { button } = render(buildCard({}, { codeStep: 'rebasing', state: 'rebase_pending' }));
    expect(button.querySelector('fd-step-chip')?.textContent?.trim()).toBe('Rebasing…');
  });

  it('reads a publish retry as a warning step', () => {
    const { button } = render(buildCard({}, { codeStep: 'publish_retrying' }));
    expect(button.querySelector('.step-chip--warning')?.textContent?.trim()).toBe('Retrying publish…');
  });

  it('shows the needs-you reason', () => {
    const { button } = render(buildCard({ cell: 'needs', reason: { label: 'Checks red', tone: 'error' } }));
    expect(button.querySelector('.board-card__reason')?.textContent).toBe('Checks red');
  });

  it('shows the queue marker of a card that has no position yet', () => {
    const { button } = render(buildCard({ cell: 'queue', queueMarker: 'Blocked' }));
    expect(button.querySelector('.board-card__muted')?.textContent).toBe('Blocked');
  });

  it('marks a running card and says so to screen readers', () => {
    const { button } = render(buildCard({ running: true }));
    expect(button.classList).toContain('board-card--running');
    expect(button.textContent).toContain(', worker running');
  });

  it('shows Completed while confirming a completion', () => {
    const { button } = render(buildCard({ phase: 'confirm', running: true }));
    expect(button.querySelector('.board-card__markers--done')?.textContent?.trim()).toBe(', Completed');
    expect(button.getAttribute('data-tone')).toBe('completed');
    expect(button.classList).not.toContain('board-card--running');
  });

  it('emits its button when activated', () => {
    // Arrange
    const { fixture, button } = render(buildCard());
    let opened: HTMLElement | null = null;
    fixture.componentInstance.open.subscribe((el) => (opened = el));

    // Act
    button.click();

    // Assert
    expect(opened).toBe(button);
  });

  it('identifies its issue and needs-you placement on its host', () => {
    const { host } = render(buildCard({ cell: 'needs' }));
    expect(host.getAttribute('data-issue-card')).toBe('issue-1');
    expect(host.getAttribute('data-needs')).toBe('true');
  });

  it('gives its host a unique view transition name', () => {
    const { host } = render(buildCard());
    expect(host.style.getPropertyValue('view-transition-name')).toBe('fd-card-issue-1');
  });
});

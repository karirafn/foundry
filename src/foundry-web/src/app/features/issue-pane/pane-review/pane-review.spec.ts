import { TestBed } from '@angular/core/testing';
import { Observable, Subject, of } from 'rxjs';
import { WorkerRunDetail } from '../../workers/worker-run.model';
import { WorkerRunService } from '../../workers/worker-run.service';
import { CARRIED_SHA, REVIEWED_SHA, reviewRun, verdict } from './pane-review.fixture';
import { PaneReviewComponent } from './pane-review';

type GetDetail = (id: string) => Observable<WorkerRunDetail | null>;

function setup(
  reviewRunId: string | null,
  run: WorkerRunDetail | null = null,
  getDetail: GetDetail = () => of(reviewRun()),
) {
  const spy = vi.fn(getDetail);
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    imports: [PaneReviewComponent],
    providers: [{ provide: WorkerRunService, useValue: { getDetail: spy } }],
  });
  const fixture = TestBed.createComponent(PaneReviewComponent);
  fixture.componentRef.setInput('reviewRunId', reviewRunId);
  fixture.componentRef.setInput('run', run);
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  return { fixture, el, getDetail: spy };
}

function text(node: Element | null | undefined): string {
  return (node?.textContent ?? '').replace(/\s+/g, ' ').trim();
}

function details(el: HTMLElement, titleStart: string): HTMLDetailsElement | undefined {
  return Array.from(el.querySelectorAll('details')).find((d) => text(d.querySelector('summary')).startsWith(titleStart));
}

describe('PaneReviewComponent', () => {
  it('renders nothing and fetches nothing without a review run', () => {
    // Act
    const { el, getDetail } = setup(null);

    // Assert
    expect(el.querySelector('section')).toBeNull();
    expect(getDetail).not.toHaveBeenCalled();
  });

  it('reuses the pane run when it is the review run', () => {
    // Act
    const { el, getDetail } = setup('review-run-1', reviewRun());

    // Assert
    expect(getDetail).not.toHaveBeenCalled();
    expect(el.querySelector('section')).not.toBeNull();
  });

  it('fetches the review run when the pane run is another run', () => {
    // Act
    const { el, getDetail } = setup('review-run-1', reviewRun({ workerRunId: 'fix-run', reviewVerdict: null }));

    // Assert
    expect(getDetail).toHaveBeenCalledExactlyOnceWith('review-run-1');
    expect(el.querySelector('section')).not.toBeNull();
  });

  it('fetches the review run once while the pane run reloads', () => {
    // Arrange
    const { fixture, getDetail } = setup('review-run-1', reviewRun({ workerRunId: 'fix-run' }));

    // Act
    fixture.componentRef.setInput('run', reviewRun({ workerRunId: 'fix-run', state: 'failed' }));
    fixture.detectChanges();

    // Assert
    expect(getDetail).toHaveBeenCalledOnce();
  });

  it('fetches again for a new review run', () => {
    // Arrange
    const { fixture, getDetail } = setup('review-run-1');

    // Act
    fixture.componentRef.setInput('reviewRunId', 'review-run-2');
    fixture.detectChanges();

    // Assert
    expect(getDetail.mock.calls.map(([id]) => id)).toEqual(['review-run-1', 'review-run-2']);
  });

  it('shows a busy skeleton while the review run loads', () => {
    // Act
    const { el } = setup('review-run-1', null, () => new Subject<WorkerRunDetail | null>());

    // Assert
    const busy = el.querySelector('[aria-busy="true"]');
    expect(text(busy)).toBe('Loading review…');
    expect(busy?.querySelector('.sr-only')).not.toBeNull();
  });

  it('renders nothing when the fetch fails', () => {
    // Act
    const { el } = setup('review-run-1', null, () => of(null));

    // Assert
    expect(el.querySelector('section')).toBeNull();
  });

  it('renders nothing for a run without a verdict', () => {
    // Act
    const { el } = setup('review-run-1', reviewRun({ reviewVerdict: null }));

    // Assert
    expect(el.querySelector('section')).toBeNull();
  });

  it('heads the section with a Review h3 that names it', () => {
    // Act
    const { el } = setup('review-run-1');

    // Assert
    const section = el.querySelector('section')!;
    const heading = el.querySelector(`#${section.getAttribute('aria-labelledby')}`);
    expect(heading?.tagName.toLowerCase()).toBe('h3');
    expect(text(heading)).toBe('Review');
  });

  it('shows the outcome as a word and the reviewed commit with its full SHA', () => {
    // Act
    const { el } = setup('review-run-1', null, () => of(reviewRun({ reviewVerdict: verdict({ outcome: 'ChangesRequested' }) })));

    // Assert
    const chip = el.querySelector('.pane-review__outcome .badge')!;
    expect(text(chip)).toBe('Changes requested');
    expect(chip.classList).toContain('pane-review__chip--warning');
    const reviewed = el.querySelector('.pane-review__reviewed')!;
    expect(text(reviewed)).toBe('Reviewed 0123456');
    expect(reviewed.querySelector('[title]')?.getAttribute('title')).toBe(REVIEWED_SHA);
  });

  it('notes the commit a carried approval came from', () => {
    // Act
    const { el } = setup(
      'review-run-1',
      null,
      () => of(reviewRun({ reviewVerdict: verdict({ carriedFrom: { runId: 'run-0', reviewedCommit: CARRIED_SHA } }) })),
    );

    // Assert
    expect(text(el.querySelector('.pane-review__outcome .badge'))).toBe('Approved');
    const carried = el.querySelector('.pane-review__carried')!;
    expect(text(carried)).toBe('Carried from fedcba9');
    expect(carried.querySelector('[title]')?.getAttribute('title')).toBe(CARRIED_SHA);
  });

  it('renders verdict text as text, never as markup', () => {
    // Arrange
    const markup = '<img src=x onerror="alert(1)"><b>bold</b>';
    const run = reviewRun({
      reviewVerdict: verdict({
        outcome: 'ChangesRequested',
        summary: markup,
        findings: [{ path: markup, line: 1, severity: 'Blocking', category: 'Security', rule: null, body: markup }],
      }),
    });

    // Act
    const { el } = setup('review-run-1', run);

    // Assert
    expect(el.querySelector('img')).toBeNull();
    expect(el.querySelector('b')).toBeNull();
    expect(text(el.querySelector('.pane-review__summary'))).toBe(markup);
    expect(el.querySelector('a')).toBeNull();
  });

  it('clamps a long summary behind Show more, which names what it controls', () => {
    // Arrange
    const { fixture, el } = setup('review-run-1', reviewRun({ reviewVerdict: verdict({ summary: 'a\nb\nc\nd\ne' }) }));
    const summary = el.querySelector('.pane-review__summary')!;
    const toggle = el.querySelector<HTMLButtonElement>('.pane-review__more')!;
    expect(summary.classList).toContain('pane-review__summary--clamped');
    expect(toggle.getAttribute('aria-controls')).toBe(summary.id);
    expect(toggle.getAttribute('aria-expanded')).toBe('false');

    // Act
    toggle.click();
    fixture.detectChanges();

    // Assert
    expect(summary.classList).not.toContain('pane-review__summary--clamped');
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    expect(text(toggle)).toBe('Show less');
  });

  it('has no Show more for a short summary', () => {
    // Act
    const { el } = setup('review-run-1');

    // Assert
    expect(el.querySelector('.pane-review__more')).toBeNull();
  });

  it('leaves the findings disclosure out when there are none', () => {
    // Act
    const { el } = setup('review-run-1');

    // Assert
    expect(details(el, 'Findings')).toBeUndefined();
  });

  it('opens the findings on changes requested and lists each finding with a severity word', () => {
    // Arrange
    const run = reviewRun({
      reviewVerdict: verdict({
        outcome: 'ChangesRequested',
        findings: [
          { path: 'src/a.cs', line: 3, severity: 'Advisory', category: 'Quality', rule: null, body: 'Rename.' },
          { path: 'src/b.cs', line: 9, severity: 'Blocking', category: 'Design', rule: { principle: 'design.1', file: null, quote: null }, body: 'Split.' },
        ],
      }),
    });

    // Act
    const { el } = setup('review-run-1', run);

    // Assert
    const findings = details(el, 'Findings')!;
    expect(findings.open).toBe(true);
    expect(text(findings.querySelector('summary'))).toBe('Findings — 1 blocking, 1 advisory');
    const items = Array.from(findings.querySelectorAll('li'));
    expect(text(items[0].querySelector('.badge'))).toBe('Blocking');
    expect(items[0].querySelector('.badge')!.classList).toContain('pane-review__chip--error');
    expect(text(items[0])).toContain('src/b.cs:9');
    expect(text(items[0])).toContain('design.1');
    expect(text(items[0])).toContain('Split.');
    expect(text(items[1].querySelector('.badge'))).toBe('Advisory');
  });

  it('keeps the findings closed on an approval', () => {
    // Arrange
    const run = reviewRun({
      reviewVerdict: verdict({
        findings: [{ path: 'src/a.cs', line: 3, severity: 'Advisory', category: 'Quality', rule: null, body: 'Rename.' }],
      }),
    });

    // Act
    const { el } = setup('review-run-1', run);

    // Assert
    expect(details(el, 'Findings')!.open).toBe(false);
  });

  it('lists each criterion with its id, status word, text and evidence', () => {
    // Act
    const { el } = setup('review-run-1');

    // Assert
    const criteria = details(el, 'Acceptance criteria')!;
    expect(text(criteria.querySelector('summary'))).toBe('Acceptance criteria — 1 of 1 met');
    const item = criteria.querySelector('li')!;
    expect(text(item)).toBe('AC1 Met Retry review queues the review again. RetryReviewTests covers it.');
  });

  it('says a person reviews when the issue had no acceptance criteria', () => {
    // Act
    const { el } = setup('review-run-1', reviewRun({ reviewVerdict: verdict({ criteria: [] }), reviewCriteria: [] }));

    // Assert
    expect(details(el, 'Acceptance criteria')).toBeUndefined();
    expect(text(el)).toContain('No acceptance criteria found; a person reviews.');
  });

  it('holds the tests reason and covering tests in the tests disclosure', () => {
    // Act
    const { el } = setup('review-run-1');

    // Assert
    const tests = details(el, 'Tests')!;
    expect(text(tests.querySelector('summary'))).toBe('Tests — Adequate');
    expect(text(tests)).toContain('Handler tests cover both paths.');
    expect(Array.from(tests.querySelectorAll('li')).map(text)).toEqual(['tests/RetryReviewTests.cs']);
  });

  it('always shows Foundry’s test check outside the disclosure when it is set', () => {
    // Act
    const { el } = setup('review-run-1', reviewRun({ productionChangedWithoutTests: true }));

    // Assert
    const check = Array.from(el.querySelectorAll('.badge')).find((b) => text(b) === 'Foundry check: production changed, no test changed');
    expect(check).toBeDefined();
    expect(check!.closest('details')).toBeNull();
    expect(check!.classList).toContain('pane-review__chip--warning');
  });

  it('hides Foundry’s test check when it is not set', () => {
    // Act
    const { el } = setup('review-run-1');

    // Assert
    expect(text(el)).not.toContain('Foundry check');
  });

  it('lists every dimension with a status word in a description list', () => {
    // Act
    const { el } = setup('review-run-1');

    // Assert
    const dimensions = details(el, 'Dimensions')!;
    expect(text(dimensions.querySelector('summary'))).toBe('Dimensions — 0 with findings');
    expect(Array.from(dimensions.querySelectorAll('dt')).map(text)).toEqual([
      'Correctness',
      'Criteria',
      'Tests',
      'Security',
      'Quality',
      'Design',
      'Conventions',
    ]);
    expect(text(dimensions.querySelectorAll('dd')[0])).toBe('Clear Logic holds.');
    expect(text(dimensions.querySelectorAll('dd')[3])).toBe('Not applicable No input handling.');
  });
});

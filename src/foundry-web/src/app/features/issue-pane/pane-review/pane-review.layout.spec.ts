import { Component, input } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { WorkerRunService } from '../../workers/worker-run.service';
import { reviewRun, verdict } from './pane-review.fixture';
import { PaneReviewComponent } from './pane-review';

const LONG_WORD = 'x'.repeat(300);
const LONG_PATH = `src/${'a'.repeat(290)}/x.ts`;

const RUN = reviewRun({
  reviewVerdict: verdict({
    outcome: 'ChangesRequested',
    summary: Array.from({ length: 12 }, (_, i) => `Line ${i} ${LONG_WORD}`).join('\n'),
    findings: [
      { path: LONG_PATH, line: 42, severity: 'Blocking', category: 'Correctness', rule: null, body: `Wrong. ${LONG_WORD}` },
      {
        path: LONG_PATH,
        line: null,
        severity: 'Advisory',
        category: 'Conventions',
        rule: { principle: null, file: 'CLAUDE.md', quote: LONG_WORD },
        body: 'Rename.',
      },
    ],
    criteria: [{ id: 'AC1', status: 'NotVerifiable', evidence: `Only the UI shows it. ${LONG_WORD}` }],
    tests: { assessment: 'Missing', reason: `No test. ${LONG_WORD}`, coveringTests: [LONG_PATH] },
    dimensions: [
      { category: 'Correctness', status: 'Findings', note: LONG_WORD },
      { category: 'Conventions', status: 'NotApplicable', note: '' },
    ],
  }),
  productionChangedWithoutTests: true,
  reviewCriteria: [{ id: 'AC1', text: `The pane shows it. ${LONG_WORD}` }],
});

@Component({
  selector: 'fd-pane-review-layout-host',
  imports: [PaneReviewComponent],
  template: `<div class="host" [style.width.px]="width()"><fd-pane-review [reviewRunId]="'review-run-1'" [run]="run" /></div>`,
})
class HostComponent {
  readonly width = input.required<number>();
  readonly run = RUN;
}

async function setup(width: number) {
  TestBed.configureTestingModule({
    providers: [{ provide: WorkerRunService, useValue: { getDetail: () => of(RUN) } }],
  });
  const fixture = TestBed.createComponent(HostComponent);
  fixture.componentRef.setInput('width', width);
  await fixture.whenStable();
  return { fixture, host: (fixture.nativeElement as HTMLElement).querySelector('.host') as HTMLElement };
}

// Runs in a real browser (test-layout target); jsdom would measure every box as 0.
describe('pane review layout', () => {
  it.each([240, 332, 720])('keeps every box inside the pane with long unbroken text at %ipx', async (width) => {
    // Arrange
    const { host } = await setup(width);
    host.querySelectorAll('details').forEach((d) => (d.open = true));
    const hostRight = host.getBoundingClientRect().right;

    // Act
    const boxes = Array.from(host.querySelectorAll<HTMLElement>('section *'));
    const overflowing = boxes.filter((b) => b.getBoundingClientRect().right > hostRight + 0.5);

    // Assert
    expect(boxes.length).toBeGreaterThan(0);
    expect(overflowing.map((b) => b.className || b.tagName)).toEqual([]);
  });

  it('clamps a long summary to four lines until it is expanded', async () => {
    // Arrange
    const { fixture, host } = await setup(720);
    const summary = host.querySelector<HTMLElement>('.pane-review__summary')!;
    const lineHeight = parseFloat(getComputedStyle(summary).lineHeight);
    const collapsed = summary.getBoundingClientRect().height;

    // Act
    host.querySelector<HTMLButtonElement>('.pane-review__more')!.click();
    await fixture.whenStable();
    const expanded = summary.getBoundingClientRect().height;

    // Assert
    expect(Math.round(collapsed)).toBeLessThanOrEqual(Math.round(lineHeight * 4) + 1);
    expect(expanded).toBeGreaterThan(collapsed);
  });

  it('gives every disclosure summary and Show more at least a 32px target', async () => {
    // Arrange
    const { host } = await setup(332);

    // Act
    const heights = Array.from(host.querySelectorAll('summary, .pane-review__more')).map((s) => s.getBoundingClientRect().height);

    // Assert
    expect(heights.length).toBe(5);
    expect(heights.filter((h) => h < 32)).toEqual([]);
  });
});

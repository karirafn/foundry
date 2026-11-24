import { Component, input } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { page } from 'vitest/browser';
import { IssueSummary } from '../../issues/issue.model';
import { LifecycleStep } from '../../issues/lifecycle.model';
import { ItemView } from '../board-state.service';
import { PipelineItem } from '../board.model';
import { PipelineBandComponent } from './pipeline-band';

// A round-2 revision queued for implement: every step served, the queue wait current.
const QUEUED_PATH: readonly LifecycleStep[] = [
  { step: 'detected', actor: 'code', status: 'done', phase: null },
  { step: 'triage_queued', actor: 'code', status: 'done', phase: null },
  { step: 'triage', actor: 'claude', status: 'done', phase: null },
  { step: 'queued', actor: 'code', status: 'current', phase: 'waiting' },
  { step: 'implement', actor: 'claude', status: 'upcoming', phase: null },
  { step: 'publish', actor: 'code', status: 'upcoming', phase: null },
  { step: 'rebase', actor: 'code', status: 'upcoming', phase: null },
  { step: 'checks', actor: 'code', status: 'upcoming', phase: null },
  { step: 'review_queued', actor: 'code', status: 'off', phase: null },
  { step: 'review', actor: 'claude', status: 'off', phase: null },
  { step: 'your_review', actor: 'person', status: 'upcoming', phase: null },
  { step: 'merge', actor: 'code', status: 'skipped', phase: null },
  { step: 'done', actor: 'code', status: 'upcoming', phase: null },
];

// A fresh issue waiting for triage: only Detected done.
const TRIAGE_QUEUED_PATH: readonly LifecycleStep[] = [
  { step: 'detected', actor: 'code', status: 'done', phase: null },
  { step: 'triage_queued', actor: 'code', status: 'current', phase: 'waiting' },
  { step: 'triage', actor: 'claude', status: 'upcoming', phase: null },
  { step: 'queued', actor: 'code', status: 'upcoming', phase: null },
  ...QUEUED_PATH.slice(4),
];

function row(
  index: number,
  lifecycle: readonly LifecycleStep[] | undefined,
  round: number,
  title = 'A title long enough to fill the title column of the row',
): ItemView<PipelineItem> {
  const issue: IssueSummary = {
    id: `issue-${index}`,
    issueNumber: 1000 + index,
    title,
    state: 'revision_queued',
    // A long slug, so the repository column is as wide as it gets.
    repositorySlug: 'acme/a-repository-name-long-enough-to-fill-its-column',
    detectedAt: '2026-01-01T00:00:00Z',
    url: `https://github.com/acme/api/issues/${index}`,
    repositoryEligibilityStatus: 'eligible',
    stage: 'implement',
    needsAction: false,
    awaitsDispatch: true,
    waitsOnBlockers: false,
    runKind: 'checks_fix',
    repositoryId: 'repo-1',
    enteredStateAt: '2026-01-01T00:00:00Z',
    round,
    lifecycle,
  };
  return {
    band: 'pipeline',
    issue,
    stage: 'implement',
    runKind: 'checks_fix',
    slot: { kind: 'position', position: index + 1 },
    phase: 'live',
    arrived: false,
    moved: false,
  };
}

const TYPICAL: readonly ItemView<PipelineItem>[] = [
  row(0, TRIAGE_QUEUED_PATH, 1),
  row(1, QUEUED_PATH, 2),
];

@Component({
  selector: 'fd-pipeline-band-layout-host',
  imports: [PipelineBandComponent],
  template: `<div style="padding: 12px"><fd-pipeline-band [items]="items()" /></div>`,
})
class HostComponent {
  readonly items = input<readonly ItemView<PipelineItem>[]>(TYPICAL);
}

function render(items: readonly ItemView<PipelineItem>[] = TYPICAL): HTMLElement {
  const fixture = TestBed.createComponent(HostComponent);
  fixture.componentRef.setInput('items', items);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

function markers(el: HTMLElement): HTMLElement[] {
  return [...el.querySelectorAll<HTMLElement>('.board-row__marker')];
}

function bars(el: HTMLElement): DOMRect[] {
  return [...el.querySelectorAll('.board-row__bar')].map((bar) => bar.getBoundingClientRect());
}

// Runs in a real browser (test-layout target); jsdom would measure every box as 0.
describe('pipeline band layout', () => {
  // Restores the runner's viewport, so later layout specs measure as before.
  const original = { width: window.innerWidth, height: window.innerHeight };
  afterEach(async () => page.viewport(original.width, original.height));

  it('starts every row’s bar at the same x on a wide board', async () => {
    // Arrange
    await page.viewport(1280, 800);

    // Act
    const el = render();

    // Assert
    expect(markers(el).map((m) => m.textContent?.trim())).toEqual([
      'Queued for triage',
      'Queued for implement · round 2',
    ]);
    const [first, second] = bars(el);
    expect(first.width).toBeGreaterThan(0);
    expect(Math.abs(first.left - second.left)).toBeLessThanOrEqual(1);
    expect(Math.abs(first.right - second.right)).toBeLessThanOrEqual(1);
  });

  it('fits the longest phrase in the marker without ellipsis', async () => {
    // Arrange
    await page.viewport(1280, 800);

    // Act
    const el = render();
    // The fit depends on the font: measure in the app's own, not a wider fallback.
    await document.fonts.load('0.75rem Inter');

    // Assert
    const marker = markers(el)[1];
    expect(marker.clientWidth).toBeGreaterThan(0);
    expect(marker.scrollWidth).toBeLessThanOrEqual(marker.clientWidth);
  });

  it('keeps the bar clear of the title and the phrase on a wide row', async () => {
    // Arrange
    await page.viewport(1280, 800);

    // Act
    const el = render();

    // Assert
    const title = el.querySelector('.board-row__title')!.getBoundingClientRect();
    const bar = bars(el)[0];
    const marker = markers(el)[0].getBoundingClientRect();
    expect(bar.left).toBeGreaterThanOrEqual(title.right);
    expect(marker.left).toBeGreaterThanOrEqual(bar.right);
  });

  it('keeps the marker in its column when a row has no lifecycle', async () => {
    // Arrange
    await page.viewport(1280, 800);

    // Act
    const el = render([row(0, undefined, 1), row(1, QUEUED_PATH, 2)]);

    // Assert
    const [empty, full] = markers(el).map((m) => m.getBoundingClientRect());
    expect(Math.abs(empty.right - full.right)).toBeLessThanOrEqual(1);
    expect(Math.abs(empty.width - full.width)).toBeLessThanOrEqual(1);
  });

  it('stacks the bar under the title, across the row, on a narrow board', async () => {
    // Arrange
    await page.viewport(360, 800);

    // Act
    const el = render();

    // Assert
    const boardRow = el.querySelector<HTMLElement>('.board-row')!.getBoundingClientRect();
    const title = el.querySelector('.board-row__title')!.getBoundingClientRect();
    const bar = bars(el)[0];
    expect(bar.top).toBeGreaterThanOrEqual(title.bottom);
    expect(bar.width).toBeGreaterThan(boardRow.width * 0.8);
    expect(bar.right).toBeLessThanOrEqual(boardRow.right);
  });

  it.each([360, 760, 1280])('overflows nothing at %ipx', async (width) => {
    // Arrange
    await page.viewport(width, 800);

    // Act
    const el = render([
      ...TYPICAL,
      row(2, QUEUED_PATH, 12, 'A very long title '.repeat(20)),
      row(
        3,
        [...QUEUED_PATH, { step: 'deploy', actor: 'code', status: 'upcoming', phase: null }],
        1,
      ),
    ]);

    // Assert
    for (const boardRow of el.querySelectorAll<HTMLElement>('.board-row')) {
      expect(boardRow.scrollWidth).toBeLessThanOrEqual(boardRow.clientWidth);
    }
    expect(document.documentElement.scrollWidth).toBeLessThanOrEqual(width);
  });

  it('moves nothing on the first render', async () => {
    // Arrange
    await page.viewport(1280, 800);

    // Act
    const el = render();

    // Assert
    const animated = [...el.querySelectorAll('.bar, .bar *')].filter(
      (node) => getComputedStyle(node).animationName !== 'none',
    );
    expect(animated).toEqual([]);
  });
});

import { Component } from '@angular/core';
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

function row(index: number): ItemView<PipelineItem> {
  const issue: IssueSummary = {
    id: `issue-${index}`,
    issueNumber: 1000 + index,
    title: 'A title long enough to fill the title column of the row',
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
    round: 2,
    lifecycle: QUEUED_PATH,
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

@Component({
  selector: 'fd-pipeline-band-layout-host',
  imports: [PipelineBandComponent],
  template: `<div style="padding: 12px"><fd-pipeline-band [items]="items" /></div>`,
})
class HostComponent {
  readonly items: readonly ItemView<PipelineItem>[] = [row(0), row(1)];
}

function render(): HTMLElement {
  const fixture = TestBed.createComponent(HostComponent);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

// Runs in a real browser (test-layout target); jsdom would measure every box as 0.
describe('pipeline band layout', () => {
  // Restores the runner's viewport, so later layout specs measure as before.
  const original = { width: window.innerWidth, height: window.innerHeight };
  afterEach(async () => page.viewport(original.width, original.height));

  it.each([360, 1280])('fits the dots-only rail inside its row at %ipx', async (width) => {
    // Arrange
    await page.viewport(width, 800);

    // Act
    const el = render();

    // Assert
    const boardRow = el.querySelector<HTMLElement>('.board-row')!;
    const rail = el.querySelector<HTMLElement>('.rail')!;
    const railBox = rail.getBoundingClientRect();
    expect(railBox.width).toBeGreaterThan(0);
    expect(railBox.right).toBeLessThanOrEqual(boardRow.getBoundingClientRect().right);
    expect(rail.scrollWidth).toBeLessThanOrEqual(Math.ceil(railBox.width));
    expect(document.documentElement.scrollWidth).toBeLessThanOrEqual(width);
  });

  it('keeps the rail clear of the title and the phrase on a wide row', async () => {
    // Arrange
    await page.viewport(1280, 800);

    // Act
    const el = render();

    // Assert
    const title = el.querySelector('.board-row__title')!.getBoundingClientRect();
    const rail = el.querySelector('.board-row__rail')!.getBoundingClientRect();
    const marker = el.querySelector('.board-row__marker')!.getBoundingClientRect();
    expect(rail.left).toBeGreaterThanOrEqual(title.right);
    expect(marker.left).toBeGreaterThanOrEqual(rail.right);
    expect(el.querySelector('.board-row__marker')?.textContent?.trim()).toBe(
      'Queued for implement · round 2',
    );
  });

  it('never moves the mini rail', async () => {
    // Arrange
    await page.viewport(1280, 800);

    // Act
    const el = render();

    // Assert
    const animated = [...el.querySelectorAll('.rail *')].filter(
      (node) => getComputedStyle(node).animationName !== 'none',
    );
    expect(animated).toEqual([]);
  });
});

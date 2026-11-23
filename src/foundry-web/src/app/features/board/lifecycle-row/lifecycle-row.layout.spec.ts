import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { page } from 'vitest/browser';
import { WorkerSignalRService } from '../../../core/services/worker-signalr.service';
import { IssueSummary } from '../../issues/issue.model';
import { LifecycleStep } from '../../issues/lifecycle.model';
import { ItemView } from '../board-state.service';
import { NeedsItem } from '../board.model';
import { LifecycleRowComponent } from './lifecycle-row';

// Every step the server serves, with the longest labels, on a needs-you issue in round 2.
const FULL_PATH: readonly LifecycleStep[] = [
  { step: 'detected', actor: 'code', status: 'done', phase: null },
  { step: 'triage_queued', actor: 'code', status: 'done', phase: null },
  { step: 'triage', actor: 'claude', status: 'done', phase: null },
  { step: 'queued', actor: 'code', status: 'done', phase: null },
  { step: 'implement', actor: 'claude', status: 'done', phase: null },
  { step: 'publish', actor: 'code', status: 'done', phase: null },
  { step: 'rebase', actor: 'code', status: 'done', phase: null },
  { step: 'checks', actor: 'code', status: 'done', phase: null },
  { step: 'review_queued', actor: 'code', status: 'done', phase: null },
  { step: 'review', actor: 'claude', status: 'done', phase: null },
  { step: 'your_review', actor: 'person', status: 'current', phase: 'needs_you' },
  { step: 'merge', actor: 'code', status: 'skipped', phase: null },
  { step: 'done', actor: 'code', status: 'upcoming', phase: null },
];

function needsItem(): ItemView<NeedsItem> {
  const issue: IssueSummary = {
    id: 'issue-1232',
    issueNumber: 1232,
    title: 'A title long enough that it has to give up width before the row scrolls sideways',
    state: 'review',
    repositorySlug: 'acme/a-repository-name-long-enough-to-crowd-the-head',
    detectedAt: '2026-01-01T00:00:00Z',
    url: 'https://github.com/acme/api/issues/1232',
    stage: 'merge',
    needsAction: true,
    awaitsDispatch: false,
    waitsOnBlockers: false,
    codeStep: null,
    runKind: null,
    repositoryId: 'repo-1',
    enteredStateAt: '2026-01-01T00:00:00Z',
    round: 2,
    pullRequestUrl: 'https://github.com/acme/api/pull/3',
    lifecycle: FULL_PATH,
  };
  return {
    band: 'needs',
    issue,
    stage: 'merge',
    reason: { label: 'PR ready', tone: 'ready' },
    explanation: 'Ready for your review',
    phase: 'live',
    arrived: false,
    moved: false,
  };
}

@Component({
  selector: 'fd-lifecycle-row-layout-host',
  imports: [LifecycleRowComponent],
  // The board page's own padding, so the row is as wide as it is on the page.
  template: `<div style="padding: 24px"><fd-lifecycle-row [item]="item" [now]="0" /></div>`,
})
class HostComponent {
  readonly item = needsItem();
}

function render(): HTMLElement {
  TestBed.configureTestingModule({
    providers: [
      {
        provide: WorkerSignalRService,
        useValue: { activeAgentCount: signal(0), agentActivityForIssue: () => null },
      },
    ],
  });
  const fixture = TestBed.createComponent(HostComponent);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

function visible(el: Element): boolean {
  return getComputedStyle(el).display !== 'none';
}

// Runs in a real browser (test-layout target); jsdom would measure every box as 0.
describe('lifecycle row layout', () => {
  // Restores the runner's viewport, so later layout specs measure as before.
  const original = { width: window.innerWidth, height: window.innerHeight };
  afterEach(async () => page.viewport(original.width, original.height));

  it.each([760, 1280])(
    'fits the labelled rail in the row at %ipx without scrolling',
    async (width) => {
      // Arrange
      await page.viewport(width, 800);

      // Act
      const el = render();

      // Assert
      const row = el.querySelector<HTMLElement>('.lifecycle-row')!;
      const rail = el.querySelector<HTMLElement>('.rail')!;
      const labels = [...el.querySelectorAll('.rail__label')];
      expect(labels).toHaveLength(10);
      expect(labels.every(visible)).toBe(true);
      expect([...el.querySelectorAll('.rail__bracket')].every(visible)).toBe(true);
      expect(rail.getBoundingClientRect().right).toBeLessThanOrEqual(
        row.getBoundingClientRect().right,
      );
      expect(row.scrollWidth).toBeLessThanOrEqual(row.clientWidth);
      expect(document.documentElement.scrollWidth).toBeLessThanOrEqual(width);
    },
  );

  it('keeps every label clear of the next one at 760px', async () => {
    // Arrange
    await page.viewport(760, 800);

    // Act
    const el = render();

    // Assert
    const boxes = [...el.querySelectorAll('.rail__label')].map((l) => l.getBoundingClientRect());
    for (let i = 1; i < boxes.length; i++) {
      expect(boxes[i].left).toBeGreaterThanOrEqual(boxes[i - 1].right - 0.5);
    }
  });

  it('drops labels and brackets on a phone, so the dots fit at 360px', async () => {
    // Arrange
    await page.viewport(360, 800);

    // Act
    const el = render();

    // Assert
    const row = el.querySelector<HTMLElement>('.lifecycle-row')!;
    const rail = el.querySelector<HTMLElement>('.rail')!;
    expect([...el.querySelectorAll('.rail__label, .rail__bracket')].some(visible)).toBe(false);
    expect(rail.getBoundingClientRect().right).toBeLessThanOrEqual(
      row.getBoundingClientRect().right,
    );
    expect(document.documentElement.scrollWidth).toBeLessThanOrEqual(360);
    expect(el.querySelector('.lifecycle-row__step')?.textContent).toContain('Your review');
    expect(visible(el.querySelector('.lifecycle-row__step')!)).toBe(true);
  });
});

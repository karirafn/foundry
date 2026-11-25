import { Component, input } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { page } from 'vitest/browser';
import { IssueSummary } from '../../issues/issue.model';
import { LifecycleStep } from '../../issues/lifecycle.model';
import { ItemView } from '../board-state.service';
import { NeedsItem, buildBoardBands } from '../board.model';
import { NeedsBandComponent } from './needs-band';

const NOW = Date.parse('2026-01-20T00:00:00Z');

const AWAITING_REVIEW: readonly LifecycleStep[] = [
  { step: 'detected', actor: 'code', status: 'done', phase: null },
  { step: 'triage', actor: 'claude', status: 'done', phase: null },
  { step: 'implement', actor: 'claude', status: 'done', phase: null },
  { step: 'publish', actor: 'code', status: 'done', phase: null },
  { step: 'rebase', actor: 'code', status: 'done', phase: null },
  { step: 'checks', actor: 'code', status: 'done', phase: null },
  { step: 'review', actor: 'claude', status: 'skipped', phase: null },
  { step: 'your_review', actor: 'person', status: 'current', phase: 'needs_you' },
  { step: 'merge', actor: 'code', status: 'upcoming', phase: null },
  { step: 'done', actor: 'code', status: 'upcoming', phase: null },
];

function issue(index: number, overrides: Partial<IssueSummary> = {}): IssueSummary {
  return {
    id: `issue-${index}`,
    issueNumber: 1000 + index,
    title: 'A title long enough to fill the title column of the row',
    state: 'review',
    repositorySlug: 'acme/a-repository-name-long-enough-to-fill-its-column',
    detectedAt: '2026-01-01T00:00:00Z',
    url: `https://gitlab.com/acme/api/-/issues/${index}`,
    stage: 'review',
    needsAction: true,
    awaitsDispatch: false,
    waitsOnBlockers: false,
    runKind: null,
    repositoryId: 'repo-1',
    // The widest waiting time the format gives: two-digit days.
    enteredStateAt: '2026-01-01T00:00:00Z',
    round: 1,
    providerType: 'gitlab',
    pullRequestUrl: `https://gitlab.com/acme/api/-/merge_requests/${index}`,
    lifecycle: AWAITING_REVIEW,
    ...overrides,
  };
}

function view(issues: readonly IssueSummary[]): readonly ItemView<NeedsItem>[] {
  return buildBoardBands(issues, [], []).needs.map((item) => ({
    ...item,
    phase: 'live',
    arrived: false,
    moved: false,
  }));
}

const TYPICAL = view([
  issue(0),
  issue(1, { reviewOutcome: 'skipped', reviewSkippedReason: 'verdict_invalid' }),
  issue(2, { title: 'A very long title '.repeat(20), mergeOutcome: 'skipped' }),
]);

@Component({
  selector: 'fd-needs-band-layout-host',
  imports: [NeedsBandComponent],
  template: `<div style="padding: 12px">
    <fd-needs-band [alerts]="[]" [items]="items()" [now]="now" />
  </div>`,
})
class HostComponent {
  readonly items = input<readonly ItemView<NeedsItem>[]>(TYPICAL);
  readonly now = NOW;
}

function render(): HTMLElement {
  const fixture = TestBed.createComponent(HostComponent);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

function rows(el: HTMLElement): HTMLElement[] {
  return [...el.querySelectorAll<HTMLElement>('.needs-band__review-rows .board-row')];
}

// Runs in a real browser (test-layout target); jsdom would measure every box as 0.
describe('needs band review rows layout', () => {
  const original = { width: window.innerWidth, height: window.innerHeight };
  afterEach(async () => page.viewport(original.width, original.height));

  it('puts the review link above the row-wide title target', async () => {
    // Arrange
    await page.viewport(1280, 800);
    const el = render();

    // Act
    const link = el.querySelector<HTMLElement>('.needs-band__link')!;
    const box = link.getBoundingClientRect();
    const hit = document.elementFromPoint(box.left + box.width / 2, box.top + box.height / 2);

    // Assert
    expect(link.contains(hit)).toBe(true);
  });

  it('lines the bars up and fits the whole marker on a wide board', async () => {
    // Arrange
    await page.viewport(1280, 800);

    // Act
    const el = render();
    await document.fonts.load('0.75rem Inter');

    // Assert
    const bars = [...el.querySelectorAll('.board-row__bar')].map((b) => b.getBoundingClientRect());
    expect(bars[0].width).toBeGreaterThan(0);
    expect(bars.every((b) => Math.abs(b.left - bars[0].left) <= 1)).toBe(true);
    const clock = el.querySelector<HTMLElement>('.needs-band__clock')!;
    expect(clock.textContent).toBe('waiting 19d');
    expect(clock.scrollWidth).toBeLessThanOrEqual(clock.clientWidth);
  });

  it('rules each row in its reason tone', async () => {
    // Arrange
    await page.viewport(1280, 800);

    // Act
    const [ready, skipped] = rows(render());

    // Assert
    expect(getComputedStyle(ready).borderInlineStartWidth).toBe('3px');
    expect(getComputedStyle(ready).borderInlineStartColor).toBe(
      getComputedStyle(ready.querySelector('.needs-band__reason')!).color,
    );
    expect(getComputedStyle(skipped).borderInlineStartColor).toBe(
      getComputedStyle(skipped.querySelector('.needs-band__reason')!).color,
    );
  });

  it('ellipsises the waiting time before it covers the issue number on a narrow board', async () => {
    // Arrange
    await page.viewport(360, 800);

    // Act
    const el = render();

    // Assert
    for (const row of rows(el)) {
      const number = row.querySelector('.issue-ref__number')!.getBoundingClientRect();
      const marker = row.querySelector('.needs-band__marker')!.getBoundingClientRect();
      expect(marker.left).toBeGreaterThanOrEqual(number.right);
    }
  });

  it.each([360, 760, 1280])('overflows nothing and keeps the link whole at %ipx', async (width) => {
    // Arrange
    await page.viewport(width, 800);

    // Act
    const el = render();

    // Assert
    const band = el.querySelector<HTMLElement>('fd-needs-band')!;
    expect(band.scrollWidth).toBeLessThanOrEqual(band.clientWidth);
    for (const row of rows(el)) {
      const bounds = row.getBoundingClientRect();
      const link = row.querySelector('.needs-band__link')!.getBoundingClientRect();
      expect(link.width).toBeGreaterThan(0);
      expect(link.right).toBeLessThanOrEqual(bounds.right);
    }
  });
});

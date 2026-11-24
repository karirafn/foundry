import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { page } from 'vitest/browser';
import { LifecycleStatus, LifecycleStep } from '../../../features/issues/lifecycle.model';
import { LifecycleRailComponent } from './lifecycle-rail';

function path(changes: Readonly<Record<string, Partial<LifecycleStep>>>): LifecycleStep[] {
  const base: readonly [string, LifecycleStep['actor']][] = [
    ['detected', 'code'],
    ['triage_queued', 'code'],
    ['triage', 'claude'],
    ['queued', 'code'],
    ['implement', 'claude'],
    ['publish', 'code'],
    ['rebase', 'code'],
    ['checks', 'code'],
    ['review_queued', 'code'],
    ['review', 'claude'],
    ['your_review', 'person'],
    ['merge', 'code'],
    ['done', 'code'],
  ];
  return base.map(([step, actor]) => ({
    step,
    actor,
    status: 'upcoming' as LifecycleStatus,
    phase: null,
    ...changes[step],
  }));
}

const DONE = { status: 'done' } as const;

// Queued for implement in round 2: the wait is current.
const QUEUED = path({
  detected: DONE,
  triage_queued: DONE,
  triage: DONE,
  queued: { status: 'current', phase: 'waiting' },
});

// Waiting for a person's review, with the Review step off for the repository.
const REVIEW_OFF = path({
  detected: DONE,
  triage_queued: DONE,
  triage: DONE,
  queued: DONE,
  implement: DONE,
  publish: DONE,
  rebase: DONE,
  checks: DONE,
  review_queued: { status: 'off' },
  review: { status: 'off' },
  your_review: { status: 'current', phase: 'needs_you' },
  merge: { status: 'skipped' },
});

@Component({
  selector: 'fd-lifecycle-rail-layout-host',
  imports: [LifecycleRailComponent],
  // The board page's padding, and the running row the board marks while a step runs.
  template: `<div style="padding: 24px" class="lifecycle-row--running">
    <fd-lifecycle-rail class="queued" [lifecycle]="queued" [round]="2" />
    <fd-lifecycle-rail class="review-off" [lifecycle]="reviewOff" />
    <fd-lifecycle-rail class="vertical" orientation="vertical" [lifecycle]="queued" />
  </div>`,
})
class HostComponent {
  readonly queued = QUEUED;
  readonly reviewOff = REVIEW_OFF;
}

function render(): HTMLElement {
  const fixture = TestBed.createComponent(HostComponent);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

function centreX(el: Element): number {
  const box = el.getBoundingClientRect();
  return box.left + box.width / 2;
}

function nodes(rail: Element): Element[] {
  return [...rail.querySelectorAll('.rail__node')];
}

// Runs in a real browser (test-layout target); jsdom would measure every box as 0.
describe('lifecycle rail layout', () => {
  // Restores the runner's viewport, so later layout specs measure as before.
  const original = { width: window.innerWidth, height: window.innerHeight };
  afterEach(async () => page.viewport(original.width, original.height));

  it.each([760, 1280])(
    'centres each node on the line and its label under it at %ipx',
    async (width) => {
      // Arrange
      await page.viewport(width, 800);

      // Act
      const el = render();

      // Assert
      const steps = [...el.querySelectorAll('.queued .rail__step')];
      expect(steps).toHaveLength(10);
      for (const step of steps.slice(1)) {
        const node = step.querySelector('.rail__node')!.getBoundingClientRect();
        const line = getComputedStyle(step, '::before');
        const lineCentre =
          step.getBoundingClientRect().top + parseFloat(line.top) + parseFloat(line.height) / 2;
        expect(Math.abs(node.top + node.height / 2 - lineCentre)).toBeLessThanOrEqual(1);
      }
      for (const step of steps) {
        const label = step.querySelector('.rail__label')!;
        expect(
          Math.abs(centreX(label) - centreX(step.querySelector('.rail__node')!)),
        ).toBeLessThanOrEqual(1);
      }
    },
  );

  it.each([760, 1280])(
    'puts matching nodes of rails at different steps at the same x at %ipx',
    async (width) => {
      // Arrange
      await page.viewport(width, 800);

      // Act
      const el = render();

      // Assert
      const queued = nodes(el.querySelector('.queued')!).map(centreX);
      const reviewOff = nodes(el.querySelector('.review-off')!).map(centreX);
      expect(queued).toHaveLength(10);
      expect(reviewOff).toHaveLength(10);
      queued.forEach((x, i) => expect(Math.abs(x - reviewOff[i])).toBeLessThanOrEqual(1));
    },
  );

  it('runs the connector from the previous node to this one', async () => {
    // Arrange
    await page.viewport(1280, 800);

    // Act
    const el = render();

    // Assert
    const [first, second] = [...el.querySelectorAll('.queued .rail__step')];
    const line = getComputedStyle(second, '::before');
    const left = second.getBoundingClientRect().left + parseFloat(line.left);
    expect(Math.abs(left - centreX(first.querySelector('.rail__node')!))).toBeLessThanOrEqual(1);
    expect(
      Math.abs(left + parseFloat(line.width) - centreX(second.querySelector('.rail__node')!)),
    ).toBeLessThanOrEqual(1);
  });

  it('keeps the queued ring still inside a running row', async () => {
    // Act
    const el = render();

    // Assert
    const rings = [...el.querySelectorAll('[data-ring="queued"]')];
    expect(rings).toHaveLength(2);
    expect(rings.map((r) => getComputedStyle(r, '::after').animationName)).toEqual([
      'none',
      'none',
    ]);
    expect(rings.map((r) => getComputedStyle(r, '::after').borderTopStyle)).toEqual([
      'dashed',
      'dashed',
    ]);
  });

  it('half-fills the vertical line into the queued node', async () => {
    // Act
    const el = render();

    // Assert
    const above = el.querySelector('.vertical li[data-line="half"]')!;
    expect(above.nextElementSibling?.getAttribute('aria-current')).toBe('step');
    expect(getComputedStyle(above, '::after').transform).toBe('matrix(1, 0, 0, 0.5, 0, 0)');
  });
});

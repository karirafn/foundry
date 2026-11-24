import { TestBed } from '@angular/core/testing';
import { LifecycleStep } from '../../../features/issues/lifecycle.model';
import { LifecycleBarComponent } from './lifecycle-bar';

const IMPLEMENTING: readonly LifecycleStep[] = [
  { step: 'triage', actor: 'claude', status: 'done', phase: null },
  { step: 'implement', actor: 'claude', status: 'current', phase: 'running' },
  { step: 'publish', actor: 'code', status: 'upcoming', phase: null },
];

const PUBLISHING: readonly LifecycleStep[] = [
  IMPLEMENTING[0],
  { step: 'implement', actor: 'claude', status: 'done', phase: null },
  { step: 'publish', actor: 'code', status: 'current', phase: 'running' },
];

const FAILED: readonly LifecycleStep[] = [
  IMPLEMENTING[0],
  { step: 'implement', actor: 'claude', status: 'current', phase: 'failed' },
  IMPLEMENTING[2],
];

function render(lifecycle: readonly LifecycleStep[]) {
  const fixture = TestBed.createComponent(LifecycleBarComponent);
  fixture.componentRef.setInput('lifecycle', lifecycle);
  fixture.detectChanges();
  const segments = (): HTMLElement[] => [
    ...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('.bar__segment'),
  ];
  return { fixture, segments };
}

// Runs in a real browser (test-layout target); jsdom computes no animations.
describe('lifecycle bar motion', () => {
  it('draws the current segment taller than the others', () => {
    // Act
    const { segments } = render(IMPLEMENTING);

    // Assert
    const [done, current, upcoming] = segments().map((s) => s.getBoundingClientRect().height);
    expect(current).toBeGreaterThan(done);
    expect(upcoming).toBe(done);
  });

  it('fills the segment that became done and settles the new current one, each once', () => {
    // Arrange
    const { fixture, segments } = render(IMPLEMENTING);

    // Act
    fixture.componentRef.setInput('lifecycle', PUBLISHING);
    fixture.detectChanges();

    // Assert
    const [, implement, publish] = segments();
    const fill = getComputedStyle(implement, '::after');
    const settle = getComputedStyle(publish);
    expect([fill.animationName, fill.animationIterationCount]).toEqual([
      'fd-task-segment-fill',
      '1',
    ]);
    expect([settle.animationName, settle.animationIterationCount]).toEqual(['fd-rail-settle', '1']);
  });

  it('shakes a segment that just failed, once', () => {
    // Arrange
    const { fixture, segments } = render(IMPLEMENTING);

    // Act
    fixture.componentRef.setInput('lifecycle', FAILED);
    fixture.detectChanges();

    // Assert
    const shake = getComputedStyle(segments()[1]);
    expect([shake.animationName, shake.animationIterationCount]).toEqual(['fd-rail-shake', '1']);
  });
});

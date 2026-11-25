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

const NEEDS_YOU: readonly LifecycleStep[] = [
  { step: 'checks', actor: 'code', status: 'done', phase: null },
  { step: 'your_review', actor: 'person', status: 'current', phase: 'needs_you' },
  { step: 'merge', actor: 'code', status: 'upcoming', phase: null },
];

describe('lifecycle bar arrival beacon', () => {
  function beacon(arriving: boolean): CSSStyleDeclaration {
    const fixture = TestBed.createComponent(LifecycleBarComponent);
    fixture.componentRef.setInput('lifecycle', NEEDS_YOU);
    fixture.componentRef.setInput('arriving', arriving);
    fixture.detectChanges();
    const current = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>(
      ".bar__segment[data-status='current']",
    )!;
    return getComputedStyle(current, '::before');
  }

  it('pulses the current segment three times when the issue just arrived in Needs you', () => {
    // Act
    const pulse = beacon(true);

    // Assert
    expect([pulse.animationName, pulse.animationIterationCount]).toEqual(['fd-rail-beacon', '3']);
  });

  it('plays nothing when the issue did not just arrive', () => {
    // Act
    const pulse = beacon(false);

    // Assert
    expect(pulse.animationName).toBe('none');
  });

  it('stops the beacon under reduced motion', () => {
    // Arrange
    beacon(true);

    // Act
    const reduced = [...document.styleSheets]
      .flatMap((sheet) => [...sheet.cssRules])
      .filter(
        (rule): rule is CSSMediaRule =>
          rule instanceof CSSMediaRule && rule.conditionText.includes('prefers-reduced-motion'),
      )
      .flatMap((rule) => [...rule.cssRules])
      .filter((rule): rule is CSSStyleRule => rule instanceof CSSStyleRule)
      .filter(
        (rule) => rule.selectorText.includes('.bar') && rule.selectorText.includes('::before'),
      );

    // Assert
    expect(reduced.map((rule) => rule.style.animationName)).toContain('none');
  });
});

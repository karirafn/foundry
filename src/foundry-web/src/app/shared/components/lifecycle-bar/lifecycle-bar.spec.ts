import { ComponentFixture, TestBed } from '@angular/core/testing';
import { LifecycleStep } from '../../../features/issues/lifecycle.model';
import { LifecycleBarComponent } from './lifecycle-bar';

const IMPLEMENTING: readonly LifecycleStep[] = [
  { step: 'triage', actor: 'claude', status: 'done', phase: null },
  { step: 'queued', actor: 'code', status: 'done', phase: null },
  { step: 'implement', actor: 'claude', status: 'current', phase: 'running' },
  { step: 'publish', actor: 'code', status: 'upcoming', phase: null },
  { step: 'review', actor: 'claude', status: 'off', phase: null },
];

const PUBLISHING: readonly LifecycleStep[] = [
  IMPLEMENTING[0],
  IMPLEMENTING[1],
  { step: 'implement', actor: 'claude', status: 'done', phase: null },
  { step: 'publish', actor: 'code', status: 'current', phase: 'running' },
  IMPLEMENTING[4],
];

const QUEUED: readonly LifecycleStep[] = [
  IMPLEMENTING[0],
  { step: 'queued', actor: 'code', status: 'current', phase: 'waiting' },
  { step: 'implement', actor: 'claude', status: 'upcoming', phase: null },
  IMPLEMENTING[3],
  IMPLEMENTING[4],
];

function setup(lifecycle: readonly LifecycleStep[]): ComponentFixture<LifecycleBarComponent> {
  const fixture = TestBed.createComponent(LifecycleBarComponent);
  fixture.componentRef.setInput('lifecycle', lifecycle);
  fixture.detectChanges();
  return fixture;
}

function segments(fixture: ComponentFixture<LifecycleBarComponent>): HTMLElement[] {
  return Array.from(fixture.nativeElement.querySelectorAll('.bar__segment'));
}

describe('LifecycleBarComponent', () => {
  it('draws one segment per node, each with its status', () => {
    // Act
    const fixture = setup(IMPLEMENTING);

    // Assert
    expect(segments(fixture).map((s) => s.getAttribute('data-status'))).toEqual([
      'done',
      'current',
      'upcoming',
      'off',
    ]);
  });

  it('is hidden from assistive tech', () => {
    // Act
    const fixture = setup(IMPLEMENTING);

    // Assert
    expect(fixture.nativeElement.getAttribute('aria-hidden')).toBe('true');
  });

  it("colours the current segment in its tone, with the step's phase", () => {
    // Act
    const current = segments(setup(IMPLEMENTING))[1];

    // Assert
    expect(current.getAttribute('data-phase')).toBe('running');
    expect(current.style.getPropertyValue('--tone')).toBe('var(--fd-accent-working)');
  });

  it('marks the segment after a current queue wait as queued', () => {
    // Act
    const fixture = setup(QUEUED);

    // Assert
    const queued = segments(fixture).filter((s) => s.hasAttribute('data-queued'));
    expect(queued).toHaveLength(1);
    expect(queued[0].getAttribute('data-status')).toBe('upcoming');
    expect(segments(fixture).indexOf(queued[0])).toBe(1);
  });

  it('moves nothing on the first render', () => {
    // Act
    const fixture = setup(IMPLEMENTING);

    // Assert
    expect(fixture.nativeElement.querySelectorAll('[data-motion]')).toHaveLength(0);
  });

  it('fills the segment that became done and settles the new current one', () => {
    // Arrange
    const fixture = setup(IMPLEMENTING);

    // Act
    fixture.componentRef.setInput('lifecycle', PUBLISHING);
    fixture.detectChanges();

    // Assert
    expect(segments(fixture).map((s) => s.getAttribute('data-motion'))).toEqual([
      null,
      'fill',
      'settle',
      null,
    ]);
  });

  it('marks the bar arriving when the issue just arrived', () => {
    // Arrange
    const fixture = setup(IMPLEMENTING);

    // Act
    fixture.componentRef.setInput('arriving', true);
    fixture.detectChanges();

    // Assert
    expect(fixture.nativeElement.querySelector('.bar').classList).toContain('bar--arriving');
  });
});

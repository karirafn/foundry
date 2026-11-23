import { ComponentFixture, TestBed } from '@angular/core/testing';
import { LifecycleStep } from '../../../features/issues/lifecycle.model';
import { LifecycleRailComponent } from './lifecycle-rail';

const IMPLEMENTING: readonly LifecycleStep[] = [
  { step: 'triage', actor: 'claude', status: 'done', phase: null },
  { step: 'queued', actor: 'code', status: 'done', phase: null },
  { step: 'implement', actor: 'claude', status: 'current', phase: 'running' },
  { step: 'publish', actor: 'code', status: 'upcoming', phase: null },
];

const PUBLISHING: readonly LifecycleStep[] = [
  IMPLEMENTING[0],
  IMPLEMENTING[1],
  { step: 'implement', actor: 'claude', status: 'done', phase: null },
  { step: 'publish', actor: 'code', status: 'current', phase: 'running' },
];

function setup(
  lifecycle: readonly LifecycleStep[],
  inputs: Partial<{ round: number; density: 'full' | 'mini' }> = {},
): ComponentFixture<LifecycleRailComponent> {
  const fixture = TestBed.createComponent(LifecycleRailComponent);
  fixture.componentRef.setInput('lifecycle', lifecycle);
  for (const [name, value] of Object.entries(inputs)) {
    fixture.componentRef.setInput(name, value);
  }
  fixture.detectChanges();
  return fixture;
}

function steps(fixture: ComponentFixture<LifecycleRailComponent>): HTMLElement[] {
  return Array.from(fixture.nativeElement.querySelectorAll('li'));
}

describe('LifecycleRailComponent', () => {
  it('lists every step in order under a Lifecycle label', () => {
    // Act
    const fixture = setup(IMPLEMENTING);

    // Assert
    expect(fixture.nativeElement.querySelector('ol').getAttribute('aria-label')).toBe('Lifecycle');
    expect(steps(fixture).map((li) => li.getAttribute('aria-label'))).toEqual([
      'Triage, by Claude, done',
      'Queued for implement, by Foundry code, done',
      'Implement, by Claude, current, running',
      'Publish, by Foundry code, upcoming',
    ]);
  });

  it('marks the current step', () => {
    // Act
    const fixture = setup(IMPLEMENTING);

    // Assert
    expect(steps(fixture).map((li) => li.getAttribute('aria-current'))).toEqual([
      null,
      null,
      'step',
      null,
    ]);
  });

  it('labels nodes but not waits', () => {
    // Act
    const fixture = setup(IMPLEMENTING);

    // Assert
    const labels = Array.from(
      fixture.nativeElement.querySelectorAll('.rail__label'),
    ) as HTMLElement[];
    expect(labels.map((l) => l.textContent?.trim())).toEqual(['Triage', 'Implement', 'Publish']);
  });

  it('shows the round on the Implement node', () => {
    // Act
    const fixture = setup(IMPLEMENTING, { round: 3 });

    // Assert
    expect(fixture.nativeElement.querySelector('.rail__badge')?.textContent).toBe('×3');
  });

  it('draws the mini rail with no labels or brackets, hidden from assistive tech', () => {
    // Act
    const fixture = setup(IMPLEMENTING, { density: 'mini' });

    // Assert
    const rail = fixture.nativeElement.querySelector('.rail') as HTMLElement;
    expect(rail.getAttribute('aria-hidden')).toBe('true');
    expect(rail.querySelectorAll('.rail__label, .rail__bracket').length).toBe(0);
  });

  it('moves nothing on the first render', () => {
    // Act
    const fixture = setup(IMPLEMENTING);

    // Assert
    expect(fixture.nativeElement.querySelectorAll('[data-motion]').length).toBe(0);
  });

  it('pops the new current step once the lifecycle moves on', () => {
    // Arrange
    const fixture = setup(IMPLEMENTING);

    // Act
    fixture.componentRef.setInput('lifecycle', PUBLISHING);
    fixture.detectChanges();

    // Assert
    const publish = steps(fixture)[3].querySelector('.rail__node');
    expect(publish?.getAttribute('data-motion')).toBe('pop');
  });

  it('never moves the mini rail', () => {
    // Arrange
    const fixture = setup(IMPLEMENTING, { density: 'mini' });

    // Act
    fixture.componentRef.setInput('lifecycle', PUBLISHING);
    fixture.detectChanges();

    // Assert
    expect(
      fixture.nativeElement.querySelectorAll('[data-motion], .rail__connector--grow').length,
    ).toBe(0);
  });
});

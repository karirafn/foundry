import { Component, TemplateRef, viewChild } from '@angular/core';
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
  inputs: Partial<{
    round: number;
    density: 'full' | 'mini';
    orientation: 'horizontal' | 'vertical';
    enteredAt: string | null;
    now: number;
    currentStepExtra: TemplateRef<void> | null;
  }> = {},
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

const ENTERED = '2026-11-24T10:00:00Z';
const TWELVE_MINUTES_LATER = Date.parse(ENTERED) + 12 * 60_000;

const WAITING: readonly LifecycleStep[] = [
  IMPLEMENTING[0],
  { step: 'queued', actor: 'code', status: 'current', phase: 'waiting' },
  { step: 'implement', actor: 'claude', status: 'upcoming', phase: null },
  { step: 'your_review', actor: 'person', status: 'off', phase: null },
];

function text(node: Element | null | undefined): string {
  return (node?.textContent ?? '').replace(/\s+/g, ' ').trim();
}

@Component({
  imports: [LifecycleRailComponent],
  template: `<ng-template #extra><p class="extra">Tasks here</p></ng-template>
    <fd-lifecycle-rail
      orientation="vertical"
      [lifecycle]="lifecycle"
      [currentStepExtra]="extra"
    />`,
})
class ExtraHostComponent {
  lifecycle: readonly LifecycleStep[] = IMPLEMENTING;
  readonly extra = viewChild.required<TemplateRef<void>>('extra');
}

describe('LifecycleRailComponent vertical', () => {
  function vertical(
    lifecycle: readonly LifecycleStep[],
    inputs: Parameters<typeof setup>[1] = {},
  ): HTMLElement {
    return setup(lifecycle, {
      orientation: 'vertical',
      enteredAt: ENTERED,
      now: TWELVE_MINUTES_LATER,
      ...inputs,
    }).nativeElement;
  }

  it('lists the steps under their stages, each stage a labelled list', () => {
    // Act
    const el = vertical(IMPLEMENTING);

    // Assert
    const root = el.querySelector('ol[aria-label="Lifecycle"]')!;
    const groups = Array.from(root.querySelectorAll(':scope > li.rail__group'));
    expect(groups.map((g) => text(g.querySelector('.rail__stage')))).toEqual([
      'Triage',
      'Implement',
    ]);
    expect(groups.map((g) => g.querySelector('ol')?.getAttribute('aria-label'))).toEqual([
      'Triage steps',
      'Implement steps',
    ]);
  });

  it('names each step, its actor and status for a screen reader, the current one marked', () => {
    // Act
    const el = vertical(IMPLEMENTING);

    // Assert
    const items = Array.from(el.querySelectorAll('li.rail__step'));
    expect(items.map((li) => li.getAttribute('aria-label'))).toEqual([
      'Triage, by Claude, done',
      'Queued for implement, by Foundry code, done',
      'Implement, by Claude, current, running',
      'Publish, by Foundry code, upcoming',
    ]);
    expect(items.map((li) => li.getAttribute('aria-current'))).toEqual([null, null, 'step', null]);
  });

  it("shows each step's label, actor and status, with the elapsed time on the current step", () => {
    // Act
    const el = vertical(IMPLEMENTING);

    // Assert
    const nodes = Array.from(el.querySelectorAll('li.rail__step:not(.rail__step--wait)'));
    expect(
      nodes.map((li) => [
        text(li.querySelector('.rail__label')),
        text(li.querySelector('.rail__word')),
        text(li.querySelector('.rail__status')),
      ]),
    ).toEqual([
      ['Triage', 'Claude', 'Done'],
      ['Implement', 'Claude', 'Running · 12m'],
      ['Publish', 'Foundry code', 'Upcoming'],
    ]);
  });

  it('labels a wait only while it is current, with its elapsed time', () => {
    // Act
    const el = vertical(WAITING);

    // Assert
    const wait = el.querySelector('li.rail__step--wait')!;
    expect(text(wait.querySelector('.rail__label'))).toBe('Queued for implement · 12m');
    expect(wait.querySelector('.rail__label')?.classList.contains('sr-only')).toBe(false);
  });

  it('keeps a wait that is not current to screen readers only', () => {
    // Act
    const el = vertical(IMPLEMENTING);

    // Assert
    const wait = el.querySelector('li.rail__step--wait')!;
    expect(wait.querySelector('.rail__label')?.classList.contains('sr-only')).toBe(true);
  });

  it('words a step that is off for the repository', () => {
    // Act
    const el = vertical(WAITING);

    // Assert
    const off = el.querySelector('li.rail__step[data-status="off"]');
    expect(text(off?.querySelector('.rail__status'))).toBe('Off for this repository');
  });

  it('shows the status without a time when the step start is unknown', () => {
    // Act
    const el = vertical(IMPLEMENTING, { enteredAt: null });

    // Assert
    const current = el.querySelector('li[aria-current="step"]');
    expect(text(current?.querySelector('.rail__status'))).toBe('Running');
  });

  it('shows the round in the Implement label and badge', () => {
    // Act
    const el = vertical(IMPLEMENTING, { round: 2 });

    // Assert
    const current = el.querySelector('li[aria-current="step"]')!;
    expect(current.getAttribute('aria-label')).toBe('Implement, ×2, by Claude, current, running');
    expect(text(current.querySelector('.rail__badge'))).toBe('×2');
  });

  it("renders the extra template inside the current step's item only", () => {
    // Arrange
    const fixture = TestBed.createComponent(ExtraHostComponent);

    // Act
    fixture.detectChanges();

    // Assert
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelectorAll('.extra').length).toBe(1);
    expect(el.querySelector('.extra')?.closest('li.rail__step')?.getAttribute('aria-current')).toBe(
      'step',
    );
  });

  it('draws no horizontal connectors or brackets', () => {
    // Act
    const el = vertical(IMPLEMENTING);

    // Assert
    expect(el.querySelectorAll('.rail__connector, .rail__bracket').length).toBe(0);
  });

  it('grows the line into a new current step once, and never on the first render', () => {
    // Arrange
    const fixture = setup(IMPLEMENTING, { orientation: 'vertical' });
    const first = fixture.nativeElement.querySelectorAll('.rail__step--grow').length;

    // Act
    fixture.componentRef.setInput('lifecycle', PUBLISHING);
    fixture.detectChanges();

    // Assert
    expect(first).toBe(0);
    const grown = Array.from(
      fixture.nativeElement.querySelectorAll('.rail__step--grow') as NodeListOf<HTMLElement>,
    );
    expect(grown.map((li) => li.getAttribute('aria-label'))).toEqual([
      'Implement, by Claude, done',
    ]);
  });
});

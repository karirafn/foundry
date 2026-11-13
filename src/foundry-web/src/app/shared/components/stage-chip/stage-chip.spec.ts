import { TestBed } from '@angular/core/testing';
import { StageChipComponent } from './stage-chip';

function render(label: string, step: number, steps: number): HTMLElement {
  const fixture = TestBed.createComponent(StageChipComponent);
  fixture.componentRef.setInput('label', label);
  fixture.componentRef.setInput('step', step);
  fixture.componentRef.setInput('steps', steps);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('StageChipComponent', () => {
  it('reads the stage to screen readers', () => {
    // Act
    const el = render('Implement', 2, 3);

    // Assert
    expect(el.textContent?.replace(/\s+/g, ' ').trim()).toBe('Stage: Implement');
  });

  it('marks the stage position with one hidden pip per step', () => {
    // Act
    const el = render('Implement', 2, 3);

    // Assert
    expect(el.querySelector('.stage-chip__pips')?.getAttribute('aria-hidden')).toBe('true');
    expect(
      [...el.querySelectorAll('.stage-chip__pip')].map((p) => p.getAttribute('data-pip')),
    ).toEqual(['past', 'current', 'next']);
  });
});

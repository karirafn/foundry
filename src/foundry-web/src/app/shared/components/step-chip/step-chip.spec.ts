import { TestBed } from '@angular/core/testing';
import { StepChipComponent } from './step-chip';

function render(label: string, tone?: 'working' | 'warning'): HTMLElement {
  const fixture = TestBed.createComponent(StepChipComponent);
  fixture.componentRef.setInput('label', label);
  if (tone !== undefined) {
    fixture.componentRef.setInput('tone', tone);
  }
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('StepChipComponent', () => {
  it('shows its label', () => {
    expect(render('Rebasing…').textContent?.trim()).toBe('Rebasing…');
  });

  it('uses the working tone by default', () => {
    expect(render('Rebasing…').querySelector('.step-chip--warning')).toBeNull();
  });

  it('uses the warning tone when asked', () => {
    expect(
      render('Retrying publish…', 'warning').querySelector('.step-chip--warning'),
    ).not.toBeNull();
  });

  it('hides its decorative dot from assistive technology', () => {
    expect(
      render('Checks running…').querySelector('.step-chip__dot')?.getAttribute('aria-hidden'),
    ).toBe('true');
  });
});

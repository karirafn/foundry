import { TestBed } from '@angular/core/testing';
import { RepositoryEligibilityComponent } from './repository-eligibility';

function setup(overrides: {
  status?: 'eligible' | 'ineligible' | 'unreachable';
  recheckPending?: boolean;
} = {}) {
  const fixture = TestBed.createComponent(RepositoryEligibilityComponent);
  fixture.componentRef.setInput('status', overrides.status ?? 'eligible');
  fixture.componentRef.setInput('recheckPending', overrides.recheckPending ?? false);
  fixture.detectChanges();
  return { fixture, component: fixture.componentInstance, el: fixture.nativeElement as HTMLElement };
}

describe('RepositoryEligibilityComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [RepositoryEligibilityComponent],
    }).compileComponents();
  });

  // Cycle 1: eligible status renders green indicator
  it('should render the eligible status with green indicator class', () => {
    // Arrange

    // Act
    const { el } = setup({ status: 'eligible' });

    // Assert
    const indicator = el.querySelector('.repository-eligibility__indicator');
    expect(indicator).toBeTruthy();
    expect(indicator?.classList.contains('repository-eligibility__indicator--eligible')).toBe(true);
  });

  it('should display "Eligible" text for eligible status', () => {
    // Arrange

    // Act
    const { el } = setup({ status: 'eligible' });

    // Assert
    const label = el.querySelector('.repository-eligibility__label');
    expect(label?.textContent?.trim()).toBe('Eligible');
  });

  // Cycle 2: ineligible status renders amber indicator
  it('should render the ineligible status with amber indicator class', () => {
    // Arrange

    // Act
    const { el } = setup({ status: 'ineligible' });

    // Assert
    const indicator = el.querySelector('.repository-eligibility__indicator');
    expect(indicator?.classList.contains('repository-eligibility__indicator--ineligible')).toBe(true);
  });

  it('should display "Ineligible" text for ineligible status', () => {
    // Arrange

    // Act
    const { el } = setup({ status: 'ineligible' });

    // Assert
    const label = el.querySelector('.repository-eligibility__label');
    expect(label?.textContent?.trim()).toContain('Ineligible');
  });

  it('should not render violations list in the chip component', () => {
    // Arrange

    // Act
    const { el } = setup({ status: 'ineligible' });

    // Assert — violations are in the details panel, not the chip
    const violationsList = el.querySelector('.repository-eligibility__violations');
    expect(violationsList).toBeFalsy();
  });

  // Cycle 3: unreachable status renders grey/muted indicator
  it('should render the unreachable status with muted indicator class', () => {
    // Arrange

    // Act
    const { el } = setup({ status: 'unreachable' });

    // Assert
    const indicator = el.querySelector('.repository-eligibility__indicator');
    expect(indicator?.classList.contains('repository-eligibility__indicator--unreachable')).toBe(true);
  });

  it('should display "Unable to verify" text for unreachable status', () => {
    // Arrange

    // Act
    const { el } = setup({ status: 'unreachable' });

    // Assert
    const label = el.querySelector('.repository-eligibility__label');
    expect(label?.textContent?.trim()).toBe('Unable to verify branch protection');
  });

  it('should not render violations list for unreachable status', () => {
    // Arrange

    // Act
    const { el } = setup({ status: 'unreachable' });

    // Assert
    const violationsList = el.querySelector('.repository-eligibility__violations');
    expect(violationsList).toBeFalsy();
  });

  it('should display "Re-checking..." as the status label when recheckPending is true', () => {
    // Arrange

    // Act
    const { el } = setup({ status: 'unreachable', recheckPending: true });

    // Assert
    const label = el.querySelector('.repository-eligibility__label');
    expect(label?.textContent?.trim()).toBe('Re-checking...');
  });
});

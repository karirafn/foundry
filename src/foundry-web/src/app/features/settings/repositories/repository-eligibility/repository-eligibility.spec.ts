import { TestBed } from '@angular/core/testing';
import { RepositoryEligibilityComponent } from './repository-eligibility';
import { EligibilityViolation } from '../repository.model';

const VIOLATIONS: EligibilityViolation[] = [
  { rule: 'AllowDirectPushes', description: 'Allow direct pushes is enabled' },
  { rule: 'NoReviewRequired', description: 'No pull request reviews are required' },
];

function setup(overrides: {
  status?: 'eligible' | 'ineligible' | 'unreachable';
  violations?: EligibilityViolation[];
  recheckPending?: boolean;
} = {}) {
  const fixture = TestBed.createComponent(RepositoryEligibilityComponent);
  fixture.componentRef.setInput('status', overrides.status ?? 'eligible');
  fixture.componentRef.setInput('violations', overrides.violations ?? []);
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

  // Cycle 2: ineligible status renders amber indicator with violations
  it('should render the ineligible status with amber indicator class', () => {
    // Arrange

    // Act
    const { el } = setup({ status: 'ineligible', violations: VIOLATIONS });

    // Assert
    const indicator = el.querySelector('.repository-eligibility__indicator');
    expect(indicator?.classList.contains('repository-eligibility__indicator--ineligible')).toBe(true);
  });

  it('should display "Ineligible" text for ineligible status', () => {
    // Arrange

    // Act
    const { el } = setup({ status: 'ineligible', violations: VIOLATIONS });

    // Assert
    const label = el.querySelector('.repository-eligibility__label');
    expect(label?.textContent?.trim()).toContain('Ineligible');
  });

  it('should render violations list when status is ineligible and violations are present', () => {
    // Arrange

    // Act
    const { el } = setup({ status: 'ineligible', violations: VIOLATIONS });

    // Assert
    const violationItems = el.querySelectorAll('.repository-eligibility__violation');
    expect(violationItems.length).toBe(2);
    expect(violationItems[0].textContent).toContain('Allow direct pushes is enabled');
    expect(violationItems[1].textContent).toContain('No pull request reviews are required');
  });

  it('should not render violations list when status is eligible', () => {
    // Arrange

    // Act
    const { el } = setup({ status: 'eligible' });

    // Assert
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

  // Cycle 4: aria-live region for re-check result
  it('should have aria-live="off" on initial passive render to avoid burst announcements', () => {
    // Arrange

    // Act
    const { el } = setup({ status: 'eligible', recheckPending: false });

    // Assert — live region exists but is silent until a recheck is in flight
    const liveRegion = el.querySelector('.sr-only[aria-live]');
    expect(liveRegion).toBeTruthy();
    expect(liveRegion?.getAttribute('aria-live')).toBe('off');
  });

  it('should activate aria-live="polite" when recheckPending is true', () => {
    // Arrange

    // Act
    const { el } = setup({ status: 'ineligible', recheckPending: true });

    // Assert
    const liveRegion = el.querySelector('.sr-only[aria-live]');
    expect(liveRegion?.getAttribute('aria-live')).toBe('polite');
  });

  // Cycle 5: recheckPending announces pending state via aria-live
  it('should display "Re-checking..." in aria-live region when recheckPending is true', () => {
    // Arrange

    // Act
    const { el } = setup({ status: 'ineligible', recheckPending: true });

    // Assert
    const liveRegion = el.querySelector('.sr-only[aria-live="polite"]');
    expect(liveRegion?.textContent?.trim()).toBe('Re-checking...');
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

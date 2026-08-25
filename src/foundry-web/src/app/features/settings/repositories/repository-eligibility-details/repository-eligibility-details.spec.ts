import { TestBed } from '@angular/core/testing';
import { RepositoryEligibilityDetailsComponent } from './repository-eligibility-details';
import { EligibilityReason, EligibilityViolation } from '../repository.model';

const VIOLATIONS: EligibilityViolation[] = [
  { rule: 'AllowDirectPushes', description: 'Allow direct pushes is enabled' },
  { rule: 'NoReviewRequired', description: 'No pull request reviews are required' },
];

function setup(overrides: {
  status?: 'eligible' | 'ineligible' | 'unreachable';
  violations?: EligibilityViolation[];
  reason?: EligibilityReason | null;
  recheckPending?: boolean;
  recheckError?: string | null;
  panelId?: string;
} = {}) {
  const fixture = TestBed.createComponent(RepositoryEligibilityDetailsComponent);
  fixture.componentRef.setInput('status', overrides.status ?? 'ineligible');
  fixture.componentRef.setInput('violations', overrides.violations ?? []);
  fixture.componentRef.setInput('reason', overrides.reason ?? null);
  fixture.componentRef.setInput('recheckPending', overrides.recheckPending ?? false);
  fixture.componentRef.setInput('recheckError', overrides.recheckError ?? null);
  fixture.componentRef.setInput('panelId', overrides.panelId ?? 'eligibility-detail-test');
  fixture.detectChanges();
  return { fixture, component: fixture.componentInstance, el: fixture.nativeElement as HTMLElement };
}

describe('RepositoryEligibilityDetailsComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [RepositoryEligibilityDetailsComponent],
    }).compileComponents();
  });

  // Cycle 1: panel has the panelId as its id attribute
  it('should set the panelId as the id attribute on the panel', () => {
    // Arrange

    // Act
    const { el } = setup({ panelId: 'eligibility-detail-abc' });

    // Assert
    const panel = el.querySelector('.repository-eligibility-details');
    expect(panel?.id).toBe('eligibility-detail-abc');
  });

  // Cycle 2: ineligible — shows violations heading
  it('should show "Branch protection violations" heading for ineligible status', () => {
    // Arrange

    // Act
    const { el } = setup({ status: 'ineligible', violations: VIOLATIONS });

    // Assert
    const heading = el.querySelector('.repository-eligibility-details__heading');
    expect(heading?.textContent?.trim()).toBe('Branch protection violations');
  });

  // Cycle 3: unreachable — shows cannot-verify heading
  it('should show "Branch protection could not be verified" heading for unreachable status', () => {
    // Arrange

    // Act
    const { el } = setup({ status: 'unreachable' });

    // Assert
    const heading = el.querySelector('.repository-eligibility-details__heading');
    expect(heading?.textContent?.trim()).toBe('Branch protection could not be verified');
  });

  // Cycle 4: ineligible — renders violations list
  it('should render violations list for ineligible status', () => {
    // Arrange

    // Act
    const { el } = setup({ status: 'ineligible', violations: VIOLATIONS });

    // Assert
    const list = el.querySelector('[aria-label="Eligibility violations"]');
    expect(list).toBeTruthy();
    const items = list?.querySelectorAll('li');
    expect(items?.length).toBe(2);
    expect(items?.[0].textContent?.trim()).toBe('Allow direct pushes is enabled');
    expect(items?.[1].textContent?.trim()).toBe('No pull request reviews are required');
  });

  // Cycle 5: unreachable — renders explanatory sentence, no violations list
  it('should render explanatory text and no violation list for unreachable status', () => {
    // Arrange

    // Act
    const { el } = setup({ status: 'unreachable' });

    // Assert
    const explanation = el.querySelector('.repository-eligibility-details__explanation');
    expect(explanation?.textContent).toBeTruthy();
    const list = el.querySelector('[aria-label="Eligibility violations"]');
    expect(list).toBeFalsy();
  });

  // Cycle 6: re-check button is rendered
  it('should render a Re-check button', () => {
    // Arrange

    // Act
    const { el } = setup({ status: 'ineligible', violations: VIOLATIONS });

    // Assert
    const recheckBtn = el.querySelector('.repository-eligibility-details__recheck-btn');
    expect(recheckBtn).toBeTruthy();
    expect(recheckBtn?.textContent?.trim()).toBe('Re-check');
  });

  // Cycle 7: re-check button emits recheck output
  it('should emit recheck output when Re-check button is clicked', () => {
    // Arrange
    const { el, component } = setup({ status: 'ineligible', violations: VIOLATIONS });
    let emitted = false;
    component.recheck.subscribe(() => { emitted = true; });

    // Act
    const recheckBtn = el.querySelector('.repository-eligibility-details__recheck-btn') as HTMLButtonElement;
    recheckBtn.click();

    // Assert
    expect(emitted).toBe(true);
  });

  // Cycle 8: recheckPending — button is disabled and shows "Re-checking..."
  it('should disable the Re-check button and show "Re-checking..." while pending', () => {
    // Arrange

    // Act
    const { el } = setup({ status: 'ineligible', violations: VIOLATIONS, recheckPending: true });

    // Assert
    const recheckBtn = el.querySelector('.repository-eligibility-details__recheck-btn') as HTMLButtonElement;
    expect(recheckBtn?.disabled).toBe(true);
    expect(recheckBtn?.textContent?.trim()).toBe('Re-checking...');
  });

  // Cycle 10: recheckError — visible error text rendered in the error span
  it('should render recheck error text in the error span', () => {
    // Arrange

    // Act
    const { el } = setup({ status: 'ineligible', recheckError: 'Re-check failed. Please try again.' });

    // Assert
    const errorSpan = el.querySelector('.repository-eligibility-details__recheck-error');
    expect(errorSpan).toBeTruthy();
    expect(errorSpan?.textContent?.trim()).toBe('Re-check failed. Please try again.');
  });

  // Cycle 11: no recheckError — error span is present (non-announcing, visible fallback)
  it('should always render the recheck error span in the DOM without role="alert"', () => {
    // Arrange

    // Act
    const { el } = setup({ status: 'ineligible' });

    // Assert
    const errorSpan = el.querySelector('.repository-eligibility-details__recheck-error');
    expect(errorSpan).toBeTruthy();
    expect(errorSpan?.hasAttribute('role')).toBe(false);
  });

  // Cycle 12: re-check button visible text is its accessible name (no redundant aria-label)
  it('should not have a redundant aria-label on the Re-check button', () => {
    // Arrange

    // Act
    const { el } = setup({ status: 'ineligible' });

    // Assert
    const recheckBtn = el.querySelector('.repository-eligibility-details__recheck-btn');
    expect(recheckBtn?.hasAttribute('aria-label')).toBe(false);
  });

  // Cycle 13: unreachable + reason=rate-limited — heading and explanation convey rate limit, not permission problem
  it('should show rate-limit heading and explanation without "check access" when reason is rate-limited', () => {
    // Arrange

    // Act
    const { el } = setup({ status: 'unreachable', reason: 'rate-limited' });

    // Assert
    const heading = el.querySelector('.repository-eligibility-details__heading');
    expect(heading?.textContent?.trim()).toBe('GitHub API rate limit reached');
    const explanation = el.querySelector('.repository-eligibility-details__explanation');
    expect(explanation?.textContent).toContain('rate limit');
    expect(explanation?.textContent?.toLowerCase()).not.toContain('check');
  });

  // Cycle 14: unreachable + reason=never-probed — heading and explanation convey not-yet-verified, not permission problem
  it('should show not-yet-verified heading and explanation without "check access" when reason is never-probed', () => {
    // Arrange

    // Act
    const { el } = setup({ status: 'unreachable', reason: 'never-probed' });

    // Assert
    const heading = el.querySelector('.repository-eligibility-details__heading');
    expect(heading?.textContent?.trim()).toBe('Write access not yet verified');
    const explanation = el.querySelector('.repository-eligibility-details__explanation');
    expect(explanation?.textContent).toContain('not been verified');
    expect(explanation?.textContent?.toLowerCase()).not.toContain('check');
  });

  // Cycle 15: unreachable + reason=branch-rules-unavailable — shows the existing "check access" explanation
  it('should show branch-rules-unavailable explanation with "check access" when reason is branch-rules-unavailable', () => {
    // Arrange

    // Act
    const { el } = setup({ status: 'unreachable', reason: 'branch-rules-unavailable' });

    // Assert
    const heading = el.querySelector('.repository-eligibility-details__heading');
    expect(heading?.textContent?.trim()).toBe('Branch protection could not be verified');
    const explanation = el.querySelector('.repository-eligibility-details__explanation');
    expect(explanation?.textContent).toContain('branch-protection settings');
    expect(explanation?.textContent?.toLowerCase()).toContain('check');
  });

  // Cycle 16: unreachable + reason=null (absent) — falls back to the existing "check access" explanation
  it('should fall back to the existing explanation when reason is null', () => {
    // Arrange

    // Act
    const { el } = setup({ status: 'unreachable', reason: null });

    // Assert
    const heading = el.querySelector('.repository-eligibility-details__heading');
    expect(heading?.textContent?.trim()).toBe('Branch protection could not be verified');
    const explanation = el.querySelector('.repository-eligibility-details__explanation');
    expect(explanation?.textContent).toContain('branch-protection settings');
  });
});

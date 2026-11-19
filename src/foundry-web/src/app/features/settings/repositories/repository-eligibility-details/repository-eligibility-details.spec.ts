import { TestBed } from '@angular/core/testing';
import { RepositoryEligibilityDetailsComponent } from './repository-eligibility-details';
import { provideRouter } from '@angular/router';
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
  providerType?: string;
  accountName?: string;
} = {}) {
  const fixture = TestBed.createComponent(RepositoryEligibilityDetailsComponent);
  fixture.componentRef.setInput('status', overrides.status ?? 'ineligible');
  fixture.componentRef.setInput('violations', overrides.violations ?? []);
  fixture.componentRef.setInput('reason', overrides.reason ?? null);
  fixture.componentRef.setInput('recheckPending', overrides.recheckPending ?? false);
  fixture.componentRef.setInput('recheckError', overrides.recheckError ?? null);
  fixture.componentRef.setInput('panelId', overrides.panelId ?? 'eligibility-detail-test');
  fixture.componentRef.setInput('providerType', overrides.providerType ?? 'github');
  fixture.componentRef.setInput('accountName', overrides.accountName ?? 'octocat');
  fixture.detectChanges();
  return { fixture, component: fixture.componentInstance, el: fixture.nativeElement as HTMLElement };
}

describe('RepositoryEligibilityDetailsComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [RepositoryEligibilityDetailsComponent],
      providers: [provideRouter([])],
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

  // Cycle 2: ineligible — shows neutral heading
  it('should show neutral heading for ineligible status', () => {
    // Arrange

    // Act
    const { el } = setup({ status: 'ineligible', violations: VIOLATIONS });

    // Assert
    const heading = el.querySelector('.repository-eligibility-details__heading');
    expect(heading?.textContent?.trim()).toBe("Why this repository can't be dispatched to");
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
    const { el } = setup({ status: 'unreachable', reason: 'rate-limited', providerType: 'github' });

    // Assert
    const heading = el.querySelector('.repository-eligibility-details__heading');
    expect(heading?.textContent?.trim()).toBe('GitHub API rate limit reached');
    const explanation = el.querySelector('.repository-eligibility-details__explanation');
    expect(explanation?.textContent).toContain('rate limit');
    expect(explanation?.textContent?.toLowerCase()).not.toContain('check');
  });

  it('should show GitLab-specific rate-limit heading when providerType is gitlab', () => {
    // Arrange

    // Act
    const { el } = setup({ status: 'unreachable', reason: 'rate-limited', providerType: 'gitlab' });

    // Assert
    const heading = el.querySelector('.repository-eligibility-details__heading');
    expect(heading?.textContent?.trim()).toBe('GitLab API rate limit reached');
    const explanation = el.querySelector('.repository-eligibility-details__explanation');
    expect(explanation?.textContent).toContain('rate limit');
  });

  // Cycle 14: unreachable + reason=never-probed — heading and explanation convey not-yet-checked, not permission problem
  it('should show not-yet-checked heading when reason is never-probed', () => {
    // Arrange

    // Act
    const { el } = setup({ status: 'unreachable', reason: 'never-probed' });

    // Assert
    const heading = el.querySelector('.repository-eligibility-details__heading');
    expect(heading?.textContent?.trim()).toBe('Eligibility not yet checked');
  });

  it('should show not-yet-checked explanation without "check access" when reason is never-probed', () => {
    // Arrange

    // Act
    const { el } = setup({ status: 'unreachable', reason: 'never-probed' });

    // Assert
    const explanation = el.querySelector('.repository-eligibility-details__explanation');
    expect(explanation?.textContent).toContain('not been checked');
    expect(explanation?.textContent?.toLowerCase()).not.toContain('check the account');
  });

  // Cycle 17: unreachable + reason=rate-limited — Re-check button is disabled
  it('should disable the Re-check button when reason is rate-limited', () => {
    // Arrange

    // Act
    const { el } = setup({ status: 'unreachable', reason: 'rate-limited' });

    // Assert
    const recheckBtn = el.querySelector('.repository-eligibility-details__recheck-btn') as HTMLButtonElement;
    expect(recheckBtn?.disabled).toBe(true);
  });

  // Cycle 18: unreachable + reason=rate-limited — Re-check button has explanatory title naming the provider
  it('should have an explanatory title on the Re-check button naming the provider when reason is rate-limited', () => {
    // Arrange

    // Act
    const { el } = setup({ status: 'unreachable', reason: 'rate-limited', providerType: 'github' });

    // Assert
    const recheckBtn = el.querySelector('.repository-eligibility-details__recheck-btn') as HTMLButtonElement;
    const title = recheckBtn?.getAttribute('title') ?? '';
    expect(title).toContain('rate limit');
    expect(title).toContain('GitHub');
  });

  // Cycle 19: unreachable + reason=never-probed — Re-check button is enabled
  it('should keep the Re-check button enabled when reason is never-probed', () => {
    // Arrange

    // Act
    const { el } = setup({ status: 'unreachable', reason: 'never-probed' });

    // Assert
    const recheckBtn = el.querySelector('.repository-eligibility-details__recheck-btn') as HTMLButtonElement;
    expect(recheckBtn?.disabled).toBe(false);
  });

  // Cycle 20: unreachable + reason=branch-rules-unavailable — Re-check button is enabled
  it('should keep the Re-check button enabled when reason is branch-rules-unavailable', () => {
    // Arrange

    // Act
    const { el } = setup({ status: 'unreachable', reason: 'branch-rules-unavailable' });

    // Assert
    const recheckBtn = el.querySelector('.repository-eligibility-details__recheck-btn') as HTMLButtonElement;
    expect(recheckBtn?.disabled).toBe(false);
  });

  // Cycle 21: recheckPending + reason=never-probed — button disabled and shows "Re-checking..." (pending composes with rate-limited)
  it('should disable the Re-check button and show "Re-checking..." when pending even if reason is not rate-limited', () => {
    // Arrange

    // Act
    const { el } = setup({ status: 'unreachable', reason: 'never-probed', recheckPending: true });

    // Assert
    const recheckBtn = el.querySelector('.repository-eligibility-details__recheck-btn') as HTMLButtonElement;
    expect(recheckBtn?.disabled).toBe(true);
    expect(recheckBtn?.textContent?.trim()).toBe('Re-checking...');
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

  describe('account link', () => {
    it('links a cannot-push violation to Accounts settings', () => {
      // Arrange
      const violations: EligibilityViolation[] = [
        { rule: 'cannot-push:octocat/api', description: 'Token is missing Administration (read and write) for octocat/api.' },
      ];

      // Act
      const { el } = setup({ status: 'ineligible', violations, accountName: 'octocat' });

      // Assert
      const link = el.querySelector<HTMLAnchorElement>('.repository-eligibility-details__account-link')!;
      expect(link.textContent?.trim()).toBe('View account');
      expect(link.getAttribute('aria-label')).toBe('View account octocat in Accounts settings');
      expect(new URL(link.getAttribute('href') ?? '', 'http://localhost').pathname).toBe('/settings/accounts');
      expect(link.closest('li')?.textContent).toContain('Token is missing Administration (read and write) for octocat/api.');
    });

    it('keeps the Re-check button alongside the account link', () => {
      // Arrange
      const violations: EligibilityViolation[] = [{ rule: 'cannot-push:octocat/api', description: 'Missing.' }];

      // Act
      const { el } = setup({ status: 'ineligible', violations });

      // Assert
      expect(el.querySelector('.repository-eligibility-details__recheck-btn')?.textContent?.trim()).toBe('Re-check');
    });

    it('adds no account link to other violations', () => {
      // Arrange / Act
      const { el } = setup({ status: 'ineligible', violations: VIOLATIONS });

      // Assert
      expect(el.querySelector('.repository-eligibility-details__account-link')).toBeNull();
    });
  });

  describe('package source link', () => {
    const SOURCE = 'https://gitlab.example.com/api/v4/groups/42/-/packages/nuget/index.json';

    it('links an unreachable package source violation to its card in Package sources settings', () => {
      // Arrange
      const violations: EligibilityViolation[] = [
        ...VIOLATIONS,
        { rule: `package-source-unreachable:${SOURCE}`, description: `Foundry can't read the package source ${SOURCE}.` },
      ];

      // Act
      const { el } = setup({ status: 'ineligible', violations });

      // Assert
      const links = el.querySelectorAll<HTMLAnchorElement>('.repository-eligibility-details__package-source-link');
      expect(links.length).toBe(1);
      expect(links[0].textContent?.trim()).toBe('View package source');
      expect(links[0].getAttribute('aria-label')).toBe(`View package source ${SOURCE} in Package sources settings`);
      const href = new URL(links[0].getAttribute('href') ?? '', 'http://localhost');
      expect(href.pathname).toBe('/settings/package-sources');
      expect(href.searchParams.get('source')).toBe(SOURCE);
      expect(links[0].closest('li')?.textContent).toContain(`Foundry can't read the package source ${SOURCE}.`);
    });

    it('adds no link to other violations', () => {
      // Arrange / Act
      const { el } = setup({ status: 'ineligible', violations: VIOLATIONS });

      // Assert
      expect(el.querySelector('.repository-eligibility-details__package-source-link')).toBeNull();
    });
  });
});

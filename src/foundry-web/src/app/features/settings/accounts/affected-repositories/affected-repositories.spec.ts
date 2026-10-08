import { TestBed } from '@angular/core/testing';
import { AffectedRepository } from '../account.model';
import { AffectedRepositoriesComponent } from './affected-repositories';

const ELIGIBLE_TO_INELIGIBLE: AffectedRepository = {
  id: 'repo-1',
  slug: 'org/api',
  previousStatus: 'eligible',
  newStatus: 'ineligible',
};

const ELIGIBLE_TO_UNREACHABLE: AffectedRepository = {
  id: 'repo-2',
  slug: 'org/ui',
  previousStatus: 'eligible',
  newStatus: 'unreachable',
};

const INELIGIBLE_TO_ELIGIBLE: AffectedRepository = {
  id: 'repo-3',
  slug: 'org/regained',
  previousStatus: 'ineligible',
  newStatus: 'eligible',
};

function setup(repositories: AffectedRepository[]) {
  TestBed.configureTestingModule({
    imports: [AffectedRepositoriesComponent],
  });
  const fixture = TestBed.createComponent(AffectedRepositoriesComponent);

  // Arrange
  fixture.componentRef.setInput('repositories', repositories);

  // Act
  fixture.detectChanges();

  return { fixture, el: fixture.nativeElement as HTMLElement };
}

describe('AffectedRepositoriesComponent', () => {
  // Cycle 1: renders region with aria-labelledby
  it('should render a region element with aria-labelledby pointing to the heading', () => {
    // Arrange
    const repositories = [ELIGIBLE_TO_INELIGIBLE];

    // Act
    const { el } = setup(repositories);

    // Assert
    const region = el.querySelector('[role="region"]');
    expect(region).toBeTruthy();
    const headingId = region?.getAttribute('aria-labelledby');
    expect(headingId).toBeTruthy();
    const heading = el.querySelector(`#${headingId}`);
    expect(heading).toBeTruthy();
  });

  // Cycle 2: no aria-live on the component root (live region moved to container)
  it('should not have aria-live on the panel region itself', () => {
    // Arrange
    const repositories = [ELIGIBLE_TO_INELIGIBLE];

    // Act
    const { el } = setup(repositories);

    // Assert
    const region = el.querySelector('[role="region"]');
    expect(region?.getAttribute('aria-live')).toBeNull();
  });

  // Cycle 3: warning heading uses lost-access count only, not total
  it('should show warning heading with lost-access count when repos lose access', () => {
    // Arrange
    const repositories = [ELIGIBLE_TO_INELIGIBLE];

    // Act
    const { el } = setup(repositories);

    // Assert
    const heading = el.querySelector('.affected-repositories__heading');
    expect(heading?.textContent).toContain('may have lost or restricted access');
  });

  // Cycle 4: neutral heading when no lost-access repos
  it('should show neutral heading when no repos lose access', () => {
    // Arrange
    const repositories = [INELIGIBLE_TO_ELIGIBLE];

    // Act
    const { el } = setup(repositories);

    // Assert
    const heading = el.querySelector('.affected-repositories__heading');
    expect(heading?.textContent).toContain('changed eligibility');
  });

  // Cycle 5: warning affordance class applied when lost-access
  it('should apply warning modifier class when repos lose access', () => {
    // Arrange
    const repositories = [ELIGIBLE_TO_INELIGIBLE];

    // Act
    const { el } = setup(repositories);

    // Assert
    const panel = el.querySelector('.affected-repositories');
    expect(panel?.classList).toContain('affected-repositories--warning');
  });

  // Cycle 6: no warning class when no lost-access repos
  it('should not apply warning modifier class when no repos lose access', () => {
    // Arrange
    const repositories = [INELIGIBLE_TO_ELIGIBLE];

    // Act
    const { el } = setup(repositories);

    // Assert
    const panel = el.querySelector('.affected-repositories');
    expect(panel?.classList).not.toContain('affected-repositories--warning');
  });

  // Cycle 7: renders rows for each repository
  it('should render a row for each repository', () => {
    // Arrange
    const repositories = [ELIGIBLE_TO_INELIGIBLE, INELIGIBLE_TO_ELIGIBLE];

    // Act
    const { el } = setup(repositories);

    // Assert
    const rows = el.querySelectorAll('.affected-repositories__row');
    expect(rows.length).toBe(2);
  });

  // Cycle 8: row contains slug
  it('should render the repository slug in each row', () => {
    // Arrange
    const repositories = [ELIGIBLE_TO_INELIGIBLE];

    // Act
    const { el } = setup(repositories);

    // Assert
    const row = el.querySelector('.affected-repositories__row');
    expect(row?.textContent).toContain('org/api');
  });

  // Cycle 9: row aria-label spells the transition
  it('should set aria-label on each row spelling the transition', () => {
    // Arrange
    const repositories = [ELIGIBLE_TO_INELIGIBLE];

    // Act
    const { el } = setup(repositories);

    // Assert
    const row = el.querySelector('.affected-repositories__row');
    const label = row?.getAttribute('aria-label');
    expect(label).toContain('org/api');
    expect(label).toContain('Eligible');
    expect(label).toContain('Ineligible');
  });

  // Cycle 10: lost-access rows sorted first
  it('should sort lost-access rows before other changes', () => {
    // Arrange — regained first in input, lost-access second
    const repositories = [INELIGIBLE_TO_ELIGIBLE, ELIGIBLE_TO_INELIGIBLE];

    // Act
    const { el } = setup(repositories);

    // Assert — lost-access (org/api) should appear first in the DOM
    const rows = el.querySelectorAll('.affected-repositories__row');
    expect(rows[0].textContent).toContain('org/api');
    expect(rows[1].textContent).toContain('org/regained');
  });

  // Cycle 11: warning heading count reflects only lost-access repos, not total
  it('should use the lost-access count (not total) in the warning heading for mixed changes', () => {
    // Arrange — 2 lose access, 1 gains
    const repositories = [ELIGIBLE_TO_INELIGIBLE, ELIGIBLE_TO_UNREACHABLE, INELIGIBLE_TO_ELIGIBLE];

    // Act
    const { el } = setup(repositories);

    // Assert — heading should say "2", not "3"
    const heading = el.querySelector('.affected-repositories__heading');
    expect(heading?.textContent?.trim()).toMatch(/^2\s/);
    expect(heading?.textContent).not.toContain('3');
  });

  // Cycle 12: dismiss button present and emits dismiss event
  it('should emit dismiss when the dismiss button is clicked', () => {
    // Arrange
    const { fixture, el } = setup([ELIGIBLE_TO_INELIGIBLE]);
    let dismissCount = 0;
    fixture.componentInstance.dismiss.subscribe(() => { dismissCount++; });

    // Act
    const btn = el.querySelector('.affected-repositories__dismiss') as HTMLButtonElement;
    btn.click();
    fixture.detectChanges();

    // Assert
    expect(dismissCount).toBe(1);
  });

  // Cycle 13: dismiss button is a real <button> element
  it('should render dismiss as a real button element', () => {
    // Arrange
    const repositories = [ELIGIBLE_TO_INELIGIBLE];

    // Act
    const { el } = setup(repositories);

    // Assert
    const btn = el.querySelector('.affected-repositories__dismiss');
    expect(btn?.tagName.toLowerCase()).toBe('button');
  });

  // Cycle 14: status dots are aria-hidden
  it('should mark status dots as aria-hidden', () => {
    // Arrange
    const repositories = [ELIGIBLE_TO_INELIGIBLE];

    // Act
    const { el } = setup(repositories);

    // Assert
    const dots = el.querySelectorAll('.affected-repositories__dot');
    dots.forEach(dot => {
      expect(dot.getAttribute('aria-hidden')).toBe('true');
    });
  });

  // Cycle 15: unreachable status — label displayed
  it('should display the unreachable label for unreachable newStatus', () => {
    // Arrange
    const repositories = [ELIGIBLE_TO_UNREACHABLE];

    // Act
    const { el } = setup(repositories);

    // Assert
    const row = el.querySelector('.affected-repositories__row');
    expect(row?.textContent).toContain('Unable to verify branch protection');
  });

  // Cycle 16: credential-unreadable — label "Token unreadable"
  it('should display "Token unreadable" label for credential-unreadable newStatus', () => {
    // Arrange
    const repositories: AffectedRepository[] = [{
      id: 'repo-4',
      slug: 'org/paused',
      previousStatus: 'eligible',
      newStatus: 'credential-unreadable',
    }];

    // Act
    const { el } = setup(repositories);

    // Assert
    const row = el.querySelector('.affected-repositories__row');
    expect(row?.textContent).toContain('Token unreadable');
  });

  // Cycle 17: credential-unreadable — reason line rendered
  it('should render a reason line for credential-unreadable rows', () => {
    // Arrange
    const repositories: AffectedRepository[] = [{
      id: 'repo-4',
      slug: 'org/paused',
      previousStatus: 'eligible',
      newStatus: 'credential-unreadable',
    }];

    // Act
    const { el } = setup(repositories);

    // Assert
    const reason = el.querySelector('.affected-repositories__reason');
    expect(reason).toBeTruthy();
    expect(reason?.textContent).toContain("the account token can't be decrypted");
  });

  // Cycle 18: credential-unreadable — no reason line for non-unreadable rows
  it('should not render a reason line for non-credential-unreadable rows', () => {
    // Arrange
    const repositories = [ELIGIBLE_TO_INELIGIBLE];

    // Act
    const { el } = setup(repositories);

    // Assert
    const reason = el.querySelector('.affected-repositories__reason');
    expect(reason).toBeNull();
  });

  // Cycle 19: credential-unreadable — error dot modifier class applied
  it('should apply the credential-unreadable dot modifier for credential-unreadable newStatus', () => {
    // Arrange
    const repositories: AffectedRepository[] = [{
      id: 'repo-4',
      slug: 'org/paused',
      previousStatus: 'eligible',
      newStatus: 'credential-unreadable',
    }];

    // Act
    const { el } = setup(repositories);

    // Assert
    const dot = el.querySelector('.affected-repositories__dot--credential-unreadable');
    expect(dot).toBeTruthy();
  });

  // Cycle 20: credential-unreadable — sorts to top (lost-access)
  it('should sort credential-unreadable rows before non-lost-access rows', () => {
    // Arrange — eligible-to-eligible first in input, credential-unreadable second
    const repositories: AffectedRepository[] = [
      INELIGIBLE_TO_ELIGIBLE,
      { id: 'repo-4', slug: 'org/paused', previousStatus: 'eligible', newStatus: 'credential-unreadable' },
    ];

    // Act
    const { el } = setup(repositories);

    // Assert — credential-unreadable (org/paused) should appear first
    const rows = el.querySelectorAll('.affected-repositories__row');
    expect(rows[0].textContent).toContain('org/paused');
    expect(rows[1].textContent).toContain('org/regained');
  });

  // Cycle 21: credential-unreadable — counts as lost-access in warning heading
  it('should include credential-unreadable repos in the lost-access count', () => {
    // Arrange
    const repositories: AffectedRepository[] = [
      ELIGIBLE_TO_INELIGIBLE,
      { id: 'repo-4', slug: 'org/paused', previousStatus: 'eligible', newStatus: 'credential-unreadable' },
    ];

    // Act
    const { el } = setup(repositories);

    // Assert — heading shows "2" (both lose access)
    const heading = el.querySelector('.affected-repositories__heading');
    expect(heading?.textContent?.trim()).toMatch(/^2\s/);
  });

  // Cycle 22: credential-unreadable — warning modifier class applied
  it('should apply warning modifier class when a credential-unreadable repo is present', () => {
    // Arrange
    const repositories: AffectedRepository[] = [{
      id: 'repo-4',
      slug: 'org/paused',
      previousStatus: 'eligible',
      newStatus: 'credential-unreadable',
    }];

    // Act
    const { el } = setup(repositories);

    // Assert
    const panel = el.querySelector('.affected-repositories');
    expect(panel?.classList).toContain('affected-repositories--warning');
  });

  // Cycle 23: credential-unreadable — aria-label includes reason
  it('should append the reason to the aria-label for credential-unreadable rows', () => {
    // Arrange
    const repositories: AffectedRepository[] = [{
      id: 'repo-4',
      slug: 'org/paused',
      previousStatus: 'eligible',
      newStatus: 'credential-unreadable',
    }];

    // Act
    const { el } = setup(repositories);

    // Assert
    const row = el.querySelector('.affected-repositories__row');
    const label = row?.getAttribute('aria-label');
    expect(label).toContain("can't be decrypted");
  });
});

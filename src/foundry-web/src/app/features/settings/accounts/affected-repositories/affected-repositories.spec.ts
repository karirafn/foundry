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
  fixture.componentRef.setInput('repositories', repositories);
  fixture.detectChanges();
  return { fixture, el: fixture.nativeElement as HTMLElement };
}

describe('AffectedRepositoriesComponent', () => {
  // Cycle 1: renders region with aria-labelledby
  it('should render a region element with aria-labelledby pointing to the heading', () => {
    // Arrange / Act
    const { el } = setup([ELIGIBLE_TO_INELIGIBLE]);

    // Assert
    const region = el.querySelector('[role="region"]');
    expect(region).toBeTruthy();
    const headingId = region?.getAttribute('aria-labelledby');
    expect(headingId).toBeTruthy();
    const heading = el.querySelector(`#${headingId}`);
    expect(heading).toBeTruthy();
  });

  // Cycle 2: aria-live="polite"
  it('should have aria-live="polite" on the region', () => {
    // Arrange / Act
    const { el } = setup([ELIGIBLE_TO_INELIGIBLE]);

    // Assert
    const region = el.querySelector('[role="region"]');
    expect(region?.getAttribute('aria-live')).toBe('polite');
  });

  // Cycle 3: warning heading when any lost-access repo present
  it('should show warning heading when repos lose access', () => {
    // Arrange / Act
    const { el } = setup([ELIGIBLE_TO_INELIGIBLE]);

    // Assert
    const heading = el.querySelector('.affected-repositories__heading');
    expect(heading?.textContent).toContain('will lose access');
  });

  // Cycle 4: neutral heading when no lost-access repos
  it('should show neutral heading when no repos lose access', () => {
    // Arrange / Act
    const { el } = setup([INELIGIBLE_TO_ELIGIBLE]);

    // Assert
    const heading = el.querySelector('.affected-repositories__heading');
    expect(heading?.textContent).toContain('changed eligibility');
  });

  // Cycle 5: warning affordance class applied when lost-access
  it('should apply warning modifier class when repos lose access', () => {
    // Arrange / Act
    const { el } = setup([ELIGIBLE_TO_INELIGIBLE]);

    // Assert
    const panel = el.querySelector('.affected-repositories');
    expect(panel?.classList).toContain('affected-repositories--warning');
  });

  // Cycle 6: no warning class when no lost-access repos
  it('should not apply warning modifier class when no repos lose access', () => {
    // Arrange / Act
    const { el } = setup([INELIGIBLE_TO_ELIGIBLE]);

    // Assert
    const panel = el.querySelector('.affected-repositories');
    expect(panel?.classList).not.toContain('affected-repositories--warning');
  });

  // Cycle 7: renders rows for each repository
  it('should render a row for each repository', () => {
    // Arrange / Act
    const { el } = setup([ELIGIBLE_TO_INELIGIBLE, INELIGIBLE_TO_ELIGIBLE]);

    // Assert
    const rows = el.querySelectorAll('.affected-repositories__row');
    expect(rows.length).toBe(2);
  });

  // Cycle 8: row contains slug
  it('should render the repository slug in each row', () => {
    // Arrange / Act
    const { el } = setup([ELIGIBLE_TO_INELIGIBLE]);

    // Assert
    const row = el.querySelector('.affected-repositories__row');
    expect(row?.textContent).toContain('org/api');
  });

  // Cycle 9: row aria-label spells the transition
  it('should set aria-label on each row spelling the transition', () => {
    // Arrange / Act
    const { el } = setup([ELIGIBLE_TO_INELIGIBLE]);

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
    const { el } = setup([INELIGIBLE_TO_ELIGIBLE, ELIGIBLE_TO_INELIGIBLE]);

    // Assert — lost-access (org/api) should appear first in the DOM
    const rows = el.querySelectorAll('.affected-repositories__row');
    expect(rows[0].textContent).toContain('org/api');
    expect(rows[1].textContent).toContain('org/regained');
  });

  // Cycle 11: heading count reflects total repositories
  it('should include the count in the warning heading', () => {
    // Arrange / Act
    const { el } = setup([ELIGIBLE_TO_INELIGIBLE, ELIGIBLE_TO_UNREACHABLE]);

    // Assert
    const heading = el.querySelector('.affected-repositories__heading');
    expect(heading?.textContent).toContain('2');
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
    // Arrange / Act
    const { el } = setup([ELIGIBLE_TO_INELIGIBLE]);

    // Assert
    const btn = el.querySelector('.affected-repositories__dismiss');
    expect(btn?.tagName.toLowerCase()).toBe('button');
  });

  // Cycle 14: status dots are aria-hidden
  it('should mark status dots as aria-hidden', () => {
    // Arrange / Act
    const { el } = setup([ELIGIBLE_TO_INELIGIBLE]);

    // Assert
    const dots = el.querySelectorAll('.affected-repositories__dot');
    dots.forEach(dot => {
      expect(dot.getAttribute('aria-hidden')).toBe('true');
    });
  });

  // Cycle 15: unreachable status — label displayed
  it('should display the unreachable label for unreachable newStatus', () => {
    // Arrange / Act
    const { el } = setup([ELIGIBLE_TO_UNREACHABLE]);

    // Assert
    const row = el.querySelector('.affected-repositories__row');
    expect(row?.textContent).toContain('Unable to verify branch protection');
  });
});

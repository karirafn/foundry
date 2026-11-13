import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RepoAlert } from '../../board.model';
import { RepoAlertComponent } from './repo-alert';

function render(overrides: Partial<RepoAlert> = {}): HTMLElement {
  TestBed.configureTestingModule({ providers: [provideRouter([])] });
  const fixture = TestBed.createComponent(RepoAlertComponent);
  fixture.componentRef.setInput('alert', {
    repositoryId: 'repo-7',
    slug: 'acme/api',
    heldCount: 3,
    firstViolation: 'Require pull requests',
    moreViolations: 0,
    ...overrides,
  });
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

function text(el: Element | null): string {
  return el?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
}

describe('RepoAlertComponent', () => {
  it('names the repository and how many issues it holds', () => {
    expect(text(render().querySelector('.repo-alert__lead'))).toBe(
      "acme/api can't be worked on · 3 issues held",
    );
  });

  it('reads one held issue in the singular', () => {
    expect(text(render({ heldCount: 1 }).querySelector('.repo-alert__lead'))).toContain(
      '1 issue held',
    );
  });

  it('shows the first violation', () => {
    expect(text(render().querySelector('.repo-alert__violation'))).toBe('Require pull requests');
  });

  it('counts the violations after the first', () => {
    expect(text(render({ moreViolations: 2 }).querySelector('.repo-alert__violation'))).toBe(
      'Require pull requests and 2 more',
    );
  });

  it('shows no violation line when none is known', () => {
    expect(render({ firstViolation: null }).querySelector('.repo-alert__violation')).toBeNull();
  });

  it('links to the repository settings by id', () => {
    // Act
    const link = render().querySelector<HTMLAnchorElement>('.repo-alert__link')!;

    // Assert
    expect(link.getAttribute('href')).toBe('/settings/repositories/repo-7');
    expect(text(link)).toBe('Fix in repository settings');
  });
});

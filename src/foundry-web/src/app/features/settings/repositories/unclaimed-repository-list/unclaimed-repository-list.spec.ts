import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { UnclaimedRepository } from '../repository.model';
import { UnclaimedRepositoryListComponent } from './unclaimed-repository-list';

const ORPHAN: UnclaimedRepository = {
  id: 'repo-1',
  host: 'gitlab.example.com',
  slug: 'orphan/project',
  violation: 'no-credential:orphan',
  violationDescription: 'No account covers the "orphan" namespace.',
};

const OTHER: UnclaimedRepository = { ...ORPHAN, id: 'repo-2', slug: 'orphan/other' };

@Component({
  standalone: true,
  imports: [UnclaimedRepositoryListComponent],
  template: `
    <h2 id="unclaimed-repositories-heading">Unclaimed repositories</h2>
    <fd-unclaimed-repository-list
      [repositories]="repositories()"
      [loadError]="loadError()"
      [reloading]="reloading()"
      [removingId]="removingId()"
      (delete)="deleted.push($event)"
      (retry)="retries = retries + 1"
    />
  `,
})
class HostComponent {
  readonly repositories = signal<readonly UnclaimedRepository[]>([ORPHAN, OTHER]);
  readonly loadError = signal<string | null>(null);
  readonly reloading = signal(false);
  readonly removingId = signal<string | null>(null);
  readonly deleted: UnclaimedRepository[] = [];
  retries = 0;
}

function setup(configure: (host: HostComponent) => void = () => {}) {
  const fixture = TestBed.createComponent(HostComponent);
  configure(fixture.componentInstance);
  fixture.detectChanges();
  return { host: fixture.componentInstance, el: fixture.nativeElement as HTMLElement };
}

function deleteButtons(el: HTMLElement): HTMLButtonElement[] {
  return Array.from(el.querySelectorAll<HTMLButtonElement>('.unclaimed-repository-list__delete-btn'));
}

describe('UnclaimedRepositoryListComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HostComponent] });
  });

  it('renders each row with its slug, host and violation description', () => {
    // Act
    const { el } = setup();

    // Assert
    const rows = Array.from(el.querySelectorAll('.unclaimed-repository-list__row'));
    expect(rows.length).toBe(2);
    expect(rows[0].querySelector('.unclaimed-repository-list__slug')?.textContent?.trim()).toBe('orphan/project');
    expect(rows[0].querySelector('.unclaimed-repository-list__meta')?.textContent?.trim()).toBe('gitlab.example.com');
    expect(rows[0].querySelector('.unclaimed-repository-list__problem-text')?.textContent?.trim())
      .toBe('No account covers the "orphan" namespace.');
  });

  it('labels the list by the section heading', () => {
    // Act
    const { el } = setup();

    // Assert
    expect(el.querySelector('ul')?.getAttribute('aria-labelledby')).toBe('unclaimed-repositories-heading');
  });

  it('names each delete button after its slug', () => {
    // Act
    const { el } = setup();

    // Assert
    const button = deleteButtons(el)[0];
    expect(button.getAttribute('aria-label')).toBe('Delete repository orphan/project');
    expect(button.getAttribute('title')).toBe('Delete repository orphan/project');
  });

  it('emits delete with the row when its delete button is clicked', () => {
    // Arrange
    const { host, el } = setup();

    // Act
    deleteButtons(el)[1].click();

    // Assert
    expect(host.deleted).toEqual([OTHER]);
  });

  it('disables only the row being removed', () => {
    // Act
    const { el } = setup(host => host.removingId.set('repo-1'));

    // Assert
    expect(deleteButtons(el).map(b => b.disabled)).toEqual([true, false]);
  });

  it('marks the list busy and disables every row while reloading', () => {
    // Act
    const { el } = setup(host => host.reloading.set(true));

    // Assert
    expect(el.querySelector('ul')?.getAttribute('aria-busy')).toBe('true');
    expect(deleteButtons(el).map(b => b.disabled)).toEqual([true, true]);
  });

  it('omits aria-busy when not reloading', () => {
    // Act
    const { el } = setup();

    // Assert
    expect(el.querySelector('ul')?.hasAttribute('aria-busy')).toBe(false);
  });

  it('shows the load error with a Retry button that emits retry', () => {
    // Arrange
    const { host, el } = setup(h => {
      h.loadError.set('Database unavailable.');
      h.repositories.set([]);
    });

    // Act
    el.querySelector<HTMLButtonElement>('.unclaimed-repository-list__retry-btn')?.click();

    // Assert
    const error = el.querySelector('.unclaimed-repository-list__error');
    expect(error?.getAttribute('role')).toBe('alert');
    expect(error?.querySelector('.unclaimed-repository-list__error-message')?.textContent?.trim()).toBe('Database unavailable.');
    expect(host.retries).toBe(1);
  });
});

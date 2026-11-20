import { vi } from 'vitest';
import { Subject } from 'rxjs';
import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { RepositoryService } from '../repository.service';
import { RecheckClaimsResponse, UnclaimedRepository } from '../repository.model';
import { ClaimsRechecked, UnclaimedRepositoryListComponent } from './unclaimed-repository-list';

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
      (rechecked)="rechecked.push($event)"
    />
  `,
})
class HostComponent {
  readonly repositories = signal<readonly UnclaimedRepository[]>([ORPHAN, OTHER]);
  readonly loadError = signal<string | null>(null);
  readonly reloading = signal(false);
  readonly removingId = signal<string | null>(null);
  readonly deleted: UnclaimedRepository[] = [];
  readonly rechecked: ClaimsRechecked[] = [];
  retries = 0;
}

function setup(configure: (host: HostComponent) => void = () => {}) {
  const fixture = TestBed.createComponent(HostComponent);
  configure(fixture.componentInstance);
  fixture.detectChanges();
  return { host: fixture.componentInstance, el: fixture.nativeElement as HTMLElement, fixture };
}

function recheckButtons(el: HTMLElement): HTMLButtonElement[] {
  return Array.from(el.querySelectorAll<HTMLButtonElement>('.unclaimed-repository-list__recheck-btn'));
}

function deleteButtons(el: HTMLElement): HTMLButtonElement[] {
  return Array.from(el.querySelectorAll<HTMLButtonElement>('.unclaimed-repository-list__delete-btn'));
}

describe('UnclaimedRepositoryListComponent', () => {
  let recheck$: Subject<RecheckClaimsResponse>;
  let recheckClaims: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    recheck$ = new Subject<RecheckClaimsResponse>();
    recheckClaims = vi.fn(() => recheck$);
    TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [{ provide: RepositoryService, useValue: { recheckClaims } }],
    });
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

  it('offers Re-check claims on every row', () => {
    // Act
    const { el } = setup();

    // Assert
    expect(recheckButtons(el).map(b => b.textContent?.trim())).toEqual(['Re-check claims', 'Re-check claims']);
  });

  it('rechecks the row host, labels the clicked row busy and disables every re-check button', () => {
    // Arrange
    const { el, fixture } = setup();

    // Act
    recheckButtons(el)[1].click();
    fixture.detectChanges();

    // Assert
    expect(recheckClaims).toHaveBeenCalledWith('gitlab.example.com');
    expect(recheckButtons(el).map(b => b.disabled)).toEqual([true, true]);
    expect(recheckButtons(el).map(b => b.textContent?.trim())).toEqual(['Re-check claims', 'Re-checking…']);
  });

  it('emits rechecked with the host and count on success and re-enables the buttons', () => {
    // Arrange
    const { host, el, fixture } = setup();
    recheckButtons(el)[0].click();

    // Act
    recheck$.next({ reclaimedRepositoryCount: 2 });
    recheck$.complete();
    fixture.detectChanges();

    // Assert
    expect(host.rechecked).toEqual([{ repositoryId: 'repo-1', host: 'gitlab.example.com', reclaimedRepositoryCount: 2 }]);
    expect(recheckButtons(el).map(b => b.disabled)).toEqual([false, false]);
  });

  it('shows an inline alert on the clicked row when the re-check fails', () => {
    // Arrange
    const { host, el, fixture } = setup();
    recheckButtons(el)[1].click();

    // Act
    recheck$.error(new Error('boom'));
    fixture.detectChanges();

    // Assert
    const rows = Array.from(el.querySelectorAll('.unclaimed-repository-list__row'));
    expect(rows[0].querySelector('.unclaimed-repository-list__problem-error')).toBeNull();
    const alert = rows[1].querySelector('.unclaimed-repository-list__problem-error');
    expect(alert?.getAttribute('role')).toBe('alert');
    expect(alert?.textContent?.trim()).toBe('Re-check failed. Please try again.');
    expect(recheckButtons(el)[1].disabled).toBe(false);
    expect(host.rechecked).toEqual([]);
  });

  it('clears the re-check error when the next re-check starts', () => {
    // Arrange
    const { el, fixture } = setup();
    recheckButtons(el)[1].click();
    recheck$.error(new Error('boom'));
    fixture.detectChanges();
    recheck$ = new Subject<RecheckClaimsResponse>();

    // Act
    recheckButtons(el)[0].click();
    fixture.detectChanges();

    // Assert
    expect(el.querySelector('.unclaimed-repository-list__problem-error')).toBeNull();
  });

  it('clears the re-check error when a remove starts', () => {
    // Arrange
    const { host, el, fixture } = setup();
    recheckButtons(el)[1].click();
    recheck$.error(new Error('boom'));
    fixture.detectChanges();

    // Act
    host.removingId.set('repo-1');
    fixture.detectChanges();

    // Assert
    expect(el.querySelector('.unclaimed-repository-list__problem-error')).toBeNull();
  });

  it('disables the re-check button while reloading and on the row being removed', () => {
    // Act
    const { el } = setup(h => h.removingId.set('repo-2'));

    // Assert
    expect(recheckButtons(el).map(b => b.disabled)).toEqual([false, true]);
  });

  it('disables every re-check button while reloading', () => {
    // Act
    const { el } = setup(h => h.reloading.set(true));

    // Assert
    expect(recheckButtons(el).map(b => b.disabled)).toEqual([true, true]);
  });
});

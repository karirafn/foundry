import { Component, input } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { UnclaimedRepository } from '../repository.model';
import { UnclaimedRepositoryListComponent } from './unclaimed-repository-list';

const REPOSITORIES: UnclaimedRepository[] = [
  {
    id: 'repo-1',
    host: 'gitlab.internal.example-corporation.com',
    slug: 'acme/platform/payments/reconciliation/billing-reconciliation-service-with-a-long-name',
    violation: 'no-credential:acme',
    violationDescription: 'No account covers the "acme" namespace. Add it to an account, or delete the repository.',
  },
  {
    id: 'repo-2',
    host: 'github.com',
    slug: 'orphan/tools',
    violation: 'no-credential:orphan',
    violationDescription: 'No account covers the "orphan" namespace.',
  },
];

@Component({
  selector: 'fd-unclaimed-repository-list-layout-host',
  imports: [UnclaimedRepositoryListComponent],
  template: `<div class="host" [style.width.px]="width()"><fd-unclaimed-repository-list [repositories]="repositories" [loadError]="loadError()" /></div>`,
})
class HostComponent {
  readonly width = input.required<number>();
  readonly loadError = input.required<string | null>();
  readonly repositories = REPOSITORIES;
}

async function render(width: number, loadError: string | null = null): Promise<HTMLElement> {
  const fixture = TestBed.createComponent(HostComponent);
  fixture.componentRef.setInput('width', width);
  fixture.componentRef.setInput('loadError', loadError);
  await fixture.whenStable();
  return (fixture.nativeElement as HTMLElement).querySelector('.host') as HTMLElement;
}

// Runs in a real browser (test-layout target); jsdom would measure every box as 0.
describe('unclaimed repository list layout', () => {
  // 332px is the list width inside a 390px viewport after the settings and section padding.
  it.each([332, 390, 1200])('has no horizontal overflow at %ipx', async (width) => {
    // Arrange
    const host = await render(width, 'The unclaimed repositories could not be loaded because the server did not answer in time.');
    const hostRight = host.getBoundingClientRect().right;

    // Act
    const overflowing = Array.from(host.querySelectorAll('*'))
      .filter(el => el.getBoundingClientRect().right > hostRight + 0.5)
      .map(el => el.className);

    // Assert
    expect(overflowing).toEqual([]);
  });

  it('keeps the delete button beside the slug and the problem block under it', async () => {
    // Arrange
    const host = await render(332);
    const row = host.querySelector('.unclaimed-repository-list__row') as HTMLElement;

    // Act
    const main = row.querySelector('.unclaimed-repository-list__main')!.getBoundingClientRect();
    const button = row.querySelector('.unclaimed-repository-list__delete-btn')!.getBoundingClientRect();
    const problem = row.querySelector('.unclaimed-repository-list__problem')!.getBoundingClientRect();

    // Assert
    expect(button.left).toBeGreaterThanOrEqual(main.right);
    expect(button.top).toBeLessThan(main.bottom);
    expect(problem.top).toBeGreaterThanOrEqual(main.bottom);
  });

  it('tightens row padding when narrow', async () => {
    // Arrange
    const narrow = await render(332);
    const narrowPadding = getComputedStyle(narrow.querySelector('.unclaimed-repository-list__row')!).paddingTop;
    TestBed.resetTestingModule();
    const wide = await render(1200);

    // Act
    const widePadding = getComputedStyle(wide.querySelector('.unclaimed-repository-list__row')!).paddingTop;

    // Assert
    expect(parseFloat(narrowPadding)).toBeLessThan(parseFloat(widePadding));
  });
});

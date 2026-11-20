import { Component, input } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { DeleteFailureComponent } from './delete-failure';
import { BlockingIssue, DeleteFailure } from './delete-failure.model';

const LONG_SLUG = 'a-very-long-group-name/with-a-deeply-nested-subgroup/and-a-repository-name-without-any-break-opportunity';

@Component({
  selector: 'fd-delete-failure-layout-host',
  imports: [DeleteFailureComponent],
  template: `<div class="host" [style.width.px]="width()"><fd-delete-failure [failure]="failure()" lead="Repository not deleted — a worker is still active on:" /></div>`,
})
class HostComponent {
  readonly width = input.required<number>();
  readonly failure = input.required<DeleteFailure>();
}

function refused(...blockingIssues: [BlockingIssue, ...BlockingIssue[]]): DeleteFailure {
  return { kind: 'refused', blockingIssues };
}

async function render(width: number, failure: DeleteFailure): Promise<HTMLElement> {
  const fixture = TestBed.createComponent(HostComponent);
  fixture.componentRef.setInput('width', width);
  fixture.componentRef.setInput('failure', failure);
  await fixture.whenStable();
  return (fixture.nativeElement as HTMLElement).querySelector('.host') as HTMLElement;
}

function overflowing(host: HTMLElement): string[] {
  const hostRight = host.getBoundingClientRect().right;
  return Array.from(host.querySelectorAll('*'))
    .filter((el) => el.getBoundingClientRect().right > hostRight + 0.5)
    .map((el) => el.tagName.toLowerCase() + '.' + el.className);
}

// Runs in a real browser (test-layout target); jsdom would measure every box as 0.
describe('delete failure layout', () => {
  it('wraps a long slug inside a narrow container', async () => {
    // Act
    const host = await render(240, refused({ repositorySlug: LONG_SLUG, issueNumber: 12345, state: 'revision_in_progress' }));

    // Assert
    expect(overflowing(host)).toEqual([]);
  });

  it('stacks lead, list and hint, with one line per issue when there is room', async () => {
    // Arrange
    const host = await render(960, refused(
      { repositorySlug: 'acme/api', issueNumber: 42, state: 'in_progress' },
      { repositorySlug: 'acme/api', issueNumber: 7, state: 'triage_in_progress' },
    ));

    // Act
    const lead = host.querySelector('.delete-failure__lead')!.getBoundingClientRect();
    const list = host.querySelector('.delete-failure__issues')!.getBoundingClientRect();
    const hint = host.querySelector('.delete-failure__hint')!.getBoundingClientRect();
    const items = Array.from(host.querySelectorAll('.delete-failure__issues li')).map((li) => li.getBoundingClientRect());

    // Assert
    expect(list.top).toBeGreaterThanOrEqual(lead.bottom);
    expect(hint.top).toBeGreaterThanOrEqual(list.bottom);
    expect(items[1].top).toBeGreaterThanOrEqual(items[0].bottom - 1);
    expect(items[0].left).toBeGreaterThan(lead.left);
    expect(overflowing(host)).toEqual([]);
  });

  it('keeps many issues inside the container', async () => {
    // Arrange
    const issues = Array.from({ length: 40 }, (_, i): BlockingIssue => ({
      repositorySlug: 'acme/api', issueNumber: i + 1, state: 'in_progress',
    }));

    // Act
    const host = await render(320, refused(issues[0], ...issues.slice(1)));

    // Assert
    expect(host.querySelectorAll('.delete-failure__issues li').length).toBe(40);
    expect(overflowing(host)).toEqual([]);
  });
});

import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { DeleteFailureComponent } from './delete-failure';
import { DeleteFailure } from './delete-failure.model';

@Component({
  standalone: true,
  imports: [DeleteFailureComponent],
  template: `<fd-delete-failure [failure]="failure()" lead="Repository not deleted — a worker is still active on:" />`,
})
class HostComponent {
  readonly failure = signal<DeleteFailure>({ kind: 'failed', message: 'Boom.' });
}

function render(failure: DeleteFailure): HTMLElement {
  const fixture = TestBed.createComponent(HostComponent);
  fixture.componentInstance.failure.set(failure);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('DeleteFailureComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HostComponent] });
  });

  it('should render the lead, one item per blocking issue and the hint for a refusal', () => {
    // Act
    const el = render({
      kind: 'refused',
      blockingIssues: [
        { repositorySlug: 'acme/api', issueNumber: 42, state: 'in_progress' },
        { repositorySlug: 'acme/api', issueNumber: 7, state: 'triage_in_progress' },
      ],
    });

    // Assert
    expect(el.querySelector('.delete-failure__lead')?.textContent?.trim())
      .toBe('Repository not deleted — a worker is still active on:');
    const items = Array.from(el.querySelectorAll('.delete-failure__issues li')).map(li => li.textContent?.trim());
    expect(items).toEqual(['acme/api#42 — in progress', 'acme/api#7 — triaging']);
    expect(el.querySelector('.delete-failure__hint')?.textContent?.trim()).toBe('Try again once these finish.');
  });

  it('should render a review run by its readable state', () => {
    // Act
    const el = render({
      kind: 'refused',
      blockingIssues: [{ repositorySlug: 'acme/api', issueNumber: 3, state: 'review_in_progress' }],
    });

    // Assert
    expect(el.querySelector('.delete-failure__issues li')?.textContent?.trim()).toBe('acme/api#3 — reviewing');
  });

  it('should render only the message for a failure', () => {
    // Act
    const el = render({ kind: 'failed', message: 'Repository is in use.' });

    // Assert
    expect(el.textContent?.trim()).toBe('Repository is in use.');
    expect(el.querySelector('.delete-failure__issues')).toBeNull();
  });
});

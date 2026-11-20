import { HttpErrorResponse } from '@angular/common/http';
import { TimeoutError } from 'rxjs';
import { deleteFailureFrom } from './delete-failure.model';

function conflict(body: unknown): HttpErrorResponse {
  return new HttpErrorResponse({ status: 409, statusText: 'Conflict', error: body });
}

describe('deleteFailureFrom', () => {
  it('should return refused with every blocking issue of a 409', () => {
    // Act
    const failure = deleteFailureFrom(conflict({
      detail: 'refused',
      blockingIssues: [
        { repositorySlug: 'acme/api', issueNumber: 42, state: 'in_progress' },
        { repositorySlug: 'acme/api', issueNumber: 7, state: 'review_in_progress' },
      ],
    }));

    // Assert
    expect(failure).toEqual({
      kind: 'refused',
      blockingIssues: [
        { repositorySlug: 'acme/api', issueNumber: 42, state: 'in_progress' },
        { repositorySlug: 'acme/api', issueNumber: 7, state: 'review_in_progress' },
      ],
    });
  });

  it('should fall back to the detail when an entry has an unknown state', () => {
    // Act
    const failure = deleteFailureFrom(conflict({
      detail: 'Server says no.',
      blockingIssues: [
        { repositorySlug: 'acme/api', issueNumber: 42, state: 'in_progress' },
        { repositorySlug: 'acme/api', issueNumber: 7, state: 'queued' },
      ],
    }));

    // Assert
    expect(failure).toEqual({ kind: 'failed', message: 'Server says no.' });
  });

  it('should fall back to the detail when an entry is malformed', () => {
    // Act
    const failure = deleteFailureFrom(conflict({
      detail: 'Server says no.',
      blockingIssues: [{ repositorySlug: 'acme/api', issueNumber: '42', state: 'in_progress' }],
    }));

    // Assert
    expect(failure).toEqual({ kind: 'failed', message: 'Server says no.' });
  });

  it('should fall back to the detail when a 409 lists no blocking issues', () => {
    // Act
    const failure = deleteFailureFrom(conflict({ detail: 'Repository is in use.', blockingIssues: [] }));

    // Assert
    expect(failure).toEqual({ kind: 'failed', message: 'Repository is in use.' });
  });

  it('should return failed with the detail for another status', () => {
    // Act
    const failure = deleteFailureFrom(new HttpErrorResponse({
      status: 404,
      error: {
        detail: 'Not found.',
        blockingIssues: [{ repositorySlug: 'acme/api', issueNumber: 42, state: 'in_progress' }],
      },
    }));

    // Assert
    expect(failure).toEqual({ kind: 'failed', message: 'Not found.' });
  });

  it('should return failed with the response message when there is no detail', () => {
    // Arrange
    const err = new HttpErrorResponse({ status: 500, statusText: 'Server Error', url: '/api/x' });

    // Act
    const failure = deleteFailureFrom(err);

    // Assert
    expect(failure).toEqual({ kind: 'failed', message: err.message });
  });

  it('should return failed with the timeout message for a timeout', () => {
    // Act
    const failure = deleteFailureFrom(new TimeoutError());

    // Assert
    expect(failure).toEqual({ kind: 'failed', message: 'The request timed out. Please try again.' });
  });
});

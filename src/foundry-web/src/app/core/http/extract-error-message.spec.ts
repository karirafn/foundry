import { HttpErrorResponse } from '@angular/common/http';
import { TimeoutError } from 'rxjs';
import { describe, expect, it } from 'vitest';
import { extractErrorMessage } from './extract-error-message';

describe('extractErrorMessage', () => {
  it('returns timeout message when error is a TimeoutError', () => {
    // Arrange
    const err = new TimeoutError();

    // Act
    const result = extractErrorMessage(err);

    // Assert
    expect(result).toBe('The request timed out. Please try again.');
  });

  it('returns null when the error body is a plain string', () => {
    // Arrange
    const err = new HttpErrorResponse({ error: 'Something went wrong on the server', status: 400 });

    // Act
    const result = extractErrorMessage(err);

    // Assert
    expect(result).toBeNull();
  });

  it('returns err.error.detail when body is ProblemDetails-shaped with a non-empty detail', () => {
    // Arrange
    const err = new HttpErrorResponse({
      error: { type: 'https://tools.ietf.org/html/rfc9110#section-15.5.5', title: 'Not Found', status: 404, detail: 'Repository not found' },
      status: 404,
    });

    // Act
    const result = extractErrorMessage(err);

    // Assert
    expect(result).toBe('Repository not found');
  });

  it('returns null when the error body is a string with no detail property', () => {
    // Arrange
    const err = new HttpErrorResponse({ error: 'Bare string from unmigrated endpoint', status: 422 });

    // Act
    const result = extractErrorMessage(err);

    // Assert
    expect(result).toBeNull();
  });

  it('returns null when err.error is an empty string', () => {
    // Arrange
    const err = new HttpErrorResponse({ error: '', status: 500 });

    // Act
    const result = extractErrorMessage(err);

    // Assert
    expect(result).toBeNull();
  });

  it('returns null when err.error is null', () => {
    // Arrange
    const err = new HttpErrorResponse({ error: null, status: 500 });

    // Act
    const result = extractErrorMessage(err);

    // Assert
    expect(result).toBeNull();
  });

  it('returns null when err.error is an object without a detail field', () => {
    // Arrange
    const err = new HttpErrorResponse({ error: { title: 'Bad Request', status: 400 }, status: 400 });

    // Act
    const result = extractErrorMessage(err);

    // Assert
    expect(result).toBeNull();
  });

  it('returns null when err.error.detail is an empty string', () => {
    // Arrange
    const err = new HttpErrorResponse({
      error: { type: 'https://tools.ietf.org/html/rfc9110', title: 'Bad Request', status: 400, detail: '' },
      status: 400,
    });

    // Act
    const result = extractErrorMessage(err);

    // Assert
    expect(result).toBeNull();
  });
});

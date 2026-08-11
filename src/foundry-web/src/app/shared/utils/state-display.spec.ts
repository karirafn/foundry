import { cardAccentFor } from './state-display';
import type { IssueState } from './issue-state';

describe('cardAccentFor', () => {
  // Working states → 'working'
  it('should return "working" for in_progress', () => {
    // Arrange
    const state: IssueState = 'in_progress';

    // Act
    const result = cardAccentFor(state);

    // Assert
    expect(result).toBe('working');
  });

  it('should return "working" for revision_in_progress', () => {
    // Arrange
    const state: IssueState = 'revision_in_progress';

    // Act
    const result = cardAccentFor(state);

    // Assert
    expect(result).toBe('working');
  });

  // Ready state → 'ready'
  it('should return "ready" for review', () => {
    // Arrange
    const state: IssueState = 'review';

    // Act
    const result = cardAccentFor(state);

    // Assert
    expect(result).toBe('ready');
  });

  // All other 10 states → null
  it('should return null for detected', () => {
    // Arrange / Act / Assert
    expect(cardAccentFor('detected')).toBeNull();
  });

  it('should return null for queued', () => {
    // Arrange / Act / Assert
    expect(cardAccentFor('queued')).toBeNull();
  });

  it('should return null for blocked', () => {
    // Arrange / Act / Assert
    expect(cardAccentFor('blocked')).toBeNull();
  });

  it('should return null for unchanged', () => {
    // Arrange / Act / Assert
    expect(cardAccentFor('unchanged')).toBeNull();
  });

  it('should return null for failed', () => {
    // Arrange / Act / Assert
    expect(cardAccentFor('failed')).toBeNull();
  });

  it('should return null for continuable_failed', () => {
    // Arrange / Act / Assert
    expect(cardAccentFor('continuable_failed')).toBeNull();
  });

  it('should return null for continuation_queued', () => {
    // Arrange / Act / Assert
    expect(cardAccentFor('continuation_queued')).toBeNull();
  });

  it('should return null for completed', () => {
    // Arrange / Act / Assert
    expect(cardAccentFor('completed')).toBeNull();
  });

  it('should return null for revision_queued', () => {
    // Arrange / Act / Assert
    expect(cardAccentFor('revision_queued')).toBeNull();
  });

  it('should return null for revision_failed', () => {
    // Arrange / Act / Assert
    expect(cardAccentFor('revision_failed')).toBeNull();
  });
});

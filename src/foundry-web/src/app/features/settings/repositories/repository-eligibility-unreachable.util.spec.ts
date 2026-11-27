import { unreachableExplanation } from './repository-eligibility-unreachable.util';

describe('unreachableExplanation', () => {
  it('says the provider refused the account token and that Foundry re-checks on the next poll for access-refused', () => {
    // Act
    const text = unreachableExplanation('access-refused', 'github');

    // Assert
    expect(text).toBe(
      'GitHub refused the account token when a run used it. Foundry re-checks write access on the next poll.',
    );
  });

  it('keeps the branch-protection explanation when the reason is absent', () => {
    // Act
    const text = unreachableExplanation(null, 'github');

    // Assert
    expect(text).toContain('branch-protection settings');
  });
});

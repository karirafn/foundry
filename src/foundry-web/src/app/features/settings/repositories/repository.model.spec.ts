import { RepositoryEligibility, canApplyBranchRules } from './repository.model';

describe('canApplyBranchRules', () => {
  const branchProtection = (rule: string) => ({ rule: `branch-protection:${rule}`, description: rule });

  it('should be true when every violation is a branch-protection violation', () => {
    // Arrange
    const eligibility: RepositoryEligibility = {
      status: 'ineligible',
      violations: [branchProtection('direct-pushes'), branchProtection('deletion')],
      reason: null,
    };

    // Act / Assert
    expect(canApplyBranchRules(eligibility)).toBe(true);
  });

  it('should be false when any violation is not a branch-protection violation', () => {
    // Arrange
    const eligibility: RepositoryEligibility = {
      status: 'ineligible',
      violations: [branchProtection('direct-pushes'), { rule: 'cannot-push:o/r', description: 'no push' }],
      reason: null,
    };

    // Act / Assert
    expect(canApplyBranchRules(eligibility)).toBe(false);
  });

  it('should be false for eligible, unreachable, null, or violation-less eligibility', () => {
    expect(canApplyBranchRules(null)).toBe(false);
    expect(canApplyBranchRules({ status: 'eligible', violations: [], reason: null })).toBe(false);
    expect(canApplyBranchRules({ status: 'unreachable', violations: [], reason: 'never-probed' })).toBe(false);
    expect(canApplyBranchRules({ status: 'ineligible', violations: [], reason: null })).toBe(false);
  });
});

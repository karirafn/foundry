import { providerLabel, providerTerminology } from './provider.util';

describe('providerLabel', () => {
  it('should return GH for GitHub', () => {
    // Arrange
    const provider = 'GitHub';

    // Act
    const result = providerLabel(provider);

    // Assert
    expect(result).toBe('GH');
  });

  it('should return GL for GitLab', () => {
    // Arrange
    const provider = 'GitLab';

    // Act
    const result = providerLabel(provider);

    // Assert
    expect(result).toBe('GL');
  });

  it('should return first two characters uppercased for unknown providers', () => {
    // Arrange
    const provider = 'Bitbucket';

    // Act
    const result = providerLabel(provider);

    // Assert
    expect(result).toBe('BI');
  });
});

describe('providerTerminology', () => {
  it('should return PR terminology for GitHub', () => {
    // Arrange
    const provider = 'GitHub';

    // Act
    const result = providerTerminology(provider);

    // Assert
    expect(result).toEqual({ pullRequest: 'Pull request', prAbbrev: 'PR' });
  });

  it('should return MR terminology for GitLab', () => {
    // Arrange
    const provider = 'GitLab';

    // Act
    const result = providerTerminology(provider);

    // Assert
    expect(result).toEqual({ pullRequest: 'Merge request', prAbbrev: 'MR' });
  });

  it('should return PR terminology for unknown providers', () => {
    // Arrange
    const provider = 'Bitbucket';

    // Act
    const result = providerTerminology(provider);

    // Assert
    expect(result).toEqual({ pullRequest: 'Pull request', prAbbrev: 'PR' });
  });
});

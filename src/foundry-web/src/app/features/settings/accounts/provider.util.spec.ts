import { providerLabel, providerTerminology } from './provider.util';

describe('providerLabel', () => {
  it('should return GH for GitHub', () => {
    // Arrange / Act
    const result = providerLabel('GitHub');

    // Assert
    expect(result).toBe('GH');
  });

  it('should return GL for GitLab', () => {
    // Arrange / Act
    const result = providerLabel('GitLab');

    // Assert
    expect(result).toBe('GL');
  });

  it('should return first two characters uppercased for unknown providers', () => {
    // Arrange / Act
    const result = providerLabel('Bitbucket');

    // Assert
    expect(result).toBe('BI');
  });
});

describe('providerTerminology', () => {
  it('should return PR terminology for GitHub', () => {
    // Arrange / Act
    const result = providerTerminology('GitHub');

    // Assert
    expect(result).toEqual({ pullRequest: 'Pull request', prAbbrev: 'PR' });
  });

  it('should return MR terminology for GitLab', () => {
    // Arrange / Act
    const result = providerTerminology('GitLab');

    // Assert
    expect(result).toEqual({ pullRequest: 'Merge request', prAbbrev: 'MR' });
  });

  it('should return PR terminology for unknown providers', () => {
    // Arrange / Act
    const result = providerTerminology('Bitbucket');

    // Assert
    expect(result).toEqual({ pullRequest: 'Pull request', prAbbrev: 'PR' });
  });
});

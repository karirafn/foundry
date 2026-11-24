import { checksUrl, providerDisplayName, providerLabel, providerTerminology } from './provider.util';

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

  it('should return GH for lowercase github', () => {
    // Arrange
    const provider = 'github';

    // Act
    const result = providerLabel(provider);

    // Assert
    expect(result).toBe('GH');
  });

  it('should return GL for lowercase gitlab', () => {
    // Arrange
    const provider = 'gitlab';

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

  it('should return MR terminology for lowercase gitlab', () => {
    // Arrange
    const provider = 'gitlab';

    // Act
    const result = providerTerminology(provider);

    // Assert
    expect(result).toEqual({ pullRequest: 'Merge request', prAbbrev: 'MR' });
  });

  it('should return PR terminology for lowercase github', () => {
    // Arrange
    const provider = 'github';

    // Act
    const result = providerTerminology(provider);

    // Assert
    expect(result).toEqual({ pullRequest: 'Pull request', prAbbrev: 'PR' });
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

describe('providerDisplayName', () => {
  it('should return GitHub for github', () => {
    // Arrange
    const provider = 'github';

    // Act
    const result = providerDisplayName(provider);

    // Assert
    expect(result).toBe('GitHub');
  });

  it('should return GitLab for gitlab', () => {
    // Arrange
    const provider = 'gitlab';

    // Act
    const result = providerDisplayName(provider);

    // Assert
    expect(result).toBe('GitLab');
  });

  it('should return GitHub for title-case GitHub', () => {
    // Arrange
    const provider = 'GitHub';

    // Act
    const result = providerDisplayName(provider);

    // Assert
    expect(result).toBe('GitHub');
  });

  it('should return GitLab for title-case GitLab', () => {
    // Arrange
    const provider = 'GitLab';

    // Act
    const result = providerDisplayName(provider);

    // Assert
    expect(result).toBe('GitLab');
  });

  it('should return the raw value for unknown providers', () => {
    // Arrange
    const provider = 'Bitbucket';

    // Act
    const result = providerDisplayName(provider);

    // Assert
    expect(result).toBe('Bitbucket');
  });
});

describe('checksUrl', () => {
  it.each([
    ['https://github.com/acme/repo/pull/42', 'https://github.com/acme/repo/pull/42/checks'],
    ['https://github.com/acme/repo/pull/42/', 'https://github.com/acme/repo/pull/42/checks'],
    ['https://ghe.example.com/acme/repo/pull/7', 'https://ghe.example.com/acme/repo/pull/7/checks'],
  ])('opens the checks tab of the GitHub pull request %s', (pullRequestUrl, expected) => {
    // Act
    const result = checksUrl(pullRequestUrl);

    // Assert
    expect(result).toBe(expected);
  });

  it.each([
    ['https://gitlab.com/group/project/-/merge_requests/5', 'https://gitlab.com/group/project/-/merge_requests/5/pipelines'],
    ['https://gitlab.example.com/a/b/c/-/merge_requests/12', 'https://gitlab.example.com/a/b/c/-/merge_requests/12/pipelines'],
  ])('opens the pipelines tab of the GitLab merge request %s', (pullRequestUrl, expected) => {
    // Act
    const result = checksUrl(pullRequestUrl);

    // Assert
    expect(result).toBe(expected);
  });

  it.each([
    ['https://github.com/acme/repo/issues/42'],
    ['https://example.com/review/42'],
    ['not a url'],
  ])('falls back to the pull request URL %s when it is of neither form', (pullRequestUrl) => {
    // Act
    const result = checksUrl(pullRequestUrl);

    // Assert
    expect(result).toBe(pullRequestUrl);
  });
});

import { accountOptionLabel } from './account-label.util';
import { AccountSummary } from './account.model';

function makeAccount(overrides: Partial<AccountSummary> = {}): AccountSummary {
  return {
    id: '00000000-0000-0000-0000-000000000001',
    name: 'My GitHub',
    providerType: 'GitHub',
    baseUrl: 'https://github.com',
    hasToken: true,
    namespaces: [],
    ...overrides,
  };
}

describe('accountOptionLabel', () => {
  it('should format label with single namespace', () => {
    // Arrange
    const account = makeAccount({ namespaces: ['my-org'] });

    // Act
    const result = accountOptionLabel(account);

    // Assert
    expect(result).toBe('My GitHub — my-org (github.com)');
  });

  it('should sort multiple namespaces alphabetically using localeCompare', () => {
    // Arrange — pass namespaces in unsorted order
    const account = makeAccount({ namespaces: ['zebra-org', 'alpha-org', 'mango-org'] });

    // Act
    const result = accountOptionLabel(account);

    // Assert — output is sorted
    expect(result).toBe('My GitHub — alpha-org, mango-org, zebra-org (github.com)');
  });

  it('should not mutate the input namespaces array', () => {
    // Arrange
    const namespaces = ['zebra-org', 'alpha-org'];
    const account = makeAccount({ namespaces });

    // Act
    accountOptionLabel(account);

    // Assert — original array is unchanged
    expect(namespaces).toEqual(['zebra-org', 'alpha-org']);
  });

  it('should format label with zero namespaces — no em dash or namespace segment', () => {
    // Arrange
    const account = makeAccount({ namespaces: [] });

    // Act
    const result = accountOptionLabel(account);

    // Assert
    expect(result).toBe('My GitHub (github.com)');
  });

  it('should not include overflow marker when namespaces count is exactly 4', () => {
    // Arrange
    const account = makeAccount({ namespaces: ['ns-a', 'ns-b', 'ns-c', 'ns-d'] });

    // Act
    const result = accountOptionLabel(account);

    // Assert
    expect(result).toBe('My GitHub — ns-a, ns-b, ns-c, ns-d (github.com)');
    expect(result).not.toContain('more');
  });

  it('should append "+N more" inline when namespaces exceed 4', () => {
    // Arrange — 7 namespaces (a..g), sorted alphabetically
    const account = makeAccount({
      namespaces: ['ns-g', 'ns-b', 'ns-e', 'ns-a', 'ns-d', 'ns-f', 'ns-c'],
    });

    // Act
    const result = accountOptionLabel(account);

    // Assert — first 4 sorted + "+3 more" before host
    expect(result).toBe('My GitHub — ns-a, ns-b, ns-c, ns-d +3 more (github.com)');
  });

  it('should fall back to raw baseUrl when URL is malformed', () => {
    // Arrange
    const account = makeAccount({ baseUrl: 'not-a-valid-url', namespaces: ['my-org'] });

    // Act
    const result = accountOptionLabel(account);

    // Assert — falls back to raw string
    expect(result).toBe('My GitHub — my-org (not-a-valid-url)');
  });

  it('should use exact em dash character U+2014 as separator', () => {
    // Arrange
    const account = makeAccount({ name: 'MyGitHub', namespaces: ['myorg'] });

    // Act
    const result = accountOptionLabel(account);

    // Assert — separator is the em dash (U+2014), not en dash or hyphen-minus
    expect(result).toBe('MyGitHub — myorg (github.com)');
  });

  it('should strip the scheme and show only the host', () => {
    // Arrange
    const account = makeAccount({ baseUrl: 'https://gitlab.example.com', namespaces: ['team'] });

    // Act
    const result = accountOptionLabel(account);

    // Assert — no "https://"
    expect(result).toContain('(gitlab.example.com)');
    expect(result).not.toContain('https://');
  });
});

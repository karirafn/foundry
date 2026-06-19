export function providerLabel(providerType: string): string {
  switch (providerType) {
    case 'GitHub': return 'GH';
    case 'GitLab': return 'GL';
    default: return providerType.substring(0, 2).toUpperCase();
  }
}

export interface ProviderTerminology {
  pullRequest: string;
  prAbbrev: string;
}

export function providerTerminology(providerType: string): ProviderTerminology {
  return providerType === 'GitLab'
    ? { pullRequest: 'Merge request', prAbbrev: 'MR' }
    : { pullRequest: 'Pull request', prAbbrev: 'PR' };
}

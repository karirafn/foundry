export function providerLabel(providerType: string): string {
  switch (providerType.toLowerCase()) {
    case 'github': return 'GH';
    case 'gitlab': return 'GL';
    default: return providerType.substring(0, 2).toUpperCase();
  }
}

export function providerDisplayName(providerType: string): string {
  switch (providerType.toLowerCase()) {
    case 'github': return 'GitHub';
    case 'gitlab': return 'GitLab';
    default: return providerType;
  }
}

export interface ProviderTerminology {
  pullRequest: string;
  prAbbrev: string;
}

export function providerTerminology(providerType: string): ProviderTerminology {
  return providerType.toLowerCase() === 'gitlab'
    ? { pullRequest: 'Merge request', prAbbrev: 'MR' }
    : { pullRequest: 'Pull request', prAbbrev: 'PR' };
}

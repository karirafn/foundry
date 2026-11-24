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

const GITHUB_PULL_PATH = /^\/[^/]+\/[^/]+\/pull\/\d+$/;
const GITLAB_MERGE_REQUEST_PATH = /^\/.+\/-\/merge_requests\/\d+$/;

/**
 * The page of a pull request's CI: `<pr>/checks` on GitHub, `<mr>/pipelines` on GitLab, read from
 * the URL's own form; the pull request URL itself when it is of neither form.
 */
export function checksUrl(pullRequestUrl: string): string {
  let url: URL;
  try {
    url = new URL(pullRequestUrl);
  } catch {
    return pullRequestUrl;
  }
  const path = url.pathname.replace(/\/+$/, '');
  if (GITHUB_PULL_PATH.test(path)) {
    return `${url.origin}${path}/checks`;
  }
  if (GITLAB_MERGE_REQUEST_PATH.test(path)) {
    return `${url.origin}${path}/pipelines`;
  }
  return pullRequestUrl;
}

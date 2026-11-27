import { EligibilityReason } from './repository.model';
import { providerDisplayName } from '../../../shared/utils/provider.util';

export function unreachableExplanation(reason: EligibilityReason | null, providerType: string): string {
  if (reason === null) {
    return 'Foundry could not read branch-protection settings for this repository. Check the account\'s access, then re-check.';
  }
  switch (reason) {
    case 'rate-limited':
      return `The ${providerDisplayName(providerType)} API rate limit has been reached. Foundry will retry automatically.`;
    case 'never-probed':
      return 'Eligibility has not been checked yet. Foundry will probe automatically.';
    case 'branch-rules-unavailable':
      return 'Foundry could not read branch-protection settings for this repository. Check the account\'s access, then re-check.';
    case 'access-refused':
      return `${providerDisplayName(providerType)} refused the account token when a run used it. Foundry re-checks write access on the next poll.`;
  }
  const exhaustive: never = reason;
  return exhaustive;
}

export function rateLimitTooltip(providerType: string): string {
  return `${providerDisplayName(providerType)} rate limit active — Foundry retries automatically`;
}

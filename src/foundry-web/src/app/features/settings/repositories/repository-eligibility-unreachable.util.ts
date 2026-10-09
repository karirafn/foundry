import { EligibilityReason } from './repository.model';
import { providerDisplayName } from '../../../shared/utils/provider.util';

export function unreachableExplanation(reason: EligibilityReason | null, providerType: string): string {
  switch (reason) {
    case 'rate-limited':
      return `The ${providerDisplayName(providerType)} API rate limit has been reached. Foundry will retry automatically.`;
    case 'never-probed':
      return 'Eligibility has not been checked yet. Foundry will probe automatically.';
    case 'branch-rules-unavailable':
      return 'Foundry could not read branch-protection settings for this repository. Check the account\'s access, then re-check.';
    default:
      return 'Foundry could not read branch-protection settings for this repository. Check the account\'s access, then re-check.';
  }
}

export function rateLimitTooltip(providerType: string): string {
  return `${providerDisplayName(providerType)} rate limit active — Foundry retries automatically`;
}

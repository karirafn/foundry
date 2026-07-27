export const NO_WRITE_ACCESS_REASON = 'no write access — token lacks push or SSO not authorized';

export type EligibilityStatus = 'eligible' | 'ineligible' | 'unreachable';

export function eligibilityStatusLabel(status: EligibilityStatus): string {
  switch (status) {
    case 'eligible':
      return 'Eligible';
    case 'ineligible':
      return 'Ineligible';
    case 'unreachable':
      return 'Unable to verify branch protection';
  }
}

export interface EligibilityViolation {
  rule: string;
  description: string;
}

export interface RepositoryEligibility {
  status: EligibilityStatus;
  violations: EligibilityViolation[];
}

export interface RepositorySummary {
  id: string;
  slug: string;
  accountId: string;
  accountName: string;
  providerType: string;
  position: number;
  pollIntervalSeconds: number | null;
  isActive: boolean;
  lastPolledAt: string | null;
  eligibility: RepositoryEligibility | null;
}

export interface AvailableRepository {
  slug: string;
  isPrivate: boolean;
  canPush: boolean;
  isMonitored: boolean;
}

export interface AvailableRepositoriesResponse {
  hasClaims: boolean;
  repositories: AvailableRepository[];
}

export interface CreateRepositoryRequest {
  slug: string;
  pollIntervalSeconds: number | null;
}

export interface UpdateRepositoryRequest {
  pollIntervalSeconds: number | null;
  isActive: boolean;
}

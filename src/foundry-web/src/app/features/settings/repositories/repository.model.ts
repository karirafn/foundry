export type EligibilityStatus = 'eligible' | 'ineligible' | 'unreachable';

export type EligibilityReason = 'never-probed' | 'rate-limited' | 'branch-rules-unavailable';

export function eligibilityStatusLabel(status: EligibilityStatus): string {
  switch (status) {
    case 'eligible':
      return 'Eligible';
    case 'ineligible':
      return 'Ineligible';
    case 'unreachable':
      return 'Unverified';
  }
}

export interface EligibilityViolation {
  rule: string;
  description: string;
}

export interface RepositoryEligibility {
  status: EligibilityStatus;
  violations: EligibilityViolation[];
  reason: EligibilityReason | null;
}

export interface RepositorySummary {
  id: string;
  slug: string;
  accountId: string;
  accountName: string;
  providerType: string;
  position: number;
  pollIntervalSeconds: number | null;
  effectivePollIntervalSeconds: number;
  pollIntervalIsDefault: boolean;
  isActive: boolean;
  maxConcurrentWorkers: number;
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
  noPushAccessExplanation: string;
}

export interface CreateRepositoryRequest {
  slug: string;
  pollIntervalSeconds: number | null;
  maxConcurrentWorkers: number | null;
}

export interface UpdateRepositoryRequest {
  pollIntervalSeconds: number | null;
  isActive: boolean;
  maxConcurrentWorkers: number;
}

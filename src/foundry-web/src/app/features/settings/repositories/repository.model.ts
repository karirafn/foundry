export type EligibilityStatus = 'eligible' | 'ineligible' | 'unreachable';

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
  pollIntervalSeconds: number | null;
  isActive: boolean;
  lastPolledAt: string | null;
  eligibility: RepositoryEligibility | null;
}

export interface AvailableRepository {
  slug: string;
  isPrivate: boolean;
}

export interface CreateRepositoryRequest {
  slug: string;
  pollIntervalSeconds: number | null;
}

export interface UpdateRepositoryRequest {
  pollIntervalSeconds: number | null;
  isActive: boolean;
}

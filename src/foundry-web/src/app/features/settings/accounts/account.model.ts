export type ProviderType = 'GitHub' | 'GitLab';

export interface AccountSummary {
  id: string;
  name: string;
  providerType: string;
  baseUrl: string;
  hasToken: boolean;
}

export interface CreateAccountRequest {
  providerType: string;
  baseUrl: string;
  token: string;
}

export interface UpdateAccountRequest {
  baseUrl: string;
  token?: string;
}

export interface TokenValidationResult {
  isValid: boolean;
  isAuthFailure: boolean;
  missingScopes: string[];
  accountName: string | null;
}

export type AffectedRepositoryStatus = 'eligible' | 'ineligible' | 'unreachable';

export interface AffectedRepository {
  id: string;
  slug: string;
  previousStatus: AffectedRepositoryStatus;
  newStatus: AffectedRepositoryStatus;
}

export interface CredentialUpdateResult {
  credential: AccountSummary;
  affectedRepositories: AffectedRepository[];
}

export function affectedStatusLabel(status: AffectedRepositoryStatus): string {
  switch (status) {
    case 'eligible':
      return 'Eligible';
    case 'ineligible':
      return 'Ineligible';
    case 'unreachable':
      return 'Unable to verify branch protection';
  }
}

export type ProviderType = 'GitHub' | 'GitLab';

export interface AccountSummary {
  id: string;
  name: string;
  providerType: string;
  baseUrl: string;
  hasToken: boolean;
  namespaces: string[];
}

export interface NamespaceConflict {
  namespace: string;
  holderCredentialId: string;
  holderName: string;
}

export interface NamespaceConflictResponse {
  conflicts: NamespaceConflict[];
}

export interface TakeoverValidationResponse {
  invalidNamespaces: string[];
}

export interface CredentialCreationResult {
  credential: AccountSummary;
  affectedRepositories: AffectedRepository[];
}

export interface CreateAccountRequest {
  providerType: string;
  baseUrl: string;
  token: string;
  takeoverNamespaces?: string[];
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

export interface TokenRequirements {
  readonly providerType: string;
  readonly tokenTypeLabel: string;
  readonly scopes: readonly string[];
  readonly creationUrlTemplate: string;
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

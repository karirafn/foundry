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

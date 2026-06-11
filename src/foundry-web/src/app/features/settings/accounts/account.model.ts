export type ProviderType = 'GitHub' | 'GitLab';

export interface AccountSummary {
  id: string;
  name: string;
  providerType: string;
  baseUrl: string;
  hasToken: boolean;
}

export interface CreateAccountRequest {
  name: string;
  providerType: string;
  baseUrl: string;
  token: string;
}

export interface UpdateAccountRequest {
  name: string;
  baseUrl: string;
  token?: string;
}

export interface TokenValidationResult {
  isValid: boolean;
  isAuthFailure: boolean;
  missingScopes: string[];
}

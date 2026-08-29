import { Schemas } from '../../../api';

// Generated type aliases — shape is owned by the OpenAPI contract in schema.ts.
// Exported names are preserved so no importer's specifier changes.

export type AccountSummary = Schemas['CredentialSummary'];
export type CredentialCreationResult = Schemas['CredentialCreationResult'];
export type CredentialUpdateResult = Schemas['CredentialUpdateResult'];
export type CreateAccountRequest = Schemas['CreateAccountRequestBody'];
export type UpdateAccountRequest = Schemas['UpdateAccountRequestBody'];
export type TokenValidationResult = Schemas['ValidateTokenResponse'];
export type ValidateTokenRequest = Schemas['ValidateTokenRequestBody'];
export type AffectedRepository = Schemas['AffectedRepository'];
export type NamespaceConflict = Schemas['NamespaceConflict'];
export type CreateAccountConflictResponse = Schemas['CreateAccountConflictResponse'];
export type UpdateAccountConflictResponse = Schemas['UpdateAccountConflictResponse'];
export type TakeoverValidationResponse = Schemas['TakeoverValidationResponse'];
export type TokenRequirements = Schemas['TokenRequirements'];

// Client-side refinements layered over the generated string wire type.
// The API returns these fields as open strings so they are intentionally not generated.
// ProviderType refines CredentialSummary.provider.
// AffectedRepositoryStatus refines AffectedRepository.previousStatus and AffectedRepository.newStatus.
// TokenValidationKind refines ValidateTokenResponse.kind.
// Update these unions when the corresponding C# contract values change.
export type ProviderType = 'GitHub' | 'GitLab';
export type AffectedRepositoryStatus = 'eligible' | 'ineligible' | 'unreachable';
export type TokenValidationKind =
  | 'authenticated'
  | 'authenticationFailed'
  | 'scopesUnverifiable'
  | 'identityUnresolved'
  | 'providerMismatch';

export function affectedStatusLabel(status: AffectedRepositoryStatus | string): string {
  switch (status) {
    case 'eligible':
      return 'Eligible';
    case 'ineligible':
      return 'Ineligible';
    case 'unreachable':
      return 'Unable to verify branch protection';
    default:
      return status;
  }
}

const KNOWN_TOKEN_VALIDATION_KINDS: ReadonlySet<TokenValidationKind> = new Set<TokenValidationKind>([
  'authenticated',
  'authenticationFailed',
  'scopesUnverifiable',
  'identityUnresolved',
  'providerMismatch',
]);

export function narrowTokenValidationKind(result: TokenValidationResult): TokenValidationKind | 'unknown' {
  const kind = result.kind as string;
  if (KNOWN_TOKEN_VALIDATION_KINDS.has(kind as TokenValidationKind)) {
    return kind as TokenValidationKind;
  }
  return 'unknown';
}

export function providerDisplayName(provider: string | null): string {
  switch (provider) {
    case 'github':
      return 'GitHub';
    case 'gitlab':
      return 'GitLab';
    default:
      if (!provider) {
        return '';
      }
      return provider.charAt(0).toUpperCase() + provider.slice(1);
  }
}

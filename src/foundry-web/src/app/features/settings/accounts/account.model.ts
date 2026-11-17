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
// TokenStatus refines CredentialSummary.tokenStatus and CredentialSummary.branchRulesTokenStatus.
// Update these unions when the corresponding C# contract values change.
export type ProviderType = 'GitHub' | 'GitLab';
export type AffectedRepositoryStatus =
  | 'eligible'
  | 'ineligible'
  | 'unreachable'
  | 'credential-unreadable'
  | 'credential-invalid';
export type TokenValidationKind =
  | 'authenticated'
  | 'authenticationFailed'
  | 'scopesUnverifiable'
  | 'identityUnresolved'
  | 'providerMismatch';

export type TokenStatus = 'present' | 'absent' | 'unreadable' | 'invalid';

const KNOWN_TOKEN_STATUSES: ReadonlySet<TokenStatus> = new Set<TokenStatus>([
  'present',
  'absent',
  'unreadable',
  'invalid',
]);

// The branch-rules token has no Invalid state, so a stray 'invalid' falls back to absent.
const KNOWN_BRANCH_RULES_TOKEN_STATUSES: ReadonlySet<TokenStatus> = new Set<TokenStatus>([
  'present',
  'absent',
  'unreadable',
]);

/**
 * Narrows the open-string wire field to a known TokenStatus, falling back to
 * hasToken for a response predating the field (defensive during rollout).
 */
export function resolveTokenStatus(account: AccountSummary): TokenStatus {
  const raw = (account as { tokenStatus?: string }).tokenStatus;
  if (raw && KNOWN_TOKEN_STATUSES.has(raw as TokenStatus)) {
    return raw as TokenStatus;
  }
  return account.hasToken ? 'present' : 'absent';
}

/** Narrows the open-string branch-rules token status; a response predating the field reads as absent. */
export function resolveBranchRulesTokenStatus(account: AccountSummary): TokenStatus {
  const raw = (account as { branchRulesTokenStatus?: string }).branchRulesTokenStatus;
  if (raw && KNOWN_BRANCH_RULES_TOKEN_STATUSES.has(raw as TokenStatus)) {
    return raw as TokenStatus;
  }
  return 'absent';
}

export type AccountStatusTone = 'neutral' | 'warning' | 'error';

export type AccountStatusTag =
  | { readonly kind: 'not-configured'; readonly label: 'Not configured'; readonly tone: 'neutral' }
  | { readonly kind: 'token-unreadable'; readonly label: 'Token unreadable'; readonly tone: 'warning' }
  | { readonly kind: 'token-invalid'; readonly label: 'Token invalid'; readonly tone: 'error' };

export interface AccountProblem {
  readonly tone: Exclude<AccountStatusTone, 'neutral'>;
  readonly message: string;
  /** Visible button text; the button's aria-label is `${actionLabel} for ${account.name}`. */
  readonly actionLabel: string;
}

const NOT_CONFIGURED_TAG: AccountStatusTag = { kind: 'not-configured', label: 'Not configured', tone: 'neutral' };
const TOKEN_UNREADABLE_TAG: AccountStatusTag = { kind: 'token-unreadable', label: 'Token unreadable', tone: 'warning' };
const TOKEN_INVALID_TAG: AccountStatusTag = { kind: 'token-invalid', label: 'Token invalid', tone: 'error' };

const TOKEN_UNREADABLE_PROBLEM: AccountProblem = {
  tone: 'warning',
  message: "The account token can't be decrypted. Re-enter it on the account to resume.",
  actionLabel: 'Re-enter token',
};
const TOKEN_INVALID_PROBLEM: AccountProblem = {
  tone: 'error',
  message: 'The provider rejected the account token. Enter a new token on the account to resume.',
  actionLabel: 'Enter new token',
};

// Both take the whole account, not its TokenStatus, so token expiry (#1040) can read from the same argument.

/** The one status tag an account row shows, or null when the token is healthy. */
export function accountStatusTag(account: AccountSummary): AccountStatusTag | null {
  const status = resolveTokenStatus(account);
  switch (status) {
    case 'present':
      return null;
    case 'absent':
      return NOT_CONFIGURED_TAG;
    case 'unreadable':
      return TOKEN_UNREADABLE_TAG;
    case 'invalid':
      return TOKEN_INVALID_TAG;
    default:
      return assertNever(status);
  }
}

/** The problem block an account row shows, or null when no action is needed. */
export function accountProblem(account: AccountSummary): AccountProblem | null {
  const status = resolveTokenStatus(account);
  switch (status) {
    case 'present':
    case 'absent':
      return null;
    case 'unreadable':
      return TOKEN_UNREADABLE_PROBLEM;
    case 'invalid':
      return TOKEN_INVALID_PROBLEM;
    default:
      return assertNever(status);
  }
}

function assertNever(value: never): never {
  throw new Error(`Unhandled token status: ${String(value)}`);
}

export function affectedStatusLabel(status: AffectedRepositoryStatus | string): string {
  switch (status) {
    case 'eligible':
      return 'Eligible';
    case 'ineligible':
      return 'Ineligible';
    case 'unreachable':
      return 'Unable to verify branch protection';
    case 'credential-unreadable':
      return 'Token unreadable';
    case 'credential-invalid':
      return 'Token invalid';
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

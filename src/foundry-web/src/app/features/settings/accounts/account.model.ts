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
export type TokenPermissionRequirement = Schemas['TokenPermissionRequirement'];
export type DeniedRepositorySummary = Schemas['DeniedRepositorySummary'];

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

type TokenExpiryWire = AccountSummary['tokenExpiry'];

export type TokenExpiryStatus = 'notChecked' | 'unavailable' | 'never' | 'current' | 'expiringSoon' | 'expired';

export type ResolvedTokenExpiry =
  | { readonly status: 'notChecked' | 'unavailable' | 'never' }
  | { readonly status: 'current' | 'expiringSoon' | 'expired'; readonly expiresAt: string };

const NOT_CHECKED_EXPIRY: ResolvedTokenExpiry = { status: 'notChecked' };

/**
 * Narrows the wire token expiry. The server owns the status; this only guards the shape. A missing
 * field (a response predating it), an unknown status, or a dated status with no date reads as notChecked.
 */
export function resolveTokenExpiry(raw: TokenExpiryWire | null | undefined): ResolvedTokenExpiry {
  if (!raw) {
    return NOT_CHECKED_EXPIRY;
  }
  switch (raw.status) {
    case 'notChecked':
    case 'unavailable':
    case 'never':
      return { status: raw.status };
    case 'current':
    case 'expiringSoon':
    case 'expired':
      return raw.expiresAt ? { status: raw.status, expiresAt: raw.expiresAt } : NOT_CHECKED_EXPIRY;
    default:
      return NOT_CHECKED_EXPIRY;
  }
}

/** Whether the expiry warrants a warning: the token expires soon or its expiry date has passed. */
export function isTokenExpiryWarning(expiry: ResolvedTokenExpiry): boolean {
  return expiry.status === 'expiringSoon' || expiry.status === 'expired';
}

export type AccountStatusTone = 'neutral' | 'warning' | 'error';

export type AccountStatusTag =
  | { readonly kind: 'not-configured'; readonly label: 'Not configured'; readonly tone: 'neutral' }
  | { readonly kind: 'token-unreadable'; readonly label: 'Token unreadable'; readonly tone: 'warning' }
  | { readonly kind: 'token-invalid'; readonly label: 'Token invalid'; readonly tone: 'error' }
  | { readonly kind: 'token-expired'; readonly label: 'Token expired'; readonly tone: 'warning' }
  | { readonly kind: 'token-expiring'; readonly label: 'Token expires soon'; readonly tone: 'warning' }
  | { readonly kind: 'missing-permissions'; readonly label: 'Missing permissions'; readonly tone: 'warning' };

/** A problem with the token itself: unreadable, invalid or near its expiry. */
export interface TokenProblem {
  readonly kind: 'token';
  readonly tone: Exclude<AccountStatusTone, 'neutral'>;
  readonly message: string;
  /** Visible button text; the button's aria-label is `${actionLabel} for ${account.name}`. */
  readonly actionLabel: string;
  /** Set for an expiry problem; the row renders the expiry phrase before the message. */
  readonly expiry?: ResolvedTokenExpiry;
}

/** How the operator gives the token what it is missing; each has its own remedy text and actions. */
export type MissingPermissionsRemedy = 'github-fine-grained' | 'github-classic' | 'gitlab';

/** A present token that lacks permissions Foundry needs on some of the account's repositories. */
export interface MissingPermissionsProblem {
  readonly kind: 'missing-permissions';
  readonly tone: 'warning';
  /** Set when the expiry also warrants a warning; the row renders the expiry phrase before the message. */
  readonly expiry?: ResolvedTokenExpiry;
  readonly message: string;
  /** The union of every denied repository's missing labels, in permission-list order. */
  readonly missingPermissions: readonly string[];
  readonly repositories: readonly DeniedRepositorySummary[];
  readonly remedy: MissingPermissionsRemedy;
  readonly remedyText: string;
  /** Where a GitHub fine-grained token's permissions are edited; null for the other remedies. */
  readonly editToken: { readonly url: string; readonly host: string } | null;
}

export type AccountProblem = TokenProblem | MissingPermissionsProblem;

const NOT_CONFIGURED_TAG: AccountStatusTag = { kind: 'not-configured', label: 'Not configured', tone: 'neutral' };
const TOKEN_UNREADABLE_TAG: AccountStatusTag = { kind: 'token-unreadable', label: 'Token unreadable', tone: 'warning' };
const TOKEN_INVALID_TAG: AccountStatusTag = { kind: 'token-invalid', label: 'Token invalid', tone: 'error' };
const TOKEN_EXPIRED_TAG: AccountStatusTag = { kind: 'token-expired', label: 'Token expired', tone: 'warning' };
const TOKEN_EXPIRING_TAG: AccountStatusTag = { kind: 'token-expiring', label: 'Token expires soon', tone: 'warning' };
const MISSING_PERMISSIONS_TAG: AccountStatusTag = {
  kind: 'missing-permissions',
  label: 'Missing permissions',
  tone: 'warning',
};

const TOKEN_UNREADABLE_PROBLEM: TokenProblem = {
  kind: 'token',
  tone: 'warning',
  message: "The account token can't be decrypted. Re-enter it on the account to resume.",
  actionLabel: 'Re-enter token',
};
const TOKEN_INVALID_PROBLEM: TokenProblem = {
  kind: 'token',
  tone: 'error',
  message: 'The provider rejected the account token. Enter a new token on the account to resume.',
  actionLabel: 'Enter new token',
};

// Both take the whole account, not its TokenStatus, because a present token also reads its denied
// repositories and its expiry. Precedence: an absent, unreadable or invalid token first, then missing
// permissions, then expiry.
// An invalid, unreadable or absent token shows no expiry: its date is stale or there is none.

/** The one status tag an account row shows, or null when the token is healthy. */
export function accountStatusTag(account: AccountSummary): AccountStatusTag | null {
  const status = resolveTokenStatus(account);
  switch (status) {
    case 'present':
      return deniedRepositories(account).length > 0
        ? MISSING_PERMISSIONS_TAG
        : expiryStatusTag(resolveTokenExpiry(account.tokenExpiry));
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
      return (
        missingPermissionsProblem(account)
        ?? expiryProblem(resolveTokenExpiry(account.tokenExpiry), providerDisplayName(account.providerType))
      );
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

function expiryStatusTag(expiry: ResolvedTokenExpiry): AccountStatusTag | null {
  switch (expiry.status) {
    case 'expired':
      return TOKEN_EXPIRED_TAG;
    case 'expiringSoon':
      return TOKEN_EXPIRING_TAG;
    default:
      return null;
  }
}

function expiryProblem(expiry: ResolvedTokenExpiry, provider: string): TokenProblem | null {
  switch (expiry.status) {
    case 'expired':
      return {
        kind: 'token',
        tone: 'warning',
        expiry,
        message: `${provider} can reject it at any time, which pauses monitoring.`,
        actionLabel: 'Replace token',
      };
    case 'expiringSoon':
      return {
        kind: 'token',
        tone: 'warning',
        expiry,
        message: 'Enter a replacement token before it expires.',
        actionLabel: 'Replace token',
      };
    default:
      return null;
  }
}

/** The label a classic GitHub token reports when only the user's admin role on the repository can grant the access. */
export const GITHUB_CLASSIC_ADMIN_ROLE_LABEL = 'Admin role on the repository';

const RECHECK_SENTENCE =
  "Foundry re-checks these repositories every hour; a repository's Re-check button checks it right away.";

/** The account's denied repositories; a response predating the field has none. */
export function deniedRepositories(account: AccountSummary): readonly DeniedRepositorySummary[] {
  return (account as { deniedRepositories?: DeniedRepositorySummary[] }).deniedRepositories ?? [];
}

/**
 * The union of the repositories' missing labels in permission-list order. Each repository's labels are
 * already in list order, so a new label goes just before the next label of its repository that the union
 * already holds, or at the end when there is none.
 */
function unionInListOrder(repositories: readonly DeniedRepositorySummary[]): string[] {
  const union: string[] = [];
  for (const repository of repositories) {
    repository.missingPermissions.forEach((label, index) => {
      if (union.includes(label)) {
        return;
      }
      const next = repository.missingPermissions.slice(index + 1).find((later) => union.includes(later));
      union.splice(next === undefined ? union.length : union.indexOf(next), 0, label);
    });
  }
  return union;
}

function missingPermissionsProblem(account: AccountSummary): MissingPermissionsProblem | null {
  const repositories = deniedRepositories(account);
  if (repositories.length === 0) {
    return null;
  }

  const missingPermissions = unionInListOrder(repositories);
  const count = repositories.length;
  const expiry = resolveTokenExpiry(account.tokenExpiry);
  const remedy = missingPermissionsRemedy(account.providerType, missingPermissions);

  return {
    kind: 'missing-permissions',
    tone: 'warning',
    ...(isTokenExpiryWarning(expiry) ? { expiry } : {}),
    message:
      `This token is missing permissions Foundry needs, so Foundry won't start work on ${count} `
      + `${count === 1 ? 'repository' : 'repositories'} until it has them.`,
    missingPermissions,
    repositories,
    remedy,
    remedyText: remedyText(remedy, account.name),
    editToken: remedy === 'github-fine-grained' ? githubEditTokenLink(account.baseUrl) : null,
  };
}

function missingPermissionsRemedy(providerType: string, missingPermissions: readonly string[]): MissingPermissionsRemedy {
  if (providerType.toLowerCase() === 'gitlab') {
    return 'gitlab';
  }
  return missingPermissions.includes(GITHUB_CLASSIC_ADMIN_ROLE_LABEL) ? 'github-classic' : 'github-fine-grained';
}

function remedyText(remedy: MissingPermissionsRemedy, name: string): string {
  switch (remedy) {
    case 'github-fine-grained':
      return `Edit the token's permissions on GitHub, or create a new token, then re-check. ${RECHECK_SENTENCE}`;
    case 'github-classic':
      return `Ask a repository admin to give the token's user, ${name}, the Admin role on these repositories, then re-check. ${RECHECK_SENTENCE}`;
    case 'gitlab':
      return `Give the token's user, ${name}, the Maintainer role on these repositories, then re-check. ${RECHECK_SENTENCE}`;
    default:
      return assertNever(remedy);
  }
}

/** GitHub's fine-grained token list on the account's host: the creation URL's origin, without `/new`. */
function githubEditTokenLink(baseUrl: string): { readonly url: string; readonly host: string } | null {
  try {
    const parsed = new URL(baseUrl);
    if (parsed.protocol !== 'https:' && parsed.protocol !== 'http:') {
      return null;
    }
    return { url: `${parsed.origin}/settings/personal-access-tokens`, host: parsed.host };
  } catch {
    return null;
  }
}

function assertNever(value: never): never {
  throw new Error(`Unhandled value: ${String(value)}`);
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

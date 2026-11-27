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
export type NonConformingTokenSummary = Schemas['NonConformingTokenSummary'];

// Client-side refinements layered over the generated string wire type.
// The API returns these fields as open strings so they are intentionally not generated.
// ProviderType refines CredentialSummary.provider.
// AffectedRepositoryStatus refines AffectedRepository.previousStatus and AffectedRepository.newStatus.
// TokenValidationKind refines ValidateTokenResponse.kind.
// TokenStatus refines CredentialSummary.tokenStatus.
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
  | 'wrongTokenKind'
  | 'identityUnresolved'
  | 'providerMismatch';

export type TokenStatus = 'present' | 'absent' | 'unreadable' | 'invalid';

const KNOWN_TOKEN_STATUSES: ReadonlySet<TokenStatus> = new Set<TokenStatus>([
  'present',
  'absent',
  'unreadable',
  'invalid',
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
  | { readonly kind: 'wrong-token-kind'; readonly label: 'Wrong token kind'; readonly tone: 'warning' }
  | { readonly kind: 'broad-reach'; readonly label: 'Token reaches too much'; readonly tone: 'warning' }
  | { readonly kind: 'not-allowed'; readonly label: 'Extra permissions'; readonly tone: 'warning' }
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
export type MissingPermissionsRemedy =
  | 'github-fine-grained'
  | 'github-wrong-kind'
  | 'gitlab-token'
  | 'gitlab-version';

/** A link to the provider's token page: `edit` opens the token list, `create` opens a new token. */
export interface TokenLink {
  readonly url: string;
  readonly host: string;
  readonly mode: 'edit' | 'create';
}

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
  /** Why the token as a whole fails Foundry's requirements, one line each, such as "Wrong token kind: classic token." */
  readonly reasons: readonly string[];
  readonly remedy: MissingPermissionsRemedy;
  readonly remedyText: string;
  /** Where the operator edits or replaces the token; null when the remedy is not on the provider's token page. */
  readonly tokenLink: TokenLink | null;
}

export type AccountProblem = TokenProblem | MissingPermissionsProblem;

const NOT_CONFIGURED_TAG: AccountStatusTag = { kind: 'not-configured', label: 'Not configured', tone: 'neutral' };
const TOKEN_UNREADABLE_TAG: AccountStatusTag = { kind: 'token-unreadable', label: 'Token unreadable', tone: 'warning' };
const TOKEN_INVALID_TAG: AccountStatusTag = { kind: 'token-invalid', label: 'Token invalid', tone: 'error' };
const TOKEN_EXPIRED_TAG: AccountStatusTag = { kind: 'token-expired', label: 'Token expired', tone: 'warning' };
const TOKEN_EXPIRING_TAG: AccountStatusTag = { kind: 'token-expiring', label: 'Token expires soon', tone: 'warning' };
const WRONG_TOKEN_KIND_TAG: AccountStatusTag = { kind: 'wrong-token-kind', label: 'Wrong token kind', tone: 'warning' };
const BROAD_REACH_TAG: AccountStatusTag = { kind: 'broad-reach', label: 'Token reaches too much', tone: 'warning' };
const NOT_ALLOWED_TAG: AccountStatusTag = { kind: 'not-allowed', label: 'Extra permissions', tone: 'warning' };
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
    case 'present': {
      const nonConforming = nonConformingToken(account);
      if (nonConforming?.wrongKind) {
        return WRONG_TOKEN_KIND_TAG;
      }
      if (nonConforming && nonConforming.broadReach.length > 0) {
        return BROAD_REACH_TAG;
      }
      if (nonConforming && nonConforming.notAllowed.length > 0) {
        return NOT_ALLOWED_TAG;
      }
      return deniedRepositories(account).length > 0
        ? MISSING_PERMISSIONS_TAG
        : expiryStatusTag(resolveTokenExpiry(account.tokenExpiry));
    }
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

/** Why the account's token as a whole fails Foundry's requirements, or null; a response predating the field has none. */
export function nonConformingToken(account: AccountSummary): NonConformingTokenSummary | null {
  return (account as { nonConformingToken?: NonConformingTokenSummary | null }).nonConformingToken ?? null;
}

/** The token kind a `wrongKind` value names, as the account row shows it after "Wrong token kind: ". */
function wrongKindLabel(wrongKind: string): string {
  switch (wrongKind) {
    case 'github-classic':
      return 'classic token';
    case 'gitlab-legacy':
      return 'legacy personal access token';
    case 'gitlab-resource-token':
      return 'group or project access token';
    case 'gitlab-version':
      return 'GitLab older than 19.5';
    default:
      return 'not a fine-grained personal access token';
  }
}

/** How a broad-reach value reads after "This token reaches", or the value itself when Foundry doesn't know it. */
function broadReachPhrase(value: string): string {
  switch (value) {
    // The server no longer reports all-projects, but a verdict stored before it accepted such tokens still carries it.
    case 'all-projects':
      return 'all projects';
    case 'personal-projects':
      return 'your personal projects';
    case 'instance':
      return 'the whole instance';
    default:
      return value;
  }
}

/** How a broad-reach value reads in the account row's "Reaches:" line. */
function broadReachListLabel(value: string): string {
  return value === 'instance' ? 'the instance' : broadReachPhrase(value);
}

/** "a", "a and b", "a, b and c". */
function joinWithAnd(items: readonly string[]): string {
  return items.length <= 1 ? items.join('') : `${items.slice(0, -1).join(', ')} and ${items[items.length - 1]}`;
}

/**
 * Why the account's token as a whole fails Foundry's requirements, one line each, in display order. The
 * row shows the missing permissions on their own line after these.
 */
function nonConformingReasons(nonConforming: NonConformingTokenSummary | null): string[] {
  if (nonConforming === null) {
    return [];
  }
  return [
    ...(nonConforming.wrongKind ? [`Wrong token kind: ${wrongKindLabel(nonConforming.wrongKind)}.`] : []),
    ...(nonConforming.broadReach.length > 0
      ? [`Reaches: ${nonConforming.broadReach.map(broadReachListLabel).join('; ')}.`]
      : []),
    ...(nonConforming.notAllowed.length > 0 ? [`Not allowed: ${nonConforming.notAllowed.join('; ')}.`] : []),
  ];
}

function missingPermissionsProblem(account: AccountSummary): MissingPermissionsProblem | null {
  const repositories = deniedRepositories(account);
  const nonConforming = nonConformingToken(account);
  const wrongKind = nonConforming?.wrongKind ?? null;
  if (repositories.length === 0 && nonConforming === null) {
    return null;
  }

  const missingPermissions = unionInListOrder(repositories);
  const count = repositories.length;
  const repositoryNoun = count === 1 ? 'repository' : 'repositories';
  const expiry = resolveTokenExpiry(account.tokenExpiry);
  const remedy = missingPermissionsRemedy(account.providerType, wrongKind);

  return {
    kind: 'missing-permissions',
    tone: 'warning',
    ...(isTokenExpiryWarning(expiry) ? { expiry } : {}),
    message:
      nonConforming === null
        ? `This token is missing permissions Foundry needs, so Foundry won't start work on ${count} `
          + `${repositoryNoun} until it has them.`
        : `This token doesn't meet Foundry's requirements, so Foundry won't start work on ${count} `
          + `${repositoryNoun} until it does.`,
    missingPermissions,
    repositories,
    reasons: nonConformingReasons(nonConforming),
    remedy,
    remedyText: remedyText(remedy, account.name, missingPermissions),
    tokenLink: tokenLink(remedy, account.baseUrl),
  };
}

const MAINTAINER_ROLE_LABEL = 'Maintainer role';

function missingPermissionsRemedy(providerType: string, wrongKind: string | null): MissingPermissionsRemedy {
  if (providerType.toLowerCase() === 'gitlab') {
    return wrongKind === 'gitlab-version' ? 'gitlab-version' : 'gitlab-token';
  }
  return wrongKind === null ? 'github-fine-grained' : 'github-wrong-kind';
}

function maintainerSentence(name: string): string {
  return `Give the token's user, ${name}, the Maintainer role on these repositories, then re-check.`;
}

function remedyText(remedy: MissingPermissionsRemedy, name: string, missingPermissions: readonly string[]): string {
  switch (remedy) {
    case 'github-fine-grained':
      return `Edit the token's permissions on GitHub, or create a new token, then re-check. ${RECHECK_SENTENCE}`;
    case 'github-wrong-kind':
      return `Create a fine-grained personal access token on GitHub, then replace this one. A classic token can't be converted. ${RECHECK_SENTENCE}`;
    case 'gitlab-token': {
      const create =
        'Create a fine-grained token on GitLab with exactly the required permissions, on all groups and projects or on selected projects only, then replace this one.';
      return missingPermissions.includes(MAINTAINER_ROLE_LABEL)
        ? `${maintainerSentence(name)} ${create} ${RECHECK_SENTENCE}`
        : `${create} ${RECHECK_SENTENCE}`;
    }
    case 'gitlab-version':
      return `Upgrade GitLab to 19.5 or later, then re-check. ${RECHECK_SENTENCE}`;
    default:
      return assertNever(remedy);
  }
}

const GITHUB_TOKEN_LIST_TEMPLATE = '{baseUrl}/settings/personal-access-tokens';
const GITHUB_NEW_TOKEN_TEMPLATE = '{baseUrl}/settings/personal-access-tokens/new';
const GITLAB_NEW_TOKEN_TEMPLATE = '{baseUrl}/-/user_settings/personal_access_tokens';

/** Where the remedy sends the operator on the account's host: the provider's token list to edit, or its new-token page. */
function tokenLink(remedy: MissingPermissionsRemedy, baseUrl: string): TokenLink | null {
  const link = tokenLinkTemplate(remedy);
  if (link === null) {
    return null;
  }
  const url = creationUrl(link.template, baseUrl);
  return url === null ? null : { url, host: new URL(url).host, mode: link.mode };
}

function tokenLinkTemplate(
  remedy: MissingPermissionsRemedy,
): { readonly template: string; readonly mode: TokenLink['mode'] } | null {
  switch (remedy) {
    case 'github-fine-grained':
      return { template: GITHUB_TOKEN_LIST_TEMPLATE, mode: 'edit' };
    case 'github-wrong-kind':
      return { template: GITHUB_NEW_TOKEN_TEMPLATE, mode: 'create' };
    case 'gitlab-token':
      return { template: GITLAB_NEW_TOKEN_TEMPLATE, mode: 'create' };
    case 'gitlab-version':
      return null;
    default:
      return assertNever(remedy);
  }
}

function isHttpUrl(url: string): boolean {
  try {
    const parsed = new URL(url);
    return parsed.protocol === 'http:' || parsed.protocol === 'https:';
  } catch {
    return false;
  }
}

/**
 * The provider page `template` points at on the host of `baseUrl`, or null when either is not an http(s) URL.
 * A template without `{baseUrl}` is used as is.
 */
export function creationUrl(template: string, baseUrl: string): string | null {
  if (!template.includes('{baseUrl}')) {
    return isHttpUrl(template) ? template : null;
  }
  const rawBaseUrl = baseUrl.trim();
  if (!rawBaseUrl) {
    return null;
  }
  try {
    const parsed = new URL(rawBaseUrl);
    if (!isHttpUrl(parsed.origin)) {
      return null;
    }
    const url = template.replace('{baseUrl}', parsed.origin);
    return isHttpUrl(url) ? url : null;
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
  'wrongTokenKind',
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

/** What the validation result says about a token Foundry refuses for its kind; an unknown kind reads generically. */
export function wrongTokenKindMessage(wrongKind: string | null | undefined): string {
  switch (wrongKind) {
    case 'github-classic':
      return 'This is a classic token. Foundry needs a fine-grained personal access token (it starts with github_pat_).';
    case 'gitlab-legacy':
      return 'This is a legacy personal access token. Foundry needs a fine-grained personal access token.';
    case 'gitlab-resource-token':
      return 'This is a group or project access token. Foundry needs a fine-grained personal access token.';
    case 'gitlab-version':
      return "This GitLab is older than 19.5, which doesn't support fine-grained tokens. Upgrade GitLab to 19.5 or later.";
    default:
      return 'Foundry needs a fine-grained personal access token.';
  }
}

/** The provider's token creation page for the requirements on the host of `baseUrl`, or null when there is none. */
export function createTokenLink(
  requirements: TokenRequirements | null,
  baseUrl: string,
): { readonly url: string; readonly host: string } | null {
  const url = requirements ? creationUrl(requirements.creationUrlTemplate, baseUrl) : null;
  return url === null ? null : { url, host: new URL(url).host };
}

/** Whether the validation result for a token Foundry refuses for its kind offers a link to create a new token. */
export function wrongTokenKindOffersCreateLink(wrongKind: string | null | undefined): boolean {
  return wrongKind !== 'gitlab-version';
}

/** The permissions a validated token holds that Foundry doesn't allow; a response predating the field has none. */
export function validationNotAllowed(result: TokenValidationResult): readonly string[] {
  return (result as { notAllowed?: string[] }).notAllowed ?? [];
}

/** How a validated token reaches beyond selected projects; a response predating the field reaches no further. */
export function validationBroadReach(result: TokenValidationResult): readonly string[] {
  return (result as { broadReach?: string[] }).broadReach ?? [];
}

/** The validation result's sentence for a token reaching more than selected projects. */
export function broadReachMessage(broadReach: readonly string[]): string {
  return `This token reaches ${joinWithAnd(broadReach.map(broadReachPhrase))}. Foundry needs a token limited to selected projects or reaching all groups and projects.`;
}

/** Whether nothing about an authenticated token needs fixing: no missing, not-allowed or broad-reach finding. */
export function isConformingToken(result: TokenValidationResult): boolean {
  return (
    result.missingScopes.length === 0
    && validationNotAllowed(result).length === 0
    && validationBroadReach(result).length === 0
  );
}

/** Whether the account form and setup wizard may save the token the result describes. */
export function isSaveEligible(result: TokenValidationResult): boolean {
  return narrowTokenValidationKind(result) === 'authenticated' && isConformingToken(result);
}

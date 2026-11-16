// Hand-written: the status is a discriminated union the OpenAPI generator cannot express.
// Update these types when the GET /api/package-sources contract changes.

export interface PackageSourceRepository {
  id: string;
  slug: string;
}

export interface PackageSourceCredential {
  username: string;
  updatedAt: string;
}

export type PackageSourceStatus =
  | { kind: 'reachableViaCredential' }
  | { kind: 'reachableViaAccount'; accountName: string; host: string }
  | { kind: 'reachableAnonymously' }
  | { kind: 'unreachable'; httpStatus: number | null; reason: string }
  | { kind: 'unchecked' };

export type PackageSourceStatusKind = PackageSourceStatus['kind'];

export interface PackageSource {
  id: string;
  url: string;
  repositories: PackageSourceRepository[];
  status: PackageSourceStatus;
  lastCheckedAt: string | null;
  credential: PackageSourceCredential | null;
}

export interface SaveCredentialRequest {
  username: string;
  token: string;
}

export type CredentialField = 'username' | 'token';

export type CredentialFieldErrors = Partial<Record<CredentialField, string>>;

export type PendingCheckAction = 'saved' | 'removed';

export interface PendingCheck {
  baseline: string | null;
  action: PendingCheckAction;
}

export const USERNAME_MAX_LENGTH = 255;
export const TOKEN_MAX_LENGTH = 2048;

const UNREACHABLE_RULE_PREFIX = 'package-source-unreachable:';

/** The DOM id of a source's card. Every anchor read and write goes through this function. */
export function packageSourceAnchorId(url: string): string {
  // FNV-1a, 32-bit: stable across sessions and short enough for an id.
  let hash = 0x811c9dc5;
  for (let i = 0; i < url.length; i++) {
    hash ^= url.charCodeAt(i);
    hash = Math.imul(hash, 0x01000193);
  }
  return `package-source-${(hash >>> 0).toString(36)}`;
}

/** The source URL an eligibility violation rule names, or null when the rule is about something else. */
export function unreachablePackageSourceUrl(rule: string): string | null {
  return rule.startsWith(UNREACHABLE_RULE_PREFIX) ? rule.slice(UNREACHABLE_RULE_PREFIX.length) : null;
}

export function isReachable(status: PackageSourceStatus): boolean {
  switch (status.kind) {
    case 'reachableViaCredential':
    case 'reachableViaAccount':
    case 'reachableAnonymously':
      return true;
    case 'unreachable':
    case 'unchecked':
      return false;
    default:
      return assertNever(status);
  }
}

/** The announcement for a source whose pending check resolved. */
export function resolvedAnnouncement(source: PackageSource): string {
  const status = source.status;
  switch (status.kind) {
    case 'reachableViaCredential':
      return `${source.url} is reachable with credential ${source.credential?.username ?? ''}.`;
    case 'reachableViaAccount':
      return `${source.url} is reachable with account ${status.accountName} · ${status.host}.`;
    case 'reachableAnonymously':
      return `${source.url} is reachable without a credential.`;
    case 'unreachable':
      return status.httpStatus !== null
        ? `${source.url} is still unreachable: HTTP ${status.httpStatus}.`
        : `${source.url} is still unreachable: ${status.reason}.`;
    case 'unchecked':
      return `${source.url} has not been checked yet.`;
    default:
      return assertNever(status);
  }
}

export function validateCredential(request: SaveCredentialRequest): CredentialFieldErrors {
  const errors: CredentialFieldErrors = {};
  if (request.username.length === 0) {
    errors.username = 'Username is required.';
  } else if (request.username.length > USERNAME_MAX_LENGTH) {
    errors.username = `Username must be ${USERNAME_MAX_LENGTH} characters or fewer.`;
  }
  if (request.token.length === 0) {
    errors.token = 'Token is required.';
  } else if (request.token.length > TOKEN_MAX_LENGTH) {
    errors.token = `Token must be ${TOKEN_MAX_LENGTH} characters or fewer.`;
  }
  return errors;
}

export function assertNever(value: never): never {
  throw new Error(`Unhandled package source status: ${JSON.stringify(value)}`);
}

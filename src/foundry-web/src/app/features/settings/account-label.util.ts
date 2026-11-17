import { AccountSummary } from './accounts/account.model';

const MAX_VISIBLE_NAMESPACES = 4;

export function resolveHost(baseUrl: string): string {
  try {
    return new URL(baseUrl).host;
  } catch {
    return baseUrl;
  }
}

export function sortNamespaces(namespaces: readonly string[]): string[] {
  return [...namespaces].sort((a, b) => a.localeCompare(b));
}

export function accountOptionLabel(account: AccountSummary): string {
  const host = resolveHost(account.baseUrl);
  const sorted = sortNamespaces(account.namespaces);

  if (sorted.length === 0) {
    return `${account.name} (${host})`;
  }

  const visible = sorted.slice(0, MAX_VISIBLE_NAMESPACES);
  const overflow = sorted.length - MAX_VISIBLE_NAMESPACES;
  const namespaceSegment = overflow > 0
    ? `${visible.join(', ')} +${overflow} more`
    : visible.join(', ');

  return `${account.name} — ${namespaceSegment} (${host})`;
}

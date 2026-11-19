/**
 * Client-side shape check for the Webhook Public Base URL (ADR 0116). Host rules (localhost, loopback,
 * link-local, unspecified) and reachability stay server-side, so the client never rejects a URL the
 * server accepts.
 */
export type WebhookUrlClientError = 'required' | 'not-https' | 'query-or-fragment' | 'userinfo';

export function validateWebhookPublicBaseUrl(value: string): WebhookUrlClientError | null {
  const trimmed = value.trim();
  if (trimmed === '') {
    return 'required';
  }
  let url: URL;
  try {
    url = new URL(trimmed);
  } catch {
    return 'not-https';
  }
  if (url.protocol !== 'https:' || url.hostname === '') {
    return 'not-https';
  }
  // `URL` drops an empty "?" or "#", so check the raw text as the server does.
  if (url.search !== '' || url.hash !== '' || trimmed.includes('?') || trimmed.includes('#')) {
    return 'query-or-fragment';
  }
  if (url.username !== '' || url.password !== '') {
    return 'userinfo';
  }
  return null;
}

const CLIENT_ERROR_MESSAGES: Record<WebhookUrlClientError, string> = {
  required: 'Enter the public https URL your tunnel or proxy serves.',
  'not-https': 'Use an absolute https URL, for example https://hooks.example.com.',
  'query-or-fragment': 'Remove the query (?…) or fragment (#…) from the URL.',
  userinfo: 'Remove the user name and password from the URL.',
};

export function webhookUrlClientErrorMessage(kind: WebhookUrlClientError): string {
  return CLIENT_ERROR_MESSAGES[kind];
}

/** The host of a URL that passed {@link validateWebhookPublicBaseUrl}; the trimmed text otherwise. */
export function webhookUrlHost(value: string): string {
  const trimmed = value.trim();
  try {
    return new URL(trimmed).host;
  } catch {
    return trimmed;
  }
}

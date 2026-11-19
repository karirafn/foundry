import { validateWebhookPublicBaseUrl, webhookUrlClientErrorMessage, webhookUrlHost } from './webhook-url';

describe('validateWebhookPublicBaseUrl', () => {
  it.each([
    ['', 'required'],
    ['   ', 'required'],
    ['http://hooks.example.com', 'not-https'],
    ['hooks.example.com', 'not-https'],
    ['ftp://hooks.example.com', 'not-https'],
    ['https://hooks.example.com/?token=1', 'query-or-fragment'],
    ['https://hooks.example.com/#top', 'query-or-fragment'],
    ['https://hooks.example.com/?', 'query-or-fragment'],
    ['https://user:pass@hooks.example.com', 'userinfo'],
  ])('rejects %j as %s', (value, kind) => {
    expect(validateWebhookPublicBaseUrl(value)).toBe(kind);
  });

  it.each([
    'https://hooks.example.com',
    ' https://hooks.example.com/foundry/ ',
    'https://localhost',
    'https://127.0.0.1',
    'https://10.0.0.5',
    'https://laptop.tailnet.ts.net',
  ])('accepts %j, leaving host rules to the server', value => {
    expect(validateWebhookPublicBaseUrl(value)).toBeNull();
  });
});

describe('webhookUrlClientErrorMessage', () => {
  it.each([
    ['required', 'Enter the public https URL your tunnel or proxy serves.'],
    ['not-https', 'Use an absolute https URL, for example https://hooks.example.com.'],
    ['query-or-fragment', 'Remove the query (?…) or fragment (#…) from the URL.'],
    ['userinfo', 'Remove the user name and password from the URL.'],
  ] as const)('words %s', (kind, message) => {
    expect(webhookUrlClientErrorMessage(kind)).toBe(message);
  });
});

describe('webhookUrlHost', () => {
  it('returns the host with its port', () => {
    expect(webhookUrlHost(' https://hooks.example.com:8443/foundry ')).toBe('hooks.example.com:8443');
  });
});

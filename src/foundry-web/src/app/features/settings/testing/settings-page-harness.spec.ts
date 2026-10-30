import {
  API_KEY_RESPONSE,
  BASE_RESPONSE,
  CREDENTIALS_API_KEY,
  CREDENTIALS_API_KEY_NOT_CONFIGURED,
  CREDENTIALS_API_KEY_UNREADABLE,
  CREDENTIALS_OAUTH,
  CREDENTIALS_OAUTH_NOT_CONFIGURED,
  CREDENTIALS_OAUTH_RELOGIN_NEEDED,
  IMAGE_FLAGS_DEFAULTS,
  OAUTH_NOT_CONFIGURED_RESPONSE,
  OAUTH_RELOGIN_NEEDED_RESPONSE,
  OAUTH_RESPONSE,
  flushSettings,
  mockRateBudgetService,
  mockSystemSignalR,
  setupSettingsPage,
} from './settings-page-harness';

describe('settings-page-harness exports', () => {
  it('should export all required fixture constants', () => {
    // Arrange / Act / Assert — presence of the exports proves compilation
    expect(IMAGE_FLAGS_DEFAULTS).toBeDefined();
    expect(BASE_RESPONSE).toBeDefined();
    expect(API_KEY_RESPONSE).toBeDefined();
    expect(CREDENTIALS_API_KEY).toBeDefined();
    expect(CREDENTIALS_API_KEY_NOT_CONFIGURED).toBeDefined();
    expect(CREDENTIALS_API_KEY_UNREADABLE).toBeDefined();
    expect(CREDENTIALS_OAUTH).toBeDefined();
    expect(CREDENTIALS_OAUTH_NOT_CONFIGURED).toBeDefined();
    expect(CREDENTIALS_OAUTH_RELOGIN_NEEDED).toBeDefined();
    expect(OAUTH_RESPONSE).toBeDefined();
    expect(OAUTH_NOT_CONFIGURED_RESPONSE).toBeDefined();
    expect(OAUTH_RELOGIN_NEEDED_RESPONSE).toBeDefined();
    expect(mockSystemSignalR).toBeDefined();
    expect(mockRateBudgetService).toBeDefined();
    expect(typeof setupSettingsPage).toBe('function');
    expect(typeof flushSettings).toBe('function');
  });
});

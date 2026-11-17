import {
  isReachable,
  packageSourceAnchorId,
  resolvedAnnouncement,
  unreachablePackageSourceUrl,
  validateCredential,
} from './package-source.model';
import { OTHER_SOURCE_URL, SOURCE_URL, makeSource } from '../testing/package-source.fixture';

describe('packageSourceAnchorId', () => {
  it('returns the same id for the same URL', () => {
    expect(packageSourceAnchorId(SOURCE_URL)).toBe(packageSourceAnchorId(SOURCE_URL));
  });

  it('returns different ids for different URLs', () => {
    expect(packageSourceAnchorId(SOURCE_URL)).not.toBe(packageSourceAnchorId(OTHER_SOURCE_URL));
  });

  it('returns an id usable as a CSS id selector', () => {
    expect(packageSourceAnchorId(SOURCE_URL)).toMatch(/^package-source-[0-9a-z]+$/);
  });
});

describe('unreachablePackageSourceUrl', () => {
  it('returns the URL a package-source-unreachable rule names', () => {
    expect(unreachablePackageSourceUrl(`package-source-unreachable:${SOURCE_URL}`)).toBe(SOURCE_URL);
  });

  it('returns null for any other rule', () => {
    expect(unreachablePackageSourceUrl('nuget-config-invalid')).toBeNull();
  });
});

describe('validateCredential', () => {
  it('accepts a non-empty username and token without trimming', () => {
    expect(validateCredential({ username: ' ', token: ' ' })).toEqual({});
  });

  it('requires both fields', () => {
    expect(validateCredential({ username: '', token: '' })).toEqual({
      username: 'Username is required.',
      token: 'Token is required.',
    });
  });

  it('accepts the maximum lengths', () => {
    expect(validateCredential({ username: 'u'.repeat(255), token: 't'.repeat(2048) })).toEqual({});
  });

  it('rejects values over the maximum lengths', () => {
    expect(validateCredential({ username: 'u'.repeat(256), token: 't'.repeat(2049) })).toEqual({
      username: 'Username must be 255 characters or fewer.',
      token: 'Token must be 2048 characters or fewer.',
    });
  });
});

describe('isReachable', () => {
  it('is true for each reachable kind', () => {
    expect(isReachable({ kind: 'reachableViaCredential' })).toBe(true);
    expect(isReachable({ kind: 'reachableViaAccount', accountName: 'a', host: 'h' })).toBe(true);
    expect(isReachable({ kind: 'reachableAnonymously' })).toBe(true);
  });

  it('is false for unreachable and unchecked', () => {
    expect(isReachable({ kind: 'unreachable', httpStatus: 401, reason: 'r' })).toBe(false);
    expect(isReachable({ kind: 'unchecked' })).toBe(false);
  });
});

describe('resolvedAnnouncement', () => {
  it('names the credential for reachableViaCredential', () => {
    const source = makeSource({
      status: { kind: 'reachableViaCredential' },
      credential: { username: 'ci-reader', updatedAt: '2026-11-16T10:00:00Z' },
    });
    expect(resolvedAnnouncement(source)).toBe(`${SOURCE_URL} is reachable with credential ci-reader.`);
  });

  it('names the account and host for reachableViaAccount', () => {
    expect(resolvedAnnouncement(makeSource())).toBe(
      `${SOURCE_URL} is reachable with account acme-bot · gitlab.example.com.`,
    );
  });

  it('says no credential for reachableAnonymously', () => {
    expect(resolvedAnnouncement(makeSource({ status: { kind: 'reachableAnonymously' } }))).toBe(
      `${SOURCE_URL} is reachable without a credential.`,
    );
  });

  it('names the HTTP status when unreachable with one', () => {
    const source = makeSource({ status: { kind: 'unreachable', httpStatus: 401, reason: 'service index returned 401' } });
    expect(resolvedAnnouncement(source)).toBe(`${SOURCE_URL} is still unreachable: HTTP 401.`);
  });

  it('names the reason when unreachable without a status', () => {
    const source = makeSource({ status: { kind: 'unreachable', httpStatus: null, reason: 'probe timed out' } });
    expect(resolvedAnnouncement(source)).toBe(`${SOURCE_URL} is still unreachable: probe timed out.`);
  });
});

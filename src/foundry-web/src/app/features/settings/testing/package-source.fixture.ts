import { Subject } from 'rxjs';
import { PackageSource } from '../package-sources/package-source.model';

export const SOURCE_URL = 'https://gitlab.example.com/api/v4/groups/42/-/packages/nuget/index.json';
export const OTHER_SOURCE_URL = 'https://feeds.example.org/v3/index.json';

export function makeSource(overrides: Partial<PackageSource> = {}): PackageSource {
  return {
    id: '00000000-0000-0000-0000-000000000001',
    url: SOURCE_URL,
    repositories: [{ id: 'repo-1', slug: 'acme/api' }],
    status: { kind: 'reachableViaAccount', accountName: 'acme-bot', host: 'gitlab.example.com' },
    lastCheckedAt: '2026-11-16T10:00:00Z',
    credential: null,
    ...overrides,
  };
}

/** Stands in for SystemSignalRService: tests emit on its subjects to simulate hub events. */
export function makeSignalRStub(): { reconnected: Subject<void>; reloadTrigger: Subject<void> } {
  return { reconnected: new Subject<void>(), reloadTrigger: new Subject<void>() };
}

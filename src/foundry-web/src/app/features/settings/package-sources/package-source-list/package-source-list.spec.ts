import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { PackageSourceListComponent } from './package-source-list';
import { PackageSource, PendingCheck, packageSourceAnchorId } from '../package-source.model';
import { OTHER_SOURCE_URL, SOURCE_URL, makeSource } from '../../testing/package-source.fixture';

interface Inputs {
  sources?: PackageSource[];
  loading?: boolean;
  error?: string | null;
  removingId?: string | null;
  pendingChecks?: ReadonlyMap<string, PendingCheck>;
  targetUrl?: string | null;
}

function render(inputs: Inputs = {}) {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({ imports: [PackageSourceListComponent], providers: [provideRouter([])] });
  const fixture = TestBed.createComponent(PackageSourceListComponent);
  for (const [key, value] of Object.entries(inputs)) {
    fixture.componentRef.setInput(key, value);
  }
  fixture.detectChanges();
  return { fixture, el: fixture.nativeElement as HTMLElement };
}

function text(el: Element | null): string {
  return (el?.textContent ?? '').replace(/\s+/g, ' ').trim();
}

const UNREACHABLE = { kind: 'unreachable', httpStatus: 401, reason: 'service index returned 401' } as const;
const CREDENTIAL = { username: 'ci-reader', updatedAt: '2026-11-16T09:00:00Z' };

describe('PackageSourceListComponent', () => {
  it('shows a spinner with a status label while loading', () => {
    const { el } = render({ loading: true });

    const status = el.querySelector('.package-source-list__loading');
    expect(status?.getAttribute('role')).toBe('status');
    expect(status?.querySelector('fd-spinner')).not.toBeNull();
    expect(text(status)).toBe('Loading package sources');
  });

  it('shows the load error with a Retry button that emits retry', () => {
    const { fixture, el } = render({ error: 'boom' });
    let retried = 0;
    fixture.componentInstance.retry.subscribe(() => retried++);

    const alert = el.querySelector('[role="alert"]');
    expect(text(alert)).toContain('Failed to load package sources');
    alert?.querySelector<HTMLButtonElement>('button')?.click();

    expect(retried).toBe(1);
  });

  it('shows the empty state with a link to repositories and no add button', () => {
    const { el } = render({ sources: [] });

    const empty = el.querySelector('.package-source-list__empty');
    expect(empty?.getAttribute('role')).toBe('status');
    expect(text(empty?.querySelector('.package-source-list__empty-heading') ?? null)).toBe('No package sources detected');
    expect(text(empty)).toContain(
      'None of your monitored repositories declares a NuGet source other than nuget.org in its root nuget.config.',
    );
    expect(empty?.querySelector('a')?.getAttribute('href')).toBe('/settings/repositories');
    expect(el.querySelector('button')).toBeNull();
  });

  it('lists sources sorted by URL, each card anchored by its URL', () => {
    const { el } = render({
      sources: [makeSource({ id: 'b', url: SOURCE_URL }), makeSource({ id: 'a', url: OTHER_SOURCE_URL })],
    });

    const items = Array.from(el.querySelectorAll('li.package-source-list__item'));
    expect(items.map((li) => text(li.querySelector('.package-source-list__url')))).toEqual([OTHER_SOURCE_URL, SOURCE_URL]);
    expect(items[1].id).toBe(packageSourceAnchorId(SOURCE_URL));
    expect(items[1].querySelector('.package-source-list__url')?.getAttribute('tabindex')).toBe('-1');
  });

  it('shows no summary callout when every source is reachable', () => {
    const { el } = render({ sources: [makeSource()] });
    expect(el.querySelector('.package-source-list__summary')).toBeNull();
  });

  it('summarises one unreachable source in the singular', () => {
    const { el } = render({ sources: [makeSource({ status: UNREACHABLE })] });

    const summary = el.querySelector('.package-source-list__summary');
    expect(text(summary?.querySelector('strong') ?? null)).toBe('1 package source is unreachable.');
    expect(text(summary)).toContain('Repositories that declare them are Ineligible and dispatch nothing until a check succeeds.');
    expect(summary?.getAttribute('role')).toBeNull();
  });

  it('summarises several unreachable sources in the plural', () => {
    const { el } = render({
      sources: [makeSource({ status: UNREACHABLE }), makeSource({ id: 'x', url: OTHER_SOURCE_URL, status: UNREACHABLE })],
    });
    expect(text(el.querySelector('.package-source-list__summary strong'))).toBe('2 package sources are unreachable.');
  });

  it('offers + Add credential for a source without one, emitting editCredential', () => {
    const source = makeSource();
    const { fixture, el } = render({ sources: [source] });
    const emitted: PackageSource[] = [];
    fixture.componentInstance.editCredential.subscribe((s) => emitted.push(s));

    const add = el.querySelector<HTMLButtonElement>('.package-source-list__add-btn')!;
    add.click();

    expect(text(add)).toBe('+ Add credential');
    expect(add.getAttribute('aria-label')).toBe(`Add credential for ${SOURCE_URL}`);
    expect(el.querySelector('fd-row-actions')).toBeNull();
    expect(emitted).toEqual([source]);
  });

  it('offers replace and remove row actions for a source with a credential', () => {
    const source = makeSource({ credential: CREDENTIAL });
    const { fixture, el } = render({ sources: [source] });
    const edited: PackageSource[] = [];
    const removed: PackageSource[] = [];
    fixture.componentInstance.editCredential.subscribe((s) => edited.push(s));
    fixture.componentInstance.removeCredential.subscribe((s) => removed.push(s));

    expect(el.querySelector('.package-source-list__add-btn')).toBeNull();
    const edit = el.querySelector<HTMLButtonElement>(`[aria-label="Replace credential for ${SOURCE_URL}"]`)!;
    edit.click();
    el.querySelector<HTMLButtonElement>(`[aria-label="Remove credential for ${SOURCE_URL}"]`)!.click();

    expect(edited).toEqual([source]);
    expect(removed).toEqual([source]);
  });

  it('labels the remove action as busy while the credential is being removed', () => {
    const source = makeSource({ credential: CREDENTIAL });
    const { el } = render({ sources: [source], removingId: source.id });
    expect(el.querySelector(`[aria-label="Removing credential for ${SOURCE_URL}…"]`)).not.toBeNull();
  });

  it('links each declaring repository and caps the chips at four', () => {
    const repositories = Array.from({ length: 6 }, (_, i) => ({ id: `r${i}`, slug: `acme/repo-${i}` }));
    const { el } = render({ sources: [makeSource({ repositories })] });

    const strip = el.querySelector(`[aria-label="Repositories declaring ${SOURCE_URL}"]`);
    expect(strip?.getAttribute('role')).toBe('group');
    expect(text(strip)).toContain('Declared by');
    const links = Array.from(strip?.querySelectorAll('a') ?? []);
    expect(links.map((a) => a.getAttribute('href'))).toEqual(['/settings/repositories/r0', '/settings/repositories/r1', '/settings/repositories/r2', '/settings/repositories/r3']);
    const overflow = strip?.querySelector('[role="img"]');
    expect(text(overflow ?? null)).toBe('+2');
    expect(overflow?.getAttribute('aria-label')).toBe('2 more repositories: acme/repo-4, acme/repo-5');
  });

  it('marks the card the deep link targets', () => {
    const { el } = render({
      sources: [makeSource(), makeSource({ id: 'x', url: OTHER_SOURCE_URL })],
      targetUrl: SOURCE_URL,
    });

    expect(el.querySelector(`#${packageSourceAnchorId(SOURCE_URL)}`)?.classList).toContain('package-source-list__item--targeted');
    expect(el.querySelector(`#${packageSourceAnchorId(OTHER_SOURCE_URL)}`)?.classList).not.toContain('package-source-list__item--targeted');
  });

  it('passes a pending check to the card status', () => {
    const source = makeSource();
    const { el } = render({
      sources: [source],
      pendingChecks: new Map([[source.id, { baseline: source.lastCheckedAt, action: 'saved' }]]),
    });
    expect(text(el.querySelector('.package-source-status__label'))).toBe('Credential saved — checking access…');
  });
});

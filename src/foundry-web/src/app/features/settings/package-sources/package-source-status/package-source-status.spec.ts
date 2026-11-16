import { TestBed } from '@angular/core/testing';
import { PackageSourceStatusComponent } from './package-source-status';
import { PackageSource, PendingCheckAction } from '../package-source.model';
import { SOURCE_URL, makeSource } from '../../testing/package-source.fixture';

const CREDENTIAL = { username: 'ci-reader', updatedAt: '2026-11-16T09:00:00Z' };

function render(source: PackageSource, pending: { action: PendingCheckAction } | null = null) {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({ imports: [PackageSourceStatusComponent] });
  const fixture = TestBed.createComponent(PackageSourceStatusComponent);
  fixture.componentRef.setInput('source', source);
  fixture.componentRef.setInput('pending', pending);
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  return { fixture, el };
}

function text(el: Element | null): string {
  return (el?.textContent ?? '').replace(/\s+/g, ' ').trim();
}

describe('PackageSourceStatusComponent', () => {
  it('labels the strip with the source URL', () => {
    const { el } = render(makeSource());
    expect(el.querySelector('[role="group"]')?.getAttribute('aria-label')).toBe(`Status of ${SOURCE_URL}`);
  });

  it('shows reachableViaCredential with the credential username and the check time', () => {
    const { el } = render(makeSource({ status: { kind: 'reachableViaCredential' }, credential: CREDENTIAL }));

    expect(el.querySelector('.package-source-status__dot--reachable')?.getAttribute('aria-hidden')).toBe('true');
    expect(text(el.querySelector('.package-source-status__label'))).toBe('Reachable');
    expect(text(el.querySelector('.package-source-status__meta'))).toBe('via credential ci-reader');
    expect(el.querySelector('.package-source-status__username')?.textContent).toBe('ci-reader');
    expect(el.querySelector('time')?.getAttribute('datetime')).toBe('2026-11-16T10:00:00Z');
  });

  it('shows reachableViaAccount with the account and host', () => {
    const { el } = render(makeSource());

    expect(text(el.querySelector('.package-source-status__label'))).toBe('Reachable');
    expect(text(el.querySelector('.package-source-status__meta'))).toBe('via account acme-bot · gitlab.example.com');
    expect(el.querySelector('time')).not.toBeNull();
  });

  it('shows reachableAnonymously as needing no credential', () => {
    const { el } = render(makeSource({ status: { kind: 'reachableAnonymously' } }));

    expect(el.querySelector('.package-source-status__dot--reachable')).not.toBeNull();
    expect(text(el.querySelector('.package-source-status__label'))).toBe('Reachable');
    expect(text(el.querySelector('.package-source-status__meta'))).toBe('no credential needed');
    expect(el.querySelector('time')).not.toBeNull();
  });

  describe('unreachable', () => {
    it('shows the warning dot, the credential and the time', () => {
      const { el } = render(
        makeSource({ status: { kind: 'unreachable', httpStatus: 401, reason: 'service index returned 401' }, credential: CREDENTIAL }),
      );

      expect(el.querySelector('.package-source-status__dot--unreachable')).not.toBeNull();
      expect(text(el.querySelector('.package-source-status__label'))).toBe('Unreachable');
      expect(text(el.querySelector('.package-source-status__meta'))).toBe('credential ci-reader');
      expect(el.querySelector('time')).not.toBeNull();
    });

    it('says no credential is registered when there is none', () => {
      const { el } = render(makeSource({ status: { kind: 'unreachable', httpStatus: 401, reason: 'r' } }));
      expect(text(el.querySelector('.package-source-status__meta'))).toBe('no credential registered');
    });

    it('explains an HTTP failure with the credential in the warning line', () => {
      const { el } = render(
        makeSource({ status: { kind: 'unreachable', httpStatus: 403, reason: 'search returned 403' }, credential: CREDENTIAL }),
      );

      const warning = el.querySelector(`[aria-label="Problem with ${SOURCE_URL}"]`);
      expect(warning?.getAttribute('role')).toBe('group');
      expect(warning?.getAttribute('role')).not.toBe('alert');
      expect(text(warning)).toContain("Can't read this source with credential ci-reader (HTTP 403): search returned 403.");
      expect(text(warning)).toContain('Repositories that declare it dispatch nothing. Foundry retries automatically after 15 minutes.');
    });

    it('explains a transport failure without a status', () => {
      const { el } = render(makeSource({ status: { kind: 'unreachable', httpStatus: null, reason: 'probe timed out' } }));
      expect(text(el.querySelector('.package-source-status__warning'))).toContain("Can't reach this source: probe timed out.");
    });

    it('offers Add credential when there is none, and emits editCredential', () => {
      const { fixture, el } = render(makeSource({ status: { kind: 'unreachable', httpStatus: 401, reason: 'r' } }));
      const emitted: unknown[] = [];
      fixture.componentInstance.editCredential.subscribe((v) => emitted.push(v));

      const button = el.querySelector<HTMLButtonElement>('.package-source-status__credential-btn')!;
      button.click();

      expect(text(button)).toBe('Add credential');
      expect(button.getAttribute('aria-label')).toBe(`Add credential for ${SOURCE_URL}`);
      expect(emitted.length).toBe(1);
    });

    it('offers Replace credential when one is registered', () => {
      const { el } = render(makeSource({ status: { kind: 'unreachable', httpStatus: 401, reason: 'r' }, credential: CREDENTIAL }));
      const button = el.querySelector('.package-source-status__credential-btn');
      expect(text(button)).toBe('Replace credential');
      expect(button?.getAttribute('aria-label')).toBe(`Replace credential for ${SOURCE_URL}`);
    });
  });

  it('shows unchecked with a hollow dot and no time', () => {
    const { el } = render(makeSource({ status: { kind: 'unchecked' }, lastCheckedAt: null }));

    expect(el.querySelector('.package-source-status__dot--unchecked')).not.toBeNull();
    expect(text(el.querySelector('.package-source-status__label'))).toBe('Not checked yet');
    expect(text(el.querySelector('.package-source-status__meta'))).toBe('no credential registered');
    expect(el.querySelector('time')).toBeNull();
    expect(el.querySelector('.package-source-status__warning')).toBeNull();
  });

  it('replaces the status with a spinner while a saved credential is being checked', () => {
    const { el } = render(makeSource({ status: { kind: 'unreachable', httpStatus: 401, reason: 'r' } }), { action: 'saved' });

    expect(el.querySelector('fd-spinner')).not.toBeNull();
    expect(text(el.querySelector('.package-source-status__label'))).toBe('Credential saved — checking access…');
    expect(el.querySelector('.package-source-status__meta')).toBeNull();
    expect(el.querySelector('time')).toBeNull();
    expect(el.querySelector('.package-source-status__warning')).toBeNull();
  });

  it('says the credential was removed while a removal is being checked', () => {
    const { el } = render(makeSource(), { action: 'removed' });
    expect(text(el.querySelector('.package-source-status__label'))).toBe('Credential removed — checking access…');
  });
});

import { vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { SystemSignalRService } from '../../../../core/services/system-signalr.service';
import { SettingsPackageSourcesComponent } from './settings-package-sources';
import { PackageSource, packageSourceAnchorId } from '../package-source.model';
import { OTHER_SOURCE_URL, SOURCE_URL, makeSignalRStub, makeSource } from '../../testing/package-source.fixture';

const API = '/api/package-sources';
const CREDENTIAL_API = `${API}/00000000-0000-0000-0000-000000000001/credential`;
const CREDENTIAL = { username: 'ci-reader', updatedAt: '2026-11-16T09:00:00Z' };

function setup(sourceParam: string | null = null) {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    imports: [SettingsPackageSourcesComponent],
    providers: [
      provideRouter([]),
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: SystemSignalRService, useValue: makeSignalRStub() },
      {
        provide: ActivatedRoute,
        useValue: { queryParamMap: of(convertToParamMap(sourceParam === null ? {} : { source: sourceParam })) },
      },
    ],
  });
  const fixture = TestBed.createComponent(SettingsPackageSourcesComponent);
  document.body.appendChild(fixture.nativeElement);
  return { fixture, el: fixture.nativeElement as HTMLElement, httpMock: TestBed.inject(HttpTestingController) };
}

async function init(ctx: ReturnType<typeof setup>, sources: PackageSource[] = [makeSource()]): Promise<void> {
  ctx.fixture.detectChanges();
  ctx.httpMock.expectOne(API).flush(sources);
  await settle(ctx);
}

async function settle(ctx: ReturnType<typeof setup>): Promise<void> {
  await Promise.resolve();
  ctx.fixture.detectChanges();
  await ctx.fixture.whenStable();
  ctx.fixture.detectChanges();
}

function text(el: Element | null): string {
  return (el?.textContent ?? '').replace(/\s+/g, ' ').trim();
}

function urlSpan(el: HTMLElement, url: string): HTMLElement | null {
  return el.querySelector<HTMLElement>(`#${packageSourceAnchorId(url)} .package-source-list__url`);
}

describe('SettingsPackageSourcesComponent', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    document.body.replaceChildren();
    TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true });
  });

  it('renders the page frame with its heading and both description paragraphs', async () => {
    const ctx = setup();
    await init(ctx);

    const heading = ctx.el.querySelector('h2');
    expect(text(heading)).toBe('Package sources');
    expect(heading?.getAttribute('tabindex')).toBe('-1');
    const paragraphs = Array.from(ctx.el.querySelectorAll('.package-sources-settings__section-description')).map(text);
    expect(paragraphs[0]).toBe(
      'Private NuGet sources declared in the root nuget.config of your monitored repositories. Foundry discovers them automatically and checks that it can read each one.',
    );
    expect(paragraphs[1]).toContain(
      "Foundry reads a source with its package source credential when one is registered here, otherwise with the account credential that covers the repository's namespace.",
    );
  });

  it('has one polite live region', async () => {
    const ctx = setup();
    await init(ctx);

    const regions = ctx.el.querySelectorAll('[aria-live]');
    expect(regions.length).toBe(1);
    expect(regions[0].getAttribute('role')).toBe('status');
    expect(regions[0].getAttribute('aria-atomic')).toBe('true');
  });

  describe('deep link', () => {
    it('scrolls to and focuses the targeted card', async () => {
      const scroll = vi.fn();
      Element.prototype.scrollIntoView = scroll;
      const ctx = setup(SOURCE_URL);
      await init(ctx, [makeSource(), makeSource({ id: 'x', url: OTHER_SOURCE_URL })]);

      expect(document.activeElement).toBe(urlSpan(ctx.el, SOURCE_URL));
      expect(scroll).toHaveBeenCalledWith({ block: 'center', behavior: 'smooth' });
      expect(ctx.el.querySelector(`#${packageSourceAnchorId(SOURCE_URL)}`)?.classList).toContain(
        'package-source-list__item--targeted',
      );
      expect(ctx.el.querySelector('.package-sources-settings__target-missing')).toBeNull();
    });

    it('scrolls without animation when the user prefers reduced motion', async () => {
      const scroll = vi.fn();
      Element.prototype.scrollIntoView = scroll;
      vi.stubGlobal('matchMedia', (query: string) => ({ matches: query.includes('reduce') }) as MediaQueryList);
      const ctx = setup(SOURCE_URL);
      await init(ctx);

      expect(scroll).toHaveBeenCalledWith({ block: 'center', behavior: 'auto' });
      vi.unstubAllGlobals();
    });

    it('says so and focuses the heading when no card matches', async () => {
      const ctx = setup('https://gone.example.com/index.json');
      await init(ctx);

      const missing = ctx.el.querySelector('.package-sources-settings__target-missing');
      expect(missing?.getAttribute('role')).toBe('status');
      expect(text(missing)).toBe('That package source is no longer declared by a monitored repository.');
      expect(document.activeElement).toBe(ctx.el.querySelector('h2'));
    });
  });

  describe('editing', () => {
    async function openForm(ctx: ReturnType<typeof setup>, sources: PackageSource[] = [makeSource()]) {
      await init(ctx, sources);
      const opener = ctx.el.querySelector<HTMLButtonElement>(
        sources[0].credential ? `[aria-label="Replace credential for ${SOURCE_URL}"]` : '.package-source-list__add-btn',
      )!;
      opener.focus();
      opener.click();
      await settle(ctx);
      return opener;
    }

    function fillAndSave(ctx: ReturnType<typeof setup>): void {
      const username = ctx.el.querySelector<HTMLInputElement>('#pscf-username')!;
      username.value = 'ci-reader';
      username.dispatchEvent(new Event('input'));
      const token = ctx.el.querySelector<HTMLInputElement>('#pscf-token')!;
      token.value = 'secret';
      token.dispatchEvent(new Event('input'));
      ctx.el.querySelector<HTMLButtonElement>('.package-source-credential-form__save-btn')!.click();
    }

    it('replaces the list with the form', async () => {
      const ctx = setup();
      await openForm(ctx);

      expect(ctx.el.querySelector('fd-package-source-list')).toBeNull();
      expect(text(ctx.el.querySelector('.package-source-credential-form__heading'))).toBe('Add credential');
    });

    it('returns to the list on cancel and focuses the button that opened the form', async () => {
      const ctx = setup();
      await openForm(ctx);

      ctx.el.querySelector<HTMLButtonElement>('.package-source-credential-form__cancel-link')!.click();
      await settle(ctx);

      expect(ctx.el.querySelector('fd-package-source-list')).not.toBeNull();
      expect(document.activeElement).toBe(ctx.el.querySelector('.package-source-list__add-btn'));
    });

    it('returns focus to the row edit button when cancelling a replace', async () => {
      const ctx = setup();
      await openForm(ctx, [makeSource({ credential: CREDENTIAL })]);

      ctx.el.querySelector<HTMLButtonElement>('.package-source-credential-form__cancel-link')!.click();
      await settle(ctx);

      expect(document.activeElement).toBe(ctx.el.querySelector(`[aria-label="Replace credential for ${SOURCE_URL}"]`));
    });

    it('on save returns to the list, focuses the card, shows the pending check and announces it', async () => {
      const ctx = setup();
      await openForm(ctx);

      fillAndSave(ctx);
      ctx.httpMock.expectOne(CREDENTIAL_API).flush(null, { status: 204, statusText: 'No Content' });
      await settle(ctx);

      expect(ctx.el.querySelector('fd-package-source-credential-form')).toBeNull();
      expect(document.activeElement).toBe(urlSpan(ctx.el, SOURCE_URL));
      expect(text(ctx.el.querySelector('.package-source-status__label'))).toBe('Credential saved — checking access…');
      expect(text(ctx.el.querySelector('[aria-live]'))).toBe(`Credential saved for ${SOURCE_URL}. Checking access…`);
    });

    it('keeps the form open with field errors on a 400', async () => {
      const ctx = setup();
      await openForm(ctx);
      const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);

      fillAndSave(ctx);
      ctx.httpMock.expectOne(CREDENTIAL_API).flush(
        { errors: { username: ['Username must be 255 characters or fewer.'] } },
        { status: 400, statusText: 'Bad Request' },
      );
      await settle(ctx);

      expect(text(ctx.el.querySelector('#pscf-username-error'))).toBe('Username must be 255 characters or fewer.');
      expect(document.activeElement).toBe(ctx.el.querySelector('#pscf-username'));
      expect(consoleError).toHaveBeenCalledWith(
        expect.objectContaining({ status: 400, url: CREDENTIAL_API }),
      );
    });

    it('clears the server error once its field is edited', async () => {
      const ctx = setup();
      await openForm(ctx);
      const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
      fillAndSave(ctx);
      ctx.httpMock.expectOne(CREDENTIAL_API).flush(
        { errors: { username: ['bad'] } },
        { status: 400, statusText: 'Bad Request' },
      );
      await settle(ctx);

      const username = ctx.el.querySelector<HTMLInputElement>('#pscf-username')!;
      username.value = 'other';
      username.dispatchEvent(new Event('input'));
      await settle(ctx);

      expect(text(ctx.el.querySelector('#pscf-username-error'))).toBe('');
      expect(consoleError).toHaveBeenCalledWith(
        expect.objectContaining({ status: 400, url: CREDENTIAL_API }),
      );
    });
  });

  describe('removal', () => {
    async function askToRemove(ctx: ReturnType<typeof setup>) {
      await init(ctx, [makeSource({ credential: CREDENTIAL })]);
      ctx.el.querySelector<HTMLButtonElement>(`[aria-label="Remove credential for ${SOURCE_URL}"]`)!.click();
      await settle(ctx);
      return ctx.el.querySelector('fd-confirm-dialog dialog') as HTMLDialogElement;
    }

    it('asks for confirmation with the destructive copy', async () => {
      const ctx = setup();
      const dialog = await askToRemove(ctx);

      expect(dialog.open).toBe(true);
      expect(text(dialog)).toContain('Remove credential?');
      expect(text(dialog.querySelector('.confirm-dialog__message'))).toBe(
        `Remove the credential for ${SOURCE_URL}? Foundry falls back to the account credential for each repository that declares this source, and re-checks the source now.`,
      );
      expect(text(dialog.querySelector('.confirm-dialog__confirm'))).toBe('Remove credential');
      expect(text(dialog.querySelector('.confirm-dialog__cancel'))).toBe('Keep credential');
    });

    it('removes on confirm, shows the pending check, announces it and focuses the card', async () => {
      const ctx = setup();
      const dialog = await askToRemove(ctx);

      (dialog.querySelector('.confirm-dialog__confirm') as HTMLButtonElement).click();
      await settle(ctx);
      const req = ctx.httpMock.expectOne(CREDENTIAL_API);
      expect(req.request.method).toBe('DELETE');
      req.flush(null, { status: 204, statusText: 'No Content' });
      await settle(ctx);

      expect(text(ctx.el.querySelector('.package-source-status__label'))).toBe('Credential removed — checking access…');
      expect(text(ctx.el.querySelector('[aria-live]'))).toBe(`Credential removed for ${SOURCE_URL}. Checking access…`);
      expect(document.activeElement).toBe(urlSpan(ctx.el, SOURCE_URL));
    });

    it('shows an alert below the list when removal fails', async () => {
      const ctx = setup();
      const dialog = await askToRemove(ctx);
      const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);

      (dialog.querySelector('.confirm-dialog__confirm') as HTMLButtonElement).click();
      await settle(ctx);
      ctx.httpMock.expectOne(CREDENTIAL_API).flush(null, { status: 500, statusText: 'Server Error' });
      await settle(ctx);

      const alert = ctx.el.querySelector('.package-sources-settings__remove-error');
      expect(alert?.getAttribute('role')).toBe('alert');
      expect(text(alert)).toBe('Failed to remove credential');
      expect(consoleError).toHaveBeenCalledWith(
        expect.objectContaining({ status: 500, url: CREDENTIAL_API }),
      );
    });

    it('makes no request when the confirmation is dismissed', async () => {
      const ctx = setup();
      const dialog = await askToRemove(ctx);

      (dialog.querySelector('.confirm-dialog__cancel') as HTMLButtonElement).click();
      await settle(ctx);

      ctx.httpMock.expectNone(CREDENTIAL_API);
      expect(dialog.open).toBe(false);
    });
  });
});

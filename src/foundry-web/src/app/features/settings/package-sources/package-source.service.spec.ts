import { vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SystemSignalRService } from '../../../core/services/system-signalr.service';
import { PENDING_CHECK_TIMEOUT_MS, PackageSourceService } from './package-source.service';
import { SOURCE_URL, makeSignalRStub, makeSource } from '../testing/package-source.fixture';

const API = '/api/package-sources';
const CREDENTIAL_API = `${API}/00000000-0000-0000-0000-000000000001/credential`;

function setup() {
  TestBed.resetTestingModule();
  const signalR = makeSignalRStub();
  TestBed.configureTestingModule({
    providers: [
      PackageSourceService,
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: SystemSignalRService, useValue: signalR },
    ],
  });
  return {
    service: TestBed.inject(PackageSourceService),
    httpMock: TestBed.inject(HttpTestingController),
    signalR,
  };
}

async function loadWith(
  service: PackageSourceService,
  httpMock: HttpTestingController,
  sources = [makeSource()],
): Promise<void> {
  const loaded = service.load();
  httpMock.expectOne(API).flush(sources);
  await loaded;
}

describe('PackageSourceService', () => {
  afterEach(() => {
    vi.useRealTimers();
    TestBed.inject(HttpTestingController).verify();
  });

  describe('load', () => {
    it('stores the sources and resolves loaded', async () => {
      const { service, httpMock } = setup();

      const loaded = service.load();
      expect(service.loading()).toBe(true);
      httpMock.expectOne(API).flush([makeSource()]);

      expect(await loaded).toBe('loaded');
      expect(service.sources()).toEqual([makeSource()]);
      expect(service.loading()).toBe(false);
    });

    it('sets the load error and resolves failed when the request fails', async () => {
      const { service, httpMock } = setup();
      const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);

      const loaded = service.load();
      httpMock.expectOne(API).flush(null, { status: 500, statusText: 'Server Error' });

      expect(await loaded).toBe('failed');
      expect(service.loadError()).toBe('Failed to load package sources');
      expect(consoleError).toHaveBeenCalledWith(expect.objectContaining({ status: 500, url: API }));
    });
  });

  describe('reload', () => {
    it('does not refetch on a reload trigger before the first load', () => {
      vi.useFakeTimers();
      const { signalR, httpMock } = setup();

      signalR.reloadTrigger.next();
      vi.advanceTimersByTime(300);

      httpMock.expectNone(API);
    });

    it('refetches after the debounce when a reload trigger arrives after the first load', async () => {
      const { service, signalR, httpMock } = setup();
      await loadWith(service, httpMock);
      vi.useFakeTimers();

      signalR.reloadTrigger.next();
      signalR.reconnected.next();
      vi.advanceTimersByTime(300);

      httpMock.expectOne(API).flush([makeSource({ lastCheckedAt: '2026-11-16T11:00:00Z' })]);
      expect(service.sources()[0].lastCheckedAt).toBe('2026-11-16T11:00:00Z');
    });
  });

  describe('saveCredential', () => {
    it('puts the credential, records a pending check, and announces it', async () => {
      const { service, httpMock } = setup();
      await loadWith(service, httpMock);

      service.saveCredential(makeSource().id, { username: 'ci-reader', token: 'secret' });
      expect(service.saving()).toBe(true);
      const req = httpMock.expectOne(CREDENTIAL_API);
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual({ username: 'ci-reader', token: 'secret' });
      req.flush(null, { status: 204, statusText: 'No Content' });

      expect(service.saving()).toBe(false);
      expect(service.saveSuccess()).toBe(true);
      expect(service.pendingChecks().get(makeSource().id)).toEqual({
        baseline: '2026-11-16T10:00:00Z',
        action: 'saved',
      });
      expect(service.announcement()).toBe(`Credential saved for ${SOURCE_URL}. Checking access…`);
      expect(service.sources()[0].credential?.username).toBe('ci-reader');
    });

    it('maps a 400 ValidationProblemDetails to field errors', async () => {
      const { service, httpMock } = setup();
      const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
      await loadWith(service, httpMock);

      service.saveCredential(makeSource().id, { username: 'u', token: 't' });
      httpMock.expectOne(CREDENTIAL_API).flush(
        { errors: { username: ['Username is required.'], token: ['Token must be 2048 characters or fewer.'] } },
        { status: 400, statusText: 'Bad Request' },
      );

      expect(service.fieldErrors()).toEqual({
        username: 'Username is required.',
        token: 'Token must be 2048 characters or fewer.',
      });
      expect(service.saveError()).toBeNull();
      expect(service.saveSuccess()).toBe(false);
      expect(consoleError).toHaveBeenCalledWith(expect.objectContaining({ status: 400, url: CREDENTIAL_API }));
    });

    it('reports any other failure as the save error', async () => {
      const { service, httpMock } = setup();
      const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
      await loadWith(service, httpMock);

      service.saveCredential(makeSource().id, { username: 'u', token: 't' });
      httpMock.expectOne(CREDENTIAL_API).flush(null, { status: 500, statusText: 'Server Error' });

      expect(service.saveError()).toBe('Failed to save credential');
      expect(service.fieldErrors()).toEqual({});
      expect(consoleError).toHaveBeenCalledWith(expect.objectContaining({ status: 500, url: CREDENTIAL_API }));
    });

    it('uses the problem detail as the save error when there is one', async () => {
      const { service, httpMock } = setup();
      const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
      await loadWith(service, httpMock);

      service.saveCredential(makeSource().id, { username: 'u', token: 't' });
      httpMock.expectOne(CREDENTIAL_API).flush({ detail: 'Package source not found.' }, { status: 404, statusText: 'Not Found' });

      expect(service.saveError()).toBe('Package source not found.');
      expect(consoleError).toHaveBeenCalledWith(expect.objectContaining({ status: 404, url: CREDENTIAL_API }));
    });

    it('clears one field error when that field is edited', async () => {
      const { service, httpMock } = setup();
      const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
      await loadWith(service, httpMock);
      service.saveCredential(makeSource().id, { username: 'u', token: 't' });
      httpMock.expectOne(CREDENTIAL_API).flush(
        { errors: { username: ['bad'], token: ['bad'] } },
        { status: 400, statusText: 'Bad Request' },
      );

      service.clearFieldError('username');

      expect(service.fieldErrors()).toEqual({ token: 'bad' });
      expect(consoleError).toHaveBeenCalledWith(expect.objectContaining({ status: 400, url: CREDENTIAL_API }));
    });
  });

  describe('pending checks', () => {
    async function savedSetup() {
      const ctx = setup();
      await loadWith(ctx.service, ctx.httpMock);
      vi.useFakeTimers();
      ctx.service.saveCredential(makeSource().id, { username: 'ci-reader', token: 'secret' });
      ctx.httpMock.expectOne(CREDENTIAL_API).flush(null, { status: 204, statusText: 'No Content' });
      return ctx;
    }

    it('keeps the pending check while a refetch shows the same lastCheckedAt', async () => {
      const { service, signalR, httpMock } = await savedSetup();

      signalR.reloadTrigger.next();
      vi.advanceTimersByTime(300);
      httpMock.expectOne(API).flush([makeSource()]);

      expect(service.pendingChecks().has(makeSource().id)).toBe(true);
    });

    it('clears the pending check and announces the verdict when lastCheckedAt changes', async () => {
      const { service, signalR, httpMock } = await savedSetup();

      signalR.reloadTrigger.next();
      vi.advanceTimersByTime(300);
      httpMock.expectOne(API).flush([
        makeSource({
          lastCheckedAt: '2026-11-16T10:05:00Z',
          status: { kind: 'reachableViaCredential' },
          credential: { username: 'ci-reader', updatedAt: '2026-11-16T10:04:00Z' },
        }),
      ]);

      expect(service.pendingChecks().size).toBe(0);
      expect(service.announcement()).toBe(`${SOURCE_URL} is reachable with credential ci-reader.`);
    });

    it('fetches once after 60 s with no change, then clears the entry and says the check is slow', async () => {
      const { service, httpMock } = await savedSetup();

      vi.advanceTimersByTime(PENDING_CHECK_TIMEOUT_MS);
      httpMock.expectOne(API).flush([makeSource()]);

      expect(service.pendingChecks().size).toBe(0);
      expect(service.announcement()).toBe(
        `The access check for ${SOURCE_URL} is taking longer than expected. The status updates when it finishes.`,
      );
    });

    it('clears the entry after 60 s even when the exit fetch fails', async () => {
      const { service, httpMock } = await savedSetup();

      vi.advanceTimersByTime(PENDING_CHECK_TIMEOUT_MS);
      httpMock.expectOne(API).flush(null, { status: 500, statusText: 'Server Error' });

      expect(service.pendingChecks().size).toBe(0);
    });

    it('announces the verdict when the 60 s exit fetch shows a change', async () => {
      const { service, httpMock } = await savedSetup();

      vi.advanceTimersByTime(PENDING_CHECK_TIMEOUT_MS);
      httpMock.expectOne(API).flush([makeSource({ lastCheckedAt: '2026-11-16T10:05:00Z' })]);

      expect(service.pendingChecks().size).toBe(0);
      expect(service.announcement()).toBe(`${SOURCE_URL} is reachable with account acme-bot · gitlab.example.com.`);
    });

    it('does not run the exit after the check resolved', async () => {
      const { signalR, httpMock } = await savedSetup();
      signalR.reloadTrigger.next();
      vi.advanceTimersByTime(300);
      httpMock.expectOne(API).flush([makeSource({ lastCheckedAt: '2026-11-16T10:05:00Z' })]);

      vi.advanceTimersByTime(PENDING_CHECK_TIMEOUT_MS);

      httpMock.expectNone(API);
    });
  });

  describe('removeCredential', () => {
    it('deletes the credential, records a pending check, and resolves true', async () => {
      const { service, httpMock } = setup();
      await loadWith(service, httpMock, [
        makeSource({ credential: { username: 'ci-reader', updatedAt: '2026-11-16T09:00:00Z' } }),
      ]);

      const removed = service.removeCredential(makeSource().id);
      expect(service.removingId()).toBe(makeSource().id);
      const req = httpMock.expectOne(CREDENTIAL_API);
      expect(req.request.method).toBe('DELETE');
      req.flush(null, { status: 204, statusText: 'No Content' });

      expect(await removed).toBe(true);
      expect(service.removingId()).toBeNull();
      expect(service.sources()[0].credential).toBeNull();
      expect(service.pendingChecks().get(makeSource().id)?.action).toBe('removed');
      expect(service.announcement()).toBe(`Credential removed for ${SOURCE_URL}. Checking access…`);
    });

    it('sets the remove error and resolves false on failure', async () => {
      const { service, httpMock } = setup();
      const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
      await loadWith(service, httpMock);

      const removed = service.removeCredential(makeSource().id);
      httpMock.expectOne(CREDENTIAL_API).flush(null, { status: 500, statusText: 'Server Error' });

      expect(await removed).toBe(false);
      expect(service.removeError()).toBe('Failed to remove credential');
      expect(service.pendingChecks().size).toBe(0);
      expect(consoleError).toHaveBeenCalledWith(expect.objectContaining({ status: 500, url: CREDENTIAL_API }));
    });
  });
});

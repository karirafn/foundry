import { Injectable, Signal, WritableSignal, inject, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse, HttpStatusCode } from '@angular/common/http';
import { components } from '../../../api/schema';
import { extractErrorMessage } from '../../../core/http/extract-error-message';

export type WorkerRoleCatalog = components['schemas']['WorkerRoleCatalogResponse'];
export type WorkerRoleCard = components['schemas']['WorkerRoleCard'];
export type WorkerRoleImage = components['schemas']['WorkerRoleImage'];
export type RolePluginComponent = components['schemas']['RolePluginComponent'];
export type PromptSectionDescriptor = components['schemas']['PromptSectionDescriptor'];
export type PromptSectionCondition = components['schemas']['PromptSectionCondition'];

/** Lifecycle of the catalog fetch. `notReady` is the 503 returned before the worker image build has read the catalog. */
export type WorkerRoleCatalogStatus = 'idle' | 'loading' | 'loaded' | 'notReady' | 'error';

const API_URL = '/api/worker-roles';
const LOAD_CATALOG_ERROR = 'Failed to load worker roles';

@Injectable({ providedIn: 'root' })
export class WorkerRoleService {
  private readonly _http = inject(HttpClient);

  private readonly _catalogSignal: WritableSignal<WorkerRoleCatalog | null> = signal(null);
  readonly catalog: Signal<WorkerRoleCatalog | null> = this._catalogSignal.asReadonly();

  private readonly _statusSignal: WritableSignal<WorkerRoleCatalogStatus> = signal('idle');
  readonly status: Signal<WorkerRoleCatalogStatus> = this._statusSignal.asReadonly();

  private readonly _loadErrorSignal: WritableSignal<string | null> = signal(null);
  readonly loadError: Signal<string | null> = this._loadErrorSignal.asReadonly();

  private _requestToken = 0;

  load(): void {
    this._requestToken += 1;
    const token = this._requestToken;
    this._loadErrorSignal.set(null);
    this._statusSignal.set('loading');

    this._http.get<WorkerRoleCatalog>(API_URL).subscribe({
      next: (catalog) => {
        if (token !== this._requestToken) {
          return;
        }
        this._catalogSignal.set(catalog);
        this._statusSignal.set('loaded');
      },
      error: (err: HttpErrorResponse) => {
        if (token !== this._requestToken) {
          return;
        }
        this._catalogSignal.set(null);
        if (err.status === HttpStatusCode.ServiceUnavailable) {
          this._statusSignal.set('notReady');
          return;
        }
        console.error(err);
        this._loadErrorSignal.set(extractErrorMessage(err) ?? LOAD_CATALOG_ERROR);
        this._statusSignal.set('error');
      },
    });
  }
}

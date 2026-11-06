import { Injectable, Signal, WritableSignal, inject, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse, HttpParams, HttpStatusCode } from '@angular/common/http';
import { components } from '../../../api/schema';
import { extractErrorCode, extractErrorMessage } from '../../../core/http/extract-error-message';

export type WorkerRoleCatalog = components['schemas']['WorkerRoleCatalogResponse'];
export type WorkerRoleCard = components['schemas']['WorkerRoleCard'];
export type WorkerRoleImage = components['schemas']['WorkerRoleImage'];
export type RolePluginComponent = components['schemas']['RolePluginComponent'];
export type PromptSectionDescriptor = components['schemas']['PromptSectionDescriptor'];
export type PromptSectionCondition = components['schemas']['PromptSectionCondition'];
export type WorkerRoleModelOptions = components['schemas']['WorkerRoleModelOptionsResponse'];
export type WorkerRoleModelOption = components['schemas']['WorkerRoleModelOption'];
export type WorkerRoleModelResponse = components['schemas']['WorkerRoleModelResponse'];

/** Lifecycle of the catalog fetch. `notReady` is the 503 returned before the worker image build has read the catalog. */
export type WorkerRoleCatalogStatus = 'idle' | 'loading' | 'loaded' | 'notReady' | 'error';

/** Lifecycle of the model-list fetch. */
export type WorkerRoleModelOptionsStatus = 'idle' | 'loading' | 'loaded' | 'error';

/** A rejected model save, keyed to the role whose model field it belongs to. */
export interface WorkerRoleModelSaveError {
  readonly role: string;
  readonly code: string | null;
  readonly message: string;
}

const API_URL = '/api/worker-roles';
const MODELS_URL = `${API_URL}/models`;
const LOAD_CATALOG_ERROR = 'Failed to load worker roles';
const LOAD_MODELS_ERROR = 'The model list could not be loaded.';
const SAVE_MODEL_ERROR = 'Failed to save the model';

@Injectable({ providedIn: 'root' })
export class WorkerRoleService {
  private readonly _http = inject(HttpClient);

  private readonly _catalogSignal: WritableSignal<WorkerRoleCatalog | null> = signal(null);
  readonly catalog: Signal<WorkerRoleCatalog | null> = this._catalogSignal.asReadonly();

  private readonly _statusSignal: WritableSignal<WorkerRoleCatalogStatus> = signal('idle');
  readonly status: Signal<WorkerRoleCatalogStatus> = this._statusSignal.asReadonly();

  private readonly _loadErrorSignal: WritableSignal<string | null> = signal(null);
  readonly loadError: Signal<string | null> = this._loadErrorSignal.asReadonly();

  private readonly _modelOptionsSignal: WritableSignal<WorkerRoleModelOptions | null> = signal(null);
  /** The aliases and listed models; kept from the last successful fetch when a later one fails. */
  readonly modelOptions: Signal<WorkerRoleModelOptions | null> = this._modelOptionsSignal.asReadonly();

  private readonly _modelOptionsStatusSignal: WritableSignal<WorkerRoleModelOptionsStatus> = signal('idle');
  readonly modelOptionsStatus: Signal<WorkerRoleModelOptionsStatus> = this._modelOptionsStatusSignal.asReadonly();

  private readonly _modelOptionsErrorSignal: WritableSignal<string | null> = signal(null);
  /** Why the model list could not be refreshed: the server's refresh error, or the request's own failure. */
  readonly modelOptionsError: Signal<string | null> = this._modelOptionsErrorSignal.asReadonly();

  private readonly _savingModelSignal: WritableSignal<string | null> = signal(null);
  /** The role whose model is being saved, if any. */
  readonly savingModel: Signal<string | null> = this._savingModelSignal.asReadonly();

  private readonly _saveModelErrorSignal: WritableSignal<WorkerRoleModelSaveError | null> = signal(null);
  readonly saveModelError: Signal<WorkerRoleModelSaveError | null> = this._saveModelErrorSignal.asReadonly();

  private readonly _savedModelSignal: WritableSignal<string | null> = signal(null);
  /** The role whose model was saved most recently. */
  readonly savedModel: Signal<string | null> = this._savedModelSignal.asReadonly();

  private _requestToken = 0;
  private _modelsRequestToken = 0;

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

  /** Fetches the model options. The role view refreshes the list from the Models API each time it opens. */
  loadModelOptions(refresh = true): void {
    this._modelsRequestToken += 1;
    const token = this._modelsRequestToken;
    this._modelOptionsStatusSignal.set('loading');
    const params = new HttpParams().set('refresh', refresh);

    this._http.get<WorkerRoleModelOptions>(MODELS_URL, { params }).subscribe({
      next: (options) => {
        if (token !== this._modelsRequestToken) {
          return;
        }
        this._modelOptionsSignal.set(options);
        this._modelOptionsErrorSignal.set(options.refreshError);
        this._modelOptionsStatusSignal.set('loaded');
      },
      error: (err: HttpErrorResponse) => {
        if (token !== this._modelsRequestToken) {
          return;
        }
        console.error(err);
        this._modelOptionsErrorSignal.set(extractErrorMessage(err) ?? LOAD_MODELS_ERROR);
        this._modelOptionsStatusSignal.set('error');
      },
    });
  }

  /** Saves a role's model; an empty value means the CLI's account default. */
  saveModel(role: string, model: string): void {
    this._savingModelSignal.set(role);
    this._saveModelErrorSignal.set(null);
    this._savedModelSignal.set(null);

    this._http
      .put<WorkerRoleModelResponse>(`${API_URL}/${encodeURIComponent(role)}/model`, { model })
      .subscribe({
        next: (saved) => {
          this._catalogSignal.update((catalog) =>
            catalog === null
              ? null
              : {
                  ...catalog,
                  roles: catalog.roles.map((card) =>
                    card.name === saved.role ? { ...card, model: saved.model } : card,
                  ),
                },
          );
          this._savingModelSignal.set(null);
          this._savedModelSignal.set(saved.role);
        },
        error: (err: HttpErrorResponse) => {
          console.error(err);
          this._saveModelErrorSignal.set({
            role,
            code: extractErrorCode(err),
            message: extractErrorMessage(err) ?? SAVE_MODEL_ERROR,
          });
          this._savingModelSignal.set(null);
        },
      });
  }
}

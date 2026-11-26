import { Injectable, Signal, WritableSignal, inject, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse, HttpParams, HttpStatusCode } from '@angular/common/http';
import { components } from '../../../api/schema';
import { extractErrorCode, extractErrorMessage } from '../../../core/http/extract-error-message';

export type WorkerRoleCatalog = components['schemas']['WorkerRoleCatalogResponse'];
export type WorkerRoleCard = components['schemas']['WorkerRoleCard'];
/** The capability profile a role runs under; `WorkerRoleCard.capability` is typed `string` so unknown values stay possible. */
export type WorkerCapability = 'restricted' | 'full';
export type WorkerRoleImage = components['schemas']['WorkerRoleImage'];
export type RolePluginComponent = components['schemas']['RolePluginComponent'];
export type PromptSectionDescriptor = components['schemas']['PromptSectionDescriptor'];
export type PromptSectionCondition = components['schemas']['PromptSectionCondition'];
export type WorkerRoleModelOptions = components['schemas']['WorkerRoleModelOptionsResponse'];
export type WorkerRoleModelOption = components['schemas']['WorkerRoleModelOption'];
export type WorkerRoleModelResponse = components['schemas']['WorkerRoleModelResponse'];
/** How demanding triage judged an issue; the implement role sets its model and effort per demand. */
export type ImplementDemand = components['schemas']['ImplementDemand'];
/** One demand's model and effort; an empty value leaves it to the account or model default. */
export type DemandModel = components['schemas']['ImplementDemandModel'];

/** The effort levels a demand setting may name besides the empty default. Mirrors the backend's accepted values. */
export const EFFORT_LEVELS: readonly string[] = ['low', 'medium', 'high', 'xhigh'];

/** One demand setting of one role. */
export interface WorkerRoleDemandSetting {
  readonly role: string;
  readonly demand: ImplementDemand;
}

/** A rejected demand save, keyed to the role and demand whose setting it belongs to. */
export interface WorkerRoleDemandSaveError extends WorkerRoleDemandSetting {
  readonly code: string | null;
  readonly message: string;
}

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

  // Each demand setting saves on its own, so these hold every setting in that state rather than the last one.
  private readonly _savingDemandSignal: WritableSignal<readonly WorkerRoleDemandSetting[]> = signal([]);
  /** The demand settings being saved. */
  readonly savingDemand: Signal<readonly WorkerRoleDemandSetting[]> = this._savingDemandSignal.asReadonly();

  private readonly _saveDemandErrorSignal: WritableSignal<readonly WorkerRoleDemandSaveError[]> = signal([]);
  readonly saveDemandError: Signal<readonly WorkerRoleDemandSaveError[]> = this._saveDemandErrorSignal.asReadonly();

  private readonly _savedDemandSignal: WritableSignal<readonly WorkerRoleDemandSetting[]> = signal([]);
  /** The demand settings saved since their last save started. */
  readonly savedDemand: Signal<readonly WorkerRoleDemandSetting[]> = this._savedDemandSignal.asReadonly();

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

  /** Saves a role's model; an empty value means the CLI's account default. Never called for a role with demand settings. */
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

  /**
   * Saves one demand's model and effort; empty values mean the account default and the model's default. A role
   * with demand settings is saved only through here: its role-wide model endpoint rejects every save.
   */
  saveDemandModel(role: string, demand: ImplementDemand, model: string, effort: string): void {
    const setting: WorkerRoleDemandSetting = { role, demand };
    this._savingDemandSignal.update((list) => [...without(list, setting), setting]);
    this._saveDemandErrorSignal.update((list) => without(list, setting));
    this._savedDemandSignal.update((list) => without(list, setting));

    this._http
      .put<DemandModel>(
        `${API_URL}/${encodeURIComponent(role)}/demand-models/${encodeURIComponent(demand)}`,
        { model, effort },
      )
      .subscribe({
        next: (saved) => {
          this._catalogSignal.update((catalog) =>
            catalog === null
              ? null
              : {
                  ...catalog,
                  roles: catalog.roles.map((card) =>
                    card.name === role
                      ? {
                          ...card,
                          demandModels: card.demandModels.map((entry) =>
                            entry.demand === saved.demand ? { ...saved } : entry,
                          ),
                        }
                      : card,
                  ),
                },
          );
          this._savingDemandSignal.update((list) => without(list, setting));
          this._savedDemandSignal.update((list) => [...without(list, setting), setting]);
        },
        error: (err: HttpErrorResponse) => {
          console.error(err);
          this._saveDemandErrorSignal.update((list) => [
            ...without(list, setting),
            { ...setting, code: extractErrorCode(err), message: extractErrorMessage(err) ?? SAVE_MODEL_ERROR },
          ]);
          this._savingDemandSignal.update((list) => without(list, setting));
        },
      });
  }
}

/** True when the entry is the given role's demand setting. */
export function isDemandSetting(entry: WorkerRoleDemandSetting, role: string, demand: ImplementDemand): boolean {
  return entry.role === role && entry.demand === demand;
}

function without<T extends WorkerRoleDemandSetting>(list: readonly T[], setting: WorkerRoleDemandSetting): T[] {
  return list.filter((entry) => !isDemandSetting(entry, setting.role, setting.demand));
}

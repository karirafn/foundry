import {
  ChangeDetectionStrategy,
  Component,
  InputSignal,
  Signal,
  WritableSignal,
  computed,
  inject,
  input,
  linkedSignal,
} from '@angular/core';
import {
  DemandModel,
  EFFORT_LEVELS,
  ImplementDemand,
  WorkerRoleCard,
  WorkerRoleModelOption,
  WorkerRoleService,
  isDemandSetting,
} from './worker-role.service';

/** The documented Claude Code aliases; used when the model-options request itself fails. Mirrors ClaudeModelAliases. */
export const CLAUDE_CODE_MODEL_ALIASES: readonly string[] = [
  'fable',
  'opus',
  'sonnet',
  'haiku',
  'opus[1m]',
  'sonnet[1m]',
];

/** The model of a run when the role's setting is empty. */
export const DEFAULT_MODEL_LABEL = 'Default (account default)';

/** The effort of a run when the demand's setting is empty. */
export const DEFAULT_EFFORT_LABEL = 'Default (model default)';

const DEMAND_TITLES: Readonly<Record<ImplementDemand, string>> = {
  routine: 'Routine',
  demanding: 'Demanding',
};

/** Rejections of the model value; every other demand-save failure but the effort's shows under the model row too. */
const MODEL_ERROR_CODES: readonly string[] = ['Worker.RoleModelNotListed', 'Worker.RoleModelTooLong'];
const EFFORT_ERROR_CODE = 'Worker.TierEffortInvalid';

interface UnlistedModel {
  readonly value: string;
  readonly label: string;
}

interface FieldIds {
  readonly heading: string;
  readonly hint: string;
  readonly select: string;
  readonly error: string;
  readonly effort: string;
  readonly effortError: string;
}

/** A save failure split by the field it belongs under. */
interface FieldErrors {
  readonly model: string;
  readonly modelInvalid: boolean;
  readonly effort: string;
}

const NO_ERRORS: FieldErrors = { model: '', modelInvalid: false, effort: '' };

/**
 * The one editable part of a worker role (ADR 0095): its model, picked from Default, the Claude Code aliases and
 * the model IDs the Models API lists for the workers' credential. With a `demand` it edits that demand's model and
 * effort instead, for a role whose model is set per demand.
 */
@Component({
  selector: 'fd-worker-role-model',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="role-model" [attr.aria-labelledby]="ids().heading">
      <h3 class="role-model__title" [id]="ids().heading">{{ title() }}</h3>
      <p class="role-model__hint" [id]="ids().hint">
        @switch (demand()) {
          @case ('routine') {
            Used for every issue triage judges routine; Default leaves the model and effort to the account
            default. Effort sets how much the model reasons; Default leaves it to the model.
          }
          @case ('demanding') {
            Used for issues triage judges demanding, and for a routine issue whose code review or CI finds
            defects. Effort sets how much the model reasons; Default leaves it to the model.
          }
          @default {
            The model this role's runs use. Default leaves it to the Claude Code account default; an
            alias follows the newest model of its family; a model ID pins a version.
          }
        }
      </p>
      <div class="role-model__row">
        <label class="sr-only" [for]="ids().select">{{ selectLabel() }}</label>
        <select
          class="role-model__select"
          [id]="ids().select"
          [attr.aria-describedby]="ids().hint + ' ' + ids().error"
          [attr.aria-invalid]="errors().modelInvalid ? true : null"
          [disabled]="saving()"
          (change)="select($any($event.target).value)"
        >
          <option value="" [selected]="selected() === ''">{{ defaultLabel }}</option>
          @if (unlisted(); as extra) {
            <option class="role-model__unlisted" [value]="extra.value" [selected]="selected() === extra.value">
              {{ extra.label }}
            </option>
          }
          <optgroup label="Aliases">
            @for (alias of aliases(); track alias) {
              <option [value]="alias" [selected]="selected() === alias">{{ alias }}</option>
            }
          </optgroup>
          @if (models().length > 0) {
            <optgroup label="Models">
              @for (model of models(); track model.id) {
                <option [value]="model.id" [selected]="selected() === model.id">
                  {{ modelLabel(model) }}
                </option>
              }
            </optgroup>
          }
        </select>
        @if (demand()) {
          <div class="role-model__field">
            <label class="role-model__label" [for]="ids().effort">Effort</label>
            <select
              class="role-model__select role-model__select--effort"
              [id]="ids().effort"
              [attr.aria-describedby]="ids().hint + ' ' + ids().effortError"
              [attr.aria-invalid]="errors().effort ? true : null"
              [disabled]="saving()"
              (change)="selectEffort($any($event.target).value)"
            >
              <option value="" [selected]="selectedEffort() === ''">{{ defaultEffortLabel }}</option>
              @for (level of effortLevels; track level) {
                <option [value]="level" [selected]="selectedEffort() === level">{{ level }}</option>
              }
            </select>
          </div>
        }
        <button
          class="role-model__save"
          type="button"
          [disabled]="!dirty() || saving()"
          (click)="save()"
        >
          {{ saving() ? 'Saving...' : 'Save' }}
        </button>
      </div>
      <span class="role-model__error role-model__error--model" role="alert" [id]="ids().error">{{
        errors().model
      }}</span>
      @if (demand()) {
        <span class="role-model__error role-model__error--effort" role="alert" [id]="ids().effortError">{{
          errors().effort
        }}</span>
      }
      <span class="role-model__status" role="status">{{ statusText() }}</span>
      @if (refreshError(); as error) {
        <p class="role-model__refresh-error" role="note">
          <span class="role-model__refresh-icon" aria-hidden="true">⚠</span>
          <span>
            <strong class="role-model__refresh-lead">The model list could not be refreshed:</strong>
            {{ error }} Default, the aliases and the saved model are still available.
          </span>
          <button
            class="role-model__retry"
            type="button"
            [disabled]="refreshing()"
            (click)="refresh()"
          >
            Retry
          </button>
        </p>
      }
    </section>
  `,
  styleUrl: './worker-role-model.scss',
})
export class WorkerRoleModelComponent {
  readonly role: InputSignal<WorkerRoleCard> = input.required<WorkerRoleCard>();
  /** The demand whose setting this edits; null edits the role-wide model. */
  readonly demand: InputSignal<ImplementDemand | null> = input<ImplementDemand | null>(null);

  private readonly _roleService = inject(WorkerRoleService);

  protected readonly defaultLabel = DEFAULT_MODEL_LABEL;
  protected readonly defaultEffortLabel = DEFAULT_EFFORT_LABEL;
  protected readonly effortLevels = EFFORT_LEVELS;

  /** The demand's saved setting, or null when this edits the role-wide model. */
  private readonly _demandSetting: Signal<DemandModel | null> = computed(() => {
    const demand = this.demand();
    return demand === null ? null : (this.role().demandModels.find((d) => d.demand === demand) ?? null);
  });

  private readonly _savedModel: Signal<string> = computed(() => this._demandSetting()?.model ?? this.role().model);
  private readonly _savedEffort: Signal<string> = computed(() => this._demandSetting()?.effort ?? '');

  /** The value picked in the dropdown; follows the saved value whenever it changes. */
  protected readonly selected: WritableSignal<string> = linkedSignal(() => this._savedModel());

  /** The effort picked; follows the demand's saved effort whenever it changes. */
  protected readonly selectedEffort: WritableSignal<string> = linkedSignal(() => this._savedEffort());

  protected readonly title: Signal<string> = computed(() => {
    const demand = this.demand();
    return demand === null ? 'Model' : `${DEMAND_TITLES[demand]} model`;
  });

  protected readonly selectLabel: Signal<string> = computed(() => {
    const demand = this.demand();
    const label = `Model for the ${this.role().name} role`;
    return demand === null ? label : `${label}, ${demand} demand`;
  });

  // Suffixed with the demand so the two settings of one role keep unique ids.
  protected readonly ids: Signal<FieldIds> = computed(() => {
    const demand = this.demand();
    const stem = `worker-role-${this.role().name.replace(/[^A-Za-z0-9_-]/g, '-')}${demand === null ? '' : `-${demand}`}`;
    const base = `${stem}-model`;
    return {
      heading: `${base}-title`,
      hint: `${base}-hint`,
      select: base,
      error: `${base}-error`,
      effort: `${stem}-effort`,
      effortError: `${stem}-effort-error`,
    };
  });

  protected readonly aliases: Signal<readonly string[]> = computed(
    () => this._roleService.modelOptions()?.aliases ?? CLAUDE_CODE_MODEL_ALIASES,
  );

  protected readonly models: Signal<readonly WorkerRoleModelOption[]> = computed(
    () => this._roleService.modelOptions()?.models ?? [],
  );

  private readonly _listRefreshError: Signal<string | null> = computed(() =>
    this._roleService.modelOptionsStatus() === 'loading' ? null : this._roleService.modelOptionsError(),
  );

  /** The callout is shared by the page: a role with demand settings shows it once, on its routine setting. */
  protected readonly refreshError: Signal<string | null> = computed(() =>
    this.demand() === 'demanding' ? null : this._listRefreshError(),
  );

  protected readonly refreshing: Signal<boolean> = computed(
    () => this._roleService.modelOptionsStatus() === 'loading',
  );

  /** The saved value when it is neither Default, an alias nor a listed model. Runs still use it. */
  protected readonly unlisted: Signal<UnlistedModel | null> = computed(() => {
    const saved = this._savedModel();
    if (saved === '' || this.aliases().includes(saved) || this.models().some((m) => m.id === saved)) {
      return null;
    }
    const listKnown = this._roleService.modelOptions()?.refreshedAt != null && this._listRefreshError() === null;
    return { value: saved, label: `${saved} (${listKnown ? 'unavailable' : 'saved, not in the current list'})` };
  });

  protected readonly saving: Signal<boolean> = computed(() => {
    const demand = this.demand();
    const role = this.role().name;
    return demand === null
      ? this._roleService.savingModel() === role
      : this._roleService.savingDemand().some((s) => isDemandSetting(s, role, demand));
  });

  protected readonly dirty: Signal<boolean> = computed(
    () => this.selected() !== this._savedModel() || this.selectedEffort() !== this._savedEffort(),
  );

  protected readonly errors: Signal<FieldErrors> = computed(() => {
    const demand = this.demand();
    const role = this.role().name;
    if (demand === null) {
      const error = this._roleService.saveModelError();
      return error !== null && error.role === role
        ? { model: error.message, modelInvalid: true, effort: '' }
        : NO_ERRORS;
    }
    const error = this._roleService.saveDemandError().find((e) => isDemandSetting(e, role, demand));
    if (error === undefined) {
      return NO_ERRORS;
    }
    if (error.code === EFFORT_ERROR_CODE) {
      return { model: '', modelInvalid: false, effort: error.message };
    }
    return { model: error.message, modelInvalid: error.code !== null && MODEL_ERROR_CODES.includes(error.code), effort: '' };
  });

  protected readonly statusText: Signal<string> = computed(() => {
    if (this.saving()) {
      return 'Saving model...';
    }
    if (this.dirty()) {
      return '';
    }
    const demand = this.demand();
    const role = this.role().name;
    if (demand === null) {
      return this._roleService.savedModel() === role ? 'Model saved' : '';
    }
    return this._roleService.savedDemand().some((s) => isDemandSetting(s, role, demand))
      ? `${DEMAND_TITLES[demand]} model saved`
      : '';
  });

  /** A save the root service is already finishing is not a change to discard. */
  hasUnsavedChanges(): boolean {
    return this.dirty() && !this.saving();
  }

  protected select(value: string): void {
    this.selected.set(value);
  }

  protected selectEffort(value: string): void {
    this.selectedEffort.set(value);
  }

  protected save(): void {
    if (!this.dirty() || this.saving()) {
      return;
    }
    const demand = this.demand();
    if (demand === null) {
      this._roleService.saveModel(this.role().name, this.selected());
    } else {
      this._roleService.saveDemandModel(this.role().name, demand, this.selected(), this.selectedEffort());
    }
  }

  protected refresh(): void {
    this._roleService.loadModelOptions(true);
  }

  protected modelLabel(model: WorkerRoleModelOption): string {
    const name = model.displayName === model.id ? model.id : `${model.displayName} (${model.id})`;
    return model.deprecated ? `${name}, deprecated` : name;
  }
}

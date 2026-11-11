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
import { WorkerRoleCard, WorkerRoleModelOption, WorkerRoleService } from './worker-role.service';

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

interface UnlistedModel {
  readonly value: string;
  readonly label: string;
}

/**
 * The one editable part of a worker role (ADR 0095): its model, picked from Default, the Claude Code aliases and
 * the model IDs the Models API lists for the workers' credential.
 */
@Component({
  selector: 'fd-worker-role-model',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="role-model" [attr.aria-labelledby]="ids().heading">
      <h3 class="role-model__title" [id]="ids().heading">Model</h3>
      <p class="role-model__hint" [id]="ids().hint">
        The model this role's runs use. Default leaves it to the Claude Code account default; an
        alias follows the newest model of its family; a model ID pins a version.
      </p>
      <div class="role-model__row">
        <label class="sr-only" [for]="ids().select">Model for the {{ role().name }} role</label>
        <select
          class="role-model__select"
          [id]="ids().select"
          [attr.aria-describedby]="ids().hint + ' ' + ids().error"
          [attr.aria-invalid]="fieldError() ? true : null"
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
        <button
          class="role-model__save"
          type="button"
          [disabled]="!dirty() || saving()"
          (click)="save()"
        >
          {{ saving() ? 'Saving...' : 'Save' }}
        </button>
      </div>
      <span class="role-model__error" role="alert" [id]="ids().error">{{ fieldError() }}</span>
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

  private readonly _roleService = inject(WorkerRoleService);

  protected readonly defaultLabel = DEFAULT_MODEL_LABEL;

  /** The value picked in the dropdown; follows the saved value whenever it changes. */
  protected readonly selected: WritableSignal<string> = linkedSignal(() => this.role().model);

  protected readonly ids: Signal<{ heading: string; hint: string; select: string; error: string }> = computed(
    () => {
      const base = `worker-role-${this.role().name.replace(/[^A-Za-z0-9_-]/g, '-')}-model`;
      return { heading: `${base}-title`, hint: `${base}-hint`, select: base, error: `${base}-error` };
    },
  );

  protected readonly aliases: Signal<readonly string[]> = computed(
    () => this._roleService.modelOptions()?.aliases ?? CLAUDE_CODE_MODEL_ALIASES,
  );

  protected readonly models: Signal<readonly WorkerRoleModelOption[]> = computed(
    () => this._roleService.modelOptions()?.models ?? [],
  );

  protected readonly refreshError: Signal<string | null> = computed(() =>
    this._roleService.modelOptionsStatus() === 'loading' ? null : this._roleService.modelOptionsError(),
  );

  protected readonly refreshing: Signal<boolean> = computed(
    () => this._roleService.modelOptionsStatus() === 'loading',
  );

  /** The saved value when it is neither Default, an alias nor a listed model. Runs still use it. */
  protected readonly unlisted: Signal<UnlistedModel | null> = computed(() => {
    const saved = this.role().model;
    if (saved === '' || this.aliases().includes(saved) || this.models().some((m) => m.id === saved)) {
      return null;
    }
    const listKnown = this._roleService.modelOptions()?.refreshedAt != null && this.refreshError() === null;
    return { value: saved, label: `${saved} (${listKnown ? 'unavailable' : 'saved, not in the current list'})` };
  });

  protected readonly saving: Signal<boolean> = computed(
    () => this._roleService.savingModel() === this.role().name,
  );

  protected readonly dirty: Signal<boolean> = computed(() => this.selected() !== this.role().model);

  protected readonly fieldError: Signal<string> = computed(() => {
    const error = this._roleService.saveModelError();
    return error !== null && error.role === this.role().name ? error.message : '';
  });

  protected readonly statusText: Signal<string> = computed(() => {
    if (this.saving()) {
      return 'Saving model...';
    }
    return this._roleService.savedModel() === this.role().name && !this.dirty() ? 'Model saved' : '';
  });

  /** A save the root service is already finishing is not a change to discard. */
  hasUnsavedChanges(): boolean {
    return this.dirty() && !this.saving();
  }

  protected select(value: string): void {
    this.selected.set(value);
  }

  protected save(): void {
    if (!this.dirty() || this.saving()) {
      return;
    }
    this._roleService.saveModel(this.role().name, this.selected());
  }

  protected refresh(): void {
    this._roleService.loadModelOptions(true);
  }

  protected modelLabel(model: WorkerRoleModelOption): string {
    const name = model.displayName === model.id ? model.id : `${model.displayName} (${model.id})`;
    return model.deprecated ? `${name}, deprecated` : name;
  }
}

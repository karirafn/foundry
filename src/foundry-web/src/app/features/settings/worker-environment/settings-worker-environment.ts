import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  Injector,
  Signal,
  WritableSignal,
  afterNextRender,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormArray, FormControl, FormGroup, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { SettingsService } from '../../../core/services/settings.service';
import { WorkerEnvironment, WorkerMountEntry } from '../../../core/models/settings.model';
import { UnsavedChanges } from '../guards/unsaved-changes.guard';
import {
  CPU_MAX,
  CPU_MIN,
  MEMORY_MAX_MB,
  MEMORY_MIN_MB,
  MountPathKind,
  PIDS_MAX,
  PIDS_MIN,
  absolutePathValidator,
  noTraversalValidator,
  uniqueContainerPathsValidator,
  wholeNumberValidator,
} from './settings-worker-environment.validators';

type MountRowGroup = FormGroup<{
  containerPath: FormControl<string>;
  hostPath: FormControl<string>;
  readOnly: FormControl<boolean>;
}>;

type LimitControlName = 'memoryLimitMb' | 'cpuLimit' | 'pidsLimit';
type MountPathField = 'containerPath' | 'hostPath';

const DEFAULT_MEMORY_MB = 8192;
const DEFAULT_CPU = 2;
const DEFAULT_PIDS = 512;
const SERVER_ERROR_KEY = 'server';

const LIMIT_MESSAGES: Record<LimitControlName, { label: string; range: string }> = {
  memoryLimitMb: { label: 'Memory', range: `between ${MEMORY_MIN_MB} and ${MEMORY_MAX_MB} MB` },
  cpuLimit: { label: 'CPU', range: `between ${CPU_MIN} and ${CPU_MAX}` },
  pidsLimit: { label: 'PIDs', range: `between ${PIDS_MIN} and ${PIDS_MAX}` },
};

const LIMIT_CODES: Record<string, LimitControlName> = {
  'Settings.InvalidMemoryLimit': 'memoryLimitMb',
  'Settings.InvalidCpuLimit': 'cpuLimit',
  'Settings.InvalidPidsLimit': 'pidsLimit',
};

const MOUNT_CODE_FIELDS: Record<string, readonly MountPathField[]> = {
  'Settings.MountContainerPathNotAbsolute': ['containerPath'],
  'Settings.SensitiveMountContainerPath': ['containerPath'],
  'Settings.DuplicateMountContainerPath': ['containerPath'],
  'Settings.MountHostPathNotAbsolute': ['hostPath'],
  'Settings.SensitiveMountHostPath': ['hostPath'],
  'Settings.MountPathTraversal': ['containerPath', 'hostPath'],
};

@Component({
  selector: 'fd-settings-worker-environment',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule],
  template: `
    <form class="general-settings worker-env" [formGroup]="form" (ngSubmit)="_save()" novalidate>
      <section class="general-settings__section">
        <h2 class="general-settings__section-title">Worker environment</h2>
        <p class="general-settings__section-description">
          Resource ceilings applied to every worker container.
        </p>

        <div class="general-settings__limits-fields worker-env__limits">
          <div class="general-settings__field">
            <label class="general-settings__field-label" for="memoryLimitMb">Memory (MB)</label>
            <input
              class="general-settings__number-input"
              type="number"
              id="memoryLimitMb"
              formControlName="memoryLimitMb"
              step="256"
              aria-describedby="memoryLimitMb-hint memoryLimitMb-error"
              [attr.aria-invalid]="_limitInvalid('memoryLimitMb') || null"
            />
            <span id="memoryLimitMb-hint" class="general-settings__field-hint">512–65,536 MB per worker.</span>
            <span id="memoryLimitMb-error" class="general-settings__field-error" role="alert">{{ _limitError('memoryLimitMb') }}</span>
          </div>

          <div class="general-settings__field">
            <label class="general-settings__field-label" for="cpuLimit">CPUs</label>
            <input
              class="general-settings__number-input"
              type="number"
              id="cpuLimit"
              formControlName="cpuLimit"
              step="0.5"
              aria-describedby="cpuLimit-hint cpuLimit-error"
              [attr.aria-invalid]="_limitInvalid('cpuLimit') || null"
            />
            <span id="cpuLimit-hint" class="general-settings__field-hint">0.5–32 CPUs per worker; fractions allowed.</span>
            <span id="cpuLimit-error" class="general-settings__field-error" role="alert">{{ _limitError('cpuLimit') }}</span>
          </div>

          <div class="general-settings__field">
            <label class="general-settings__field-label" for="pidsLimit">PIDs</label>
            <input
              class="general-settings__number-input"
              type="number"
              id="pidsLimit"
              formControlName="pidsLimit"
              step="1"
              aria-describedby="pidsLimit-hint pidsLimit-error"
              [attr.aria-invalid]="_limitInvalid('pidsLimit') || null"
            />
            <span id="pidsLimit-hint" class="general-settings__field-hint">64–8,192 processes per worker.</span>
            <span id="pidsLimit-error" class="general-settings__field-error" role="alert">{{ _limitError('pidsLimit') }}</span>
          </div>
        </div>
      </section>

      <section class="general-settings__section">
        <h2 class="general-settings__section-title">Host bind mounts</h2>
        <p class="general-settings__section-description">
          Host paths are bind-mounted into every worker as-is. If a host path is a symlink, enter its
          <strong>resolved target</strong> — Docker follows the link on the host, so the worker sees the real
          directory, not the link. On Windows, a drive-rooted path such as <code>C:\\dev\\ai\\skills</code> is valid.
        </p>

        <div class="worker-env__mounts" formArrayName="mounts">
          @if (mounts.length === 0) {
            <p class="general-settings__field-hint worker-env__empty">
              No mounts configured. Workers run with only their own workspace. Add a mount to expose a host
              directory (for example a shared skills folder) inside every worker.
            </p>
          } @else {
            <div class="worker-env__row worker-env__row--header" aria-hidden="true">
              <span>Container path</span>
              <span>Host path</span>
              <span>Read-only</span>
              <span></span>
            </div>
          }

          @for (row of mounts.controls; track row; let i = $index) {
            <div class="worker-env__row" [formGroupName]="i">
              <div class="general-settings__field">
                <label class="general-settings__field-label" [for]="'containerPath-' + i">Container path</label>
                <input
                  class="general-settings__text-input"
                  type="text"
                  [id]="'containerPath-' + i"
                  formControlName="containerPath"
                  placeholder="/work/skills"
                  autocomplete="off"
                  autocapitalize="off"
                  autocorrect="off"
                  spellcheck="false"
                  [attr.aria-describedby]="'containerPath-' + i + '-error'"
                  [attr.aria-invalid]="_pathInvalid(i, 'containerPath') || null"
                />
                <span [id]="'containerPath-' + i + '-error'" class="general-settings__field-error" role="alert">{{ _pathError(i, 'containerPath') }}</span>
              </div>

              <div class="general-settings__field">
                <label class="general-settings__field-label" [for]="'hostPath-' + i">Host path</label>
                <input
                  class="general-settings__text-input"
                  type="text"
                  [id]="'hostPath-' + i"
                  formControlName="hostPath"
                  placeholder="/srv/data  or  C:\\dev\\ai\\skills"
                  autocomplete="off"
                  autocapitalize="off"
                  autocorrect="off"
                  spellcheck="false"
                  [attr.aria-describedby]="'hostPath-' + i + '-error'"
                  [attr.aria-invalid]="_pathInvalid(i, 'hostPath') || null"
                />
                <span [id]="'hostPath-' + i + '-error'" class="general-settings__field-error" role="alert">{{ _pathError(i, 'hostPath') }}</span>
              </div>

              <label class="general-settings__checkbox-label" [for]="'readOnly-' + i">
                <input type="checkbox" [id]="'readOnly-' + i" formControlName="readOnly" />
                Read-only
              </label>

              <button
                type="button"
                class="general-settings__toggle-visibility-btn worker-env__remove"
                [id]="'removeMount-' + i"
                [attr.aria-label]="'Remove mount row ' + (i + 1)"
                (click)="_removeMount(i)"
              >
                <span aria-hidden="true">×</span>
              </button>
            </div>
          }

          <button type="button" id="addMount" class="general-settings__scan-btn" (click)="_addMount()">Add mount</button>
        </div>

        <p class="general-settings__field-hint worker-env__hooks">
          Host hooks — commands run on the host around a worker's lifecycle — will be configurable here later.
        </p>
      </section>

      <div class="worker-env__actions">
        <span class="general-settings__save-error" role="alert">{{ _summaryError() }}</span>
        <span class="general-settings__save-success" role="status">{{ _successText() }}</span>
        <button
          type="submit"
          class="general-settings__save-btn"
          [disabled]="settingsService.savingWorkerEnvironment() || form.invalid || !_isDirty()"
        >
          {{ settingsService.savingWorkerEnvironment() ? 'Saving...' : 'Save' }}
        </button>
      </div>
    </form>
  `,
  styleUrl: './settings-worker-environment.scss',
})
export class SettingsWorkerEnvironmentComponent implements UnsavedChanges {
  protected readonly settingsService = inject(SettingsService);
  private readonly _fb = inject(NonNullableFormBuilder);
  private readonly _host: ElementRef<HTMLElement> = inject(ElementRef);
  private readonly _injector = inject(Injector);
  private readonly _cdr = inject(ChangeDetectorRef);

  protected readonly form = this._fb.group({
    memoryLimitMb: this._fb.control(DEFAULT_MEMORY_MB, [
      Validators.required,
      Validators.min(MEMORY_MIN_MB),
      Validators.max(MEMORY_MAX_MB),
      wholeNumberValidator,
    ]),
    cpuLimit: this._fb.control(DEFAULT_CPU, [Validators.required, Validators.min(CPU_MIN), Validators.max(CPU_MAX)]),
    pidsLimit: this._fb.control(DEFAULT_PIDS, [
      Validators.required,
      Validators.min(PIDS_MIN),
      Validators.max(PIDS_MAX),
      wholeNumberValidator,
    ]),
    mounts: this._fb.array<MountRowGroup>([], uniqueContainerPathsValidator),
  });

  protected get mounts(): FormArray<MountRowGroup> {
    return this.form.controls.mounts;
  }

  protected readonly _summaryError: WritableSignal<string> = signal('');
  private readonly _formValue: WritableSignal<WorkerEnvironment> = signal(this.form.getRawValue());

  protected readonly _isDirty: Signal<boolean> = computed(() => {
    const baseline: WorkerEnvironment | null = this.settingsService.workerEnvironment();
    return baseline !== null && !environmentsEqual(baseline, this._formValue());
  });

  protected readonly _successText: Signal<string> = computed(() =>
    this.settingsService.saveWorkerEnvironmentSuccess() && !this._isDirty()
      ? 'Worker environment saved successfully'
      : ''
  );

  constructor() {
    this.form.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => {
      this._formValue.set(this.form.getRawValue());
      this._summaryError.set('');
      this._clearServerErrors();
    });

    effect(() => {
      const environment: WorkerEnvironment | null = this.settingsService.workerEnvironment();
      if (environment !== null) {
        untracked(() => this._seed(environment));
      }
    });

    effect(() => {
      const message: string | null = this.settingsService.saveWorkerEnvironmentError();
      const code: string | null = this.settingsService.saveWorkerEnvironmentErrorCode();
      if (message !== null) {
        untracked(() => this._showServerError(code, message));
      }
    });
  }

  hasUnsavedChanges(): boolean {
    return this._isDirty();
  }

  protected _save(): void {
    if (this.form.invalid || !this._isDirty() || this.settingsService.savingWorkerEnvironment()) {
      return;
    }
    this.settingsService.updateWorkerEnvironment(this.form.getRawValue());
  }

  protected _addMount(): void {
    this.mounts.push(this._createRow({ containerPath: '', hostPath: '', readOnly: true }));
    const index: number = this.mounts.length - 1;
    afterNextRender(() => this._focus(`containerPath-${index}`), { injector: this._injector });
  }

  protected _removeMount(index: number): void {
    this.mounts.removeAt(index);
    const targetId: string = index > 0 ? `removeMount-${index - 1}` : 'addMount';
    afterNextRender(() => this._focus(targetId), { injector: this._injector });
  }

  protected _limitInvalid(name: LimitControlName): boolean {
    const control: FormControl<number> = this.form.controls[name];
    return (control.touched && control.invalid) || !!control.errors?.[SERVER_ERROR_KEY];
  }

  protected _limitError(name: LimitControlName): string {
    const control: FormControl<number> = this.form.controls[name];
    const errors = control.errors;
    if (errors === null || !(control.touched || errors[SERVER_ERROR_KEY])) {
      return '';
    }
    const { label, range } = LIMIT_MESSAGES[name];
    if (errors[SERVER_ERROR_KEY]) {
      return errors[SERVER_ERROR_KEY];
    }
    if (errors['required']) {
      return `${label} is required.`;
    }
    if (errors['wholeNumber']) {
      return `${label} must be a whole number.`;
    }
    return `${label} must be ${range}.`;
  }

  protected _pathInvalid(index: number, field: MountPathField): boolean {
    return this._pathError(index, field) !== '';
  }

  protected _pathError(index: number, field: MountPathField): string {
    const control: FormControl<string> | undefined = this.mounts.at(index)?.controls[field];
    if (control === undefined) {
      return '';
    }
    const errors = control.errors;
    if (errors?.[SERVER_ERROR_KEY]) {
      return errors[SERVER_ERROR_KEY];
    }
    if (!control.touched) {
      return '';
    }
    if (errors?.['required']) {
      return `${field === 'containerPath' ? 'Container' : 'Host'} path is required.`;
    }
    if (errors?.['notAbsolute']) {
      return field === 'containerPath'
        ? 'Container path must start with /.'
        : 'Host path must be absolute (/… or a drive path such as C:\\…).';
    }
    if (errors?.['traversal']) {
      return "Path must not contain '..' segments.";
    }
    const duplicates: number[] = this.mounts.errors?.['duplicateContainerPath'] ?? [];
    if (field === 'containerPath' && duplicates.includes(index)) {
      return 'Container path is already used by another mount.';
    }
    return '';
  }

  private _createRow(mount: WorkerMountEntry): MountRowGroup {
    return this._fb.group({
      containerPath: this._fb.control(mount.containerPath, [
        Validators.required,
        absolutePathValidator('container' satisfies MountPathKind),
        noTraversalValidator,
      ]),
      hostPath: this._fb.control(mount.hostPath, [
        Validators.required,
        absolutePathValidator('host' satisfies MountPathKind),
        noTraversalValidator,
      ]),
      readOnly: this._fb.control(mount.readOnly),
    });
  }

  private _seed(environment: WorkerEnvironment): void {
    this.mounts.clear({ emitEvent: false });
    environment.mounts.forEach((mount) => this.mounts.push(this._createRow(mount), { emitEvent: false }));
    this.form.patchValue(
      {
        memoryLimitMb: environment.memoryLimitMb,
        cpuLimit: environment.cpuLimit,
        pidsLimit: environment.pidsLimit,
      },
      { emitEvent: false }
    );
    this.form.markAsPristine();
    this.form.markAsUntouched();
    this.form.updateValueAndValidity();
    this._cdr.markForCheck();
  }

  private _showServerError(code: string | null, detail: string): void {
    const limit: LimitControlName | undefined = code === null ? undefined : LIMIT_CODES[code];
    if (limit !== undefined) {
      this.form.controls[limit].setErrors({ [SERVER_ERROR_KEY]: detail });
    } else if (!this._attachToMountRow(code, detail)) {
      this._summaryError.set(detail);
    }
    this._cdr.markForCheck();
  }

  private _attachToMountRow(code: string | null, detail: string): boolean {
    const fields: readonly MountPathField[] | undefined = code === null ? undefined : MOUNT_CODE_FIELDS[code];
    if (fields === undefined) {
      return false;
    }
    for (const row of this.mounts.controls) {
      for (const field of fields) {
        const value: string = row.controls[field].value;
        if (value !== '' && detail.includes(value)) {
          row.controls[field].setErrors({ [SERVER_ERROR_KEY]: detail });
          return true;
        }
      }
    }
    return false;
  }

  private _clearServerErrors(): void {
    const controls: FormControl[] = [
      ...Object.values(this.form.controls).filter((control): control is FormControl => control instanceof FormControl),
      ...this.mounts.controls.flatMap((row) => [row.controls.containerPath, row.controls.hostPath]),
    ];
    controls
      .filter((control) => control.errors?.[SERVER_ERROR_KEY])
      .forEach((control) => control.updateValueAndValidity({ emitEvent: false }));
  }

  private _focus(id: string): void {
    this._host.nativeElement.querySelector<HTMLElement>(`#${id}`)?.focus();
  }
}

function environmentsEqual(left: WorkerEnvironment, right: WorkerEnvironment): boolean {
  return (
    left.memoryLimitMb === right.memoryLimitMb &&
    left.cpuLimit === right.cpuLimit &&
    left.pidsLimit === right.pidsLimit &&
    left.mounts.length === right.mounts.length &&
    left.mounts.every(
      (mount, index) =>
        mount.containerPath === right.mounts[index].containerPath &&
        mount.hostPath === right.mounts[index].hostPath &&
        mount.readOnly === right.mounts[index].readOnly
    )
  );
}

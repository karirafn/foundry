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
  hostSettingsPathValidator,
  noTraversalValidator,
  notBlankValidator,
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
const SAVING_TEXT = 'Saving worker environment...';
const SAVED_TEXT = 'Worker environment saved successfully';

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

const HOST_SETTINGS_PATH_CODES: ReadonlySet<string> = new Set([
  'Settings.HostSettingsPathNotAbsolute',
  'Settings.HostSettingsPathTraversal',
  'Settings.InvalidHostSettingsPathCharacters',
  'Settings.SensitiveHostSettingsPath',
  'Settings.HostSettingsPathTooLong',
]);

const MOUNT_CODE_FIELDS: Record<string, readonly MountPathField[]> = {
  'Settings.MountContainerPathNotAbsolute': ['containerPath'],
  'Settings.SensitiveMountContainerPath': ['containerPath'],
  'Settings.DuplicateMountContainerPath': ['containerPath'],
  'Settings.MountHostPathNotAbsolute': ['hostPath'],
  'Settings.SensitiveMountHostPath': ['hostPath'],
  'Settings.MountPathTraversal': ['containerPath', 'hostPath'],
  'Settings.InvalidMountPathCharacters': ['containerPath', 'hostPath'],
};

@Component({
  selector: 'fd-settings-worker-environment',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule],
  template: `
    <form class="general-settings worker-env" [formGroup]="form" (ngSubmit)="_save()" novalidate>
      <fieldset class="worker-env__fieldset" [disabled]="settingsService.savingWorkerEnvironment()">
      <legend class="sr-only">Worker environment settings. All fields are required.</legend>
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
              aria-required="true"
              min="${MEMORY_MIN_MB}"
              max="${MEMORY_MAX_MB}"
              aria-describedby="memoryLimitMb-hint memoryLimitMb-error"
              [attr.aria-invalid]="_limitInvalid('memoryLimitMb') || null"
            />
            <span id="memoryLimitMb-hint" class="general-settings__field-hint worker-env__hint">512–65,536 MB per worker.</span>
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
              aria-required="true"
              min="${CPU_MIN}"
              max="${CPU_MAX}"
              aria-describedby="cpuLimit-hint cpuLimit-error"
              [attr.aria-invalid]="_limitInvalid('cpuLimit') || null"
            />
            <span id="cpuLimit-hint" class="general-settings__field-hint worker-env__hint">0.5–32 CPUs per worker; fractions allowed.</span>
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
              aria-required="true"
              min="${PIDS_MIN}"
              max="${PIDS_MAX}"
              aria-describedby="pidsLimit-hint pidsLimit-error"
              [attr.aria-invalid]="_limitInvalid('pidsLimit') || null"
            />
            <span id="pidsLimit-hint" class="general-settings__field-hint worker-env__hint">64–8,192 processes per worker.</span>
            <span id="pidsLimit-error" class="general-settings__field-error" role="alert">{{ _limitError('pidsLimit') }}</span>
          </div>
        </div>
      </section>

      <section class="general-settings__section">
        <h2 class="general-settings__section-title">Host bind mounts</h2>
        <p id="hostPathGuidance" class="general-settings__section-description">
          Host paths are bind-mounted into every worker as-is. Accepted formats: an absolute POSIX path such as
          <code>/srv/data</code>, or a Windows drive-rooted path such as <code>C:\\dev\\ai\\skills</code>. If a host
          path is a symlink, enter its <strong>resolved target</strong> — Docker follows the link on the host, so the
          worker sees the real directory, not the link.
        </p>

        <div class="worker-env__mounts" formArrayName="mounts">
          @if (mounts.length === 0) {
            <p class="general-settings__field-hint worker-env__hint worker-env__empty">
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
            <div class="worker-env__row" role="group" [formGroupName]="i" [attr.aria-labelledby]="'mountLabel-' + i">
              <span class="sr-only" [id]="'mountLabel-' + i">Mount {{ i + 1 }}</span>
              <div class="general-settings__field">
                <label class="general-settings__field-label" [for]="'containerPath-' + i">Container path</label>
                <input
                  type="text"
                  [id]="'containerPath-' + i"
                  formControlName="containerPath"
                  class="general-settings__text-input worker-env__input"
                  aria-required="true"
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
                  class="general-settings__text-input worker-env__input"
                  aria-required="true"
                  type="text"
                  [id]="'hostPath-' + i"
                  formControlName="hostPath"
                  placeholder="/srv/data  or  C:\\dev\\ai\\skills"
                  autocomplete="off"
                  autocapitalize="off"
                  autocorrect="off"
                  spellcheck="false"
                  [attr.aria-describedby]="'hostPathGuidance hostPath-' + i + '-error'"
                  [attr.aria-invalid]="_pathInvalid(i, 'hostPath') || null"
                />
                <span [id]="'hostPath-' + i + '-error'" class="general-settings__field-error" role="alert">{{ _pathError(i, 'hostPath') }}</span>
              </div>

              <div class="worker-env__readonly">
                <label class="general-settings__checkbox-label" [for]="'readOnly-' + i">
                  <input type="checkbox" [id]="'readOnly-' + i" formControlName="readOnly" [attr.aria-describedby]="'readOnly-' + i + '-hint'" />
                  Read-only
                </label>
                <span [id]="'readOnly-' + i + '-hint'" class="general-settings__field-hint worker-env__hint">Unchecked: the worker can modify this host directory.</span>
              </div>

              <button
                type="button"
                class="general-settings__toggle-visibility-btn worker-env__remove"
                [id]="'removeMount-' + i"
                [attr.aria-label]="_removeLabel(i)"
                (click)="_removeMount(i)"
              >
                <span aria-hidden="true">×</span>
              </button>
            </div>
          }

          <button type="button" id="addMount" class="general-settings__scan-btn worker-env__add" (click)="_addMount()">Add mount</button>
        </div>
      </section>

      <section class="general-settings__section">
        <h2 class="general-settings__section-title">Host hooks</h2>
        <p id="hostHooksGuidance" class="general-settings__section-description">
          Worker hooks come from your host Claude Code <code>settings.json</code>. The file is mounted read-only into
          every worker and its <code>hooks</code> are merged into the worker settings each time a container starts, so
          a hook added on the host reaches the next worker. Other keys (permissions, env, status line) are not
          imported. If the configured file is missing or is not valid JSON the worker fails at bootstrap. Leave the
          path empty to run workers without host hooks.
        </p>

        <div class="general-settings__field">
          <label class="general-settings__field-label" for="hostSettingsPath">Host settings file</label>
          <input
            type="text"
            id="hostSettingsPath"
            formControlName="hostSettingsPath"
            class="general-settings__text-input worker-env__input"
            placeholder="/home/you/.claude/settings.json  or  C:\\Users\\you\\.claude\\settings.json"
            autocomplete="off"
            autocapitalize="off"
            autocorrect="off"
            spellcheck="false"
            aria-describedby="hostHooksGuidance hostSettingsPath-error"
            [attr.aria-invalid]="_hostSettingsPathInvalid() || null"
          />
          <span id="hostSettingsPath-error" class="general-settings__field-error" role="alert">{{ _hostSettingsPathError() }}</span>
        </div>

        <h3 class="general-settings__field-label">Excluded hook commands</h3>
        <p id="excludedHooksGuidance" class="general-settings__field-hint worker-env__hint">
          A host hook whose command exactly matches an entry is not run in workers — use this for host-specific hooks
          such as a Windows path check. Each worker logs the hooks it applied and any exclusion that matched no hook.
        </p>

        <div class="worker-env__mounts" formArrayName="excludedHookCommands">
          @if (excludedHookCommands.length === 0) {
            <p class="general-settings__field-hint worker-env__hint worker-env__empty">
              No exclusions. Every hook in the host settings file runs in workers.
            </p>
          }

          @for (row of excludedHookCommands.controls; track row; let i = $index) {
            <div class="worker-env__exclusion-row" role="group" [attr.aria-labelledby]="'exclusionLabel-' + i">
              <span class="sr-only" [id]="'exclusionLabel-' + i">Excluded hook command {{ i + 1 }}</span>
              <div class="general-settings__field">
                <label class="sr-only" [for]="'excludedHookCommand-' + i">Excluded hook command {{ i + 1 }}</label>
                <input
                  type="text"
                  [id]="'excludedHookCommand-' + i"
                  [formControlName]="i"
                  class="general-settings__text-input worker-env__input"
                  aria-required="true"
                  placeholder="bash ~/.claude/hooks/windows-path-check.sh"
                  autocomplete="off"
                  autocapitalize="off"
                  autocorrect="off"
                  spellcheck="false"
                  [attr.aria-describedby]="'excludedHooksGuidance excludedHookCommand-' + i + '-error'"
                  [attr.aria-invalid]="_exclusionInvalid(i) || null"
                />
                <span [id]="'excludedHookCommand-' + i + '-error'" class="general-settings__field-error" role="alert">{{ _exclusionError(i) }}</span>
              </div>
              <button
                type="button"
                class="general-settings__toggle-visibility-btn worker-env__remove"
                [id]="'removeExclusion-' + i"
                [attr.aria-label]="_removeExclusionLabel(i)"
                (click)="_removeExclusion(i)"
              >
                <span aria-hidden="true">×</span>
              </button>
            </div>
          }

          <button type="button" id="addExclusion" class="general-settings__scan-btn worker-env__add" (click)="_addExclusion()">
            Add exclusion
          </button>
        </div>
      </section>
      </fieldset>

      <div class="worker-env__actions">
        <span class="general-settings__save-error" role="alert">{{ _summaryError() }}</span>
        <span class="general-settings__save-success" role="status" aria-live="polite">{{ _statusText() }}</span>
        <button
          type="submit"
          class="general-settings__save-btn"
          [disabled]="settingsService.savingWorkerEnvironment() || !_isDirty()"
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
    hostSettingsPath: this._fb.control('', hostSettingsPathValidator),
    excludedHookCommands: this._fb.array<FormControl<string>>([]),
  });

  protected get mounts(): FormArray<MountRowGroup> {
    return this.form.controls.mounts;
  }

  protected get excludedHookCommands(): FormArray<FormControl<string>> {
    return this.form.controls.excludedHookCommands;
  }

  protected readonly _summaryError: WritableSignal<string> = signal('');
  private readonly _formValue: WritableSignal<WorkerEnvironment> = signal(this.form.getRawValue());

  protected readonly _isDirty: Signal<boolean> = computed(() => {
    const baseline: WorkerEnvironment | null = this.settingsService.workerEnvironment();
    return baseline !== null && !environmentsEqual(baseline, this._formValue());
  });

  protected readonly _statusText: Signal<string> = computed(() => {
    if (this.settingsService.savingWorkerEnvironment()) {
      return SAVING_TEXT;
    }
    return this.settingsService.saveWorkerEnvironmentSuccess() && !this._isDirty() ? SAVED_TEXT : '';
  });

  constructor() {
    this.form.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => {
      this._formValue.set(this.form.getRawValue());
      this._summaryError.set(this._attentionSummary());
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
    if (!this._isDirty() || this.settingsService.savingWorkerEnvironment()) {
      return;
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this._summaryError.set(this._attentionSummary());
      this._focusFirstInvalid();
      return;
    }
    this.settingsService.updateWorkerEnvironment(this._readForm());
  }

  protected _addMount(): void {
    this.mounts.push(this._createRow({ containerPath: '', hostPath: '', readOnly: true }));
    const index: number = this.mounts.length - 1;
    afterNextRender(() => this._focus(`containerPath-${index}`), { injector: this._injector });
  }

  protected _removeMount(index: number): void {
    this.mounts.removeAt(index);
    const remaining: number = this.mounts.length;
    let targetId: string = 'addMount';
    if (index < remaining) {
      targetId = `removeMount-${index}`;
    } else if (remaining > 0) {
      targetId = `removeMount-${remaining - 1}`;
    }
    afterNextRender(() => this._focus(targetId), { injector: this._injector });
  }

  protected _removeLabel(index: number): string {
    const containerPath: string | undefined = this.mounts.at(index)?.controls.containerPath.value;
    return `Remove mount ${containerPath || index + 1}`;
  }

  protected _addExclusion(): void {
    this.excludedHookCommands.push(this._createExclusion(''));
    const index: number = this.excludedHookCommands.length - 1;
    afterNextRender(() => this._focus(`excludedHookCommand-${index}`), { injector: this._injector });
  }

  protected _removeExclusion(index: number): void {
    this.excludedHookCommands.removeAt(index);
    const remaining: number = this.excludedHookCommands.length;
    let targetId: string = 'addExclusion';
    if (index < remaining) {
      targetId = `removeExclusion-${index}`;
    } else if (remaining > 0) {
      targetId = `removeExclusion-${remaining - 1}`;
    }
    afterNextRender(() => this._focus(targetId), { injector: this._injector });
  }

  protected _removeExclusionLabel(index: number): string {
    return `Remove excluded hook command ${this.excludedHookCommands.at(index)?.value || index + 1}`;
  }

  protected _hostSettingsPathInvalid(): boolean {
    return this._hostSettingsPathError() !== '';
  }

  protected _hostSettingsPathError(): string {
    const control: FormControl<string> = this.form.controls.hostSettingsPath;
    const errors = control.errors;
    if (errors?.[SERVER_ERROR_KEY]) {
      return errors[SERVER_ERROR_KEY];
    }
    if (!control.touched) {
      return '';
    }
    if (errors?.['notAbsolute']) {
      return 'Host settings file must be an absolute path (/… or a drive path such as C:\\…).';
    }
    if (errors?.['traversal']) {
      return "Path must not contain '..' segments.";
    }
    return '';
  }

  protected _exclusionInvalid(index: number): boolean {
    return this._exclusionError(index) !== '';
  }

  protected _exclusionError(index: number): string {
    const control: FormControl<string> | undefined = this.excludedHookCommands.at(index);
    if (control === undefined) {
      return '';
    }
    const errors = control.errors;
    if (errors?.[SERVER_ERROR_KEY]) {
      return errors[SERVER_ERROR_KEY];
    }
    return control.touched && (errors?.['required'] || errors?.['blank']) ? 'Excluded hook command is required.' : '';
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

  private _createExclusion(command: string): FormControl<string> {
    return this._fb.control(command, [Validators.required, notBlankValidator]);
  }

  private _readForm(): WorkerEnvironment {
    const value: WorkerEnvironment = this.form.getRawValue();
    return {
      ...value,
      hostSettingsPath: value.hostSettingsPath.trim(),
      excludedHookCommands: value.excludedHookCommands.map((command) => command.trim()),
    };
  }

  private _seed(environment: WorkerEnvironment): void {
    if (environmentsEqual(environment, this.form.getRawValue())) {
      this.form.markAsPristine();
      this.form.updateValueAndValidity();
      this._cdr.markForCheck();
      return;
    }
    this.mounts.clear({ emitEvent: false });
    environment.mounts.forEach((mount) => this.mounts.push(this._createRow(mount), { emitEvent: false }));
    this.excludedHookCommands.clear({ emitEvent: false });
    environment.excludedHookCommands.forEach((command) =>
      this.excludedHookCommands.push(this._createExclusion(command), { emitEvent: false })
    );
    this.form.patchValue(
      {
        memoryLimitMb: environment.memoryLimitMb,
        cpuLimit: environment.cpuLimit,
        pidsLimit: environment.pidsLimit,
        hostSettingsPath: environment.hostSettingsPath,
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
    } else if (code !== null && HOST_SETTINGS_PATH_CODES.has(code)) {
      this.form.controls.hostSettingsPath.setErrors({ [SERVER_ERROR_KEY]: detail });
    } else if (!this._attachToMountRow(code, detail)) {
      this._summaryError.set(detail);
      this._cdr.markForCheck();
      return;
    }
    this._summaryError.set(this._attentionSummary());
    this._cdr.markForCheck();
    this._focusFirstInvalid();
  }

  private _focusFirstInvalid(): void {
    afterNextRender(
      () => this._host.nativeElement.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus(),
      { injector: this._injector }
    );
  }

  private _attentionSummary(): string {
    const count: number = this._serverErrorControls().length;
    if (count === 0) {
      return '';
    }
    return count === 1 ? '1 field needs attention.' : `${count} fields need attention.`;
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

  private _serverErrorControls(): FormControl[] {
    const controls: FormControl[] = [
      ...Object.values(this.form.controls).filter((control): control is FormControl => control instanceof FormControl),
      ...this.mounts.controls.flatMap((row) => [row.controls.containerPath, row.controls.hostPath]),
      ...this.excludedHookCommands.controls,
    ];
    return controls.filter((control) => control.errors?.[SERVER_ERROR_KEY]);
  }

  private _focus(id: string): void {
    this._host.nativeElement.querySelector<HTMLElement>(`#${id}`)?.focus();
  }
}

function environmentsEqual(left: WorkerEnvironment, right: WorkerEnvironment): boolean {
  return (
    left.memoryLimitMb === right.memoryLimitMb &&
    left.hostSettingsPath === right.hostSettingsPath &&
    left.excludedHookCommands.length === right.excludedHookCommands.length &&
    left.excludedHookCommands.every((command, index) => command === right.excludedHookCommands[index]) &&
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

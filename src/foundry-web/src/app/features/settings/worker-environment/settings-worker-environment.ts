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
import { FormControl, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { SettingsService } from '../../../core/services/settings.service';
import { WorkerEnvironment } from '../../../core/models/settings.model';
import { UnsavedChanges } from '../guards/unsaved-changes.guard';
import {
  CPU_MAX,
  CPU_MIN,
  MEMORY_MAX_MB,
  MEMORY_MIN_MB,
  PIDS_MAX,
  PIDS_MIN,
  wholeNumberValidator,
} from './settings-worker-environment.validators';

type LimitControlName = 'memoryLimitMb' | 'cpuLimit' | 'pidsLimit';

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
  });

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

  private _readForm(): WorkerEnvironment {
    return this.form.getRawValue();
  }

  private _seed(environment: WorkerEnvironment): void {
    if (environmentsEqual(environment, this.form.getRawValue())) {
      this.form.markAsPristine();
      this.form.updateValueAndValidity();
      this._cdr.markForCheck();
      return;
    }
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
    if (limit === undefined) {
      this._summaryError.set(detail);
      this._cdr.markForCheck();
      return;
    }
    this.form.controls[limit].setErrors({ [SERVER_ERROR_KEY]: detail });
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

  private _serverErrorControls(): FormControl[] {
    const controls: FormControl[] = Object.values(this.form.controls);
    return controls.filter((control) => control.errors?.[SERVER_ERROR_KEY]);
  }
}

function environmentsEqual(left: WorkerEnvironment, right: WorkerEnvironment): boolean {
  return (
    left.memoryLimitMb === right.memoryLimitMb &&
    left.cpuLimit === right.cpuLimit &&
    left.pidsLimit === right.pidsLimit
  );
}

import {
  ChangeDetectionStrategy,
  Component,
  Signal,
  WritableSignal,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { SettingsService } from '../../../core/services/settings.service';
import { ImageBuildStatus, WorkerImageFlags } from '../../../core/models/settings.model';
import { UnsavedChanges } from '../guards/unsaved-changes.guard';
import { WorkerImageToolchainComponent } from './worker-image-toolchain';

const LATEST_TAG_SUFFIX = ':latest';
const ERROR_IMAGE_TAG_REQUIRED = 'Image tag is required.';
const ERROR_IMAGE_TAG_LATEST = "Pin a specific tag — ':latest' is not allowed because it is ambiguous across rebuilds.";

@Component({
  selector: 'fd-settings-worker-image',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, WorkerImageToolchainComponent],
  providers: [DatePipe],
  template: `
    <div class="general-settings">
      <section class="general-settings__section">
        <h2 class="general-settings__section-title">Worker Image</h2>
        <p class="general-settings__section-description">
          Every worker image comes with the full toolchain: the .NET SDK, the Angular CLI, headless Chromium and the Docker
          engine. Saving the image tag or the build toggle rebuilds the image in the background; dispatch pauses until the
          build finishes. Foundry also rebuilds the image a day after its last successful build; when that scheduled
          rebuild fails, dispatch keeps running on the current image and the failure shows below.
        </p>

        @if (settingsService.settings()) {
          <fd-worker-image-toolchain />
        }

        <div class="general-settings__image-form">
          <div class="general-settings__field">
            <label class="general-settings__field-label" for="imageTag">Worker image tag</label>
            <input
              class="general-settings__text-input"
              type="text"
              id="imageTag"
              autocomplete="off"
              autocapitalize="off"
              autocorrect="off"
              spellcheck="false"
              [ngModel]="_imageTagValue()"
              (ngModelChange)="_onImageTagChange($event)"
              aria-describedby="imageTag-hint imageTag-error image-flags-error"
              [attr.aria-invalid]="(_hasInteracted() && _imageTagHasError()) || !!settingsService.saveImageFlagsError() || null"
            />
            <span id="imageTag-hint" class="general-settings__field-hint">
              The Docker image tag for the worker container, e.g. <code>foundry-worker:local</code>. Pin a specific tag.
            </span>
            <span id="imageTag-error" class="general-settings__field-error" role="alert">{{ _hasInteracted() ? _imageTagErrorText() : '' }}</span>
          </div>

          <div class="general-settings__toggle">
            <label class="general-settings__toggle-row" for="buildEnabled">
              <input
                type="checkbox"
                id="buildEnabled"
                [ngModel]="_buildEnabledValue()"
                (ngModelChange)="_buildEnabledValue.set($event)"
                [attr.disabled]="settingsService.imageBuildStatus() === 'Building' || settingsService.savingImageFlags() || null"
                aria-describedby="buildEnabled-hint buildEnabled-off-note"
              />
              Build the worker image on change
            </label>
            <span id="buildEnabled-hint" class="general-settings__field-hint general-settings__toggle-hint">
              Turning this off stops Foundry building the image; Foundry runs the tag above as it is.
            </span>
            <p
              id="buildEnabled-off-note"
              class="general-settings__callout general-settings__callout--warning general-settings__toggle-hint"
              [hidden]="_buildEnabledValue()"
            >
              <span class="general-settings__callout-icon" aria-hidden="true">⚠</span>
              <span>
                <strong class="general-settings__callout-lead">Warning:</strong>
                Foundry never pulls images. With building off, the tag above must already exist on the Docker host —
                Foundry only inspects it. A missing tag leaves the image build failed and dispatch paused until the
                image exists or you turn building back on.
              </span>
            </p>
          </div>

          <div class="general-settings__image-feedback">
            <span class="sr-only general-settings__image-failure-alert" role="alert" aria-live="assertive">{{ imageFailureAlertText() }}</span>
            <div class="general-settings__image-status" role="status" aria-live="polite">@if (settingsService.imageBuildStatus() === 'Building') {<span class="general-settings__image-spinner" aria-hidden="true"></span>}{{ _imageBuildingText() }}</div>
            <div class="general-settings__image-status" role="status" aria-live="polite">{{ _imageFailedText() }}</div>
            <div id="image-flags-error" role="alert" class="general-settings__save-error">{{ settingsService.saveImageFlagsError() ?? '' }}</div>
            <div role="status" class="general-settings__save-success">{{ settingsService.saveImageFlagsSuccess() ? 'Worker image settings saved — building now…' : '' }}</div>
          </div>

          @if ((settingsService.imageBuildStatus() === 'Failed' || _scheduledRebuildFailed()) && settingsService.imageBuildLogTail()) {
            <pre
              class="general-settings__image-log"
              tabindex="0"
              aria-label="Build log output"
            >{{ settingsService.imageBuildLogTail() }}</pre>
          }

          <div class="general-settings__image-actions">
            <button
              class="general-settings__save-btn"
              type="button"
              [disabled]="settingsService.savingImageFlags() || settingsService.imageBuildStatus() === 'Building' || !_isImageFlagsDirty() || _imageTagHasError()"
              (click)="saveImageFlags()"
            >{{ settingsService.savingImageFlags() ? 'Saving...' : 'Save' }}</button>

            @if (settingsService.imageBuildStatus() === 'Failed') {
              <button
                class="general-settings__retry-btn"
                type="button"
                aria-label="Retry image build now"
                [disabled]="settingsService.savingImageFlags()"
                (click)="retryImageBuild()"
              >Retry</button>
            }
          </div>
        </div>

        <!-- A privilege switch, not image contents (ADR 0110): saved on its own, it rebuilds nothing, so it stays
             usable while a build runs or building is off. -->
        <div class="general-settings__image-form general-settings__nested-docker-form">
          <div class="general-settings__toggle">
            <label class="general-settings__toggle-row" for="nestedDockerEnabled">
              <input
                type="checkbox"
                id="nestedDockerEnabled"
                [ngModel]="_nestedDockerValue()"
                (ngModelChange)="_nestedDockerValue.set($event)"
                [attr.disabled]="settingsService.savingNestedDocker() || null"
                aria-describedby="nestedDockerEnabled-hint nestedDockerEnabled-warning nested-docker-error"
              />
              Nested Docker for implement runs
            </label>
            <span id="nestedDockerEnabled-hint" class="general-settings__field-hint general-settings__toggle-hint">Runs Testcontainers integration tests inside implement workers through a nested rootless Docker daemon.</span>
            <p id="nestedDockerEnabled-warning" class="general-settings__callout general-settings__callout--warning general-settings__toggle-hint">
              <span class="general-settings__callout-icon" aria-hidden="true">⚠</span>
              <span>
                <strong class="general-settings__callout-lead">Security cost:</strong>
                implement workers run with CAP_SYS_ADMIN and seccomp/AppArmor unconfined, so a privilege escalation inside
                a worker can reach the Docker host (the WSL VM on Docker Desktop). Applies to implement runs in every
                repository; triage runs never get it. Enable only if you trust all monitored repositories.
              </span>
            </p>
          </div>

          <div class="general-settings__image-feedback">
            <div id="nested-docker-error" role="alert" class="general-settings__save-error">{{ settingsService.saveNestedDockerError() ?? '' }}</div>
            <div role="status" class="general-settings__save-success">{{ settingsService.saveNestedDockerSuccess() ? 'Nested Docker setting saved — applies to runs dispatched from now on.' : '' }}</div>
          </div>

          <div class="general-settings__image-actions">
            <button
              class="general-settings__save-btn"
              type="button"
              aria-label="Save nested Docker setting"
              [disabled]="settingsService.savingNestedDocker() || !_isNestedDockerDirty()"
              (click)="saveNestedDocker()"
            >{{ settingsService.savingNestedDocker() ? 'Saving...' : 'Save' }}</button>
          </div>
        </div>
      </section>
    </div>
  `,
  styleUrl: './settings-worker-image.scss',
})
export class SettingsWorkerImageComponent implements UnsavedChanges {
  protected readonly settingsService = inject(SettingsService);
  private readonly _datePipe = inject(DatePipe);

  protected readonly _imageTagValue: WritableSignal<string> = signal('');
  protected readonly _buildEnabledValue: WritableSignal<boolean> = signal(true);
  protected readonly _nestedDockerValue: WritableSignal<boolean> = signal(false);
  protected readonly _hasInteracted: WritableSignal<boolean> = signal(false);
  private _imageInitialized = false;
  private _nestedDockerInitialized = false;

  protected readonly _imageTagHasError: Signal<boolean> = computed(() => {
    const tag = this._imageTagValue().trim();
    return tag.length === 0 || tag.toLowerCase().endsWith(LATEST_TAG_SUFFIX);
  });

  protected readonly _imageTagErrorText: Signal<string> = computed(() => {
    const tag = this._imageTagValue().trim();
    if (tag.length === 0) {
      return ERROR_IMAGE_TAG_REQUIRED;
    }
    if (tag.toLowerCase().endsWith(LATEST_TAG_SUFFIX)) {
      return ERROR_IMAGE_TAG_LATEST;
    }
    return '';
  });

  protected readonly _isImageFlagsDirty: Signal<boolean> = computed(() => {
    const saved = this.settingsService.workerImageFlags();
    if (saved === null) {
      return false;
    }
    return (
      this._imageTagValue().trim() !== saved.imageTag ||
      this._buildEnabledValue() !== saved.buildEnabled
    );
  });

  protected readonly _isNestedDockerDirty: Signal<boolean> = computed(() => {
    const saved = this.settingsService.nestedDockerEnabled();
    return saved !== null && this._nestedDockerValue() !== saved;
  });

  protected readonly _imageBuildingText: Signal<string> = computed(() =>
    this.settingsService.imageBuildStatus() === 'Building' ? 'Building worker image…' : ''
  );

  // A failed scheduled rebuild keeps the status Idle so dispatch runs on the current image; only its error says so.
  protected readonly _scheduledRebuildFailed: Signal<boolean> = computed(
    () => this.settingsService.imageBuildStatus() === 'Idle' && !!this.settingsService.imageBuildLogTail()
  );

  protected readonly _imageFailedText: Signal<string> = computed(() => {
    if (this._scheduledRebuildFailed()) {
      return this._scheduledRebuildFailedText();
    }
    if (this.settingsService.imageBuildStatus() !== 'Failed') {
      return '';
    }
    const retryAt = this.settingsService.imageBuildNextRetryAt();
    const attempt = this.settingsService.imageBuildAttempt();
    const formattedTime = retryAt ? this._datePipe.transform(retryAt, 'HH:mm') : null;
    const hasRetryAt = formattedTime !== null;
    const hasAttempt = attempt >= 1;

    if (hasRetryAt && hasAttempt) {
      return `Worker image build failed — retrying at ${formattedTime} (attempt ${attempt})`;
    }
    if (hasRetryAt) {
      return `Worker image build failed — retrying at ${formattedTime}`;
    }
    if (hasAttempt) {
      return `Worker image build failed (attempt ${attempt})`;
    }
    return 'Worker image build failed.';
  });

  private _scheduledRebuildFailedText(): string {
    const retryAt = this.settingsService.imageBuildNextRetryAt();
    const attempt = this.settingsService.imageBuildAttempt();
    const formattedTime = retryAt ? this._datePipe.transform(retryAt, 'HH:mm') : null;
    const retry = formattedTime !== null ? `; retrying at ${formattedTime}` : '';
    const attemptText = attempt >= 1 ? ` (attempt ${attempt})` : '';
    return `Scheduled rebuild failed — dispatch continues on the current image${retry}${attemptText}`;
  }

  private _imagePreviousStatus: ImageBuildStatus = 'Idle';
  private readonly _imageFailureAlertTextSignal: WritableSignal<string> = signal('');
  protected readonly imageFailureAlertText: Signal<string> = this._imageFailureAlertTextSignal.asReadonly();

  constructor() {
    effect(() => {
      const flags = this.settingsService.workerImageFlags();
      if (flags === null) {
        this._imageInitialized = false;
        return;
      }
      if (!this._imageInitialized) {
        this._imageInitialized = true;
        this._imageTagValue.set(flags.imageTag);
        this._buildEnabledValue.set(flags.buildEnabled);
      }
    });

    effect(() => {
      const enabled = this.settingsService.nestedDockerEnabled();
      if (enabled === null) {
        this._nestedDockerInitialized = false;
        return;
      }
      if (!this._nestedDockerInitialized) {
        this._nestedDockerInitialized = true;
        this._nestedDockerValue.set(enabled);
      }
    });

    // Fire a one-shot assertive alert when the image build status first transitions to Failed.
    effect(() => {
      const status = this.settingsService.imageBuildStatus();
      const prevStatus = this._imagePreviousStatus;
      this._imagePreviousStatus = status;

      if (status === 'Failed' && prevStatus !== 'Failed') {
        this._imageFailureAlertTextSignal.set('Worker image build failed.');
        // Clear after the AT has announced it; countdown ticks must not re-trigger the alert.
        setTimeout(() => this._imageFailureAlertTextSignal.set(''), 0);
      } else if (status !== 'Failed') {
        this._imageFailureAlertTextSignal.set('');
      }
    });
  }

  protected _onImageTagChange(value: string): void {
    this._hasInteracted.set(true);
    this._imageTagValue.set(value);
  }

  hasUnsavedChanges(): boolean {
    return this._isImageFlagsDirty() || this._isNestedDockerDirty();
  }

  saveImageFlags(): void {
    const flags: WorkerImageFlags = {
      imageTag: this._imageTagValue().trim(),
      buildEnabled: this._buildEnabledValue(),
    };
    this.settingsService.updateWorkerImageFlags(flags);
  }

  saveNestedDocker(): void {
    this.settingsService.updateNestedDocker(this._nestedDockerValue());
  }

  retryImageBuild(): void {
    this.settingsService.retryImageBuild();
  }
}

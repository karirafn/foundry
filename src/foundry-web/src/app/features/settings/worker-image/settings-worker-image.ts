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

const LATEST_TAG_SUFFIX = ':latest';
const ERROR_IMAGE_TAG_REQUIRED = 'Image tag is required.';
const ERROR_IMAGE_TAG_LATEST = "Pin a specific tag — ':latest' is not allowed because it is ambiguous across rebuilds.";

@Component({
  selector: 'fd-settings-worker-image',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  providers: [DatePipe],
  template: `
    <div class="general-settings">
      <section class="general-settings__section">
        <h2 class="general-settings__section-title">Worker Image</h2>
        <p class="general-settings__section-description">
          Choose which toolchains are preinstalled in the worker container image. Saving rebuilds the image in the background; dispatch pauses until the build finishes.
        </p>

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

          <label class="general-settings__checkbox-label" for="buildEnabled">
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

          <span id="buildEnabled-hint" class="general-settings__field-hint">
            Turning this off stops Foundry building the image and locks the toolchain settings below — the tag must already exist on the Docker host.
          </span>

          <p
            id="buildEnabled-off-note"
            class="general-settings__field-hint general-settings__field-hint--warning"
            [hidden]="_buildEnabledValue()"
          >
            Foundry never pulls images. With building off, the tag above must already exist on the Docker host —
            Foundry only inspects it. A missing tag leaves the image build failed and dispatch paused until the
            image exists or you turn building back on.
          </p>

          <fieldset class="general-settings__image-fieldset">
            <legend class="sr-only">Preinstalled toolchains</legend>

            <div class="general-settings__image-flags">
              <div class="general-settings__image-flag">
                <input
                  type="checkbox"
                  id="installDotnet"
                  [ngModel]="_installDotnetValue()"
                  (ngModelChange)="_installDotnetValue.set($event)"
                  [attr.disabled]="_flagsDisabled() || null"
                  aria-describedby="installDotnet-hint"
                />
                <label for="installDotnet">Install .NET SDK</label>
                <span id="installDotnet-hint" class="general-settings__field-hint">Adds the .NET SDK to the worker image.</span>
              </div>

              <div class="general-settings__image-flag">
                <input
                  type="checkbox"
                  id="installAngular"
                  [ngModel]="_installAngularValue()"
                  (ngModelChange)="_installAngularValue.set($event)"
                  [attr.disabled]="_flagsDisabled() || null"
                  aria-describedby="installAngular-hint"
                />
                <label for="installAngular">Install Angular CLI</label>
                <span id="installAngular-hint" class="general-settings__field-hint">Adds the Angular CLI (ng).</span>
              </div>

              <div class="general-settings__image-flag">
                <input
                  type="checkbox"
                  id="installChromium"
                  [ngModel]="_installChromiumValue()"
                  (ngModelChange)="_installChromiumValue.set($event)"
                  [attr.disabled]="_flagsDisabled() || null"
                  aria-describedby="installChromium-hint"
                />
                <label for="installChromium">Install Chromium (headless)</label>
                <span id="installChromium-hint" class="general-settings__field-hint">Adds a headless Chromium browser for Angular component tests.</span>
              </div>

              <div class="general-settings__image-flag">
                <input
                  type="checkbox"
                  id="installDocker"
                  [ngModel]="_installDockerValue()"
                  (ngModelChange)="_installDockerValue.set($event)"
                  [attr.disabled]="_flagsDisabled() || null"
                  aria-describedby="installDocker-hint"
                />
                <label for="installDocker">Install Docker engine + CLI</label>
                <span id="installDocker-hint" class="general-settings__field-hint general-settings__field-hint--warning">Runs Testcontainers integration tests inside the worker via a nested rootless Docker daemon. Security cost: the worker runs with CAP_SYS_ADMIN and seccomp/AppArmor unconfined, so a privilege escalation inside a worker can reach the Docker host (the WSL VM on Docker Desktop). Off by default; enable only for repositories you trust.</span>
              </div>
            </div>
          </fieldset>

          <span class="sr-only general-settings__image-failure-alert" role="alert" aria-live="assertive">{{ imageFailureAlertText() }}</span>
          <div class="general-settings__image-status" role="status" aria-live="polite">
            @if (settingsService.imageBuildStatus() === 'Building') {
              <span class="general-settings__image-spinner" aria-hidden="true"></span>
            }
            {{ _imageBuildingText() }}
          </div>
          <div class="general-settings__image-status" role="status" aria-live="polite">{{ _imageFailedText() }}</div>

          @if (settingsService.imageBuildStatus() === 'Failed' && settingsService.imageBuildLogTail()) {
            <pre
              class="general-settings__image-log"
              tabindex="0"
              aria-label="Build log output"
            >{{ settingsService.imageBuildLogTail() }}</pre>
          }

          <div id="image-flags-error" role="alert" class="general-settings__save-error">{{ settingsService.saveImageFlagsError() ?? '' }}</div>

          <div role="status" class="general-settings__save-success">{{ settingsService.saveImageFlagsSuccess() ? 'Worker image settings saved — building now…' : '' }}</div>

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
  protected readonly _installDotnetValue: WritableSignal<boolean> = signal(false);
  protected readonly _installAngularValue: WritableSignal<boolean> = signal(false);
  protected readonly _installChromiumValue: WritableSignal<boolean> = signal(false);
  protected readonly _installDockerValue: WritableSignal<boolean> = signal(false);
  protected readonly _hasInteracted: WritableSignal<boolean> = signal(false);
  private _imageInitialized = false;

  protected readonly _flagsDisabled: Signal<boolean> = computed(
    () => this.settingsService.imageBuildStatus() === 'Building' || this.settingsService.savingImageFlags() || !this._buildEnabledValue()
  );

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
      this._buildEnabledValue() !== saved.buildEnabled ||
      this._installDotnetValue() !== saved.installDotnet ||
      this._installAngularValue() !== saved.installAngular ||
      this._installChromiumValue() !== saved.installChromium ||
      this._installDockerValue() !== saved.installDocker
    );
  });

  protected readonly _imageBuildingText: Signal<string> = computed(() =>
    this.settingsService.imageBuildStatus() === 'Building' ? 'Building worker image…' : ''
  );

  protected readonly _imageFailedText: Signal<string> = computed(() => {
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
        this._installDotnetValue.set(flags.installDotnet);
        this._installAngularValue.set(flags.installAngular);
        this._installChromiumValue.set(flags.installChromium);
        this._installDockerValue.set(flags.installDocker);
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
    return this._isImageFlagsDirty();
  }

  saveImageFlags(): void {
    const flags: WorkerImageFlags = {
      imageTag: this._imageTagValue().trim(),
      buildEnabled: this._buildEnabledValue(),
      installDotnet: this._installDotnetValue(),
      installAngular: this._installAngularValue(),
      installChromium: this._installChromiumValue(),
      installDocker: this._installDockerValue(),
    };
    this.settingsService.updateWorkerImageFlags(flags);
  }

  retryImageBuild(): void {
    this.settingsService.retryImageBuild();
  }
}

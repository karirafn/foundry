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
          <fieldset class="general-settings__image-fieldset">
            <legend class="sr-only">Preinstalled toolchains</legend>

            <div class="general-settings__image-flags">
              <div class="general-settings__image-flag">
                <input
                  type="checkbox"
                  id="installDotnet"
                  [ngModel]="_installDotnetValue()"
                  (ngModelChange)="_installDotnetValue.set($event)"
                  [attr.disabled]="settingsService.imageBuildStatus() === 'Building' || null"
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
                  [attr.disabled]="settingsService.imageBuildStatus() === 'Building' || null"
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
                  [attr.disabled]="settingsService.imageBuildStatus() === 'Building' || null"
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
                  [attr.disabled]="settingsService.imageBuildStatus() === 'Building' || null"
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
              [disabled]="settingsService.savingImageFlags() || settingsService.imageBuildStatus() === 'Building' || !_isImageFlagsDirty()"
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

  protected readonly _installDotnetValue: WritableSignal<boolean> = signal(false);
  protected readonly _installAngularValue: WritableSignal<boolean> = signal(false);
  protected readonly _installChromiumValue: WritableSignal<boolean> = signal(false);
  protected readonly _installDockerValue: WritableSignal<boolean> = signal(false);
  private _imageInitialized = false;

  protected readonly _isImageFlagsDirty: Signal<boolean> = computed(() => {
    const saved = this.settingsService.workerImageFlags();
    if (saved === null) {
      return false;
    }
    return (
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

  hasUnsavedChanges(): boolean {
    return this._isImageFlagsDirty();
  }

  saveImageFlags(): void {
    const flags: WorkerImageFlags = {
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

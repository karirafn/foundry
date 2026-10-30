import {
  ChangeDetectionStrategy,
  Component,
  WritableSignal,
  effect,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { SettingsService } from '../../../core/services/settings.service';
import { UpdatePromptTemplatesRequest } from '../../../core/models/settings.model';
import { UnsavedChanges } from '../guards/unsaved-changes.guard';

@Component({
  selector: 'fd-settings-prompts',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  template: `
    <div class="general-settings">
      <section class="general-settings__section">
        <h2 class="general-settings__section-title">Worker Prompts</h2>
        <p class="general-settings__section-description">
          Customize the system and worker prompts sent to Claude Code containers. Use &#123;issueNumber&#125;, &#123;issueContent&#125;, and &#123;branchNamingInstruction&#125; as template variables.
        </p>

        <div class="general-settings__prompts-form">
          <div class="general-settings__field">
            <label class="general-settings__field-label" for="systemPromptTemplate">System Prompt Template</label>
            <textarea
              class="general-settings__textarea"
              id="systemPromptTemplate"
              rows="6"
              [ngModel]="_systemPromptValue()"
              (ngModelChange)="_systemPromptValue.set($event)"
              aria-describedby="prompts-error"
            ></textarea>
          </div>

          <div class="general-settings__field">
            <label class="general-settings__field-label" for="workerPromptTemplate">Worker Prompt Template</label>
            <textarea
              class="general-settings__textarea"
              id="workerPromptTemplate"
              rows="4"
              [ngModel]="_workerPromptValue()"
              (ngModelChange)="_workerPromptValue.set($event)"
              aria-describedby="prompts-error"
            ></textarea>
          </div>

          <div id="prompts-error" role="alert" class="general-settings__save-error">{{ settingsService.savePromptsError() ?? '' }}</div>

          <div role="status" class="general-settings__save-success">{{ settingsService.savePromptsSuccess() ? 'Prompt templates saved successfully' : '' }}</div>

          <button
            class="general-settings__save-btn"
            type="button"
            [disabled]="settingsService.savingPrompts()"
            (click)="savePrompts()"
          >{{ settingsService.savingPrompts() ? 'Saving...' : 'Save' }}</button>
        </div>
      </section>
    </div>
  `,
  styleUrl: './settings-prompts.scss',
})
export class SettingsPromptsComponent implements UnsavedChanges {
  protected readonly settingsService = inject(SettingsService);

  protected readonly _systemPromptValue: WritableSignal<string> = signal('');
  protected readonly _workerPromptValue: WritableSignal<string> = signal('');
  private _promptsInitialized = false;

  constructor() {
    effect(() => {
      const limits = this.settingsService.workerLimits();
      if (limits !== null && !this._promptsInitialized) {
        this._promptsInitialized = true;
        this._systemPromptValue.set(this.settingsService.systemPromptTemplate() ?? '');
        this._workerPromptValue.set(this.settingsService.workerPromptTemplate() ?? '');
      }
    });
  }

  hasUnsavedChanges(): boolean {
    const savedSystem = this.settingsService.systemPromptTemplate() ?? '';
    const savedWorker = this.settingsService.workerPromptTemplate() ?? '';
    return (
      this._systemPromptValue() !== savedSystem ||
      this._workerPromptValue() !== savedWorker
    );
  }

  savePrompts(): void {
    const request: UpdatePromptTemplatesRequest = {
      systemPromptTemplate: this._systemPromptValue() || null,
      workerPromptTemplate: this._workerPromptValue() || null,
    };
    this.settingsService.updatePromptTemplates(request);
  }
}

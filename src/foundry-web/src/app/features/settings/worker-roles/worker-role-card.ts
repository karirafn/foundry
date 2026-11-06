import {
  ChangeDetectionStrategy,
  Component,
  InputSignal,
  Signal,
  computed,
  input,
} from '@angular/core';
import { DatePipe, NgTemplateOutlet } from '@angular/common';
import { PromptTextComponent } from './prompt-text';
import {
  PromptSectionCondition,
  RolePluginComponent,
  WorkerRoleCard,
  WorkerRoleCatalog,
} from './worker-role.service';

const CONDITION_LABELS: Readonly<Record<PromptSectionCondition, string>> = {
  Always: 'Always',
  DotnetImage: '.NET image',
  DockerImage: 'Docker image',
  Fresh: 'Fresh',
  Continuation: 'Continuation',
  Revision: 'Revision',
  RebaseConflict: 'Rebase conflict',
};

const SHORT_COMMIT_LENGTH = 7;

interface PluginEntry {
  readonly component: RolePluginComponent;
  readonly shared: boolean;
}

/** One read-only card per worker role: prompts, prompt assembly, plugin skills/agents and verdict schema. */
@Component({
  selector: 'fd-worker-role-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, NgTemplateOutlet, PromptTextComponent],
  template: `
    <article class="role-card" [attr.aria-labelledby]="titleId()">
      <header class="role-card__header">
        <div class="role-card__heading">
          <h3 class="role-card__title" [id]="titleId()">{{ role().name }}</h3>
          @if (!catalog().image.verified) {
            <span
              class="role-card__badge role-card__badge--warning role-card__unverified"
              title="Image building is disabled; this catalog was read from disk and may differ from the image"
              >Unverified</span
            >
          }
        </div>
        @if (role().purpose) {
          <p class="role-card__purpose">{{ role().purpose }}</p>
        }
        <dl class="role-card__meta" aria-label="Worker image">
          <div class="role-card__meta-item">
            <dt>Image</dt>
            <dd>
              <code class="role-card__image-tag">{{ catalog().image.tag }}</code>
            </dd>
          </div>
          <div class="role-card__meta-item">
            <dt>Built</dt>
            <dd>
              <time class="role-card__built-at" [attr.datetime]="catalog().image.builtAt">{{
                catalog().image.builtAt | date: 'medium'
              }}</time>
            </dd>
          </div>
          <div class="role-card__meta-item">
            <dt>Commit</dt>
            <dd>
              <code class="role-card__commit" [attr.title]="catalog().image.commit">{{
                shortCommit()
              }}</code>
            </dd>
          </div>
        </dl>
      </header>

      <section class="role-card__block">
        <h4 class="role-card__block-title">Role prompt</h4>
        <h5 class="role-card__sub-title">System prompt</h5>
        <fd-prompt-text
          class="role-card__system-prompt"
          [text]="role().systemPrompt"
          [label]="role().name + ' system prompt'"
        />
        <h5 class="role-card__sub-title">Task prompt</h5>
        <fd-prompt-text
          class="role-card__task-prompt"
          [text]="role().taskPrompt"
          [label]="role().name + ' task prompt'"
        />
      </section>

      <section class="role-card__block">
        <h4 class="role-card__block-title">Prompt assembly</h4>
        <p class="role-card__hint">
          Foundry assembles the system prompt from these sections, in order. Conditional sections
          are included only when their condition holds for the run.
        </p>
        <ol class="role-card__list">
          @for (section of catalog().promptSections; track section.key) {
            <li>
              <details class="role-card__details role-card__section">
                <summary class="role-card__summary">
                  <span class="role-card__summary-name">{{ section.title }}</span>
                  <span class="role-card__badge role-card__condition">{{
                    conditionLabel(section.condition)
                  }}</span>
                </summary>
                @if (section.content === catalog().rolePromptSlot) {
                  <p class="role-card__slot-note">
                    The role's system prompt (above) is inserted here.
                  </p>
                } @else {
                  <fd-prompt-text [text]="section.content" [label]="section.title" />
                }
              </details>
            </li>
          }
        </ol>
      </section>

      <section class="role-card__block role-card__skills">
        <h4 class="role-card__block-title">Skills</h4>
        <ng-container
          *ngTemplateOutlet="pluginList; context: { $implicit: skills(), kind: 'skills' }"
        />
      </section>

      <section class="role-card__block role-card__agents">
        <h4 class="role-card__block-title">Agents</h4>
        <ng-container
          *ngTemplateOutlet="pluginList; context: { $implicit: agents(), kind: 'agents' }"
        />
      </section>

      <section class="role-card__block">
        <h4 class="role-card__block-title">Verdict schema</h4>
        @if (verdictSchema(); as schema) {
          <pre
            class="role-card__schema"
            tabindex="0"
            [attr.aria-label]="role().name + ' verdict schema'"
          ><code>{{ schema }}</code></pre>
        } @else {
          <p class="role-card__hint role-card__no-schema">No verdict schema (generative role).</p>
        }
      </section>
    </article>

    <ng-template #pluginList let-entries let-kind="kind">
      @if (entries.length === 0) {
        <p class="role-card__hint">No {{ kind }}.</p>
      } @else {
        <ul class="role-card__list">
          @for (entry of entries; track entry.component.path) {
            <li>
              <details class="role-card__details role-card__plugin">
                <summary class="role-card__summary">
                  <code class="role-card__summary-name">{{ entry.component.name }}</code>
                  @if (entry.shared) {
                    <span class="role-card__badge role-card__shared">Shared</span>
                  }
                  @if (entry.component.description) {
                    <span class="role-card__summary-description">{{
                      entry.component.description
                    }}</span>
                  }
                </summary>
                <p class="role-card__hint role-card__path">
                  <code>{{ entry.component.path }}</code>
                </p>
                <fd-prompt-text [text]="entry.component.content" [label]="entry.component.name" />
              </details>
            </li>
          }
        </ul>
      }
    </ng-template>
  `,
  styleUrl: './worker-role-card.scss',
})
export class WorkerRoleCardComponent {
  readonly role: InputSignal<WorkerRoleCard> = input.required<WorkerRoleCard>();
  readonly catalog: InputSignal<WorkerRoleCatalog> = input.required<WorkerRoleCatalog>();

  protected readonly titleId: Signal<string> = computed(
    () => `worker-role-${this.role().name.replace(/[^A-Za-z0-9_-]/g, '-')}-title`,
  );

  protected readonly shortCommit: Signal<string> = computed(() => {
    const commit = this.catalog().image.commit;
    return commit ? commit.slice(0, SHORT_COMMIT_LENGTH) : 'unknown';
  });

  protected readonly skills: Signal<PluginEntry[]> = computed(() => [
    ...this.role().skills.map((c) => toEntry(c, false)),
    ...this.catalog().shared.skills.map((c) => toEntry(c, true)),
  ]);

  protected readonly agents: Signal<PluginEntry[]> = computed(() => [
    ...this.role().agents.map((c) => toEntry(c, false)),
    ...this.catalog().shared.agents.map((c) => toEntry(c, true)),
  ]);

  protected readonly verdictSchema: Signal<string | null> = computed(() =>
    formatVerdictSchema(this.role().verdictSchema),
  );

  protected conditionLabel(condition: PromptSectionCondition): string {
    return CONDITION_LABELS[condition] ?? condition;
  }
}

function toEntry(component: RolePluginComponent, shared: boolean): PluginEntry {
  return { component, shared };
}

/** Pretty-prints the schema when it is valid JSON; otherwise returns it unchanged. */
function formatVerdictSchema(schema: string | null): string | null {
  if (schema === null) {
    return null;
  }
  try {
    return JSON.stringify(JSON.parse(schema), null, 2);
  } catch {
    return schema;
  }
}

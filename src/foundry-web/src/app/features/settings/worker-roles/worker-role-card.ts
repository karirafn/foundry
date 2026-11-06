import {
  ChangeDetectionStrategy,
  Component,
  InputSignal,
  Signal,
  computed,
  input,
} from '@angular/core';
import { DatePipe, NgTemplateOutlet } from '@angular/common';
import { PromptAssemblyComponent } from './prompt-assembly';
import { PromptTextComponent } from './prompt-text';
import { RolePluginComponent, WorkerRoleCard, WorkerRoleCatalog } from './worker-role.service';

const SHORT_COMMIT_LENGTH = 7;

interface PluginEntry {
  readonly component: RolePluginComponent;
  readonly shared: boolean;
}

/** One read-only card per worker role: prompts, prompt assembly, plugin skills/agents and verdict schema. */
@Component({
  selector: 'fd-worker-role-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, NgTemplateOutlet, PromptAssemblyComponent, PromptTextComponent],
  template: `
    <article class="role-card" [attr.aria-labelledby]="titleId()">
      <header class="role-card__header">
        <h3 class="role-card__title" [id]="titleId()">{{ role().name }}</h3>
        @if (role().purpose) {
          <p class="role-card__purpose">{{ role().purpose }}</p>
        }
        @if (!catalog().image.verified) {
          <p class="role-card__unverified" role="note">
            <span class="role-card__unverified-icon" aria-hidden="true">⚠</span>
            <span
              ><strong class="role-card__unverified-lead">Unverified image:</strong> the roles below
              were read from disk and may differ from <code>{{ catalog().image.tag }}</code
              >.</span
            >
          </p>
        }
        <dl class="role-card__meta">
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
        <p class="role-card__hint role-card__placeholder-hint">
          Placeholders such as
          <span class="role-card__placeholder-sample">&#123;issueNumber&#125;</span> are filled in
          by the worker at dispatch time.
        </p>
        <h5 class="role-card__sub-title">System prompt</h5>
        <fd-prompt-text class="role-card__system-prompt" [text]="role().systemPrompt" />
        <h5 class="role-card__sub-title">Task prompt</h5>
        <fd-prompt-text class="role-card__task-prompt" [text]="role().taskPrompt" />
      </section>

      <fd-prompt-assembly
        class="role-card__block"
        [sections]="catalog().promptSections"
        [rolePromptSlot]="catalog().rolePromptSlot"
      />

      <section class="role-card__block">
        <h4 class="role-card__block-title">Skills and agents</h4>
        @if (hasSharedEntries()) {
          <p class="role-card__hint role-card__shared-hint">
            Shared: {{ catalog().shared.description }}
          </p>
        }
        @if (skills().length === 0 && agents().length === 0) {
          <p class="role-card__hint">This role loads no skills or agents.</p>
        } @else {
          <div class="role-card__skills">
            <h5 class="role-card__sub-title">Skills</h5>
            <ng-container
              *ngTemplateOutlet="pluginList; context: { $implicit: skills(), kind: 'skills' }"
            />
          </div>
          <div class="role-card__agents">
            <h5 class="role-card__sub-title">Agents</h5>
            <ng-container
              *ngTemplateOutlet="pluginList; context: { $implicit: agents(), kind: 'agents' }"
            />
          </div>
        }
      </section>

      <section class="role-card__block">
        <h4 class="role-card__block-title">Verdict schema</h4>
        @if (verdictSchema(); as schema) {
          <pre
            class="role-card__schema"
            role="region"
            tabindex="0"
            [attr.aria-label]="role().name + ' verdict schema'"
          ><code>{{ schema }}</code></pre>
        } @else {
          <p class="role-card__hint role-card__no-schema">None. This role returns no verdict.</p>
        }
      </section>
    </article>

    <ng-template #pluginList let-entries let-kind="kind">
      @if (entries.length === 0) {
        <p class="role-card__hint">No {{ kind }}.</p>
      } @else {
        <ul class="role-card__list" role="list">
          @for (entry of entries; track entry.component.path) {
            <li>
              <details class="role-card__details role-card__plugin">
                <summary>
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
                <fd-prompt-text
                  [text]="entry.component.content"
                  [regionLabel]="entry.component.name + ' file'"
                />
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

  protected readonly hasSharedEntries: Signal<boolean> = computed(
    () => this.catalog().shared.skills.length + this.catalog().shared.agents.length > 0,
  );
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

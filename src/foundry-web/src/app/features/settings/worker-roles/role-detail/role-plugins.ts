import { ChangeDetectionStrategy, Component, InputSignal, Signal, computed, input } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { PromptTextComponent } from '../prompt-text';
import { RolePluginComponent, WorkerRoleCard, WorkerRoleCatalog } from '../worker-role.service';

interface PluginEntry {
  readonly component: RolePluginComponent;
  readonly shared: boolean;
}

/** The plugin skills and agents a role loads: its own, then the shared ones every role loads. */
@Component({
  selector: 'fd-role-plugins',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgTemplateOutlet, PromptTextComponent],
  template: `
    <section class="role-plugins" [attr.aria-labelledby]="titleId()">
      <h3 class="role-plugins__title" [id]="titleId()">Skills and agents</h3>
      @if (hasSharedEntries()) {
        <p class="role-plugins__hint role-plugins__shared-hint">
          Shared: {{ shared().description }}
        </p>
      }
      @if (skills().length === 0 && agents().length === 0) {
        <p class="role-plugins__hint">This role loads no skills or agents.</p>
      } @else {
        <div class="role-plugins__skills">
          <h4 class="role-plugins__sub-title">Skills</h4>
          <ng-container
            *ngTemplateOutlet="pluginList; context: { $implicit: skills(), kind: 'skills' }"
          />
        </div>
        <div class="role-plugins__agents">
          <h4 class="role-plugins__sub-title">Agents</h4>
          <ng-container
            *ngTemplateOutlet="pluginList; context: { $implicit: agents(), kind: 'agents' }"
          />
        </div>
      }
    </section>

    <ng-template #pluginList let-entries let-kind="kind">
      @if (entries.length === 0) {
        <p class="role-plugins__hint">No {{ kind }}.</p>
      } @else {
        <ul class="role-plugins__list" role="list">
          @for (entry of entries; track entry.component.path) {
            <li>
              <details class="role-plugins__plugin">
                <summary>
                  <code class="role-plugins__summary-name">{{ entry.component.name }}</code>
                  @if (entry.shared) {
                    <span class="role-plugins__badge role-plugins__shared">Shared</span>
                  }
                  @if (entry.component.description) {
                    <span class="role-plugins__summary-description">{{
                      entry.component.description
                    }}</span>
                  }
                </summary>
                <p class="role-plugins__hint role-plugins__path">
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
  styleUrl: './role-plugins.scss',
})
export class RolePluginsComponent {
  readonly role: InputSignal<WorkerRoleCard> = input.required<WorkerRoleCard>();
  readonly shared: InputSignal<WorkerRoleCatalog['shared']> =
    input.required<WorkerRoleCatalog['shared']>();

  protected readonly titleId: Signal<string> = computed(
    () => `worker-role-${this.role().name.replace(/[^A-Za-z0-9_-]/g, '-')}-plugins-title`,
  );

  protected readonly skills: Signal<PluginEntry[]> = computed(() => [
    ...this.role().skills.map((c) => toEntry(c, false)),
    ...this.shared().skills.map((c) => toEntry(c, true)),
  ]);

  protected readonly agents: Signal<PluginEntry[]> = computed(() => [
    ...this.role().agents.map((c) => toEntry(c, false)),
    ...this.shared().agents.map((c) => toEntry(c, true)),
  ]);

  protected readonly hasSharedEntries: Signal<boolean> = computed(
    () => this.shared().skills.length + this.shared().agents.length > 0,
  );
}

function toEntry(component: RolePluginComponent, shared: boolean): PluginEntry {
  return { component, shared };
}

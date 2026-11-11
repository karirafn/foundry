import {
  ChangeDetectionStrategy,
  Component,
  InputSignal,
  Signal,
  WritableSignal,
  computed,
  input,
  linkedSignal,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { RoleTagComponent } from '../../../shared/components/role-tag/role-tag';
import { PromptTextComponent } from './prompt-text';
import { PromptSegment, splitPromptPlaceholders } from './prompt-segments';
import { PromptSectionCondition, PromptSectionDescriptor } from './worker-role.service';

type ConditionKind = 'always' | 'image' | 'run';

interface ConditionTag {
  readonly label: string;
  readonly kind: ConditionKind;
}

const CONDITION_TAGS: Readonly<Record<PromptSectionCondition, ConditionTag>> = {
  Always: { label: 'Always', kind: 'always' },
  DotnetImage: { label: '.NET image', kind: 'image' },
  DockerImage: { label: 'Docker image', kind: 'image' },
  Fresh: { label: 'Fresh run', kind: 'run' },
  Continuation: { label: 'Continuation run', kind: 'run' },
  Revision: { label: 'Revision run', kind: 'run' },
  RebaseConflict: { label: 'Rebase conflict', kind: 'run' },
  Clarifications: { label: 'Clarification log', kind: 'run' },
};

interface SectionView {
  readonly key: string;
  readonly title: PromptSegment[];
  readonly content: string;
  readonly tag: ConditionTag;
  readonly isRoleSlot: boolean;
  readonly ordinal: number;
}

function toTag(condition: PromptSectionCondition): ConditionTag {
  return CONDITION_TAGS[condition] ?? { label: condition, kind: 'run' };
}

/** The ordered sections Foundry assembles into the system prompt, each with the condition that includes it. */
@Component({
  selector: 'fd-prompt-assembly',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgTemplateOutlet, PromptTextComponent, RoleTagComponent],
  template: `
    <div class="prompt-assembly__header">
      <h3 class="prompt-assembly__title">System prompt</h3>
      <button class="prompt-assembly__toggle-all" type="button" (click)="toggleAll()">
        {{ allOpen() ? 'Collapse all' : 'Expand all' }}
      </button>
    </div>
    <p class="prompt-assembly__hint">
      Foundry assembles the system prompt from these sections, in order. Conditional sections are
      included only when their condition holds for the run. This role's own prompt starts open.
    </p>
    <ol class="prompt-assembly__list" role="list">
      @for (section of views(); track section.key) {
        <li class="prompt-assembly__item">
          <span class="prompt-assembly__ordinal" aria-hidden="true">{{ section.ordinal }}</span>
          <details
            class="prompt-assembly__section"
            [class.prompt-assembly__section--role]="section.isRoleSlot"
            [open]="openKeys().has(section.key)"
            (toggle)="onToggle(section.key, $event)"
          >
            <summary class="prompt-assembly__summary">
              <span class="prompt-assembly__summary-name"
                ><span class="prompt-assembly__title-text"
                  ><ng-container
                    *ngTemplateOutlet="titleText; context: { $implicit: section.title }" /></span
                >@if (section.isRoleSlot) {
                  <fd-role-tag class="prompt-assembly__role-tag" [role]="roleName()" />
                }</span
              >
              <span [class]="conditionClass(section)">{{ section.tag.label }}</span>
            </summary>
            <fd-prompt-text [text]="section.isRoleSlot ? roleSystemPrompt() : section.content" />
          </details>
        </li>
      }
    </ol>

    <ng-template #titleText let-segments>
      @for (segment of segments; track $index) {
        @if (segment.placeholder) {
          <mark class="prompt-text__placeholder">{{ segment.text }}</mark>
        } @else {
          <span>{{ segment.text }}</span>
        }
      }
    </ng-template>
  `,
  styleUrl: './prompt-assembly.scss',
})
export class PromptAssemblyComponent {
  readonly sections: InputSignal<readonly PromptSectionDescriptor[]> =
    input.required<readonly PromptSectionDescriptor[]>();
  readonly rolePromptSlot: InputSignal<string> = input.required<string>();
  readonly roleName: InputSignal<string> = input.required<string>();
  readonly roleSystemPrompt: InputSignal<string> = input.required<string>();

  protected readonly views: Signal<SectionView[]> = computed(() =>
    this.sections().map((section, index) => ({
      key: section.key,
      title: splitPromptPlaceholders(section.title),
      content: section.content,
      tag: toTag(section.condition),
      isRoleSlot: section.content === this.rolePromptSlot(),
      ordinal: index + 1,
    })),
  );

  // Compared by value, so a catalog refresh with the same sections keeps what the user opened.
  private readonly _roleSlotKeys: Signal<readonly string[]> = computed(
    () => this.views().filter((view) => view.isRoleSlot).map((view) => view.key),
    { equal: (a, b) => a.length === b.length && a.every((key, i) => key === b[i]) },
  );

  private readonly _openKeys: WritableSignal<ReadonlySet<string>> = linkedSignal(
    () => new Set(this._roleSlotKeys()),
  );
  protected readonly openKeys: Signal<ReadonlySet<string>> = this._openKeys.asReadonly();

  protected readonly allOpen: Signal<boolean> = computed(() => {
    const views = this.views();
    return views.length > 0 && views.every((view) => this.openKeys().has(view.key));
  });

  protected conditionClass(section: SectionView): string {
    return `prompt-assembly__condition prompt-assembly__condition--${section.tag.kind}`;
  }

  protected toggleAll(): void {
    const keys = this.allOpen() ? [] : this.views().map((view) => view.key);
    this._openKeys.set(new Set(keys));
  }

  protected onToggle(key: string, event: Event): void {
    const open = (event.target as HTMLDetailsElement).open;
    if (open === this._openKeys().has(key)) {
      return;
    }
    const next = new Set(this._openKeys());
    if (open) {
      next.add(key);
    } else {
      next.delete(key);
    }
    this._openKeys.set(next);
  }
}

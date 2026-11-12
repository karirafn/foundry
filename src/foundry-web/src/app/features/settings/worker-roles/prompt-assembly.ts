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
import { PrincipleSectionsComponent } from './principle-sections';
import { PromptTextComponent } from './prompt-text';
import { PromptSegment, splitPromptPlaceholders } from './prompt-segments';
import { PromptSectionCondition, PromptSectionDescriptor } from './worker-role.service';

type ConditionKind = 'always' | 'image' | 'run' | 'excluded';

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

const EXCLUDED_TAG: ConditionTag = { label: 'Not included', kind: 'excluded' };

const NO_PRINCIPLES_NOTE = 'This role declares no principle sections, so its prompt has none.';

/** Which role-specific slot a section stands for, if any. */
type SectionSlot = 'rolePrompt' | 'principles' | null;

interface SectionView {
  readonly key: string;
  readonly title: PromptSegment[];
  readonly content: string;
  readonly tag: ConditionTag;
  readonly slot: SectionSlot;
  /** Whether this role's prompt carries the section; an excluded one renders as a static row. */
  readonly included: boolean;
  /** Only included sections are numbered. */
  readonly ordinal: number | null;
  readonly note: string | null;
}

function toTag(condition: PromptSectionCondition): ConditionTag {
  return CONDITION_TAGS[condition] ?? { label: condition, kind: 'run' };
}

/** The ordered sections Foundry assembles into the system prompt, each with the condition that includes it. */
@Component({
  selector: 'fd-prompt-assembly',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgTemplateOutlet, PrincipleSectionsComponent, PromptTextComponent, RoleTagComponent],
  template: `
    <div class="prompt-assembly__header">
      <h3 class="prompt-assembly__title">System prompt</h3>
      <button class="prompt-assembly__toggle-all" type="button" (click)="toggleAll()">
        {{ allOpen() ? 'Collapse all' : 'Expand all' }}
      </button>
    </div>
    <p class="prompt-assembly__hint">
      Foundry assembles the system prompt from these sections, in order. Conditional sections are
      included only when their condition holds for the run. Worker principles list the sections this
      role receives. This role's own prompt starts open.
    </p>
    <ol class="prompt-assembly__list" role="list">
      @for (section of views(); track section.key) {
        <li class="prompt-assembly__item">
          <span class="prompt-assembly__ordinal" aria-hidden="true">{{ section.ordinal }}</span>
          @if (section.included) {
            <details
              class="prompt-assembly__section"
              [class.prompt-assembly__section--role]="section.slot === 'rolePrompt'"
              [open]="openKeys().has(section.key)"
              (toggle)="onToggle(section.key, $event)"
            >
              <summary class="prompt-assembly__summary">
                <ng-container *ngTemplateOutlet="summaryName; context: { $implicit: section }" />
                <span [class]="conditionClass(section)">{{ section.tag.label }}</span>
              </summary>
              @switch (section.slot) {
                @case ('rolePrompt') {
                  <fd-prompt-text [text]="roleSystemPrompt()" />
                }
                @case ('principles') {
                  <fd-principle-sections
                    [sections]="principleSections()"
                    [selected]="rolePrincipleSections()"
                  />
                  <fd-prompt-text [text]="rolePrinciplesText()" />
                }
                @default {
                  <fd-prompt-text [text]="section.content" />
                }
              }
            </details>
          } @else {
            <div class="prompt-assembly__section prompt-assembly__static">
              <ng-container *ngTemplateOutlet="summaryName; context: { $implicit: section }" />
              <span [class]="conditionClass(section)">{{ section.tag.label }}</span>
            </div>
          }
        </li>
      }
    </ol>

    <ng-template #summaryName let-section>
      <span class="prompt-assembly__summary-name"
        ><span class="prompt-assembly__title-text"
          ><ng-container *ngTemplateOutlet="titleText; context: { $implicit: section.title }"
        /></span>
        @if (section.slot === 'rolePrompt') {
          <fd-role-tag class="prompt-assembly__role-tag" [role]="roleName()" />
        }
        @if (section.note) {
          <span class="prompt-assembly__note">{{ section.note }}</span>
        }
      </span>
    </ng-template>

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
  readonly principlesSlot: InputSignal<string> = input.required<string>();
  /** Every Principle Section name, in the fixed order. */
  readonly principleSections: InputSignal<readonly string[]> = input.required<readonly string[]>();
  readonly roleName: InputSignal<string> = input.required<string>();
  readonly roleSystemPrompt: InputSignal<string> = input.required<string>();
  /** The Principle Sections this role selects; none means its prompt carries no principles. */
  readonly rolePrincipleSections: InputSignal<readonly string[]> =
    input.required<readonly string[]>();
  readonly rolePrinciplesText: InputSignal<string> = input.required<string>();

  protected readonly views: Signal<SectionView[]> = computed(() => {
    let ordinal = 0;
    return this.sections().map((section) => {
      const slot = this.slotOf(section);
      // Decided from the selected names, never from empty text.
      const included = slot !== 'principles' || this.rolePrincipleSections().length > 0;
      return {
        key: section.key,
        title: splitPromptPlaceholders(section.title),
        content: section.content,
        tag: included ? toTag(section.condition) : EXCLUDED_TAG,
        slot,
        included,
        ordinal: included ? ++ordinal : null,
        note: slot === 'principles' ? this.principlesNote(included) : null,
      };
    });
  });

  // Compared by value, so a catalog refresh with the same sections keeps what the user opened.
  private readonly _roleSlotKeys: Signal<readonly string[]> = computed(
    () =>
      this.views()
        .filter((view) => view.slot === 'rolePrompt')
        .map((view) => view.key),
    { equal: (a, b) => a.length === b.length && a.every((key, i) => key === b[i]) },
  );

  private readonly _openKeys: WritableSignal<ReadonlySet<string>> = linkedSignal(
    () => new Set(this._roleSlotKeys()),
  );
  protected readonly openKeys: Signal<ReadonlySet<string>> = this._openKeys.asReadonly();

  /** The sections that can open: a static excluded row never counts toward Expand all. */
  private readonly _expandableKeys: Signal<readonly string[]> = computed(() =>
    this.views()
      .filter((view) => view.included)
      .map((view) => view.key),
  );

  protected readonly allOpen: Signal<boolean> = computed(() => {
    const keys = this._expandableKeys();
    return keys.length > 0 && keys.every((key) => this.openKeys().has(key));
  });

  protected conditionClass(section: SectionView): string {
    return `prompt-assembly__condition prompt-assembly__condition--${section.tag.kind}`;
  }

  protected toggleAll(): void {
    const keys = this.allOpen() ? [] : this._expandableKeys();
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

  private slotOf(section: PromptSectionDescriptor): SectionSlot {
    if (section.content === this.rolePromptSlot()) {
      return 'rolePrompt';
    }
    return section.content === this.principlesSlot() ? 'principles' : null;
  }

  private principlesNote(included: boolean): string {
    if (!included) {
      return NO_PRINCIPLES_NOTE;
    }
    const selected = this.rolePrincipleSections();
    const inOrder = this.principleSections().filter((name) => selected.includes(name));
    return `Includes: ${inOrder.join(', ')}`;
  }
}

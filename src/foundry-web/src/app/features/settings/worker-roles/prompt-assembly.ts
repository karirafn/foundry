import {
  ChangeDetectionStrategy,
  Component,
  InputSignal,
  Signal,
  WritableSignal,
  computed,
  input,
  signal,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
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
  imports: [NgTemplateOutlet, PromptTextComponent],
  template: `
    <div class="prompt-assembly__header">
      <h4 class="prompt-assembly__title">System prompt assembly</h4>
      <button class="prompt-assembly__toggle-all" type="button" (click)="toggleAll()">
        {{ allOpen() ? 'Collapse all' : 'Expand all' }}
      </button>
    </div>
    <p class="prompt-assembly__hint">
      Foundry assembles the system prompt from these sections, in order. Conditional sections are
      included only when their condition holds for the run.
    </p>
    <ol class="prompt-assembly__list" role="list">
      @for (section of views(); track section.key) {
        <li class="prompt-assembly__item">
          <span class="prompt-assembly__ordinal" aria-hidden="true">{{ section.ordinal }}</span>
          @if (section.isRoleSlot) {
            <div class="prompt-assembly__slot">
              <span class="prompt-assembly__summary-name"
                ><ng-container *ngTemplateOutlet="titleText; context: { $implicit: section.title }"
              /></span>
              <span class="prompt-assembly__slot-note">Shown under System prompt above.</span>
              <span [class]="conditionClass(section)">{{ section.tag.label }}</span>
            </div>
          } @else {
            <details
              class="prompt-assembly__section"
              [open]="openKeys().has(section.key)"
              (toggle)="onToggle(section.key, $event)"
            >
              <summary class="prompt-assembly__summary">
                <span class="prompt-assembly__summary-name"
                  ><ng-container
                    *ngTemplateOutlet="titleText; context: { $implicit: section.title }"
                /></span>
                <span [class]="conditionClass(section)">{{ section.tag.label }}</span>
              </summary>
              <fd-prompt-text [text]="section.content" />
            </details>
          }
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

  private readonly _openKeys: WritableSignal<ReadonlySet<string>> = signal(new Set<string>());
  protected readonly openKeys: Signal<ReadonlySet<string>> = this._openKeys.asReadonly();

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

  protected readonly allOpen: Signal<boolean> = computed(() => {
    const expandable = this.views().filter((view) => !view.isRoleSlot);
    return expandable.length > 0 && expandable.every((view) => this.openKeys().has(view.key));
  });

  protected conditionClass(section: SectionView): string {
    return `prompt-assembly__condition prompt-assembly__condition--${section.tag.kind}`;
  }

  protected toggleAll(): void {
    const keys = this.allOpen()
      ? []
      : this.views()
          .filter((view) => !view.isRoleSlot)
          .map((view) => view.key);
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

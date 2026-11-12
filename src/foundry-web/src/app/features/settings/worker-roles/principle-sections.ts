import {
  ChangeDetectionStrategy,
  Component,
  InputSignal,
  Signal,
  computed,
  input,
} from '@angular/core';

/** Which Principle Sections a role's system prompt carries and which it leaves out, each in the fixed order. */
@Component({
  selector: 'fd-principle-sections',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <dl class="principle-sections">
      <div class="principle-sections__group">
        <dt class="principle-sections__label">Included</dt>
        <dd class="principle-sections__value">
          <ul class="principle-sections__pills" role="list">
            @for (name of included(); track name) {
              <li class="principle-sections__pill principle-sections__pill--included">
                {{ name }}
              </li>
            }
          </ul>
        </dd>
      </div>
      @if (notIncluded().length > 0) {
        <div class="principle-sections__group">
          <dt class="principle-sections__label">Not included</dt>
          <dd class="principle-sections__value">
            <ul class="principle-sections__pills" role="list">
              @for (name of notIncluded(); track name) {
                <li class="principle-sections__pill principle-sections__pill--excluded">
                  {{ name }}
                </li>
              }
            </ul>
          </dd>
        </div>
      }
    </dl>
  `,
  styleUrl: './principle-sections.scss',
})
export class PrincipleSectionsComponent {
  /** Every Principle Section name, in the fixed order. */
  readonly sections: InputSignal<readonly string[]> = input.required<readonly string[]>();
  /** The names the role selects. */
  readonly selected: InputSignal<readonly string[]> = input.required<readonly string[]>();

  protected readonly included: Signal<readonly string[]> = computed(() =>
    this.sections().filter((name) => this.selected().includes(name)),
  );

  protected readonly notIncluded: Signal<readonly string[]> = computed(() =>
    this.sections().filter((name) => !this.selected().includes(name)),
  );
}

import { ChangeDetectionStrategy, Component, InputSignal, Signal, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { RoleCapabilityComponent } from '../role-shared/role-capability';
import { ImplementDemand, WorkerRoleCard } from '../worker-role.service';

const DEMAND_FACTS: readonly { readonly demand: ImplementDemand; readonly term: string }[] = [
  { demand: 'routine', term: 'Routine' },
  { demand: 'demanding', term: 'Demanding' },
];

/** A small card per worker role that opens its details page; the whole card is one link. */
@Component({
  selector: 'fd-role-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, RoleCapabilityComponent],
  template: `
    <article class="role-card" [attr.aria-labelledby]="titleId()">
      <div class="role-card__title-row">
        <h3 class="role-card__title" [id]="titleId()">
          <a class="role-card__link" [routerLink]="['/settings/roles', role().name]">{{
            role().name
          }}</a>
        </h3>
        <fd-role-capability [capability]="role().capability" />
      </div>
      @if (role().purpose) {
        <p class="role-card__purpose">{{ role().purpose }}</p>
      }
      <dl class="role-card__facts">
        @for (fact of demandFacts(); track fact.term) {
          <div>
            <dt>{{ fact.term }}</dt>
            <dd class="role-card__model">{{ fact.value }}</dd>
          </div>
        } @empty {
          <div>
            <dt>Model</dt>
            <dd class="role-card__model">{{ modelLabel() }}</dd>
          </div>
        }
        <div>
          <dt>Skills</dt>
          <dd class="role-card__skills">{{ role().skills.length }}</dd>
        </div>
        <div>
          <dt>Agents</dt>
          <dd class="role-card__agents">{{ role().agents.length }}</dd>
        </div>
        <div>
          <dt>Verdict</dt>
          <dd class="role-card__verdict">{{ role().verdictSchema === null ? 'No' : 'Yes' }}</dd>
        </div>
      </dl>
    </article>
  `,
  styleUrl: './role-card.scss',
})
export class RoleCardComponent {
  readonly role: InputSignal<WorkerRoleCard> = input.required<WorkerRoleCard>();

  protected readonly titleId: Signal<string> = computed(
    () => `worker-role-${this.role().name.replace(/[^A-Za-z0-9_-]/g, '-')}-title`,
  );

  /** An empty model leaves the run to the account default. */
  protected readonly modelLabel: Signal<string> = computed(() => this.role().model || 'Default');

  /** One fact per demand, routine first, for a role whose model is set per demand; empty otherwise. */
  protected readonly demandFacts: Signal<readonly { term: string; value: string }[]> = computed(() =>
    DEMAND_FACTS.flatMap(({ demand, term }) => {
      const setting = this.role().demandModels.find((d) => d.demand === demand);
      if (setting === undefined) {
        return [];
      }
      const model = setting.model || 'Default';
      return [{ term, value: setting.effort ? `${model} · ${setting.effort}` : model }];
    }),
  );
}

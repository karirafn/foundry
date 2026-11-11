import { ChangeDetectionStrategy, Component, InputSignal, input } from '@angular/core';
import { RoleCardComponent } from './role-card';
import { WorkerRoleCard } from '../worker-role.service';

/** The worker roles as a grid of cards that reflows to one column on narrow screens. */
@Component({
  selector: 'fd-role-card-grid',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RoleCardComponent],
  template: `
    <ul class="role-card-grid" role="list">
      @for (role of roles(); track role.name) {
        <li><fd-role-card [role]="role" /></li>
      }
    </ul>
  `,
  styleUrl: './role-card-grid.scss',
})
export class RoleCardGridComponent {
  readonly roles: InputSignal<readonly WorkerRoleCard[]> = input.required<readonly WorkerRoleCard[]>();
}

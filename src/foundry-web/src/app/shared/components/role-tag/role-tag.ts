import { ChangeDetectionStrategy, Component, InputSignal, input } from '@angular/core';

/** A run's Worker Role name as a small outlined, lower-case mono tag; screen readers hear "Role: <name>". */
@Component({
  selector: 'fd-role-tag',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="role-tag"><span class="sr-only">Role: </span>{{ role() }}</span>`,
  styleUrl: './role-tag.scss',
})
export class RoleTagComponent {
  readonly role: InputSignal<string> = input.required<string>();
}

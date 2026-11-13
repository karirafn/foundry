import {
  ChangeDetectionStrategy,
  Component,
  InputSignal,
  Signal,
  computed,
  input,
} from '@angular/core';
import { repoName } from '../board.model';

/**
 * An issue's repository and number. The repository name gives up width before the number does;
 * the full slug stays in the title and in screen-reader text. The consumer sets the font size.
 */
@Component({
  selector: 'fd-issue-ref',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '[attr.title]': 'slug()',
  },
  templateUrl: './issue-ref.html',
  styleUrl: './issue-ref.scss',
})
export class IssueRefComponent {
  readonly slug: InputSignal<string> = input.required<string>();
  readonly issueNumber: InputSignal<number> = input.required<number>();

  protected readonly name: Signal<string> = computed(() => repoName(this.slug()));
}

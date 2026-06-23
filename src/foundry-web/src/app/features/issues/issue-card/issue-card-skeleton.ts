import { Component } from '@angular/core';

@Component({
  selector: 'fd-issue-card-skeleton',
  standalone: true,
  template: `
    <div class="issue-card-skeleton" aria-hidden="true">
      <div class="issue-card-skeleton__meta">
        <span class="issue-card-skeleton__line issue-card-skeleton__line--number"></span>
        <span class="issue-card-skeleton__line issue-card-skeleton__line--slug"></span>
        <span class="issue-card-skeleton__line issue-card-skeleton__line--badge"></span>
      </div>
      <span class="issue-card-skeleton__line issue-card-skeleton__line--title"></span>
      <div class="issue-card-skeleton__footer">
        <span class="issue-card-skeleton__line issue-card-skeleton__line--timestamp"></span>
        <span class="issue-card-skeleton__line issue-card-skeleton__line--icon"></span>
      </div>
    </div>
  `,
  styleUrl: './issue-card-skeleton.scss',
})
export class IssueCardSkeletonComponent {}

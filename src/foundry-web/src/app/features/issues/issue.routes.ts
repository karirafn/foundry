import { Routes } from '@angular/router';

export const ISSUE_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./issue-list/issue-list').then((m) => m.IssueListComponent),
  },
];

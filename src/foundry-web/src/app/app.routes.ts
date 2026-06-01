import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', redirectTo: 'issues', pathMatch: 'full' },
  {
    path: 'issues',
    loadComponent: () =>
      import('./features/issues/issue-list/issue-list').then(
        (m) => m.IssueListComponent
      ),
  },
];

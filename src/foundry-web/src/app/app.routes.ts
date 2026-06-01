import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', redirectTo: 'issues', pathMatch: 'full' },
  {
    path: 'issues',
    loadChildren: () =>
      import('./features/issues/issue.routes').then((m) => m.ISSUE_ROUTES),
  },
];

import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', redirectTo: 'issues', pathMatch: 'full' },
  {
    path: 'issues',
    loadChildren: () =>
      import('./features/issues/issue.routes').then((m) => m.ISSUE_ROUTES),
  },
  {
    path: 'settings',
    loadChildren: () =>
      import('./features/settings/settings.routes').then((m) => m.SETTINGS_ROUTES),
  },
];

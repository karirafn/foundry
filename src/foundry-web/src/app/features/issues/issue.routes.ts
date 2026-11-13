import { Routes } from '@angular/router';

export const ISSUE_ROUTES: Routes = [
  {
    path: '',
    title: 'Resolved history',
    loadComponent: () =>
      import('./history-page/history-page').then((m) => m.HistoryPageComponent),
  },
];

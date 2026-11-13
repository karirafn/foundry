import { Routes } from '@angular/router';

export const BOARD_ROUTES: Routes = [
  {
    path: '',
    title: 'Board',
    loadComponent: () => import('./board-page/board-page').then((m) => m.BoardPageComponent),
  },
];

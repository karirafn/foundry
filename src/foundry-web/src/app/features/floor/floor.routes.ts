import { Routes } from '@angular/router';

export const FLOOR_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./floor-page/floor-page').then((m) => m.FloorPageComponent),
  },
];

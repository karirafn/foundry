import { Component } from '@angular/core';

@Component({
  selector: 'fd-empty-state',
  standalone: true,
  template: `
    <div
      class="empty-state"
      role="status"
      aria-label="No tracked issues"
    >
      <div class="empty-state__icon">
        <svg
          xmlns="http://www.w3.org/2000/svg"
          width="64"
          height="64"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="1.5"
          stroke-linecap="round"
          stroke-linejoin="round"
          aria-hidden="true"
        >
          <circle cx="12" cy="12" r="10" />
          <circle cx="12" cy="12" r="6" />
          <circle cx="12" cy="12" r="2" />
          <line x1="12" y1="2" x2="12" y2="4" />
          <line x1="12" y1="20" x2="12" y2="22" />
          <line x1="2" y1="12" x2="4" y2="12" />
          <line x1="20" y1="12" x2="22" y2="12" />
        </svg>
      </div>
      <h2 class="empty-state__heading">No tracked issues</h2>
      <p class="empty-state__subtext">
        Issues tagged with the trigger label will appear here automatically.
      </p>
    </div>
  `,
  styleUrl: './empty-state.scss',
})
export class EmptyStateComponent {}

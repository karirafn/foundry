import { Component, input, InputSignal } from '@angular/core';
import { ConnectionStatus } from '../../../core/services/signalr.service';

const STATUS_LABELS: Record<ConnectionStatus, string> = {
  connected: 'Connected',
  reconnecting: 'Reconnecting...',
  disconnected: 'Disconnected',
};

@Component({
  selector: 'fd-connection-indicator',
  standalone: true,
  template: `
    <div
      class="connection-indicator"
      role="status"
      aria-live="polite"
      [attr.aria-label]="ariaLabel()"
      [attr.title]="ariaLabel()"
    >
      <span class="connection-indicator__dot" [class]="dotClass()"></span>
      <span class="connection-indicator__label">{{ label() }}</span>
    </div>
  `,
  styleUrl: './connection-indicator.scss',
})
export class ConnectionIndicatorComponent {
  readonly status: InputSignal<ConnectionStatus> = input.required<ConnectionStatus>();

  label(): string {
    return STATUS_LABELS[this.status()];
  }

  ariaLabel(): string {
    return `Connection status: ${STATUS_LABELS[this.status()]}`;
  }

  dotClass(): string {
    return `connection-indicator__dot dot--${this.status()}`;
  }
}

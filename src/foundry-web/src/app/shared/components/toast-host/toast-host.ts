import { Component, Signal, inject } from '@angular/core';
import { Toast, ToastService } from '../../../core/services/toast.service';

@Component({
  selector: 'fd-toast',
  standalone: true,
  templateUrl: './toast-host.html',
  styleUrl: './toast-host.scss',
})
export class ToastHostComponent {
  private readonly _toastService = inject(ToastService);

  readonly toasts: Signal<Toast[]> = this._toastService.toasts;

  dismiss(id: number): void {
    this._toastService.dismiss(id);
  }

  onKeydown(event: KeyboardEvent, id: number): void {
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      this._toastService.dismiss(id);
    }
  }
}

import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { vi } from 'vitest';
import { ToastHostComponent } from './toast-host';
import { ToastService, Toast } from '../../../core/services/toast.service';

function createMockToastService(initial: Toast[] = []) {
  const toastsSignal = signal<Toast[]>(initial);
  const dismiss = vi.fn();
  return {
    toasts: toastsSignal.asReadonly(),
    dismiss,
    _signal: toastsSignal,
    show: vi.fn(),
  };
}

interface SetupOptions {
  toasts?: Toast[];
}

function setup(options: SetupOptions = {}) {
  const mockService = createMockToastService(options.toasts ?? []);

  TestBed.configureTestingModule({
    imports: [ToastHostComponent],
    providers: [{ provide: ToastService, useValue: mockService }],
  });

  const fixture = TestBed.createComponent(ToastHostComponent);
  fixture.detectChanges();
  return { fixture, mockService, el: fixture.nativeElement as HTMLElement };
}

describe('ToastHostComponent', () => {
  // Cycle 1: renders no toast elements when queue is empty
  it('should render no toast elements when the queue is empty', () => {
    // Arrange / Act
    const { el } = setup({ toasts: [] });

    // Assert
    expect(el.querySelectorAll('.toast').length).toBe(0);
  });

  // Cycle 2: renders one element per toast
  it('should render one toast element per entry in the queue', () => {
    // Arrange / Act
    const { el } = setup({
      toasts: [
        { id: 1, message: 'First' },
        { id: 2, message: 'Second' },
      ],
    });

    // Assert
    expect(el.querySelectorAll('.toast').length).toBe(2);
  });

  // Cycle 3: toast element shows message text
  it('should display the toast message text', () => {
    // Arrange / Act
    const { el } = setup({ toasts: [{ id: 1, message: 'Usage limit reset' }] });

    // Assert
    const toast = el.querySelector('.toast') as HTMLElement;
    expect(toast.textContent).toContain('Usage limit reset');
  });

  // Cycle 4: two identical messages stay distinct (tracked by id, not message)
  it('should render two distinct elements for two toasts with identical messages', () => {
    // Arrange / Act
    const { el } = setup({
      toasts: [
        { id: 1, message: 'Usage limit reset' },
        { id: 2, message: 'Usage limit reset' },
      ],
    });

    // Assert
    const toasts = el.querySelectorAll('.toast');
    expect(toasts.length).toBe(2);
  });

  // Cycle 5: clicking a toast calls service.dismiss with its id
  it('should call service.dismiss with the toast id when clicked', () => {
    // Arrange
    const { el, mockService } = setup({ toasts: [{ id: 42, message: 'Hello' }] });

    // Act
    const toast = el.querySelector('.toast') as HTMLElement;
    toast.click();

    // Assert
    expect(mockService.dismiss).toHaveBeenCalledWith(42);
  });

  // Cycle 6: each toast element has role="status"
  it('should have role="status" on each toast element', () => {
    // Arrange / Act
    const { el } = setup({ toasts: [{ id: 1, message: 'Test' }] });

    // Assert
    const toast = el.querySelector('.toast') as HTMLElement;
    expect(toast.getAttribute('role')).toBe('status');
  });

  // Cycle 7: container carries aria-live="polite"
  it('should have aria-live="polite" on the container', () => {
    // Arrange / Act
    const { el } = setup();

    // Assert
    const container = el.querySelector('.toast-host') as HTMLElement;
    expect(container.getAttribute('aria-live')).toBe('polite');
  });

  // Cycle 8: toast is focusable via tabindex="0"
  it('should make each toast focusable with tabindex="0"', () => {
    // Arrange / Act
    const { el } = setup({ toasts: [{ id: 1, message: 'Focusable' }] });

    // Assert
    const toast = el.querySelector('.toast') as HTMLElement;
    expect(toast.getAttribute('tabindex')).toBe('0');
  });

  // Cycle 9: toast has aria-label "Dismiss notification: <message>"
  it('should have an aria-label of "Dismiss notification: <message>" on each toast', () => {
    // Arrange / Act
    const { el } = setup({ toasts: [{ id: 1, message: 'Usage limit reset' }] });

    // Assert
    const toast = el.querySelector('.toast') as HTMLElement;
    expect(toast.getAttribute('aria-label')).toBe('Dismiss notification: Usage limit reset');
  });

  // Cycle 10: pressing Enter dismisses the toast
  it('should dismiss the toast when Enter is pressed', () => {
    // Arrange
    const { el, mockService } = setup({ toasts: [{ id: 7, message: 'Press Enter' }] });
    const toast = el.querySelector('.toast') as HTMLElement;

    // Act
    toast.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));

    // Assert
    expect(mockService.dismiss).toHaveBeenCalledWith(7);
  });

  // Cycle 11: pressing Space dismisses the toast
  it('should dismiss the toast when Space is pressed', () => {
    // Arrange
    const { el, mockService } = setup({ toasts: [{ id: 8, message: 'Press Space' }] });
    const toast = el.querySelector('.toast') as HTMLElement;

    // Act
    toast.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', bubbles: true }));

    // Assert
    expect(mockService.dismiss).toHaveBeenCalledWith(8);
  });

  // Cycle 12: reactive — new toast appears after signal update
  it('should render a new toast when the signal is updated', () => {
    // Arrange
    const { fixture, mockService, el } = setup({ toasts: [] });

    // Act
    mockService._signal.set([{ id: 99, message: 'New toast' }]);
    fixture.detectChanges();

    // Assert
    expect(el.querySelectorAll('.toast').length).toBe(1);
    expect(el.querySelector('.toast')?.textContent).toContain('New toast');
  });
});

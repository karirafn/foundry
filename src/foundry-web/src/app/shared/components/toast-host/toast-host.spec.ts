import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { vi } from 'vitest';
import { ToastHostComponent } from './toast-host';
import { ToastService, Toast } from '../../../core/services/toast.service';

function createMockToastService(initial: Toast[] = []) {
  const toastsSignal = signal<Toast[]>(initial);
  const dismiss = vi.fn();
  const pause = vi.fn();
  const resume = vi.fn();
  return {
    toasts: toastsSignal.asReadonly(),
    dismiss,
    pause,
    resume,
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

  // Cycle 6: each toast element has role="status" (live region per toast, not on container)
  it('should have role="status" on each toast element', () => {
    // Arrange / Act
    const { el } = setup({ toasts: [{ id: 1, message: 'Test' }] });

    // Assert
    const toast = el.querySelector('.toast') as HTMLElement;
    expect(toast.getAttribute('role')).toBe('status');
  });

  // Cycle 7: container must NOT have aria-live (role=status on each toast is the live region)
  it('should not have aria-live on the container — role="status" on each toast is the live region', () => {
    // Arrange / Act
    const { el } = setup();

    // Assert
    const container = el.querySelector('.toast-host') as HTMLElement;
    expect(container.hasAttribute('aria-live')).toBe(false);
  });

  // Cycle 8: toast outer div must NOT be focusable (button inside handles keyboard/AT)
  it('should not make the toast div focusable — the dismiss button handles keyboard access', () => {
    // Arrange / Act
    const { el } = setup({ toasts: [{ id: 1, message: 'Focusable' }] });

    // Assert
    const toast = el.querySelector('.toast') as HTMLElement;
    expect(toast.hasAttribute('tabindex')).toBe(false);
  });

  // Cycle 9: toast must NOT have aria-label on the outer div (live region announces visible text)
  it('should not have an aria-label on the toast div so the visible message text is announced', () => {
    // Arrange / Act
    const { el } = setup({ toasts: [{ id: 1, message: 'Usage limit reset' }] });

    // Assert
    const toast = el.querySelector('.toast') as HTMLElement;
    expect(toast.hasAttribute('aria-label')).toBe(false);
  });

  // Cycle 10: dismiss button exists inside each toast with correct aria-label
  it('should have a dismiss button inside each toast with aria-label "Dismiss notification"', () => {
    // Arrange / Act
    const { el } = setup({ toasts: [{ id: 1, message: 'Hello' }] });

    // Assert
    const button = el.querySelector('.toast .toast__dismiss') as HTMLButtonElement;
    expect(button).not.toBeNull();
    expect(button.getAttribute('type')).toBe('button');
    expect(button.getAttribute('aria-label')).toBe('Dismiss notification');
  });

  // Cycle 11: pressing Enter on the dismiss button dismisses the toast
  it('should dismiss the toast when Enter is pressed on the dismiss button', () => {
    // Arrange
    const { el, mockService } = setup({ toasts: [{ id: 7, message: 'Press Enter' }] });
    const button = el.querySelector('.toast .toast__dismiss') as HTMLButtonElement;

    // Act
    button.click();

    // Assert
    expect(mockService.dismiss).toHaveBeenCalledWith(7);
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

  // Cycle 13: mouseenter calls service.pause with toast id
  it('should call service.pause with the toast id on mouseenter', () => {
    // Arrange
    const { el, mockService } = setup({ toasts: [{ id: 5, message: 'Hover me' }] });

    // Act
    const toast = el.querySelector('.toast') as HTMLElement;
    toast.dispatchEvent(new MouseEvent('mouseenter', { bubbles: true }));

    // Assert
    expect(mockService.pause).toHaveBeenCalledWith(5);
  });

  // Cycle 14: mouseleave calls service.resume with toast id
  it('should call service.resume with the toast id on mouseleave', () => {
    // Arrange
    const { el, mockService } = setup({ toasts: [{ id: 5, message: 'Hover me' }] });

    // Act
    const toast = el.querySelector('.toast') as HTMLElement;
    toast.dispatchEvent(new MouseEvent('mouseleave', { bubbles: true }));

    // Assert
    expect(mockService.resume).toHaveBeenCalledWith(5);
  });

  // Cycle 15: focusin calls service.pause with toast id
  it('should call service.pause with the toast id on focusin', () => {
    // Arrange
    const { el, mockService } = setup({ toasts: [{ id: 5, message: 'Focus me' }] });

    // Act
    const toast = el.querySelector('.toast') as HTMLElement;
    toast.dispatchEvent(new FocusEvent('focusin', { bubbles: true }));

    // Assert
    expect(mockService.pause).toHaveBeenCalledWith(5);
  });

  // Cycle 16: focusout calls service.resume with toast id
  it('should call service.resume with the toast id on focusout', () => {
    // Arrange
    const { el, mockService } = setup({ toasts: [{ id: 5, message: 'Focus me' }] });

    // Act
    const toast = el.querySelector('.toast') as HTMLElement;
    toast.dispatchEvent(new FocusEvent('focusout', { bubbles: true }));

    // Assert
    expect(mockService.resume).toHaveBeenCalledWith(5);
  });
});

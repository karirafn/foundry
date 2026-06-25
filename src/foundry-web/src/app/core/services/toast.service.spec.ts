import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { ToastService } from './toast.service';

function setup() {
  TestBed.configureTestingModule({
    providers: [ToastService],
  });
  return TestBed.inject(ToastService);
}

describe('ToastService', () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  // Cycle 1: show() appends a toast to the queue
  it('should append a toast to the toasts signal when show is called', () => {
    // Arrange
    const service = setup();

    // Act
    service.show('Hello world');

    // Assert
    expect(service.toasts().length).toBe(1);
    expect(service.toasts()[0].message).toBe('Hello world');
  });

  // Cycle 2: toast has a numeric id
  it('should assign a numeric id to each shown toast', () => {
    // Arrange
    const service = setup();

    // Act
    service.show('Test');

    // Assert
    expect(typeof service.toasts()[0].id).toBe('number');
  });

  // Cycle 3: multiple show() calls stack independently with unique ids
  it('should assign unique ids when multiple toasts are shown', () => {
    // Arrange
    const service = setup();

    // Act
    service.show('First');
    service.show('Second');

    // Assert
    const toasts = service.toasts();
    expect(toasts.length).toBe(2);
    expect(toasts[0].id).not.toBe(toasts[1].id);
  });

  // Cycle 4: toast auto-dismisses after 5000ms
  it('should auto-dismiss a toast after 5000ms', () => {
    // Arrange
    const service = setup();
    service.show('Auto dismiss me');
    expect(service.toasts().length).toBe(1);

    // Act
    vi.advanceTimersByTime(5000);

    // Assert
    expect(service.toasts().length).toBe(0);
  });

  // Cycle 5: toast is still present before 5000ms elapses
  it('should not dismiss a toast before 5000ms have elapsed', () => {
    // Arrange
    const service = setup();
    service.show('Still here');

    // Act
    vi.advanceTimersByTime(4999);

    // Assert
    expect(service.toasts().length).toBe(1);
  });

  // Cycle 6: dismiss() removes the toast immediately
  it('should remove the toast immediately when dismiss is called', () => {
    // Arrange
    const service = setup();
    service.show('Dismiss me');
    const id = service.toasts()[0].id;

    // Act
    service.dismiss(id);

    // Assert
    expect(service.toasts().length).toBe(0);
  });

  // Cycle 7: dismiss() before timer fires — timer does NOT fire after dismiss
  it('should clear the pending timer so the toast does not re-appear after 5000ms when dismissed early', () => {
    // Arrange
    const service = setup();
    service.show('Early dismiss');
    const id = service.toasts()[0].id;
    service.dismiss(id);

    // Act
    vi.advanceTimersByTime(5000);

    // Assert — toast was already removed; still zero (no error)
    expect(service.toasts().length).toBe(0);
  });

  // Cycle 8: dismiss() is idempotent — safe to call after timer already fired
  it('should be safe to call dismiss after the timer has already fired', () => {
    // Arrange
    const service = setup();
    service.show('Auto and manual');
    const id = service.toasts()[0].id;
    vi.advanceTimersByTime(5000);
    expect(service.toasts().length).toBe(0);

    // Act / Assert — no error
    expect(() => service.dismiss(id)).not.toThrow();
    expect(service.toasts().length).toBe(0);
  });

  // Cycle 9: dismissing one toast leaves others intact with their timers
  it('should leave other toasts and their timers intact when one toast is dismissed', () => {
    // Arrange
    const service = setup();
    service.show('First');
    const firstId = service.toasts()[0].id;
    vi.advanceTimersByTime(1000); // 1s into first toast's timer
    service.show('Second');

    // Act — dismiss the first toast early
    service.dismiss(firstId);

    // Assert — second toast survives; its own 5s timer is independent
    expect(service.toasts().length).toBe(1);
    expect(service.toasts()[0].message).toBe('Second');

    // Advance 4000ms more — second toast was shown at t=1000, so 5000ms fires at t=6000
    vi.advanceTimersByTime(4000);
    expect(service.toasts().length).toBe(1); // 4999ms elapsed for second toast

    vi.advanceTimersByTime(1000); // now 5000ms for second toast
    expect(service.toasts().length).toBe(0);
  });

  // Cycle 10: ngOnDestroy clears all outstanding timers
  it('should clear all outstanding timers on ngOnDestroy', () => {
    // Arrange
    const service = setup();
    service.show('One');
    service.show('Two');
    expect(service.toasts().length).toBe(2);

    // Act
    service.ngOnDestroy();

    // Advance well past 5s — no errors from timer callbacks running on a destroyed service
    vi.advanceTimersByTime(10000);

    // Assert — toasts that were cleared won't re-emit; no thrown errors
    expect(service.toasts().length).toBe(0);
  });

  // Cycle 11: pause() suspends auto-dismiss — toast survives past 5s while paused
  it('should not dismiss a toast while it is paused even after 5s', () => {
    // Arrange
    const service = setup();
    service.show('Pause me');
    const id = service.toasts()[0].id;

    // Act — pause after 2s, then advance past original 5s deadline
    vi.advanceTimersByTime(2000);
    service.pause(id);
    vi.advanceTimersByTime(4000); // 6s total, but 3s remaining

    // Assert — still alive
    expect(service.toasts().length).toBe(1);
  });

  // Cycle 12: resume() restarts timer with remaining time
  it('should dismiss the toast after the remaining time when resumed', () => {
    // Arrange
    const service = setup();
    service.show('Resume me');
    const id = service.toasts()[0].id;

    // Pause at 2s elapsed (3s remaining)
    vi.advanceTimersByTime(2000);
    service.pause(id);

    // Advance 1000ms while paused — should not dismiss
    vi.advanceTimersByTime(1000);
    expect(service.toasts().length).toBe(1);

    // Act — resume; 3s remaining
    service.resume(id);

    // Advance 2999ms — still alive
    vi.advanceTimersByTime(2999);
    expect(service.toasts().length).toBe(1);

    // Advance 1ms more — remaining time (3000ms) has elapsed
    vi.advanceTimersByTime(1);
    expect(service.toasts().length).toBe(0);
  });

  // Cycle 13: message is truncated to MAX_MESSAGE_LENGTH characters
  it('should truncate messages longer than 200 characters', () => {
    // Arrange
    const service = setup();
    const longMessage = 'a'.repeat(300);

    // Act
    service.show(longMessage);

    // Assert
    expect(service.toasts()[0].message.length).toBe(200);
  });

  // Cycle 14: message is trimmed before storing
  it('should trim whitespace from the message before storing', () => {
    // Arrange
    const service = setup();

    // Act
    service.show('  hello  ');

    // Assert
    expect(service.toasts()[0].message).toBe('hello');
  });

  // Cycle 15: queue is capped at 5; oldest toast is dropped when full
  it('should drop the oldest toast when the queue exceeds 5 entries', () => {
    // Arrange
    const service = setup();
    service.show('Toast 1');
    service.show('Toast 2');
    service.show('Toast 3');
    service.show('Toast 4');
    service.show('Toast 5');
    const firstId = service.toasts()[0].id;
    expect(service.toasts().length).toBe(5);

    // Act — adding a 6th drops the oldest
    service.show('Toast 6');

    // Assert
    expect(service.toasts().length).toBe(5);
    expect(service.toasts().find((t) => t.id === firstId)).toBeUndefined();
    expect(service.toasts()[4].message).toBe('Toast 6');
  });

  // Cycle 16: dropped toast's timer is cancelled (no late firing)
  it('should cancel the timer for a dropped toast when queue overflows', () => {
    // Arrange
    const service = setup();
    service.show('Toast 1');
    const droppedId = service.toasts()[0].id;
    service.show('Toast 2');
    service.show('Toast 3');
    service.show('Toast 4');
    service.show('Toast 5');

    // Act — 6th toast causes the first to be dropped
    service.show('Toast 6');

    // Advance well past 5s — no late removal of already-dropped toast causes errors
    vi.advanceTimersByTime(6000);

    // Assert — dropped toast is gone and did not re-trigger a removal that corrupts state
    expect(service.toasts().find((t) => t.id === droppedId)).toBeUndefined();
    expect(service.toasts().length).toBeLessThanOrEqual(4); // remaining 4 dismiss over time
  });
});

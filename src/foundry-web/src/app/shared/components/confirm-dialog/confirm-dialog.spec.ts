import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ConfirmDialogComponent } from './confirm-dialog';

@Component({
  imports: [ConfirmDialogComponent],
  template: `
    <fd-confirm-dialog
      [open]="open()"
      title="Stop the worker?"
      message="Commits are kept."
      confirmLabel="Stop worker"
      cancelLabel="Keep running"
      [destructive]="destructive()"
      (confirmed)="onConfirmed()"
      (dismissed)="onDismissed()"
    />
  `,
})
class HostComponent {
  readonly open = signal(false);
  readonly destructive = signal(false);
  confirmedCount = 0;
  dismissedCount = 0;

  onConfirmed(): void {
    this.confirmedCount++;
    this.open.set(false);
  }

  onDismissed(): void {
    this.dismissedCount++;
    this.open.set(false);
  }
}

function setup() {
  TestBed.configureTestingModule({ imports: [HostComponent] });
  const fixture = TestBed.createComponent(HostComponent);
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  return {
    fixture,
    host: fixture.componentInstance,
    dialog: () => el.querySelector('dialog') as HTMLDialogElement,
  };
}

describe('ConfirmDialogComponent', () => {
  it('should render a native dialog that is closed by default', () => {
    // Arrange / Act
    const { dialog } = setup();

    // Assert
    expect(dialog()).not.toBeNull();
    expect(dialog().open).toBe(false);
  });

  it('should open the dialog when open becomes true', () => {
    // Arrange
    const { fixture, host, dialog } = setup();

    // Act
    host.open.set(true);
    fixture.detectChanges();

    // Assert
    expect(dialog().open).toBe(true);
  });

  it('should render the title, message and button labels', () => {
    // Arrange / Act
    const { dialog } = setup();

    // Assert
    expect(dialog().querySelector('.confirm-dialog__title')?.textContent).toBe('Stop the worker?');
    expect(dialog().querySelector('.confirm-dialog__message')?.textContent).toBe('Commits are kept.');
    expect(dialog().querySelector('.confirm-dialog__confirm')?.textContent).toBe('Stop worker');
    expect(dialog().querySelector('.confirm-dialog__cancel')?.textContent).toBe('Keep running');
  });

  it('should label the dialog by its title and describe it by its message', () => {
    // Arrange / Act
    const { dialog } = setup();
    const title = dialog().querySelector('.confirm-dialog__title') as HTMLElement;
    const message = dialog().querySelector('.confirm-dialog__message') as HTMLElement;

    // Assert
    expect(dialog().getAttribute('aria-labelledby')).toBe(title.id);
    expect(dialog().getAttribute('aria-describedby')).toBe(message.id);
  });

  it('should emit confirmed and close when the confirm button is clicked', () => {
    // Arrange
    const { fixture, host, dialog } = setup();
    host.open.set(true);
    fixture.detectChanges();

    // Act
    (dialog().querySelector('.confirm-dialog__confirm') as HTMLButtonElement).click();
    fixture.detectChanges();

    // Assert
    expect(host.confirmedCount).toBe(1);
    expect(host.dismissedCount).toBe(0);
    expect(dialog().open).toBe(false);
  });

  it('should emit dismissed and close when the cancel button is clicked', () => {
    // Arrange
    const { fixture, host, dialog } = setup();
    host.open.set(true);
    fixture.detectChanges();

    // Act
    (dialog().querySelector('.confirm-dialog__cancel') as HTMLButtonElement).click();
    fixture.detectChanges();

    // Assert
    expect(host.dismissedCount).toBe(1);
    expect(host.confirmedCount).toBe(0);
    expect(dialog().open).toBe(false);
  });

  it('should emit dismissed when the dialog is cancelled with Escape', () => {
    // Arrange
    const { fixture, host, dialog } = setup();
    host.open.set(true);
    fixture.detectChanges();

    // Act
    dialog().dispatchEvent(new Event('cancel', { cancelable: true }));
    fixture.detectChanges();

    // Assert
    expect(host.dismissedCount).toBe(1);
    expect(dialog().open).toBe(false);
  });

  it('should apply the destructive style to the confirm button when destructive', () => {
    // Arrange
    const { fixture, host, dialog } = setup();

    // Act
    host.destructive.set(true);
    fixture.detectChanges();

    // Assert
    expect(
      dialog().querySelector('.confirm-dialog__confirm')?.classList.contains('confirm-dialog__confirm--destructive'),
    ).toBe(true);
  });
});

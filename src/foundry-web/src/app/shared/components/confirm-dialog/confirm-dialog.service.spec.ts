import { TestBed } from '@angular/core/testing';
import { ConfirmDialogService } from './confirm-dialog.service';

function dialogEl(): HTMLDialogElement | null {
  return document.body.querySelector('fd-confirm-dialog dialog');
}

describe('ConfirmDialogService', () => {
  let service: ConfirmDialogService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(ConfirmDialogService);
  });

  afterEach(() => {
    document.body.querySelectorAll('fd-confirm-dialog').forEach((el) => el.remove());
  });

  it('should mount an open fd-confirm-dialog with the given title and message', () => {
    // Act
    void service.confirm({ title: 'Leave?', message: 'Unsaved work will be lost.' });

    // Assert
    const dialog = dialogEl();
    expect(dialog).toBeTruthy();
    expect(dialog?.open).toBe(true);
    expect(dialog?.querySelector('.confirm-dialog__title')?.textContent).toContain('Leave?');
    expect(dialog?.querySelector('.confirm-dialog__message')?.textContent).toContain('Unsaved work will be lost.');
  });

  it('should apply custom labels and destructive styling', () => {
    // Act
    void service.confirm({
      title: 'T',
      message: 'M',
      confirmLabel: 'Discard',
      cancelLabel: 'Stay',
      destructive: true,
    });

    // Assert
    const confirmBtn = document.body.querySelector('.confirm-dialog__confirm');
    expect(confirmBtn?.textContent).toContain('Discard');
    expect(confirmBtn?.classList.contains('confirm-dialog__confirm--destructive')).toBe(true);
    expect(document.body.querySelector('.confirm-dialog__cancel')?.textContent).toContain('Stay');
  });

  it('should resolve true and remove the dialog when confirmed', async () => {
    // Arrange
    const result = service.confirm({ title: 'T', message: 'M' });

    // Act
    (document.body.querySelector('.confirm-dialog__confirm') as HTMLButtonElement).click();

    // Assert
    await expect(result).resolves.toBe(true);
    expect(document.body.querySelector('fd-confirm-dialog')).toBeNull();
  });

  it('should resolve false and remove the dialog when cancelled', async () => {
    // Arrange
    const result = service.confirm({ title: 'T', message: 'M' });

    // Act
    (document.body.querySelector('.confirm-dialog__cancel') as HTMLButtonElement).click();

    // Assert
    await expect(result).resolves.toBe(false);
    expect(document.body.querySelector('fd-confirm-dialog')).toBeNull();
  });

  it('should resolve false when dismissed with Escape', async () => {
    // Arrange
    const result = service.confirm({ title: 'T', message: 'M' });

    // Act
    dialogEl()?.dispatchEvent(new Event('cancel', { cancelable: true }));

    // Assert
    await expect(result).resolves.toBe(false);
  });

  it('should dismiss a still-open confirmation when a new one is requested', async () => {
    // Arrange
    const first = service.confirm({ title: 'First', message: 'M' });

    // Act
    const second = service.confirm({ title: 'Second', message: 'M' });

    // Assert
    await expect(first).resolves.toBe(false);
    expect(document.body.querySelectorAll('fd-confirm-dialog').length).toBe(1);
    expect(dialogEl()?.querySelector('.confirm-dialog__title')?.textContent).toContain('Second');
    (document.body.querySelector('.confirm-dialog__confirm') as HTMLButtonElement).click();
    await expect(second).resolves.toBe(true);
  });
});

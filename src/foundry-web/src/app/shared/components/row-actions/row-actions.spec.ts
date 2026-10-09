import { TestBed } from '@angular/core/testing';
import { RowActionsComponent } from './row-actions';

function setup(overrides: { editLabel?: string; deleteLabel?: string; deleteBusy?: boolean } = {}) {
  const fixture = TestBed.createComponent(RowActionsComponent);
  fixture.componentRef.setInput('editLabel', overrides.editLabel ?? 'Edit item');
  fixture.componentRef.setInput('deleteLabel', overrides.deleteLabel ?? 'Delete item');
  if (overrides.deleteBusy !== undefined) {
    fixture.componentRef.setInput('deleteBusy', overrides.deleteBusy);
  }
  fixture.detectChanges();
  return {
    fixture,
    component: fixture.componentInstance,
    el: fixture.nativeElement as HTMLElement,
  };
}

describe('RowActionsComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [RowActionsComponent],
    }).compileComponents();
  });

  // Cycle 1: renders both buttons
  it('should render an edit button and a delete button', () => {
    // Arrange / Act
    const { el } = setup();

    // Assert
    const buttons = el.querySelectorAll('button');
    expect(buttons.length).toBe(2);
  });

  // Cycle 2: edit button aria-label comes from input
  it('should apply editLabel as the aria-label on the edit button', () => {
    // Arrange / Act
    const { el } = setup({ editLabel: 'Edit account my-github' });

    // Assert
    const editBtn = el.querySelector('.row-actions__edit-btn');
    expect(editBtn?.getAttribute('aria-label')).toBe('Edit account my-github');
  });

  // Cycle 3: delete button aria-label comes from input
  it('should apply deleteLabel as the aria-label on the delete button', () => {
    // Arrange / Act
    const { el } = setup({ deleteLabel: 'Delete account my-github' });

    // Assert
    const deleteBtn = el.querySelector('.row-actions__delete-btn');
    expect(deleteBtn?.getAttribute('aria-label')).toBe('Delete account my-github');
  });

  // Cycle 4: edit button title matches aria-label
  it('should apply editLabel as the title on the edit button', () => {
    // Arrange / Act
    const { el } = setup({ editLabel: 'Edit repository my-org/my-repo' });

    // Assert
    const editBtn = el.querySelector('.row-actions__edit-btn');
    expect(editBtn?.getAttribute('title')).toBe('Edit repository my-org/my-repo');
  });

  // Cycle 5: delete button title matches aria-label
  it('should apply deleteLabel as the title on the delete button', () => {
    // Arrange / Act
    const { el } = setup({ deleteLabel: 'Delete repository my-org/my-repo' });

    // Assert
    const deleteBtn = el.querySelector('.row-actions__delete-btn');
    expect(deleteBtn?.getAttribute('title')).toBe('Delete repository my-org/my-repo');
  });

  // Cycle 6: edit output emits when edit button clicked
  it('should emit the edit output when the edit button is clicked', () => {
    // Arrange
    const { el, component } = setup();
    let emitted = false;
    component.edit.subscribe(() => { emitted = true; });

    // Act
    const editBtn = el.querySelector('.row-actions__edit-btn') as HTMLButtonElement;
    editBtn.click();

    // Assert
    expect(emitted).toBe(true);
  });

  // Cycle 7: delete output emits when delete button clicked
  it('should emit the delete output when the delete button is clicked', () => {
    // Arrange
    const { el, component } = setup();
    let emitted = false;
    component.delete.subscribe(() => { emitted = true; });

    // Act
    const deleteBtn = el.querySelector('.row-actions__delete-btn') as HTMLButtonElement;
    deleteBtn.click();

    // Assert
    expect(emitted).toBe(true);
  });

  // Cycle 8: SVG icons are aria-hidden
  it('should render SVG icons as aria-hidden in both buttons', () => {
    // Arrange / Act
    const { el } = setup();

    // Assert
    const svgs = el.querySelectorAll('svg[aria-hidden="true"]');
    expect(svgs.length).toBe(2);
  });

  // Cycle 9: buttons are type="button" so they don't submit forms
  it('should set type="button" on both buttons', () => {
    // Arrange / Act
    const { el } = setup();

    // Assert
    const buttons = el.querySelectorAll('button');
    expect(buttons[0]?.getAttribute('type')).toBe('button');
    expect(buttons[1]?.getAttribute('type')).toBe('button');
  });

  // Cycle 10: deleteBusy=true renders fd-spinner, no trash SVG in delete button
  it('should render fd-spinner inside the delete button and hide the trash SVG when deleteBusy is true', () => {
    // Arrange / Act
    const { el } = setup({ deleteBusy: true });

    // Assert
    const deleteBtn = el.querySelector('.row-actions__delete-btn') as HTMLElement;
    const spinner = deleteBtn.querySelector('fd-spinner');
    const svg = deleteBtn.querySelector('svg');
    expect(spinner).not.toBeNull();
    expect(svg).toBeNull();
  });

  // Cycle 11: deleteBusy=true → edit button carries aria-disabled="true"; delete button is natively disabled
  it('should set aria-disabled="true" on the edit button and native disabled on the delete button when deleteBusy is true', () => {
    // Arrange / Act
    const { el } = setup({ deleteBusy: true });

    // Assert
    const editBtn = el.querySelector('.row-actions__edit-btn');
    const deleteBtn = el.querySelector('.row-actions__delete-btn') as HTMLButtonElement;
    expect(editBtn?.getAttribute('aria-disabled')).toBe('true');
    expect(deleteBtn?.disabled).toBe(true);
  });

  // Cycle 12: deleteBusy=true → clicking delete emits nothing (no-op guard)
  it('should not emit delete when deleteBusy is true and the delete button is clicked', () => {
    // Arrange
    const { el, component } = setup({ deleteBusy: true });
    let emitCount = 0;
    component.delete.subscribe(() => { emitCount++; });

    // Act
    const deleteBtn = el.querySelector('.row-actions__delete-btn') as HTMLButtonElement;
    deleteBtn.click();

    // Assert
    expect(emitCount).toBe(0);
  });

  // Cycle 13: deleteBusy=false (default) → trash SVG present, no spinner, no aria-disabled, not disabled
  it('should show trash SVG, no spinner, no aria-disabled on edit, and not disabled on delete when deleteBusy is false (default)', () => {
    // Arrange / Act
    const { el } = setup();

    // Assert
    const deleteBtn = el.querySelector('.row-actions__delete-btn') as HTMLButtonElement;
    const editBtn = el.querySelector('.row-actions__edit-btn') as HTMLElement;
    expect(deleteBtn.querySelector('svg')).not.toBeNull();
    expect(deleteBtn.querySelector('fd-spinner')).toBeNull();
    expect(editBtn.getAttribute('aria-disabled')).toBeNull();
    expect(deleteBtn.disabled).toBe(false);
  });

  // Cycle 14a: deleteBusy=true → clicking edit emits nothing (no-op guard)
  it('should not emit edit when deleteBusy is true and the edit button is clicked', () => {
    // Arrange
    const { el, component } = setup({ deleteBusy: true });
    let emitCount = 0;
    component.edit.subscribe(() => { emitCount++; });

    // Act
    const editBtn = el.querySelector('.row-actions__edit-btn') as HTMLButtonElement;
    editBtn.click();

    // Assert
    expect(emitCount).toBe(0);
  });

  // Cycle 14b: deleteBusy=false (default) → clicking edit emits once
  it('should emit edit exactly once when deleteBusy is false and the edit button is clicked', () => {
    // Arrange
    const { el, component } = setup({ deleteBusy: false });
    let emitCount = 0;
    component.edit.subscribe(() => { emitCount++; });

    // Act
    const editBtn = el.querySelector('.row-actions__edit-btn') as HTMLButtonElement;
    editBtn.click();

    // Assert
    expect(emitCount).toBe(1);
  });

  // Cycle 14: disabled styling — edit button uses aria-disabled, delete button uses native disabled
  it('should set aria-disabled="true" on the edit button and native disabled on the delete button when deleteBusy is true', () => {
    // Arrange / Act
    const { el } = setup({ deleteBusy: true });

    // Assert — edit button uses aria-disabled for CSS [aria-disabled="true"] { cursor: not-allowed }
    // delete button is natively disabled (removes it from tab order, blocks click events)
    const editBtn = el.querySelector('.row-actions__edit-btn') as HTMLElement;
    const deleteBtn = el.querySelector('.row-actions__delete-btn') as HTMLButtonElement;
    expect(editBtn.getAttribute('aria-disabled')).toBe('true');
    expect(deleteBtn.disabled).toBe(true);
  });
});

import { TestBed } from '@angular/core/testing';
import { RowActionsComponent } from './row-actions';

function setup(overrides: { editLabel?: string; deleteLabel?: string } = {}) {
  const fixture = TestBed.createComponent(RowActionsComponent);
  fixture.componentRef.setInput('editLabel', overrides.editLabel ?? 'Edit item');
  fixture.componentRef.setInput('deleteLabel', overrides.deleteLabel ?? 'Delete item');
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
});

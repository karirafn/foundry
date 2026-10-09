import { TestBed } from '@angular/core/testing';
import { DeleteButtonComponent } from './delete-button';

function setup(overrides: { deleteLabel?: string; deleteBusy?: boolean } = {}) {
  const fixture = TestBed.createComponent(DeleteButtonComponent);
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

describe('DeleteButtonComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DeleteButtonComponent],
    }).compileComponents();
  });

  // Cycle 1: renders a button with deleteLabel as aria-label
  it('should render a button with deleteLabel as aria-label', () => {
    // Arrange / Act
    const { el } = setup({ deleteLabel: 'Delete account my-github' });

    // Assert
    const btn = el.querySelector('button');
    expect(btn).not.toBeNull();
    expect(btn?.getAttribute('aria-label')).toBe('Delete account my-github');
  });

  // Cycle 2: deleteLabel also set as title attribute
  it('should set deleteLabel as the title attribute on the button', () => {
    // Arrange / Act
    const { el } = setup({ deleteLabel: 'Delete repository my-org/my-repo' });

    // Assert
    const btn = el.querySelector('button');
    expect(btn?.getAttribute('title')).toBe('Delete repository my-org/my-repo');
  });

  // Cycle 3: button is type="button" so it does not submit forms
  it('should set type="button" on the delete button', () => {
    // Arrange / Act
    const { el } = setup();

    // Assert
    const btn = el.querySelector('button');
    expect(btn?.getAttribute('type')).toBe('button');
  });

  // Cycle 4: trash SVG is present and aria-hidden when not busy
  it('should render the trash icon SVG as aria-hidden when not busy', () => {
    // Arrange / Act
    const { el } = setup({ deleteBusy: false });

    // Assert
    const btn = el.querySelector('button');
    const svg = btn?.querySelector('svg[aria-hidden="true"]');
    expect(svg).not.toBeNull();
  });

  // Cycle 5: clicking the button emits the delete output
  it('should emit the delete output when clicked and not busy', () => {
    // Arrange
    const { el, component } = setup();
    let emitted = false;
    component.delete.subscribe(() => { emitted = true; });

    // Act
    const btn = el.querySelector('button') as HTMLButtonElement;
    btn.click();

    // Assert
    expect(emitted).toBe(true);
  });

  // Cycle 6: deleteBusy=true shows fd-spinner and hides SVG
  it('should show fd-spinner and hide the trash SVG when deleteBusy is true', () => {
    // Arrange / Act
    const { el } = setup({ deleteBusy: true });

    // Assert
    const btn = el.querySelector('button') as HTMLElement;
    const spinner = btn.querySelector('fd-spinner');
    const svg = btn.querySelector('svg');
    expect(spinner).not.toBeNull();
    expect(svg).toBeNull();
  });

  // Cycle 7: deleteBusy=true suppresses the delete emit
  it('should not emit delete when deleteBusy is true and the button is clicked', () => {
    // Arrange
    const { el, component } = setup({ deleteBusy: true });
    let emitCount = 0;
    component.delete.subscribe(() => { emitCount++; });

    // Act
    const btn = el.querySelector('button') as HTMLButtonElement;
    btn.click();

    // Assert
    expect(emitCount).toBe(0);
  });

  // Cycle 8: deleteBusy=true sets aria-disabled="true" on the button
  it('should set aria-disabled="true" on the button when deleteBusy is true', () => {
    // Arrange / Act
    const { el } = setup({ deleteBusy: true });

    // Assert
    const btn = el.querySelector('button');
    expect(btn?.getAttribute('aria-disabled')).toBe('true');
  });

  // Cycle 9: deleteBusy=false (default) — no aria-disabled attribute
  it('should not set aria-disabled when deleteBusy is false', () => {
    // Arrange / Act
    const { el } = setup({ deleteBusy: false });

    // Assert
    const btn = el.querySelector('button');
    expect(btn?.getAttribute('aria-disabled')).toBeNull();
  });
});

import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { vi } from 'vitest';
import { IssueFilterSheetComponent } from './issue-filter-sheet';
import { IssueService } from '../issue.service';
import { IssueState } from '../issue.model';
import { isResolvedState } from '../issue-lifecycle.model';

function createMockIssueService() {
  const countsSignal = signal<Record<string, number>>({});
  const selectedActiveStatesSignal = signal<ReadonlySet<IssueState>>(new Set());
  const selectedResolvedStatesSignal = signal<ReadonlySet<IssueState>>(new Set());
  const toggleState = vi.fn();

  return {
    counts: countsSignal.asReadonly(),
    selectedActiveStates: selectedActiveStatesSignal.asReadonly(),
    selectedResolvedStates: selectedResolvedStatesSignal.asReadonly(),
    countFor: (_state: IssueState): number => 0,
    isStateSelected: (state: IssueState): boolean => {
      if (isResolvedState(state)) {
        return selectedResolvedStatesSignal().has(state);
      }
      return selectedActiveStatesSignal().has(state);
    },
    toggleState,
    activeFilterCount: signal(0).asReadonly(),
  };
}

function setup(open = false) {
  const mockIssueService = createMockIssueService();
  const triggerEl = document.createElement('button');
  document.body.appendChild(triggerEl);

  TestBed.configureTestingModule({
    imports: [IssueFilterSheetComponent],
    providers: [
      { provide: IssueService, useValue: mockIssueService },
    ],
  });

  const fixture = TestBed.createComponent(IssueFilterSheetComponent);
  if (open) {
    fixture.componentRef.setInput('open', true);
    fixture.componentRef.setInput('triggerRef', { nativeElement: triggerEl });
  }
  fixture.detectChanges();

  return { fixture, mockIssueService, triggerEl };
}

describe('IssueFilterSheetComponent', () => {
  afterEach(() => {
    TestBed.resetTestingModule();
    document.querySelectorAll('button[data-test-trigger]').forEach(el => el.remove());
  });

  // Cycle 1: renders dialog role + aria-modal + aria-labelledby when open
  it('should render a panel with role="dialog" when open', () => {
    // Arrange / Act
    const { fixture } = setup(true);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const dialog = el.querySelector('[role="dialog"]');
    expect(dialog).not.toBeNull();
  });

  it('should render the panel with aria-modal="true" when open', () => {
    // Arrange / Act
    const { fixture } = setup(true);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const dialog = el.querySelector('[role="dialog"]');
    expect(dialog?.getAttribute('aria-modal')).toBe('true');
  });

  it('should render the panel with aria-labelledby pointing at the heading', () => {
    // Arrange / Act
    const { fixture } = setup(true);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const dialog = el.querySelector('[role="dialog"]') as HTMLElement;
    const labelledBy = dialog?.getAttribute('aria-labelledby');
    expect(labelledBy).toBeTruthy();
    const heading = labelledBy ? el.querySelector(`#${labelledBy}`) : null;
    expect(heading).not.toBeNull();
    expect(heading?.textContent?.trim()).toBe('Filter issues');
  });

  // Cycle 2: renders a scrim/backdrop when open
  it('should render a scrim element when open', () => {
    // Arrange / Act
    const { fixture } = setup(true);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const scrim = el.querySelector('.filter-sheet__scrim');
    expect(scrim).not.toBeNull();
  });

  it('should not render the sheet when open is false', () => {
    // Arrange / Act
    const { fixture } = setup(false);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const dialog = el.querySelector('[role="dialog"]');
    expect(dialog).toBeNull();
  });

  // Cycle 3: backdrop click emits close
  it('should emit close when the scrim is clicked', () => {
    // Arrange
    const { fixture } = setup(true);
    const el = fixture.nativeElement as HTMLElement;
    const closeSpy = vi.fn();
    fixture.componentInstance.close.subscribe(closeSpy);

    // Act
    const scrim = el.querySelector('.filter-sheet__scrim') as HTMLElement;
    scrim.click();

    // Assert
    expect(closeSpy).toHaveBeenCalledOnce();
  });

  // Cycle 4: close button emits close
  it('should render a close button inside the panel', () => {
    // Arrange / Act
    const { fixture } = setup(true);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const closeBtn = el.querySelector('.filter-sheet__close-btn');
    expect(closeBtn).not.toBeNull();
  });

  it('should emit close when the close button is clicked', () => {
    // Arrange
    const { fixture } = setup(true);
    const el = fixture.nativeElement as HTMLElement;
    const closeSpy = vi.fn();
    fixture.componentInstance.close.subscribe(closeSpy);

    // Act
    const closeBtn = el.querySelector('.filter-sheet__close-btn') as HTMLButtonElement;
    closeBtn.click();

    // Assert
    expect(closeSpy).toHaveBeenCalledOnce();
  });

  // Cycle 5: Escape key emits close
  it('should emit close when the Escape key is pressed while open', () => {
    // Arrange
    const { fixture } = setup(true);
    const el = fixture.nativeElement as HTMLElement;
    const closeSpy = vi.fn();
    fixture.componentInstance.close.subscribe(closeSpy);

    // Act
    el.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));

    // Assert
    expect(closeSpy).toHaveBeenCalledOnce();
  });

  // Cycle 6: focus moves into the dialog on open
  it('should move focus into the panel when opened', () => {
    // Arrange / Act
    const { fixture } = setup(true);
    TestBed.flushEffects();
    const el = fixture.nativeElement as HTMLElement;

    // Assert — active element should be inside the panel
    const dialog = el.querySelector('[role="dialog"]') as HTMLElement;
    expect(dialog?.contains(document.activeElement)).toBe(true);
  });

  // Cycle 7: focus restores to trigger when open transitions to false
  it('should restore focus to the trigger element when open becomes false', () => {
    // Arrange
    const { fixture, triggerEl } = setup(true);
    TestBed.flushEffects();

    // Act — close the sheet by setting open to false
    fixture.componentRef.setInput('open', false);
    fixture.detectChanges();
    TestBed.flushEffects();

    // Assert — focus should be back on the trigger
    expect(document.activeElement).toBe(triggerEl);
  });

  // Cycle 8: Tab cycles focus within the panel
  it('should contain Tab navigation within the panel', () => {
    // Arrange
    const { fixture } = setup(true);
    TestBed.flushEffects();
    const el = fixture.nativeElement as HTMLElement;
    const dialog = el.querySelector('[role="dialog"]') as HTMLElement;

    // Get all focusable elements inside
    const focusable = Array.from(
      dialog.querySelectorAll<HTMLElement>('button, [tabindex]:not([tabindex="-1"])')
    ).filter(el => !el.hasAttribute('disabled'));

    // Put focus on the last focusable element
    const last = focusable[focusable.length - 1];
    last?.focus();

    // Act — Tab from last element
    last?.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true }));

    // Assert — focus cycles back into the panel (not escaped)
    expect(dialog.contains(document.activeElement)).toBe(true);
  });

  it('should contain Shift+Tab navigation within the panel', () => {
    // Arrange
    const { fixture } = setup(true);
    TestBed.flushEffects();
    const el = fixture.nativeElement as HTMLElement;
    const dialog = el.querySelector('[role="dialog"]') as HTMLElement;

    // Get all focusable elements inside
    const focusable = Array.from(
      dialog.querySelectorAll<HTMLElement>('button, [tabindex]:not([tabindex="-1"])')
    ).filter(el => !el.hasAttribute('disabled'));

    // Put focus on the first focusable element
    const first = focusable[0];
    first?.focus();

    // Act — Shift+Tab from first element
    first?.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', shiftKey: true, bubbles: true }));

    // Assert — focus cycles back into the panel (not escaped)
    expect(dialog.contains(document.activeElement)).toBe(true);
  });

  // Cycle 9: hosts fd-issue-filter-rail
  it('should render the fd-issue-filter-rail component inside the panel', () => {
    // Arrange / Act
    const { fixture } = setup(true);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const rail = el.querySelector('fd-issue-filter-rail');
    expect(rail).not.toBeNull();
  });

  // Cycle 10: drag handle affordance in panel header
  it('should render a drag handle in the panel header', () => {
    // Arrange / Act
    const { fixture } = setup(true);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const handle = el.querySelector('.filter-sheet__drag-handle');
    expect(handle).not.toBeNull();
  });

  // Cycle 11: swipe-to-dismiss threshold logic
  it('should classify drag past threshold (96px) as a dismiss gesture', () => {
    // Arrange
    const { fixture } = setup(true);
    const component = fixture.componentInstance;

    // Act / Assert
    expect(component.shouldDismiss(96)).toBe(true);
    expect(component.shouldDismiss(100)).toBe(true);
    expect(component.shouldDismiss(200)).toBe(true);
  });

  it('should classify drag below threshold (96px) as a snap-back gesture', () => {
    // Arrange
    const { fixture } = setup(true);
    const component = fixture.componentInstance;

    // Act / Assert
    expect(component.shouldDismiss(0)).toBe(false);
    expect(component.shouldDismiss(50)).toBe(false);
    expect(component.shouldDismiss(95)).toBe(false);
  });

  it('should emit close when handle is dragged past the dismiss threshold', () => {
    // Arrange
    const { fixture } = setup(true);
    const el = fixture.nativeElement as HTMLElement;
    const closeSpy = vi.fn();
    fixture.componentInstance.close.subscribe(closeSpy);
    const handle = el.querySelector('.filter-sheet__drag-handle') as HTMLElement;
    const panel = el.querySelector('.filter-sheet__panel') as HTMLElement;

    // Act — simulate a pointer drag exceeding 96px downward from the handle
    handle.dispatchEvent(new PointerEvent('pointerdown', { clientY: 100, bubbles: true }));
    panel.dispatchEvent(new PointerEvent('pointermove', { clientY: 200, bubbles: true }));
    panel.dispatchEvent(new PointerEvent('pointerup', { clientY: 200, bubbles: true }));

    // Assert
    expect(closeSpy).toHaveBeenCalledOnce();
  });

  it('should not emit close when handle is dragged below the dismiss threshold', () => {
    // Arrange
    const { fixture } = setup(true);
    const el = fixture.nativeElement as HTMLElement;
    const closeSpy = vi.fn();
    fixture.componentInstance.close.subscribe(closeSpy);
    const handle = el.querySelector('.filter-sheet__drag-handle') as HTMLElement;
    const panel = el.querySelector('.filter-sheet__panel') as HTMLElement;

    // Act — simulate a pointer drag of only 50px downward from the handle
    handle.dispatchEvent(new PointerEvent('pointerdown', { clientY: 100, bubbles: true }));
    panel.dispatchEvent(new PointerEvent('pointermove', { clientY: 150, bubbles: true }));
    panel.dispatchEvent(new PointerEvent('pointerup', { clientY: 150, bubbles: true }));

    // Assert
    expect(closeSpy).not.toHaveBeenCalled();
  });

  // Finding A: per-instance heading IDs must be unique across instances
  it('should generate a unique headingId per component instance', () => {
    // Arrange — create two separate component instances
    const { fixture: fixture1 } = setup(true);
    TestBed.resetTestingModule();

    const mockIssueService = {
      counts: signal<Record<string, number>>({}).asReadonly(),
      selectedActiveStates: signal<ReadonlySet<IssueState>>(new Set()).asReadonly(),
      selectedResolvedStates: signal<ReadonlySet<IssueState>>(new Set()).asReadonly(),
      countFor: (_state: IssueState): number => 0,
      isStateSelected: (_state: IssueState): boolean => false,
      toggleState: vi.fn(),
      activeFilterCount: signal(0).asReadonly(),
    };

    TestBed.configureTestingModule({
      imports: [IssueFilterSheetComponent],
      providers: [{ provide: IssueService, useValue: mockIssueService }],
    });

    const fixture2 = TestBed.createComponent(IssueFilterSheetComponent);
    fixture2.componentRef.setInput('open', true);
    fixture2.detectChanges();

    // Act
    const id1 = fixture1.componentInstance['headingId'];
    const id2 = fixture2.componentInstance['headingId'];

    // Assert — each instance has its own unique ID
    expect(id1).toBeTruthy();
    expect(id2).toBeTruthy();
    expect(id1).not.toBe(id2);
  });

  it('should set aria-labelledby to match the heading id on the same instance', () => {
    // Arrange / Act
    const { fixture } = setup(true);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const dialog = el.querySelector('[role="dialog"]') as HTMLElement;
    const headingId = fixture.componentInstance['headingId'];
    expect(dialog.getAttribute('aria-labelledby')).toBe(headingId);
    const heading = el.querySelector(`#${headingId}`);
    expect(heading).not.toBeNull();
  });

  // Finding B: pointerdown on a body element must NOT start a drag / must NOT capture the pointer
  it('should not start a drag when pointerdown occurs on a body element (not the handle)', () => {
    // Arrange
    const { fixture } = setup(true);
    const el = fixture.nativeElement as HTMLElement;
    const closeSpy = vi.fn();
    fixture.componentInstance.close.subscribe(closeSpy);
    const body = el.querySelector('.filter-sheet__body') as HTMLElement;

    // Act — simulate a downward drag starting from the body
    body.dispatchEvent(new PointerEvent('pointerdown', { clientY: 100, bubbles: true }));
    const panel = el.querySelector('.filter-sheet__panel') as HTMLElement;
    panel.dispatchEvent(new PointerEvent('pointermove', { clientY: 300, bubbles: true }));
    panel.dispatchEvent(new PointerEvent('pointerup', { clientY: 300, bubbles: true }));

    // Assert — drag from body does not dismiss the sheet
    expect(closeSpy).not.toHaveBeenCalled();
  });

  it('should start a drag and dismiss when pointerdown occurs on the drag handle', () => {
    // Arrange
    const { fixture } = setup(true);
    const el = fixture.nativeElement as HTMLElement;
    const closeSpy = vi.fn();
    fixture.componentInstance.close.subscribe(closeSpy);
    const handle = el.querySelector('.filter-sheet__drag-handle') as HTMLElement;
    const panel = el.querySelector('.filter-sheet__panel') as HTMLElement;

    // Act — simulate a downward drag starting from the handle
    handle.dispatchEvent(new PointerEvent('pointerdown', { clientY: 100, bubbles: true }));
    panel.dispatchEvent(new PointerEvent('pointermove', { clientY: 300, bubbles: true }));
    panel.dispatchEvent(new PointerEvent('pointerup', { clientY: 300, bubbles: true }));

    // Assert — drag from handle dismisses the sheet
    expect(closeSpy).toHaveBeenCalledOnce();
  });

  it('should start a drag and dismiss when pointerdown occurs on the header', () => {
    // Arrange
    const { fixture } = setup(true);
    const el = fixture.nativeElement as HTMLElement;
    const closeSpy = vi.fn();
    fixture.componentInstance.close.subscribe(closeSpy);
    const header = el.querySelector('.filter-sheet__header') as HTMLElement;
    const panel = el.querySelector('.filter-sheet__panel') as HTMLElement;

    // Act — simulate a downward drag starting from the header
    header.dispatchEvent(new PointerEvent('pointerdown', { clientY: 100, bubbles: true }));
    panel.dispatchEvent(new PointerEvent('pointermove', { clientY: 300, bubbles: true }));
    panel.dispatchEvent(new PointerEvent('pointerup', { clientY: 300, bubbles: true }));

    // Assert — drag from header dismisses the sheet
    expect(closeSpy).toHaveBeenCalledOnce();
  });
});

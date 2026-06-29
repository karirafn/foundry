import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { vi } from 'vitest';
import { IssueFilterBarComponent } from './issue-filter-bar';
import { IssueService } from '../issue.service';
import { IssueState } from '../issue.model';
import { isResolvedState } from '../issue-lifecycle.model';

function createMockIssueService(activeFilterCount = 0) {
  const countsSignal = signal<Record<string, number>>({});
  const selectedActiveStatesSignal = signal<ReadonlySet<IssueState>>(new Set());
  const selectedResolvedStatesSignal = signal<ReadonlySet<IssueState>>(new Set());
  const activeFilterCountSignal = signal(activeFilterCount);
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
    activeFilterCount: activeFilterCountSignal.asReadonly(),
  };
}

function setup(activeFilterCount = 0) {
  const mockIssueService = createMockIssueService(activeFilterCount);

  TestBed.configureTestingModule({
    imports: [IssueFilterBarComponent],
    providers: [
      { provide: IssueService, useValue: mockIssueService },
    ],
  });

  const fixture = TestBed.createComponent(IssueFilterBarComponent);
  fixture.detectChanges();

  return { fixture, mockIssueService };
}

describe('IssueFilterBarComponent', () => {
  afterEach(() => {
    TestBed.resetTestingModule();
  });

  // Cycle 1: Filter button renders
  it('should render a Filter button', () => {
    // Arrange / Act
    const { fixture } = setup();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const btn = el.querySelector('.filter-bar__btn') as HTMLButtonElement;
    expect(btn).not.toBeNull();
    expect(btn.textContent).toContain('Filter');
  });

  // Cycle 2: Clicking the button opens the sheet (open becomes true / sheet rendered)
  it('should render fd-issue-filter-sheet after clicking the Filter button', () => {
    // Arrange
    const { fixture } = setup();
    const el = fixture.nativeElement as HTMLElement;

    // Act
    const btn = el.querySelector('.filter-bar__btn') as HTMLButtonElement;
    btn.click();
    fixture.detectChanges();

    // Assert — sheet component is now in the DOM
    const sheet = el.querySelector('fd-issue-filter-sheet');
    expect(sheet).not.toBeNull();
  });

  it('should open the sheet (open input true) when Filter button is clicked', () => {
    // Arrange
    const { fixture } = setup();
    const el = fixture.nativeElement as HTMLElement;
    const btn = el.querySelector('.filter-bar__btn') as HTMLButtonElement;

    // Act
    btn.click();
    fixture.detectChanges();

    // Assert — sheet's open attribute reflects true
    const sheet = el.querySelector('fd-issue-filter-sheet') as HTMLElement;
    expect(fixture.componentInstance['_sheetOpen']()).toBe(true);
  });

  // Cycle 3: Badge hidden at count 0
  it('should not render the badge when activeFilterCount is 0', () => {
    // Arrange / Act
    const { fixture } = setup(0);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const badge = el.querySelector('.filter-bar__badge');
    expect(badge).toBeNull();
  });

  // Cycle 4: Badge shows number when count > 0
  it('should render a badge with the count when activeFilterCount is greater than 0', () => {
    // Arrange / Act
    const { fixture } = setup(3);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const badge = el.querySelector('.filter-bar__badge');
    expect(badge).not.toBeNull();
    expect(badge?.textContent?.trim()).toBe('3');
  });

  // Cycle 5: aria-label composes count
  it('should use aria-label="Filter" when activeFilterCount is 0', () => {
    // Arrange / Act
    const { fixture } = setup(0);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const btn = el.querySelector('.filter-bar__btn') as HTMLButtonElement;
    expect(btn.getAttribute('aria-label')).toBe('Filter');
  });

  it('should compose aria-label with count when activeFilterCount is greater than 0', () => {
    // Arrange / Act
    const { fixture } = setup(2);
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const btn = el.querySelector('.filter-bar__btn') as HTMLButtonElement;
    expect(btn.getAttribute('aria-label')).toBe('Filter, 2 filters active');
  });

  // Cycle 6: sheet close output sets _sheetOpen to false
  it('should close the sheet when the sheet emits close', () => {
    // Arrange
    const { fixture } = setup();
    const el = fixture.nativeElement as HTMLElement;

    // Open the sheet first
    const btn = el.querySelector('.filter-bar__btn') as HTMLButtonElement;
    btn.click();
    fixture.detectChanges();
    expect(fixture.componentInstance['_sheetOpen']()).toBe(true);

    // Act — emit close from the sheet component
    const sheetInstance = fixture.componentInstance['_sheetRef']();
    sheetInstance?.close.emit();
    fixture.detectChanges();

    // Assert
    expect(fixture.componentInstance['_sheetOpen']()).toBe(false);
  });

  // Finding D: Filter button must expose dialog-control ARIA attributes
  it('should have aria-haspopup="dialog" on the Filter button', () => {
    // Arrange / Act
    const { fixture } = setup();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const btn = el.querySelector('.filter-bar__btn') as HTMLButtonElement;
    expect(btn.getAttribute('aria-haspopup')).toBe('dialog');
  });

  it('should have aria-expanded="false" on the Filter button when the sheet is closed', () => {
    // Arrange / Act
    const { fixture } = setup();
    const el = fixture.nativeElement as HTMLElement;

    // Assert
    const btn = el.querySelector('.filter-bar__btn') as HTMLButtonElement;
    expect(btn.getAttribute('aria-expanded')).toBe('false');
  });

  it('should have aria-expanded="true" on the Filter button when the sheet is open', () => {
    // Arrange
    const { fixture } = setup();
    const el = fixture.nativeElement as HTMLElement;
    const btn = el.querySelector('.filter-bar__btn') as HTMLButtonElement;

    // Act — open the sheet
    btn.click();
    fixture.detectChanges();

    // Assert
    expect(btn.getAttribute('aria-expanded')).toBe('true');
  });
});

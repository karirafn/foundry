import { Component, ChangeDetectionStrategy } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Overlay } from '@angular/cdk/overlay';
import { TooltipDirective } from './tooltip.directive';

// Host component for testing the directive on a real DOM element.
@Component({
  selector: 'fd-test-host',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TooltipDirective],
  template: `<button [fdTooltip]="tooltipText" type="button">Trigger</button>`,
})
class TestHostComponent {
  tooltipText: string | null = 'Tooltip content';
}

function setup(tooltipText: string | null = 'Tooltip content') {
  TestBed.configureTestingModule({
    imports: [TestHostComponent],
    providers: [Overlay],
  });

  const fixture = TestBed.createComponent(TestHostComponent);
  fixture.componentInstance.tooltipText = tooltipText;
  fixture.detectChanges();

  const trigger = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
  return { fixture, trigger };
}

function dispatchEvent(el: HTMLElement, type: string, init?: EventInit): Event {
  const event = new Event(type, { bubbles: true, cancelable: true, ...init });
  el.dispatchEvent(event);
  return event;
}

function dispatchKeyboardEvent(el: HTMLElement, type: string, key: string): KeyboardEvent {
  const event = new KeyboardEvent(type, { bubbles: true, cancelable: true, key });
  el.dispatchEvent(event);
  return event;
}

describe('TooltipDirective', () => {
  afterEach(() => TestBed.resetTestingModule());

  // ─── Behavior a: shows on mouseenter ───────────────────────────────────────

  it('(a) shows the overlay on mouseenter', () => {
    // Arrange
    const { fixture, trigger } = setup();

    // Act
    dispatchEvent(trigger, 'mouseenter');
    fixture.detectChanges();

    // Assert
    expect(document.querySelector('.fd-tooltip-panel')).not.toBeNull();
    expect(document.querySelector('.fd-tooltip-panel')?.textContent?.trim()).toBe('Tooltip content');
  });

  it('(a) overlay text matches the fdTooltip input', () => {
    // Arrange
    const { fixture, trigger } = setup('Custom text');

    // Act
    dispatchEvent(trigger, 'mouseenter');
    fixture.detectChanges();

    // Assert
    expect(document.querySelector('.fd-tooltip-panel')?.textContent?.trim()).toBe('Custom text');
  });

  // ─── Behavior b: shows on focus, hides on blur ─────────────────────────────

  it('(b) shows on keyboard focus', () => {
    // Arrange
    const { fixture, trigger } = setup();

    // Act
    dispatchEvent(trigger, 'focus');
    fixture.detectChanges();

    // Assert
    expect(document.querySelector('.fd-tooltip-panel')).not.toBeNull();
  });

  it('(b) hides on blur', () => {
    // Arrange
    const { fixture, trigger } = setup();
    dispatchEvent(trigger, 'focus');
    fixture.detectChanges();
    expect(document.querySelector('.fd-tooltip-panel')).not.toBeNull();

    // Act
    dispatchEvent(trigger, 'blur');
    fixture.detectChanges();

    // Assert
    expect(document.querySelector('.fd-tooltip-panel')).toBeNull();
  });

  // ─── Behavior c: hides on mouseleave and Escape ─────────────────────────────

  it('(c) hides on mouseleave when pointer is not over the tooltip panel', () => {
    // Arrange
    const { fixture, trigger } = setup();
    dispatchEvent(trigger, 'mouseenter');
    fixture.detectChanges();
    expect(document.querySelector('.fd-tooltip-panel')).not.toBeNull();

    // Act — simulate leaving to a target that is not the tooltip panel
    const mouseleave = new MouseEvent('mouseleave', { bubbles: true, cancelable: true, relatedTarget: document.body });
    trigger.dispatchEvent(mouseleave);
    fixture.detectChanges();

    // Assert
    expect(document.querySelector('.fd-tooltip-panel')).toBeNull();
  });

  it('(c) hides on Escape keydown', () => {
    // Arrange
    const { fixture, trigger } = setup();
    dispatchEvent(trigger, 'mouseenter');
    fixture.detectChanges();
    expect(document.querySelector('.fd-tooltip-panel')).not.toBeNull();

    // Act
    dispatchKeyboardEvent(document.body, 'keydown', 'Escape');
    fixture.detectChanges();

    // Assert
    expect(document.querySelector('.fd-tooltip-panel')).toBeNull();
  });

  // ─── Behavior d: stays open while pointer is on the tooltip panel ───────────

  it('(d) stays open when mouseleave target is the tooltip panel itself', () => {
    // Arrange
    const { fixture, trigger } = setup();
    dispatchEvent(trigger, 'mouseenter');
    fixture.detectChanges();
    const panel = document.querySelector('.fd-tooltip-panel') as HTMLElement;
    expect(panel).not.toBeNull();

    // Act — leave trigger to the tooltip panel
    const mouseleave = new MouseEvent('mouseleave', { bubbles: true, cancelable: true, relatedTarget: panel });
    trigger.dispatchEvent(mouseleave);
    fixture.detectChanges();

    // Assert — tooltip should stay visible
    expect(document.querySelector('.fd-tooltip-panel')).not.toBeNull();
  });

  // ─── Behavior e: coarse-pointer tap toggles ─────────────────────────────────

  it('(e) tap opens the tooltip when it is closed (coarse-pointer pointer event)', () => {
    // Arrange
    const { fixture, trigger } = setup();

    // Act — simulate a coarse-pointer tap via a PointerEvent with pointerType 'touch'
    const pointerdown = new PointerEvent('pointerdown', { bubbles: true, cancelable: true, pointerType: 'touch' });
    trigger.dispatchEvent(pointerdown);
    fixture.detectChanges();

    // Assert
    expect(document.querySelector('.fd-tooltip-panel')).not.toBeNull();
  });

  it('(e) second tap closes the tooltip when it is already open', () => {
    // Arrange
    const { fixture, trigger } = setup();
    const openTap = new PointerEvent('pointerdown', { bubbles: true, cancelable: true, pointerType: 'touch' });
    trigger.dispatchEvent(openTap);
    fixture.detectChanges();
    expect(document.querySelector('.fd-tooltip-panel')).not.toBeNull();

    // Act
    const closeTap = new PointerEvent('pointerdown', { bubbles: true, cancelable: true, pointerType: 'touch' });
    trigger.dispatchEvent(closeTap);
    fixture.detectChanges();

    // Assert
    expect(document.querySelector('.fd-tooltip-panel')).toBeNull();
  });

  // ─── Behavior f: aria-describedby wiring ────────────────────────────────────

  it('(f) sets aria-describedby on the trigger when the tooltip opens', () => {
    // Arrange
    const { fixture, trigger } = setup();

    // Act
    dispatchEvent(trigger, 'mouseenter');
    fixture.detectChanges();

    // Assert — aria-describedby is set and points to the tooltip panel element
    const describedBy = trigger.getAttribute('aria-describedby');
    expect(describedBy).toBeTruthy();
    const panelEl = document.getElementById(describedBy!);
    expect(panelEl).not.toBeNull();
    expect(panelEl?.textContent?.trim()).toBe('Tooltip content');
  });

  it('(f) clears aria-describedby when the tooltip hides', () => {
    // Arrange
    const { fixture, trigger } = setup();
    dispatchEvent(trigger, 'mouseenter');
    fixture.detectChanges();
    expect(trigger.getAttribute('aria-describedby')).toBeTruthy();

    // Act
    dispatchEvent(trigger, 'mouseleave');
    fixture.detectChanges();

    // Assert
    expect(trigger.getAttribute('aria-describedby')).toBeNull();
  });

  it('(f) disposes the overlay and clears aria-describedby on ngOnDestroy', () => {
    // Arrange
    const { fixture, trigger } = setup();
    dispatchEvent(trigger, 'mouseenter');
    fixture.detectChanges();
    expect(trigger.getAttribute('aria-describedby')).toBeTruthy();

    // Act
    fixture.destroy();

    // Assert — no panel remains in the document
    expect(document.querySelector('.fd-tooltip-panel')).toBeNull();
  });

  // ─── Behavior g: role="tooltip" on the panel ────────────────────────────────

  it('(g) sets role="tooltip" on the panel element when shown', () => {
    // Arrange
    const { fixture, trigger } = setup();

    // Act
    dispatchEvent(trigger, 'mouseenter');
    fixture.detectChanges();

    // Assert
    const panel = document.querySelector('.fd-tooltip-panel');
    expect(panel?.getAttribute('role')).toBe('tooltip');
  });

  // ─── Inert when input is null or empty ──────────────────────────────────────

  it('is inert when fdTooltip is null — no overlay on mouseenter', () => {
    // Arrange
    const { fixture, trigger } = setup(null);

    // Act
    dispatchEvent(trigger, 'mouseenter');
    fixture.detectChanges();

    // Assert
    expect(document.querySelector('.fd-tooltip-panel')).toBeNull();
    expect(trigger.getAttribute('aria-describedby')).toBeNull();
  });

  it('is inert when fdTooltip is an empty string — no overlay on mouseenter', () => {
    // Arrange
    const { fixture, trigger } = setup('');

    // Act
    dispatchEvent(trigger, 'mouseenter');
    fixture.detectChanges();

    // Assert
    expect(document.querySelector('.fd-tooltip-panel')).toBeNull();
  });
});

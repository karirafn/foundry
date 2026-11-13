import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { SideSheetComponent } from './side-sheet';

@Component({
  imports: [SideSheetComponent],
  template: `
    <button type="button" data-issue-card="a" class="card">Card</button>
    <button type="button" class="outside">Outside</button>
    <fd-side-sheet
      [open]="open()"
      labelledBy="sheet-title"
      closeLabel="Close issue details"
      (dismissed)="dismissals = dismissals + 1"
    >
      <ng-template>
        <h2 id="sheet-title" tabindex="-1">#1 Title</h2>
        <button type="button" class="inside">Inside</button>
        <dialog class="confirm"><button type="button" class="in-dialog">Confirm</button></dialog>
      </ng-template>
    </fd-side-sheet>
  `,
})
class HostComponent {
  readonly open = signal(false);
  dismissals = 0;
}

async function setup(open = true) {
  const fixture = TestBed.createComponent(HostComponent);
  fixture.componentInstance.open.set(open);
  document.body.appendChild(fixture.nativeElement);
  fixture.detectChanges();
  await fixture.whenStable();
  return { fixture, host: fixture.componentInstance, el: fixture.nativeElement as HTMLElement };
}

describe('SideSheetComponent', () => {
  it('renders nothing while closed', async () => {
    const { el } = await setup(false);
    expect(el.querySelector('[role="dialog"]')).toBeNull();
  });

  it('renders its content in a non-modal dialog labelled by the heading', async () => {
    // Act
    const { el } = await setup();

    // Assert
    const dialog = el.querySelector('[role="dialog"]')!;
    expect(dialog.getAttribute('aria-modal')).toBe('false');
    expect(dialog.getAttribute('aria-labelledby')).toBe('sheet-title');
    expect(dialog.querySelector('#sheet-title')).not.toBeNull();
  });

  it('moves focus to the heading when it opens', async () => {
    // Act
    const { el } = await setup();

    // Assert
    expect(document.activeElement).toBe(el.querySelector('#sheet-title'));
  });

  it('names its close button', async () => {
    const { el } = await setup();
    expect(el.querySelector('.side-sheet__close')?.getAttribute('aria-label')).toBe(
      'Close issue details',
    );
  });

  it('is dismissed by its close button', async () => {
    // Arrange
    const { el, host } = await setup();

    // Act
    (el.querySelector('.side-sheet__close') as HTMLButtonElement).click();

    // Assert
    expect(host.dismissals).toBe(1);
  });

  it('is dismissed by Escape inside it', async () => {
    // Arrange
    const { el, host } = await setup();

    // Act
    el.querySelector('.inside')!.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }),
    );

    // Assert
    expect(host.dismissals).toBe(1);
  });

  it('leaves Escape to an open confirm dialog inside it', async () => {
    // Arrange
    const { el, host } = await setup();
    el.querySelector('dialog')!.setAttribute('open', '');

    // Act
    el.querySelector('.in-dialog')!.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }),
    );

    // Assert
    expect(host.dismissals).toBe(0);
  });

  it('is dismissed by a pointer down outside it', async () => {
    // Arrange
    const { el, host } = await setup();

    // Act
    el.querySelector('.outside')!.dispatchEvent(new Event('pointerdown', { bubbles: true }));

    // Assert
    expect(host.dismissals).toBe(1);
  });

  it('ignores a pointer down inside it', async () => {
    // Arrange
    const { el, host } = await setup();

    // Act
    el.querySelector('.inside')!.dispatchEvent(new Event('pointerdown', { bubbles: true }));

    // Assert
    expect(host.dismissals).toBe(0);
  });

  it('ignores a pointer down on a card, which swaps its content instead', async () => {
    // Arrange
    const { el, host } = await setup();

    // Act
    el.querySelector('.card')!.dispatchEvent(new Event('pointerdown', { bubbles: true }));

    // Assert
    expect(host.dismissals).toBe(0);
  });

  it('ignores pointer downs while closed', async () => {
    // Arrange
    const { el, host } = await setup(false);

    // Act
    el.querySelector('.outside')!.dispatchEvent(new Event('pointerdown', { bubbles: true }));

    // Assert
    expect(host.dismissals).toBe(0);
  });
});

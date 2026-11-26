import { Component, input } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { LoginFlowComponent } from './login-flow';

const SERVER_DETAIL = "That doesn't look like the full code. Copy all of it, including the # in the middle.";
const UNBROKEN_DETAIL = 'Code-'.padEnd(400, 'x');

@Component({
  selector: 'fd-login-flow-layout-host',
  imports: [LoginFlowComponent],
  template: `<div class="host" [style.width.px]="width()"><fd-oauth-login-flow phase="WaitingForAuthorization" url="https://claude.ai/oauth/authorize" [codeError]="codeError()" /></div>`,
})
class HostComponent {
  readonly width = input.required<number>();
  readonly codeError = input.required<string | null>();
}

async function render(width: number, codeError: string | null): Promise<HTMLElement> {
  const fixture = TestBed.createComponent(HostComponent);
  fixture.componentRef.setInput('width', width);
  fixture.componentRef.setInput('codeError', codeError);
  await fixture.whenStable();
  return (fixture.nativeElement as HTMLElement).querySelector('.host') as HTMLElement;
}

function overflowing(host: HTMLElement): string[] {
  const hostRight = host.getBoundingClientRect().right;
  return Array.from(host.querySelectorAll('*'))
    .filter((el) => el.getBoundingClientRect().right > hostRight + 0.5)
    .map((el) => el.tagName.toLowerCase() + '.' + el.className);
}

// Runs in a real browser (test-layout target); jsdom would measure every box as 0.
describe('login flow refused code layout', () => {
  it('places the message between the code input and the hint', async () => {
    // Arrange
    const host = await render(960, SERVER_DETAIL);

    // Act
    const input = host.querySelector('.login-flow__code-input')!.getBoundingClientRect();
    const message = host.querySelector('.login-flow__code-error')!.getBoundingClientRect();
    const hint = host.querySelector('.login-flow__code-hint')!.getBoundingClientRect();

    // Assert
    expect(message.top).toBeGreaterThanOrEqual(input.bottom);
    expect(hint.top).toBeGreaterThanOrEqual(message.bottom);
  });

  it('wraps an unbroken message inside a narrow container', async () => {
    // Act
    const host = await render(240, UNBROKEN_DETAIL);
    const message = host.querySelector('.login-flow__code-error') as HTMLElement;

    // Assert
    expect(overflowing(host)).toEqual([]);
    expect(message.scrollWidth).toBeLessThanOrEqual(message.clientWidth);
  });

  it('keeps the code input full width and at least 44px tall when the code is refused', async () => {
    // Arrange
    const host = await render(240, SERVER_DETAIL);

    // Act
    const input = host.querySelector('.login-flow__code-input')!.getBoundingClientRect();

    // Assert
    expect(input.width).toBeCloseTo(240, 0);
    expect(input.height).toBeGreaterThanOrEqual(44);
  });

  it('borders the refused code input in the failed-state colour', async () => {
    // Arrange
    const host = await render(960, SERVER_DETAIL);
    const probe = document.createElement('span');
    probe.style.color = 'var(--fd-state-failed)';
    host.appendChild(probe);

    // Act
    const border = getComputedStyle(host.querySelector('.login-flow__code-input')!).borderTopColor;

    // Assert
    expect(border).toBe(getComputedStyle(probe).color);
  });
});

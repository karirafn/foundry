import { TestBed } from '@angular/core/testing';
import { page } from 'vitest/browser';
import { WebhookExposeGuideComponent } from './webhook-expose-guide';

async function render(port: number): Promise<HTMLElement> {
  const fixture = TestBed.createComponent(WebhookExposeGuideComponent);
  fixture.componentRef.setInput('port', port);
  await fixture.whenStable();
  return fixture.nativeElement as HTMLElement;
}

// Runs in a real browser (test-layout target); jsdom would measure every box as 0.
describe('webhook expose guide layout', () => {
  // Restores the runner's viewport, so later layout specs measure as before.
  const original = { width: window.innerWidth, height: window.innerHeight };

  afterEach(async () => {
    await page.viewport(original.width, original.height);
  });

  it('stacks each option under its name below 640px, without overflowing 320px', async () => {
    // Arrange
    await page.viewport(320, 800);

    // Act
    const host = await render(65535);
    const list = host.querySelector('.webhook-expose-guide__list')!.getBoundingClientRect();
    const term = host.querySelector('dt')!.getBoundingClientRect();
    const detail = host.querySelector('dd')!.getBoundingClientRect();

    // Assert
    expect(detail.top).toBeGreaterThanOrEqual(term.bottom);
    expect(detail.left).toBeCloseTo(term.left, 0);
    for (const code of Array.from(host.querySelectorAll('code'))) {
      expect(code.getBoundingClientRect().right).toBeLessThanOrEqual(list.right + 0.5);
    }
    expect(host.scrollWidth).toBeLessThanOrEqual(320);
  });

  it('places each option beside its name from 640px', async () => {
    // Arrange
    await page.viewport(1280, 800);

    // Act
    const host = await render(5099);
    const term = host.querySelector('dt')!.getBoundingClientRect();
    const detail = host.querySelector('dd')!.getBoundingClientRect();

    // Assert
    expect(detail.left).toBeGreaterThan(term.right);
    expect(detail.top).toBeLessThan(term.bottom);
  });
});

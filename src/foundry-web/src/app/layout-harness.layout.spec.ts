import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';

@Component({
  selector: 'fd-layout-harness',
  template: '<p>layout harness</p>',
})
class LayoutHarnessComponent {}

// jsdom performs no layout and applies no global stylesheet, so this only passes in the
// browser run of the test-layout target, where src/styles.scss is loaded.
describe('layout harness', () => {
  it('applies the global stylesheet to rendered components', async () => {
    const fixture = TestBed.createComponent(LayoutHarnessComponent);
    await fixture.whenStable();

    const root = fixture.nativeElement as HTMLElement;

    expect(getComputedStyle(root).boxSizing).toBe('border-box');
  });
});

import { Component, input } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TokenRequirements } from '../account.model';
import { TokenRequirementsComponent } from './token-requirements';

const REQUIREMENTS: TokenRequirements = {
  providerType: 'github',
  tokenTypeLabel: 'GitHub fine-grained personal access token',
  permissions: [
    { label: 'Repository access: Only select repositories', verification: 'not-checked' },
    { label: 'Contents (read and write)', verification: 'probe' },
    { label: 'Administration (read and write)', verification: 'probe' },
    { label: 'Metadata (read)', verification: 'always-granted' },
    { label: 'Workflows (read and write)', verification: 'unverifiable' },
  ],
  creationUrlTemplate: '{baseUrl}/settings/personal-access-tokens/new?name=Foundry',
  resourceOwnerHint: 'Fine-grained tokens are bound to a single resource owner.',
  closingNote:
    "GitHub can't report a token's extra permissions, so Foundry can't check for any. Grant only the permissions listed here.",
  creationNote: null,
};

@Component({
  selector: 'fd-token-requirements-layout-host',
  imports: [TokenRequirementsComponent],
  template: `<div class="host" [style.width.px]="width()"><fd-token-requirements [requirements]="requirements" baseUrl="https://github.com" idPrefix="layout" /></div>`,
})
class HostComponent {
  readonly width = input.required<number>();
  readonly requirements = REQUIREMENTS;
}

async function render(width: number): Promise<HTMLElement> {
  const fixture = TestBed.createComponent(HostComponent);
  fixture.componentRef.setInput('width', width);
  await fixture.whenStable();
  return (fixture.nativeElement as HTMLElement).querySelector('.host') as HTMLElement;
}

function workflowsItem(host: HTMLElement): { label: DOMRect; note: DOMRect } {
  const item = host.querySelector('[data-verification="unverifiable"]') as HTMLElement;
  return {
    label: item.querySelector('.token-requirements__label')!.getBoundingClientRect(),
    note: item.querySelector('.token-requirements__note')!.getBoundingClientRect(),
  };
}

// Runs in a real browser (test-layout target); jsdom would measure every box as 0.
describe('token requirements layout', () => {
  it('keeps a note on its label line when there is room', async () => {
    // Arrange / Act
    const { label, note } = workflowsItem(await render(600));

    // Assert
    expect(Math.abs(note.top - label.top)).toBeLessThan(4);
  });

  it('wraps a note under its label on a narrow list without overflow', async () => {
    // Arrange
    const host = await render(240);

    // Act
    const { label, note } = workflowsItem(host);
    const hostRight = host.getBoundingClientRect().right;
    const overflowing = Array.from(host.querySelectorAll('*'))
      .filter((el) => el.getBoundingClientRect().right > hostRight + 0.5)
      .map((el) => el.className);

    // Assert
    expect(note.top).toBeGreaterThanOrEqual(label.bottom - 1);
    expect(overflowing).toEqual([]);
  });

  for (const width of [240, 360]) {
    it(`fits the longest label and its note at ${width}px with no horizontal overflow`, async () => {
      // Arrange
      const host = await render(width);

      // Act
      const item = host.querySelector('[data-verification="not-checked"]') as HTMLElement;
      const hostRight = host.getBoundingClientRect().right;
      const overflowing = Array.from(host.querySelectorAll('*'))
        .filter((el) => el.getBoundingClientRect().right > hostRight + 0.5)
        .map((el) => el.className);

      // Assert
      expect(item.querySelector('.token-requirements__note')!.getBoundingClientRect().width).toBeGreaterThan(0);
      expect(overflowing).toEqual([]);
      expect(host.scrollWidth).toBeLessThanOrEqual(host.clientWidth);
    });
  }
});

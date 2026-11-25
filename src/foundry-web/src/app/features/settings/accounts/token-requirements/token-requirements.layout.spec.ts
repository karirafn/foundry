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

const GITLAB_REQUIREMENTS: TokenRequirements = {
  providerType: 'gitlab',
  tokenTypeLabel: 'GitLab fine-grained personal access token',
  permissions: [
    {
      label: 'Only selected projects (select each monitored project individually)',
      verification: 'granted',
      group: 'Group and project access',
    },
    { label: 'Merge Request (Repository): Read, Create, Update, Merge', verification: 'granted', group: 'On each selected project' },
    {
      label: 'Webhook (Integrations): Read, Create, Update, Delete, Trigger',
      verification: 'granted',
      group: 'On each selected project',
    },
    { label: 'Personal Access Token (System Access): Read', verification: 'granted', group: 'On your user' },
    { label: 'Package (Packages and Registry): Read', verification: 'granted', group: "On a package feed's group (optional)" },
    { label: 'Maintainer role', verification: 'role', group: 'Repository role' },
  ],
  creationUrlTemplate: '{baseUrl}/-/user_settings/personal_access_tokens',
  resourceOwnerHint: null,
  closingNote: 'Foundry rejects a GitLab token that grants anything else or reaches all projects.',
  creationNote: 'Requires GitLab 19.5 or later. On the page, choose Generate token, then Fine-grained token.',
};

@Component({
  selector: 'fd-token-requirements-layout-host',
  imports: [TokenRequirementsComponent],
  template: `<div class="host" [style.width.px]="width()"><fd-token-requirements [requirements]="requirements()" baseUrl="https://github.com" idPrefix="layout" /></div>`,
})
class HostComponent {
  readonly width = input.required<number>();
  readonly requirements = input<TokenRequirements>(REQUIREMENTS);
}

async function render(width: number, requirements: TokenRequirements = REQUIREMENTS): Promise<HTMLElement> {
  const fixture = TestBed.createComponent(HostComponent);
  fixture.componentRef.setInput('width', width);
  fixture.componentRef.setInput('requirements', requirements);
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

  it('fits the grouped GitLab list at 360px with no horizontal overflow and wraps the webhook row', async () => {
    // Arrange
    const host = await render(360, GITLAB_REQUIREMENTS);

    // Act
    const hostRight = host.getBoundingClientRect().right;
    const overflowing = Array.from(host.querySelectorAll('*'))
      .filter((el) => el.getBoundingClientRect().right > hostRight + 0.5)
      .map((el) => el.className);
    const webhook = Array.from(host.querySelectorAll('.token-requirements__label')).find((label) =>
      label.textContent?.startsWith('Webhook'),
    ) as HTMLElement;
    const lineHeight = parseFloat(getComputedStyle(webhook).lineHeight);

    // Assert
    expect(overflowing).toEqual([]);
    expect(host.scrollWidth).toBeLessThanOrEqual(host.clientWidth);
    expect(webhook.getBoundingClientRect().height).toBeGreaterThan(lineHeight * 1.5);
  });

  it('indents each group list under its heading with a start border', async () => {
    // Arrange
    const host = await render(600, GITLAB_REQUIREMENTS);

    // Act
    const heading = host.querySelector('.token-requirements__group-heading') as HTMLElement;
    const list = host.querySelector('.token-requirements__group-list') as HTMLElement;
    const style = getComputedStyle(list);

    // Assert
    expect(getComputedStyle(heading).fontWeight).toBe('600');
    expect(style.borderInlineStartWidth).toBe('2px');
    expect(list.getBoundingClientRect().left).toBeGreaterThanOrEqual(heading.getBoundingClientRect().left);
  });
});

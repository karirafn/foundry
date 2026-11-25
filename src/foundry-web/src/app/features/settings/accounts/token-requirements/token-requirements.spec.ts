import { TestBed } from '@angular/core/testing';
import { TokenRequirements } from '../account.model';
import { TokenRequirementsComponent } from './token-requirements';

const GITHUB_REQUIREMENTS: TokenRequirements = {
  providerType: 'github',
  tokenTypeLabel: 'GitHub fine-grained personal access token',
  permissions: [
    { label: 'Repository access: Only select repositories', verification: 'not-checked' },
    { label: 'Contents (read and write)', verification: 'probe' },
    { label: 'Issues (read and write)', verification: 'probe' },
    { label: 'Pull requests (read and write)', verification: 'probe' },
    { label: 'Administration (read and write)', verification: 'probe' },
    { label: 'Webhooks (read and write)', verification: 'probe' },
    { label: 'Commit statuses (read)', verification: 'probe' },
    { label: 'Actions (read and write)', verification: 'probe' },
    { label: 'Metadata (read)', verification: 'always-granted' },
    { label: 'Workflows (read and write)', verification: 'unverifiable' },
  ],
  creationUrlTemplate: '{baseUrl}/settings/personal-access-tokens/new?name=Foundry&contents=write',
  resourceOwnerHint: 'Fine-grained tokens are bound to a single resource owner.',
  closingNote:
    "GitHub can't report a token's extra permissions, so Foundry can't check for any. Grant only the permissions listed here.",
  creationNote: null,
};

const GITLAB_REQUIREMENTS: TokenRequirements = {
  providerType: 'gitlab',
  tokenTypeLabel: 'GitLab personal access token',
  permissions: [
    { label: 'api', verification: 'scope' },
    { label: 'Maintainer role', verification: 'role' },
  ],
  creationUrlTemplate: '{baseUrl}/-/user_settings/personal_access_tokens',
  resourceOwnerHint: null,
  closingNote: null,
  creationNote: null,
};

function render(requirements: TokenRequirements, baseUrl = 'https://github.com', idPrefix = 'test'): HTMLElement {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({ imports: [TokenRequirementsComponent] });
  const fixture = TestBed.createComponent(TokenRequirementsComponent);
  fixture.componentRef.setInput('requirements', requirements);
  fixture.componentRef.setInput('baseUrl', baseUrl);
  fixture.componentRef.setInput('idPrefix', idPrefix);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

function itemFor(el: HTMLElement, label: string): HTMLLIElement {
  const items = Array.from(el.querySelectorAll<HTMLLIElement>('ul.token-requirements__list > li'));
  const item = items.find((li) => li.querySelector('.token-requirements__label')?.textContent === label);
  if (!item) {
    throw new Error(`No requirement item labelled ${label}`);
  }
  return item;
}

describe('TokenRequirementsComponent', () => {
  it('should render one list item per permission in API order', () => {
    // Arrange / Act
    const el = render(GITHUB_REQUIREMENTS);

    // Assert
    const labels = Array.from(el.querySelectorAll('li .token-requirements__label'), (label) => label.textContent);
    expect(labels).toEqual(GITHUB_REQUIREMENTS.permissions.map((permission) => permission.label));
  });

  it('should render a probe permission as plain text with no note', () => {
    // Arrange / Act
    const item = itemFor(render(GITHUB_REQUIREMENTS), 'Contents (read and write)');

    // Assert
    expect(item.querySelector('.token-requirements__label')?.tagName).toBe('SPAN');
    expect(item.querySelector('.token-requirements__note')).toBeNull();
  });

  it('should render a scope permission in code with no note', () => {
    // Arrange / Act
    const item = itemFor(render(GITLAB_REQUIREMENTS, 'https://gitlab.example.com'), 'api');

    // Assert
    expect(item.querySelector('.token-requirements__label')?.tagName).toBe('CODE');
    expect(item.querySelector('.token-requirements__note')).toBeNull();
  });

  it('should note that a role permission is set on each repository', () => {
    // Arrange / Act
    const item = itemFor(render(GITLAB_REQUIREMENTS, 'https://gitlab.example.com'), 'Maintainer role');

    // Assert
    expect(item.querySelector('.token-requirements__note')?.textContent?.trim()).toBe(
      '· set on each repository, not on the token',
    );
  });

  it('should note that an always-granted permission is always granted', () => {
    // Arrange / Act
    const item = itemFor(render(GITHUB_REQUIREMENTS), 'Metadata (read)');

    // Assert
    expect(item.querySelector('.token-requirements__note')?.textContent?.trim()).toBe('· always granted');
  });

  it('should note that an unverifiable permission is checked when used', () => {
    // Arrange / Act
    const item = itemFor(render(GITHUB_REQUIREMENTS), 'Workflows (read and write)');

    // Assert
    expect(item.querySelector('.token-requirements__note')?.textContent?.trim()).toBe('· checked when used');
  });

  it('should hide the note separator from assistive technology', () => {
    // Arrange / Act
    const item = itemFor(render(GITHUB_REQUIREMENTS), 'Metadata (read)');

    // Assert
    expect(item.querySelector('.token-requirements__note-separator')?.getAttribute('aria-hidden')).toBe('true');
  });

  it('should explain "checked when used" when any permission is unverifiable', () => {
    // Arrange / Act
    const el = render(GITHUB_REQUIREMENTS);

    // Assert
    expect(el.querySelector('.token-requirements__explanation')?.textContent?.trim()).toBe(
      'Foundry checks each permission on every repository before it starts work. Permissions marked "checked when used" can\'t be checked ahead of time, so a missing one fails the push that needs it.',
    );
  });

  it('should leave out the explanation when no permission is unverifiable', () => {
    // Arrange / Act
    const el = render(GITLAB_REQUIREMENTS, 'https://gitlab.example.com');

    // Assert
    expect(el.querySelector('.token-requirements__explanation')).toBeNull();
  });

  it('should prefix the heading and owner-hint ids with idPrefix', () => {
    // Arrange / Act
    const el = render(GITHUB_REQUIREMENTS, 'https://github.com', 'setup');

    // Assert
    const section = el.querySelector('section')!;
    expect(section.getAttribute('aria-labelledby')).toBe('setup-req-heading');
    expect(el.querySelector('h3')?.id).toBe('setup-req-heading');
    expect(el.querySelector('.token-requirements__owner-hint')?.id).toBe('setup-owner-hint');
    expect(el.querySelector('.token-requirements__link')?.getAttribute('aria-describedby')).toBe('setup-owner-hint');
  });

  it('should link to the creation page on the base URL host', () => {
    // Arrange / Act
    const el = render(GITLAB_REQUIREMENTS, 'https://gitlab.example.com/');

    // Assert
    const link = el.querySelector<HTMLAnchorElement>('.token-requirements__link')!;
    expect(link.href).toBe('https://gitlab.example.com/-/user_settings/personal_access_tokens');
    expect(link.textContent).toContain('Create token on gitlab.example.com');
    expect(link.textContent).toContain('(opens in a new tab)');
  });

  it('should show the no-Base-URL hint instead of a link when the base URL is empty', () => {
    // Arrange / Act
    const el = render(GITHUB_REQUIREMENTS, '');

    // Assert
    expect(el.querySelector('.token-requirements__link')).toBeNull();
    expect(el.querySelector('.token-requirements__link-hint')?.textContent).toContain('Enter a Base URL above');
  });
  it('should note that a not-checked permission is not checked', () => {
    // Arrange / Act
    const item = itemFor(render(GITHUB_REQUIREMENTS), 'Repository access: Only select repositories');

    // Assert
    expect(item.querySelector('.token-requirements__note')?.textContent?.trim()).toBe('· not checked');
  });

  it('should render the closing note right after the list', () => {
    // Arrange / Act
    const el = render(GITHUB_REQUIREMENTS);

    // Assert
    const note = el.querySelector('.token-requirements__closing-note');
    expect(note?.textContent?.trim()).toBe(GITHUB_REQUIREMENTS.closingNote);
    expect(note?.previousElementSibling?.classList.contains('token-requirements__list')).toBe(true);
    expect(note?.nextElementSibling?.classList.contains('token-requirements__explanation')).toBe(true);
  });

  it('should leave out the closing note when there is none', () => {
    // Arrange / Act
    const el = render(GITLAB_REQUIREMENTS, 'https://gitlab.example.com');

    // Assert
    expect(el.querySelector('.token-requirements__closing-note')).toBeNull();
  });

  it('should leave out the explanation when only a not-checked permission cannot be verified', () => {
    // Arrange
    const requirements: TokenRequirements = {
      ...GITHUB_REQUIREMENTS,
      permissions: GITHUB_REQUIREMENTS.permissions.filter((permission) => permission.verification !== 'unverifiable'),
    };

    // Act
    const el = render(requirements);

    // Assert
    expect(el.querySelector('.token-requirements__explanation')).toBeNull();
  });

  it('should render the creation note before the link and describe the link by it and the owner hint', () => {
    // Arrange
    const requirements: TokenRequirements = { ...GITHUB_REQUIREMENTS, creationNote: 'Choose Generate token.' };

    // Act
    const el = render(requirements, 'https://github.com', 'setup');

    // Assert
    const note = el.querySelector('.token-requirements__creation-note');
    const link = el.querySelector('.token-requirements__link');
    expect(note?.textContent?.trim()).toBe('Choose Generate token.');
    expect(note?.id).toBe('setup-creation-note');
    expect(note?.nextElementSibling).toBe(link);
    expect(link?.getAttribute('aria-describedby')).toBe('setup-owner-hint setup-creation-note');
  });

  it('should leave the link undescribed when there is neither an owner hint nor a creation note', () => {
    // Arrange / Act
    const el = render(GITLAB_REQUIREMENTS, 'https://gitlab.example.com');

    // Assert
    expect(el.querySelector('.token-requirements__link')?.hasAttribute('aria-describedby')).toBe(false);
  });
});

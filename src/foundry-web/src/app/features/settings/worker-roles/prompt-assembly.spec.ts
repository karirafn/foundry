import { TestBed } from '@angular/core/testing';
import { PromptAssemblyComponent } from './prompt-assembly';
import {
  IMPLEMENT_PRINCIPLES,
  PRINCIPLES_SLOT,
  PRINCIPLE_SECTIONS,
  ROLE_PROMPT_SLOT,
  buildWorkerRoleCatalog,
} from '../testing/worker-role-catalog.fixture';
import { PromptSectionDescriptor } from './worker-role.service';

const ROLE_SYSTEM_PROMPT = 'You are implementing issue #{issueNumber} in {repository}.';

interface RenderOptions {
  readonly rolePrincipleSections?: readonly string[];
  readonly rolePrinciplesText?: string;
}

function render(
  sections: readonly PromptSectionDescriptor[] = buildWorkerRoleCatalog().promptSections,
  options: RenderOptions = {},
) {
  TestBed.configureTestingModule({ imports: [PromptAssemblyComponent] });
  const fixture = TestBed.createComponent(PromptAssemblyComponent);
  fixture.componentRef.setInput('sections', sections);
  fixture.componentRef.setInput('rolePromptSlot', ROLE_PROMPT_SLOT);
  fixture.componentRef.setInput('principlesSlot', PRINCIPLES_SLOT);
  fixture.componentRef.setInput('principleSections', PRINCIPLE_SECTIONS);
  fixture.componentRef.setInput('roleName', 'implement');
  fixture.componentRef.setInput('roleSystemPrompt', ROLE_SYSTEM_PROMPT);
  fixture.componentRef.setInput(
    'rolePrincipleSections',
    options.rolePrincipleSections ?? PRINCIPLE_SECTIONS,
  );
  fixture.componentRef.setInput(
    'rolePrinciplesText',
    options.rolePrinciplesText ?? IMPLEMENT_PRINCIPLES,
  );
  fixture.detectChanges();
  return { fixture, el: fixture.nativeElement as HTMLElement };
}

function text(el: Element | null | undefined): string {
  return el?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
}

function sections(el: HTMLElement): HTMLDetailsElement[] {
  return Array.from(el.querySelectorAll<HTMLDetailsElement>('details.prompt-assembly__section'));
}

function roleSlot(el: HTMLElement): HTMLDetailsElement {
  return el.querySelector<HTMLDetailsElement>('details.prompt-assembly__section--role')!;
}

describe('PromptAssemblyComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('should title the block "System prompt" as an h3', () => {
    // Act
    const { el } = render();

    // Assert
    expect(text(el.querySelector('h3'))).toBe('System prompt');
  });

  it('should explain that the role prompt starts open', () => {
    // Act
    const { el } = render();

    // Assert
    expect(text(el.querySelector('.prompt-assembly__hint'))).toBe(
      "Foundry assembles the system prompt from these sections, in order. Conditional sections are included only when their condition holds for the run. Worker principles list the sections this role receives. This role's own prompt starts open.",
    );
  });

  it('should list every section in order', () => {
    // Act
    const { el } = render();

    // Assert
    const names = Array.from(el.querySelectorAll('li .prompt-assembly__title-text'));
    expect(names.map((n) => text(n))).toEqual([
      'Base instructions',
      'Worker principles',
      'Role prompt',
      '.NET toolchain',
      'Docker',
      'Fresh run',
      'Continuation',
      'Revision',
      'Rebase',
    ]);
  });

  it('should number sections in a gutter outside the disclosure', () => {
    // Act
    const { el } = render();

    // Assert
    const items = Array.from(el.querySelectorAll('li.prompt-assembly__item'));
    const ordinals = items.map((li) => li.querySelector(':scope > span[aria-hidden="true"]'));
    expect(ordinals.map((o) => text(o))).toEqual(['1', '2', '3', '4', '5', '6', '7', '8', '9']);
  });

  it('should label each condition with a human tag', () => {
    // Act
    const { el } = render();

    // Assert
    const tags = Array.from(el.querySelectorAll('.prompt-assembly__condition'));
    expect(tags.map((t) => text(t))).toEqual([
      'Always',
      'Always',
      'Always',
      '.NET image',
      'Docker image',
      'Fresh run',
      'Continuation run',
      'Revision run',
      'Rebase conflict',
    ]);
  });

  it('should colour condition tags by kind: always, image and run', () => {
    // Act
    const { el } = render();

    // Assert
    const tags = Array.from(el.querySelectorAll('.prompt-assembly__condition'));
    expect(tags.map((t) => t.className.match(/condition--(\w+)/)![1])).toEqual([
      'always',
      'always',
      'always',
      'image',
      'image',
      'run',
      'run',
      'run',
      'run',
    ]);
  });

  it('should treat an unknown condition as a run-kind tag labelled with its raw value', () => {
    // Act
    const { el } = render([{ key: 'x', title: 'X', condition: 'Future' as never, content: 'x' }]);

    // Assert
    const tag = el.querySelector('.prompt-assembly__condition')!;
    expect(text(tag)).toBe('Future');
    expect(tag.classList.contains('prompt-assembly__condition--run')).toBe(true);
  });

  it('should render every section, the role slot included, as a details/summary expander', () => {
    // Act
    const { el } = render();

    // Assert
    expect(sections(el)).toHaveLength(9);
    expect(sections(el).every((d) => d.querySelector(':scope > summary') !== null)).toBe(true);
  });

  it('should show the role system prompt inline in the role slot, open by default', () => {
    // Act
    const { el } = render();

    // Assert
    const slot = roleSlot(el);
    expect(slot.open).toBe(true);
    expect(slot.querySelector('pre')!.textContent).toBe(ROLE_SYSTEM_PROMPT);
    expect(slot.textContent).not.toContain(ROLE_PROMPT_SLOT);
    expect(text(el)).not.toContain('Shown under System prompt above.');
  });

  it('should tag the role slot with the role name', () => {
    // Act
    const { el } = render();

    // Assert
    const tag = roleSlot(el).querySelector('summary fd-role-tag')!;
    expect(text(tag)).toBe('Role: implement');
  });

  it('should start every other section collapsed', () => {
    // Act
    const { el } = render();

    // Assert
    const others = sections(el).filter((d) => d !== roleSlot(el));
    expect(others).toHaveLength(8);
    expect(others.some((d) => d.open)).toBe(false);
  });

  it('should highlight placeholders in section content', () => {
    // Act
    const { el } = render();

    // Assert
    expect(text(sections(el)[0].querySelector('mark.prompt-text__placeholder'))).toBe('{branchName}');
  });

  it('should highlight placeholders in section titles', () => {
    // Act
    const { el } = render([
      { key: 'x', title: 'Issue {issueContent}', condition: 'Always', content: 'body' },
    ]);

    // Assert
    const name = el.querySelector('.prompt-assembly__summary-name')!;
    expect(text(name.querySelector('mark.prompt-text__placeholder'))).toBe('{issueContent}');
    expect(text(name)).toBe('Issue {issueContent}');
  });

  it('should not make section content a scroll region', () => {
    // Act
    const { el } = render();

    // Assert
    const pre = el.querySelector('.prompt-assembly__section pre')!;
    expect(pre.getAttribute('role')).toBeNull();
    expect(pre.getAttribute('tabindex')).toBeNull();
  });

  it('should offer Expand all, which opens every section', () => {
    // Arrange
    const { el, fixture } = render();
    const button = el.querySelector<HTMLButtonElement>('button')!;
    expect(text(button)).toBe('Expand all');
    expect(button.type).toBe('button');

    // Act
    button.click();
    fixture.detectChanges();

    // Assert
    expect(sections(el).every((d) => d.open)).toBe(true);
    expect(text(button)).toBe('Collapse all');
  });

  it('should collapse every section, the role slot included, with Collapse all', () => {
    // Arrange
    const { el, fixture } = render();
    const button = el.querySelector<HTMLButtonElement>('button')!;
    button.click();
    fixture.detectChanges();

    // Act
    button.click();
    fixture.detectChanges();

    // Assert
    expect(sections(el).some((d) => d.open)).toBe(false);
    expect(text(button)).toBe('Expand all');
  });

  it('should switch to Collapse all once every section was opened by hand', () => {
    // Arrange
    const { el, fixture } = render();

    // Act
    for (const details of sections(el)) {
      details.open = true;
      details.dispatchEvent(new Event('toggle'));
    }
    fixture.detectChanges();

    // Assert
    expect(text(el.querySelector('button'))).toBe('Collapse all');
  });

  it('should offer Expand all again once the role slot was closed by hand', () => {
    // Arrange
    const { el, fixture } = render();
    for (const details of sections(el)) {
      details.open = true;
      details.dispatchEvent(new Event('toggle'));
    }
    fixture.detectChanges();

    // Act
    roleSlot(el).open = false;
    roleSlot(el).dispatchEvent(new Event('toggle'));
    fixture.detectChanges();

    // Assert
    expect(text(el.querySelector('button'))).toBe('Expand all');
  });

  describe('worker principles', () => {
    const TRIAGE_PRINCIPLES = 'Engineering principles.\n\nDesign:\n- Safe defaults.';

    function principlesSlot(el: HTMLElement): HTMLDetailsElement | undefined {
      return sections(el).find(
        (d) => text(d.querySelector('.prompt-assembly__title-text')) === 'Worker principles',
      );
    }

    it('should start the principles section collapsed, with no role tag or accent', () => {
      // Act
      const { el } = render();

      // Assert
      const slot = principlesSlot(el)!;
      expect(slot.open).toBe(false);
      expect(slot.classList.contains('prompt-assembly__section--role')).toBe(false);
      expect(slot.querySelector('fd-role-tag')).toBeNull();
    });

    it('should name the selected sections in the summary, so the selection is heard while collapsed', () => {
      // Act
      const { el } = render(undefined, {
        rolePrincipleSections: ['Design', 'Domain modelling'],
        rolePrinciplesText: TRIAGE_PRINCIPLES,
      });

      // Assert
      const summary = principlesSlot(el)!.querySelector('summary')!;
      expect(text(summary.querySelector('.prompt-assembly__note'))).toBe(
        'Includes: Design, Domain modelling',
      );
      expect(text(summary)).toContain('Includes: Design, Domain modelling');
    });

    it('should list the selected sections in the fixed order, whatever order the role gives', () => {
      // Act
      const { el } = render(undefined, { rolePrincipleSections: ['Domain modelling', 'Testing'] });

      // Assert
      expect(text(principlesSlot(el)!.querySelector('.prompt-assembly__note'))).toBe(
        'Includes: Testing, Domain modelling',
      );
    });

    it("should show the role's own assembled principles text in the body", () => {
      // Act
      const { el } = render(undefined, {
        rolePrincipleSections: ['Design', 'Domain modelling'],
        rolePrinciplesText: TRIAGE_PRINCIPLES,
      });

      // Assert
      const slot = principlesSlot(el)!;
      expect(slot.querySelector('fd-principle-sections')).not.toBeNull();
      expect(slot.querySelector('pre')!.textContent).toBe(TRIAGE_PRINCIPLES);
      expect(slot.textContent).not.toContain(PRINCIPLES_SLOT);
    });

    describe('when the role selects no section', () => {
      function renderEmpty() {
        return render(undefined, { rolePrincipleSections: [], rolePrinciplesText: '' });
      }

      function staticRow(el: HTMLElement): HTMLElement {
        return el.querySelector<HTMLElement>('.prompt-assembly__static')!;
      }

      it('should keep the row in place as a static row that cannot open', () => {
        // Act
        const { el } = renderEmpty();

        // Assert
        const row = staticRow(el);
        expect(row.tagName).toBe('DIV');
        expect(row.getAttribute('tabindex')).toBeNull();
        expect(row.getAttribute('aria-expanded')).toBeNull();
        expect(text(row.querySelector('.prompt-assembly__title-text'))).toBe('Worker principles');
        expect(principlesSlot(el)).toBeUndefined();
        expect(sections(el)).toHaveLength(8);
      });

      it('should tag the row Not included and say why', () => {
        // Act
        const { el } = renderEmpty();

        // Assert
        const row = staticRow(el);
        const tag = row.querySelector('.prompt-assembly__condition')!;
        expect(text(tag)).toBe('Not included');
        expect(tag.classList.contains('prompt-assembly__condition--excluded')).toBe(true);
        expect(text(row.querySelector('.prompt-assembly__note'))).toBe(
          'This role declares no principle sections, so its prompt has none.',
        );
      });

      it('should number only the included sections', () => {
        // Act
        const { el } = renderEmpty();

        // Assert
        const ordinals = Array.from(el.querySelectorAll('li.prompt-assembly__item')).map((li) =>
          text(li.querySelector(':scope > span[aria-hidden="true"]')),
        );
        expect(ordinals).toEqual(['1', '', '2', '3', '4', '5', '6', '7', '8']);
      });

      it('should leave the static row out of Expand all', () => {
        // Arrange
        const { el, fixture } = renderEmpty();
        const button = el.querySelector<HTMLButtonElement>('button')!;

        // Act
        button.click();
        fixture.detectChanges();

        // Assert
        expect(sections(el).every((d) => d.open)).toBe(true);
        expect(text(button)).toBe('Collapse all');
      });
    });
  });
});

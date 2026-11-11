import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { WorkerRoleCardComponent } from './worker-role-card';
import { buildWorkerRole, buildWorkerRoleCatalog } from '../testing/worker-role-catalog.fixture';

const RESTRICTED_HINT =
  'Runs read-only: no command tools, no provider token and no branch. It returns a verdict only.';
const FULL_HINT = 'Runs with command tools and the account token, on its own branch.';

function render(capability: string) {
  TestBed.configureTestingModule({
    imports: [WorkerRoleCardComponent],
    providers: [provideHttpClient(), provideHttpClientTesting()],
  });
  const fixture = TestBed.createComponent(WorkerRoleCardComponent);
  fixture.componentRef.setInput('role', buildWorkerRole({ capability }));
  fixture.componentRef.setInput('catalog', buildWorkerRoleCatalog());
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  return {
    el,
    badge: el.querySelector<HTMLElement>('.role-card__capability'),
    hint: el.querySelector<HTMLElement>('.role-card__capability-hint'),
  };
}

function visibleText(el: Element | null): string {
  if (el === null) {
    return '';
  }
  const clone = el.cloneNode(true) as Element;
  clone.querySelectorAll('.sr-only').forEach((node) => node.remove());
  return clone.textContent?.replace(/\s+/g, ' ').trim() ?? '';
}

describe('WorkerRoleCardComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  describe('capability badge', () => {
    it('should place the badge right after the title, inside the title row', () => {
      // Act
      const { el, badge } = render('full');

      // Assert
      const title = el.querySelector('.role-card__title-row > .role-card__title');
      expect(title).not.toBeNull();
      expect(title?.nextElementSibling).toBe(badge);
    });

    it('should show "Restricted" with the restricted modifier for a restricted role', () => {
      // Act
      const { badge } = render('restricted');

      // Assert
      expect(visibleText(badge)).toBe('Restricted');
      expect(badge?.className).toBe(
        'role-card__badge role-card__capability role-card__capability--restricted',
      );
    });

    it('should show a decorative lock icon on the restricted badge', () => {
      // Act
      const { badge } = render('restricted');

      // Assert
      const icon = badge?.querySelector('svg');
      expect(icon).not.toBeNull();
      expect(icon?.getAttribute('aria-hidden')).toBe('true');
      expect(icon?.getAttribute('focusable')).toBe('false');
      expect(icon?.getAttribute('width')).toBe('11');
      expect(icon?.getAttribute('height')).toBe('11');
      expect(icon?.getAttribute('stroke')).toBe('currentColor');
    });

    it('should show "Full access" with the full modifier and no icon for a full role', () => {
      // Act
      const { badge } = render('full');

      // Assert
      expect(visibleText(badge)).toBe('Full access');
      expect(badge?.className).toBe(
        'role-card__badge role-card__capability role-card__capability--full',
      );
      expect(badge?.querySelector('svg')).toBeNull();
    });

    it('should show "Unknown capability" with no modifier for an unrecognised value', () => {
      // Act
      const { badge } = render('sandboxed');

      // Assert
      expect(visibleText(badge)).toBe('Unknown capability');
      expect(badge?.className).toBe('role-card__badge role-card__capability');
      expect(badge?.querySelector('svg')).toBeNull();
    });

    it('should read as "Capability: Restricted" to a screen reader', () => {
      // Act
      const { badge } = render('restricted');

      // Assert
      expect(badge?.tagName).toBe('SPAN');
      expect(badge?.firstElementChild?.classList.contains('sr-only')).toBe(true);
      expect(badge?.firstElementChild?.textContent).toBe('Capability: ');
      expect(badge?.textContent?.replace(/\s+/g, ' ').trim()).toBe('Capability: Restricted');
    });
  });

  describe('capability hint', () => {
    it('should show the restricted hint after the purpose for a restricted role', () => {
      // Act
      const { el, hint } = render('restricted');

      // Assert
      expect(hint?.textContent?.trim()).toBe(RESTRICTED_HINT);
      expect(hint?.classList.contains('role-card__hint')).toBe(true);
      expect(el.querySelector('.role-card__purpose')?.nextElementSibling).toBe(hint);
    });

    it('should show the full hint for a full role', () => {
      // Act
      const { hint } = render('full');

      // Assert
      expect(hint?.textContent?.trim()).toBe(FULL_HINT);
    });

    it('should show no hint for an unrecognised capability', () => {
      // Act
      const { hint } = render('sandboxed');

      // Assert
      expect(hint).toBeNull();
    });
  });
});

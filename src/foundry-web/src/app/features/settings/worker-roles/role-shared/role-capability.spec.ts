import { TestBed } from '@angular/core/testing';
import { RoleCapabilityComponent, capabilityHint } from './role-capability';

const RESTRICTED_HINT =
  'Runs read-only: no command tools, no provider token and no branch. It returns a verdict only.';
const FULL_HINT = 'Runs with command tools and the account token, on its own branch.';

function render(capability: string): HTMLElement | null {
  TestBed.configureTestingModule({ imports: [RoleCapabilityComponent] });
  const fixture = TestBed.createComponent(RoleCapabilityComponent);
  fixture.componentRef.setInput('capability', capability);
  fixture.detectChanges();
  return (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('.role-capability');
}

function visibleText(el: Element | null): string {
  if (el === null) {
    return '';
  }
  const clone = el.cloneNode(true) as Element;
  clone.querySelectorAll('.sr-only').forEach((node) => node.remove());
  return clone.textContent?.replace(/\s+/g, ' ').trim() ?? '';
}

describe('RoleCapabilityComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('should show "Restricted" with the restricted modifier for a restricted role', () => {
    // Act
    const badge = render('restricted');

    // Assert
    expect(visibleText(badge)).toBe('Restricted');
    expect(badge?.className).toBe('role-capability role-capability--restricted');
  });

  it('should show a decorative lock icon on the restricted badge', () => {
    // Act
    const badge = render('restricted');

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
    const badge = render('full');

    // Assert
    expect(visibleText(badge)).toBe('Full access');
    expect(badge?.className).toBe('role-capability role-capability--full');
    expect(badge?.querySelector('svg')).toBeNull();
  });

  it('should show "Unknown capability" with no modifier for an unrecognised value', () => {
    // Act
    const badge = render('sandboxed');

    // Assert
    expect(visibleText(badge)).toBe('Unknown capability');
    expect(badge?.className).toBe('role-capability');
    expect(badge?.querySelector('svg')).toBeNull();
  });

  it('should read as "Capability: Restricted" to a screen reader', () => {
    // Act
    const badge = render('restricted');

    // Assert
    expect(badge?.tagName).toBe('SPAN');
    expect(badge?.firstElementChild?.classList.contains('sr-only')).toBe(true);
    expect(badge?.firstElementChild?.textContent).toBe('Capability: ');
    expect(badge?.textContent?.replace(/\s+/g, ' ').trim()).toBe('Capability: Restricted');
  });
});

describe('capabilityHint', () => {
  it('should explain a restricted role runs read-only', () => {
    // Act / Assert
    expect(capabilityHint('restricted')).toBe(RESTRICTED_HINT);
  });

  it('should explain a full role runs with command tools', () => {
    // Act / Assert
    expect(capabilityHint('full')).toBe(FULL_HINT);
  });

  it('should give no hint for an unrecognised capability', () => {
    // Act / Assert
    expect(capabilityHint('sandboxed')).toBeNull();
  });
});

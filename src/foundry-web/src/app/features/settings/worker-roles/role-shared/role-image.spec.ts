import { TestBed } from '@angular/core/testing';
import { RoleImageComponent } from './role-image';
import { buildWorkerRoleCatalog } from '../../testing/worker-role-catalog.fixture';
import { WorkerRoleImage } from '../worker-role.service';

function render(overrides: Partial<WorkerRoleImage> = {}): HTMLElement {
  TestBed.configureTestingModule({ imports: [RoleImageComponent] });
  const fixture = TestBed.createComponent(RoleImageComponent);
  fixture.componentRef.setInput('image', { ...buildWorkerRoleCatalog().image, ...overrides });
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

function text(el: Element | null | undefined): string {
  return el?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
}

describe('RoleImageComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('should show the image tag, build time and short commit', () => {
    // Act
    const el = render();

    // Assert
    expect(text(el.querySelector('.role-image__tag'))).toBe('foundry-worker:local');
    const builtAt = el.querySelector('time.role-image__built-at')!;
    expect(builtAt.getAttribute('datetime')).toBe('2026-11-01T10:30:00Z');
    expect(text(builtAt)).toContain('2026');
    const commit = el.querySelector('.role-image__commit')!;
    expect(text(commit)).toBe('0123456');
    expect(commit.getAttribute('title')).toBe('0123456789abcdef0123456789abcdef01234567');
  });

  it('should show "unknown" when the image commit is null', () => {
    // Act
    const el = render({ commit: null });

    // Assert
    expect(text(el.querySelector('.role-image__commit'))).toBe('unknown');
  });

  it('should not label the image metadata list', () => {
    // Act
    const el = render();

    // Assert
    expect(el.querySelector('dl.role-image__meta')!.hasAttribute('aria-label')).toBe(false);
  });

  it('should not show the unverified warning for a verified image', () => {
    // Act
    const el = render();

    // Assert
    expect(el.querySelector('.role-image__unverified')).toBeNull();
  });

  it('should warn that the catalog is unverified as a note when the image is not verified', () => {
    // Act
    const el = render({ verified: false });

    // Assert
    const note = el.querySelector('p.role-image__unverified')!;
    expect(note.getAttribute('role')).toBe('note');
    expect(text(note)).toBe(
      '⚠Unverified: image building is disabled, so this catalog was read from disk and may differ from the roles in the operator-supplied image foundry-worker:local.',
    );
  });
});

import { Component, input } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { page } from 'vitest/browser';
import { RoleDetailComponent } from './role-detail';
import { WorkerRoleService } from '../worker-role.service';
import {
  buildImplementRole,
  buildModelOptions,
  buildWorkerRole,
  buildWorkerRoleCatalog,
} from '../../testing/worker-role-catalog.fixture';

@Component({
  selector: 'fd-role-detail-layout-host',
  imports: [RoleDetailComponent],
  template: `<div class="host" [style.width.px]="width()"><fd-role-detail /></div>`,
})
class HostComponent {
  readonly width = input.required<number>();
}

/**
 * The settings content width at a viewport width: the settings layout caps at 960px with 1rem padding, and sits
 * beside its 200px sidebar (1.5rem gap) above 768px.
 */
function settingsContentWidth(viewport: number): number {
  const inner = Math.min(viewport, 960) - 32;
  return viewport > 768 ? inner - 200 - 24 : inner;
}

/**
 * Renders the implement role. `worstCase` shows everything the rail can hold at once: an unverified image, the
 * model-list refresh callout and a save error under both settings.
 */
async function render(
  width: number,
  worstCase = false,
  roleName = 'implement',
): Promise<HTMLElement> {
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter([]),
      {
        provide: ActivatedRoute,
        useValue: { paramMap: of(convertToParamMap({ roleName })) },
      },
    ],
  });
  const fixture = TestBed.createComponent(HostComponent);
  fixture.componentRef.setInput('width', width);
  fixture.detectChanges();
  const httpMock = TestBed.inject(HttpTestingController);
  const catalog = buildWorkerRoleCatalog({
    roles: [
      buildImplementRole({ model: 'claude-opus-4-5-20251101' }, { model: 'opus', effort: 'xhigh' }),
      buildWorkerRole({ name: 'triage', capability: 'restricted', model: 'sonnet' }),
    ],
  });
  if (worstCase) {
    catalog.image = { ...catalog.image, verified: false };
  }
  httpMock.expectOne('/api/worker-roles').flush(catalog);
  httpMock
    .expectOne((req) => req.url === '/api/worker-roles/models')
    .flush(
      buildModelOptions({
        refreshError: worstCase ? 'The Models API could not be reached.' : null,
      }),
    );
  if (worstCase) {
    vi.spyOn(console, 'error').mockImplementation(() => {});
    const service = TestBed.inject(WorkerRoleService);
    for (const demand of ['routine', 'demanding'] as const) {
      service.saveDemandModel('implement', demand, 'claude-typo', '');
      httpMock.expectOne(`/api/worker-roles/implement/demand-models/${demand}`).flush(
        {
          detail: "'claude-typo' is not a Claude Code alias or a model listed by the Models API.",
          type: 'tag:foundry,2026:problems/Worker.RoleModelNotListed',
        },
        { status: 400, statusText: 'Bad Request' },
      );
    }
  }
  await fixture.whenStable();
  return (fixture.nativeElement as HTMLElement).querySelector('.host') as HTMLElement;
}

function box(el: Element): DOMRect {
  return el.getBoundingClientRect();
}

function overflowing(host: HTMLElement): string[] {
  const hostRight = box(host).right;
  return Array.from(host.querySelectorAll('*'))
    .filter((el) => box(el).right > hostRight + 0.5)
    .map((el) => el.tagName.toLowerCase() + '.' + el.className);
}

function rail(host: HTMLElement): HTMLElement {
  return host.querySelector<HTMLElement>('.role-detail__rail')!;
}

/** True when the sticky rail fits below its inset inside the viewport. */
function stickyRailFits(host: HTMLElement): boolean {
  const style = getComputedStyle(rail(host));
  return parseFloat(style.insetBlockStart) + box(rail(host)).height <= window.innerHeight;
}

function controls(setting: Element): { model: DOMRect; effort: DOMRect; save: DOMRect } {
  return {
    model: box(
      setting.querySelector('select.role-model__select:not(.role-model__select--effort)')!,
    ),
    effort: box(setting.querySelector('.role-model__select--effort')!),
    save: box(setting.querySelector('.role-model__save')!),
  };
}

// Runs in a real browser (test-layout target); jsdom would measure every box as 0.
describe('role detail layout with demand settings', () => {
  // Restores the runner's viewport, so later layout specs measure as before.
  const original = { width: window.innerWidth, height: window.innerHeight };
  afterEach(async () => {
    TestBed.resetTestingModule();
    await page.viewport(original.width, original.height);
  });

  it('stacks the rail above the content, in flow, on the settings page at 1280x800', async () => {
    // Arrange
    await page.viewport(1280, 800);

    // Act
    const host = await render(settingsContentWidth(1280), true);

    // Assert
    expect(host.querySelectorAll('fd-worker-role-model')).toHaveLength(2);
    expect(getComputedStyle(rail(host)).position).not.toBe('sticky');
    expect(overflowing(host)).toEqual([]);
    expect(document.documentElement.scrollWidth).toBeLessThanOrEqual(1280);
  });

  it('does not stick the two-setting rail beside the content at 1280x800, where it may not fit', async () => {
    // Arrange: wide enough for the two-column body (52rem).
    await page.viewport(1280, 800);

    // Act
    const host = await render(1000, true);

    // Assert
    expect(box(host.querySelector('.role-detail__content')!).left).toBeGreaterThanOrEqual(
      box(rail(host)).right,
    );
    expect(getComputedStyle(rail(host)).position).not.toBe('sticky');
  });

  it('sticks the two-setting rail beside the content once the viewport fits its worst case', async () => {
    // Arrange
    await page.viewport(1280, 992);

    // Act
    const host = await render(1000, true);

    // Assert
    expect(getComputedStyle(rail(host)).position).toBe('sticky');
    expect(stickyRailFits(host)).toBe(true);
  });

  it('puts the effort select and Save side by side under the model select in the desktop rail', async () => {
    // Arrange
    await page.viewport(1280, 900);

    // Act
    const host = await render(1000);

    // Assert
    for (const setting of Array.from(host.querySelectorAll('fd-worker-role-model'))) {
      const { model, effort, save } = controls(setting);
      expect(effort.top).toBeGreaterThanOrEqual(model.bottom);
      expect(Math.abs(save.bottom - effort.bottom)).toBeLessThanOrEqual(1);
      expect(save.left).toBeGreaterThanOrEqual(effort.right);
    }
  });

  it('gives the effort select and Save their own rows at 360px, at 40px, with no horizontal scroll', async () => {
    // Arrange
    await page.viewport(360, 800);

    // Act
    const host = await render(settingsContentWidth(360), true);

    // Assert
    for (const setting of Array.from(host.querySelectorAll('fd-worker-role-model'))) {
      const { model, effort, save } = controls(setting);
      expect(effort.top).toBeGreaterThanOrEqual(model.bottom);
      expect(save.top).toBeGreaterThanOrEqual(effort.bottom);
      for (const control of [model, effort, save]) {
        expect(control.height).toBeGreaterThanOrEqual(40);
      }
    }
    expect(overflowing(host)).toEqual([]);
    expect(document.documentElement.scrollWidth).toBeLessThanOrEqual(360);
  });

  it('keeps a single-model role sticky beside the content at 1280x800, as before', async () => {
    // Arrange
    await page.viewport(1280, 800);

    // Act
    const host = await render(1000, false, 'triage');

    // Assert
    expect(host.querySelectorAll('fd-worker-role-model')).toHaveLength(1);
    expect(getComputedStyle(rail(host)).position).toBe('sticky');
    expect(stickyRailFits(host)).toBe(true);
  });
});

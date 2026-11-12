import { TestBed } from '@angular/core/testing';
import { PrincipleSectionsComponent } from './principle-sections';
import { PRINCIPLE_SECTIONS } from '../testing/worker-role-catalog.fixture';

function render(selected: readonly string[]) {
  TestBed.configureTestingModule({ imports: [PrincipleSectionsComponent] });
  const fixture = TestBed.createComponent(PrincipleSectionsComponent);
  fixture.componentRef.setInput('sections', PRINCIPLE_SECTIONS);
  fixture.componentRef.setInput('selected', selected);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

function text(el: Element | null | undefined): string {
  return el?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
}

function group(el: HTMLElement, label: string): string[] | null {
  const dt = Array.from(el.querySelectorAll('dt')).find((d) => text(d) === label);
  if (!dt) {
    return null;
  }
  const pills = dt.nextElementSibling!.querySelectorAll('ul[role="list"] > li');
  return Array.from(pills).map((li) => text(li));
}

describe('PrincipleSectionsComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('should list the included and the not included sections as labelled groups in the fixed order', () => {
    // Act
    const el = render(['Domain modelling', 'Design']);

    // Assert
    expect(group(el, 'Included')).toEqual(['Design', 'Domain modelling']);
    expect(group(el, 'Not included')).toEqual(['Testing', 'Code']);
  });

  it('should hide the Not included group when the role has every section', () => {
    // Act
    const el = render(PRINCIPLE_SECTIONS);

    // Assert
    expect(group(el, 'Included')).toEqual(['Testing', 'Design', 'Code', 'Domain modelling']);
    expect(group(el, 'Not included')).toBeNull();
  });

  it('should mark whether a section is included by class as well as by its group label', () => {
    // Act
    const el = render(['Design']);

    // Assert
    expect(el.querySelectorAll('.principle-sections__pill--included')).toHaveLength(1);
    expect(el.querySelectorAll('.principle-sections__pill--excluded')).toHaveLength(3);
  });
});

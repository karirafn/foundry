import { TestBed } from '@angular/core/testing';
import { ToolchainListComponent } from './toolchain-list';
import { ToolchainEntry } from '../../../core/models/toolchain.model';

function render(tools: readonly ToolchainEntry[]): HTMLElement {
  TestBed.configureTestingModule({ imports: [ToolchainListComponent] });
  const fixture = TestBed.createComponent(ToolchainListComponent);
  fixture.componentRef.setInput('tools', tools);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('ToolchainListComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('should render one name/version pair per tool in the given order', () => {
    // Act
    const el = render([
      { name: 'node', version: '22.22.3' },
      { name: 'docker', version: '29.9.3' },
    ]);

    // Assert
    const items = Array.from(el.querySelectorAll('dl.toolchain-list > div.toolchain-list__item'));
    expect(items.map((item) => item.querySelector('dt')?.textContent)).toEqual(['node', 'docker']);
    expect(items.map((item) => item.querySelector('dd > code')?.textContent)).toEqual(['22.22.3', '29.9.3']);
  });

  it('should render a tool listed twice twice', () => {
    // Act
    const el = render([
      { name: 'git', version: '2.47.0' },
      { name: 'git', version: '2.47.0' },
    ]);

    // Assert
    expect(el.querySelectorAll('.toolchain-list__item').length).toBe(2);
  });

  it('should render an empty list when there are no tools', () => {
    // Act
    const el = render([]);

    // Assert
    expect(el.querySelectorAll('.toolchain-list__item').length).toBe(0);
  });

  it('should not be interactive', () => {
    // Act
    const el = render([{ name: 'node', version: '22.22.3' }]);

    // Assert
    expect(el.querySelector('a, button, [tabindex]')).toBeNull();
  });
});

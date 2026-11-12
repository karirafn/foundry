import { TestBed } from '@angular/core/testing';
import { RevertedFilesComponent } from './reverted-files';
import { RevertedFile } from '../../workers/worker-run.model';

const twoFiles: RevertedFile[] = [
  { path: 'assets/demo.mp4', reason: 'oversized', sizeBytes: 50_541_363 },
  { path: 'tools/protoc.exe', reason: 'binary', sizeBytes: 3_250_586 },
];

function many(count: number): RevertedFile[] {
  return Array.from({ length: count }, (_, i) => ({ path: `file-${i}.bin`, reason: 'binary' as const, sizeBytes: 940 }));
}

function render(files: readonly RevertedFile[]) {
  TestBed.configureTestingModule({ imports: [RevertedFilesComponent] });
  const fixture = TestBed.createComponent(RevertedFilesComponent);
  fixture.componentRef.setInput('files', files);
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  const text = (node: Element): string => (node.textContent ?? '').replace(/\s+/g, ' ').trim();
  return { fixture, el, text };
}

describe('RevertedFilesComponent', () => {
  it('should render a "Reverted files" heading that labels the list', () => {
    // Arrange / Act
    const { el, text } = render(twoFiles);

    // Assert
    const list = el.querySelector('ul') as HTMLElement;
    const headingId = list.getAttribute('aria-labelledby');
    expect(list.getAttribute('role')).toBe('list');
    expect(headingId).toBeTruthy();
    const heading = el.querySelector(`#${headingId}`);
    expect(heading).not.toBeNull();
    expect(text(heading!)).toBe('Reverted files');
  });

  it('should explain that the files were reverted in a separate commit', () => {
    // Arrange / Act
    const { el, text } = render(twoFiles);

    // Assert
    expect(text(el)).toContain('The diff gate reverted these files in a separate commit before pushing.');
    expect(text(el)).toContain('The pull request body lists them too.');
  });

  it('should render the path, reason chip and formatted size of each file', () => {
    // Arrange / Act
    const { el, text } = render(twoFiles);

    // Assert
    const rows = Array.from(el.querySelectorAll('li'));
    expect(rows.length).toBe(2);
    expect(text(rows[0].querySelector('code')!)).toBe('assets/demo.mp4');
    const tooLarge = rows[0].querySelector('.badge') as HTMLElement;
    expect(text(tooLarge)).toBe('Too large');
    expect(tooLarge.classList.contains('badge--reverted-oversized')).toBe(true);
    expect(text(rows[0].querySelector('.reverted-files__size')!)).toBe('48.2 MB');

    expect(text(rows[1].querySelector('code')!)).toBe('tools/protoc.exe');
    const binary = rows[1].querySelector('.badge') as HTMLElement;
    expect(text(binary)).toBe('Binary');
    expect(binary.classList.contains('badge--reverted-binary')).toBe(true);
    expect(text(rows[1].querySelector('.reverted-files__size')!)).toBe('3.1 MB');
  });

  it('should hide the chip from assistive technology and give each row an sr-only tail', () => {
    // Arrange / Act
    const { el, text } = render(twoFiles);

    // Assert
    const rows = Array.from(el.querySelectorAll('li'));
    expect(rows[0].querySelector('.badge')?.getAttribute('aria-hidden')).toBe('true');
    expect(rows[0].querySelector('.reverted-files__size')?.getAttribute('aria-hidden')).toBe('true');
    expect(text(rows[0].querySelector('.sr-only')!)).toBe(', too large, 48.2 MB');
    expect(text(rows[1].querySelector('.sr-only')!)).toBe(', binary, 3.1 MB');
  });

  it('should show all files without a disclosure when there are 10 or fewer', () => {
    // Arrange / Act
    const { el } = render(many(10));

    // Assert
    expect(el.querySelector('details')).toBeNull();
    expect(el.querySelectorAll('li').length).toBe(10);
  });

  it('should put files beyond the first 10 behind a "more files" disclosure', () => {
    // Arrange / Act
    const { el, text } = render(many(14));

    // Assert
    const details = el.querySelector('details') as HTMLDetailsElement;
    expect(details).not.toBeNull();
    expect(details.open).toBe(false);
    expect(text(details.querySelector('summary')!)).toBe('4 more files');
    expect(details.querySelectorAll('li').length).toBe(4);
    expect(el.querySelectorAll('li').length).toBe(14);
    expect(text(details.querySelector('li code')!)).toBe('file-10.bin');
  });

  it('should say "1 more file" for a single overflow file', () => {
    // Arrange / Act
    const { el, text } = render(many(11));

    // Assert
    expect(text(el.querySelector('summary')!)).toBe('1 more file');
  });

  it('should give two instances distinct heading ids', () => {
    // Arrange
    TestBed.configureTestingModule({ imports: [RevertedFilesComponent] });
    const a = TestBed.createComponent(RevertedFilesComponent);
    const b = TestBed.createComponent(RevertedFilesComponent);
    a.componentRef.setInput('files', twoFiles);
    b.componentRef.setInput('files', twoFiles);

    // Act
    a.detectChanges();
    b.detectChanges();

    // Assert
    const idA = (a.nativeElement as HTMLElement).querySelector('ul')?.getAttribute('aria-labelledby');
    const idB = (b.nativeElement as HTMLElement).querySelector('ul')?.getAttribute('aria-labelledby');
    expect(idA).not.toBe(idB);
  });
});

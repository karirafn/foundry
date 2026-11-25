import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Subject } from 'rxjs';
import { SystemSignalRService } from '../../../core/services/system-signalr.service';
import { WorkerImageToolchain } from '../../../core/models/toolchain.model';
import { WORKER_IMAGE_TOOLCHAIN_URL, WorkerImageToolchainComponent } from './worker-image-toolchain';

const IMAGE_ID = 'sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef';
const UNKNOWN_TEXT =
  'Toolchain unknown. Foundry reads it from this image after the build and keeps retrying on its own.';

const RECORDED: WorkerImageToolchain = {
  status: 'recorded',
  imageId: IMAGE_ID,
  recordedAt: '2026-11-16T09:30:00+00:00',
  tools: [
    { name: 'node', version: '22.22.3' },
    { name: 'docker', version: '29.9.3' },
  ],
};

interface Harness {
  fixture: ComponentFixture<WorkerImageToolchainComponent>;
  httpMock: HttpTestingController;
  reloadTrigger: Subject<void>;
  reconnected: Subject<void>;
  el: HTMLElement;
}

function setup(): Harness {
  const reloadTrigger = new Subject<void>();
  const reconnected = new Subject<void>();
  TestBed.configureTestingModule({
    imports: [WorkerImageToolchainComponent],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: SystemSignalRService, useValue: { reloadTrigger, reconnected } },
    ],
  });
  const fixture = TestBed.createComponent(WorkerImageToolchainComponent);
  fixture.detectChanges();
  return {
    fixture,
    httpMock: TestBed.inject(HttpTestingController),
    reloadTrigger,
    reconnected,
    el: fixture.nativeElement as HTMLElement,
  };
}

function respond(harness: Harness, toolchain: WorkerImageToolchain): void {
  harness.httpMock.expectOne(WORKER_IMAGE_TOOLCHAIN_URL).flush(toolchain);
  harness.fixture.detectChanges();
}

function text(el: Element | null | undefined): string {
  return el?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
}

function metaTerms(el: HTMLElement): string[] {
  return Array.from(el.querySelectorAll('dl.worker-image-toolchain__meta dt')).map((dt) => text(dt));
}

describe('WorkerImageToolchainComponent', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    TestBed.resetTestingModule();
  });

  it('should render nothing until the toolchain has loaded', () => {
    // Act
    const harness = setup();

    // Assert
    expect(harness.el.querySelector('h3')).toBeNull();
    harness.httpMock.expectOne(WORKER_IMAGE_TOOLCHAIN_URL);
  });

  describe('recorded', () => {
    it('should show the heading, the short image ID with the full one as its title, the recorded time and the tools', () => {
      // Arrange
      const harness = setup();

      // Act
      respond(harness, RECORDED);

      // Assert
      const el = harness.el;
      expect(text(el.querySelector('h3'))).toBe('Installed toolchain');
      expect(metaTerms(el)).toEqual(['Image', 'Recorded']);
      const image = el.querySelector('.worker-image-toolchain__image')!;
      expect(text(image)).toBe('0123456789ab');
      expect(image.getAttribute('title')).toBe(IMAGE_ID);
      const recordedAt = el.querySelector('time.worker-image-toolchain__recorded-at')!;
      expect(recordedAt.getAttribute('datetime')).toBe('2026-11-16T09:30:00+00:00');
      expect(text(recordedAt)).toContain('2026');
      const names = Array.from(el.querySelectorAll('fd-toolchain-list dt')).map((dt) => text(dt));
      expect(names).toEqual(['node', 'docker']);
      expect(el.querySelector('.worker-image-toolchain__unknown')).toBeNull();
    });
  });

  describe('unknown with an image ID', () => {
    it('should show the heading, the image only and the unknown text, without a list', () => {
      // Arrange
      const harness = setup();

      // Act
      respond(harness, { status: 'unknown', imageId: IMAGE_ID });

      // Assert
      const el = harness.el;
      expect(text(el.querySelector('h3'))).toBe('Installed toolchain');
      expect(metaTerms(el)).toEqual(['Image']);
      expect(text(el.querySelector('.worker-image-toolchain__image'))).toBe('0123456789ab');
      expect(text(el.querySelector('p.worker-image-toolchain__unknown'))).toBe(UNKNOWN_TEXT);
      expect(el.querySelector('fd-toolchain-list')).toBeNull();
    });
  });

  describe('unknown without an image', () => {
    it('should show the heading and the unknown text without the meta list', () => {
      // Arrange
      const harness = setup();

      // Act
      respond(harness, { status: 'unknown', imageId: null });

      // Assert
      const el = harness.el;
      expect(text(el.querySelector('h3'))).toBe('Installed toolchain');
      expect(el.querySelector('dl.worker-image-toolchain__meta')).toBeNull();
      expect(text(el.querySelector('p.worker-image-toolchain__unknown'))).toBe(UNKNOWN_TEXT);
      expect(el.querySelector('fd-toolchain-list')).toBeNull();
    });
  });

  it('should show an image ID without a sha256 prefix cut to 12 characters', () => {
    // Arrange
    const harness = setup();

    // Act
    respond(harness, { status: 'unknown', imageId: 'fedcba9876543210fedcba' });

    // Assert
    expect(text(harness.el.querySelector('.worker-image-toolchain__image'))).toBe('fedcba987654');
  });

  it('should replace the unknown state with the recorded one when a reload is triggered', () => {
    // Arrange
    const harness = setup();
    respond(harness, { status: 'unknown', imageId: IMAGE_ID });

    // Act
    harness.reloadTrigger.next();
    respond(harness, RECORDED);

    // Assert
    expect(harness.el.querySelector('.worker-image-toolchain__unknown')).toBeNull();
    expect(harness.el.querySelectorAll('fd-toolchain-list dt').length).toBe(2);
  });

  it('should re-fetch the toolchain after the hub reconnects', () => {
    // Arrange
    const harness = setup();
    respond(harness, { status: 'unknown', imageId: IMAGE_ID });

    // Act
    harness.reconnected.next();

    // Assert
    respond(harness, RECORDED);
    expect(harness.el.querySelectorAll('fd-toolchain-list dt').length).toBe(2);
  });

  it('should keep the last loaded toolchain when a re-fetch fails', () => {
    // Arrange
    const harness = setup();
    respond(harness, RECORDED);
    const consoleWarn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);

    // Act
    harness.reloadTrigger.next();
    harness.httpMock
      .expectOne(WORKER_IMAGE_TOOLCHAIN_URL)
      .flush('boom', { status: 500, statusText: 'Server Error' });
    harness.fixture.detectChanges();

    // Assert
    expect(harness.el.querySelectorAll('fd-toolchain-list dt').length).toBe(2);
    expect(consoleWarn).toHaveBeenCalledWith(
      expect.stringContaining('Failed to load the worker image toolchain')
    );
  });

  it('should have nothing focusable', () => {
    // Arrange
    const harness = setup();

    // Act
    respond(harness, RECORDED);

    // Assert
    expect(harness.el.querySelector('a, button, input, [tabindex]')).toBeNull();
  });
});

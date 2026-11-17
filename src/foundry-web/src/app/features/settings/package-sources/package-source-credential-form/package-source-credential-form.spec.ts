import { TestBed } from '@angular/core/testing';
import { PackageSourceCredentialFormComponent } from './package-source-credential-form';
import { CredentialField, CredentialFieldErrors, PackageSource, SaveCredentialRequest } from '../package-source.model';
import { SOURCE_URL, makeSource } from '../../testing/package-source.fixture';

const CREDENTIAL = { username: 'ci-reader', updatedAt: '2026-11-16T09:00:00Z' };

function render(source: PackageSource = makeSource()) {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({ imports: [PackageSourceCredentialFormComponent] });
  const fixture = TestBed.createComponent(PackageSourceCredentialFormComponent);
  fixture.componentRef.setInput('source', source);
  fixture.detectChanges();
  document.body.appendChild(fixture.nativeElement);
  const el = fixture.nativeElement as HTMLElement;
  const saved: SaveCredentialRequest[] = [];
  const edited: CredentialField[] = [];
  let cancelled = 0;
  fixture.componentInstance.save.subscribe((r) => saved.push(r));
  fixture.componentInstance.fieldEdited.subscribe((f) => edited.push(f));
  fixture.componentInstance.cancel.subscribe(() => cancelled++);
  return { fixture, el, saved, edited, cancelled: () => cancelled };
}

function text(el: Element | null): string {
  return (el?.textContent ?? '').replace(/\s+/g, ' ').trim();
}

function type(el: HTMLElement, id: string, value: string): void {
  const input = el.querySelector<HTMLInputElement>(`#${id}`)!;
  input.value = value;
  input.dispatchEvent(new Event('input'));
}

function saveButton(el: HTMLElement): HTMLButtonElement {
  return el.querySelector<HTMLButtonElement>('.package-source-credential-form__save-btn')!;
}

describe('PackageSourceCredentialFormComponent', () => {
  afterEach(() => document.body.replaceChildren());

  describe('add', () => {
    it('titles the form Add credential and shows the source and its scope', () => {
      const { el } = render(makeSource({ repositories: [{ id: 'a', slug: 'a/a' }, { id: 'b', slug: 'b/b' }] }));

      expect(text(el.querySelector('h2'))).toBe('Add credential');
      expect(el.querySelector('h2')?.getAttribute('tabindex')).toBe('-1');
      expect(text(el.querySelector('.package-source-credential-form__url'))).toBe(SOURCE_URL);
      expect(text(el)).toContain('Covers every repository that declares this source (2).');
    });

    it('starts empty, with a plain username and a password token field', () => {
      const { el } = render();

      const username = el.querySelector<HTMLInputElement>('#pscf-username')!;
      const token = el.querySelector<HTMLInputElement>('#pscf-token')!;
      expect(username.value).toBe('');
      expect(username.type).toBe('text');
      expect(username.getAttribute('autocomplete')).toBe('off');
      expect(token.type).toBe('password');
      expect(token.getAttribute('autocomplete')).toBe('new-password');
      expect(token.hasAttribute('maxlength')).toBe(false);
      expect(text(el.querySelector('label[for="pscf-token"]'))).toBe('Token');
      expect(text(el.querySelector('#pscf-token-hint'))).toBe("Needs read access to this source's packages.");
    });

    it('emits the entered username and token on save', () => {
      const { el, saved } = render();

      type(el, 'pscf-username', 'ci-reader');
      type(el, 'pscf-token', 'secret');
      saveButton(el).click();

      expect(saved).toEqual([{ username: 'ci-reader', token: 'secret' }]);
      expect(text(saveButton(el))).toBe('Save credential');
    });
  });

  describe('replace', () => {
    it('titles the form Replace credential, pre-fills the username and leaves the token empty', () => {
      const { el } = render(makeSource({ credential: CREDENTIAL }));

      expect(text(el.querySelector('h2'))).toBe('Replace credential');
      expect(el.querySelector<HTMLInputElement>('#pscf-username')!.value).toBe('ci-reader');
      expect(el.querySelector<HTMLInputElement>('#pscf-token')!.value).toBe('');
      expect(text(el.querySelector('label[for="pscf-token"]'))).toBe('New token');
      expect(text(el.querySelector('#pscf-token-hint'))).toBe(
        'The stored token is never shown. Enter it again to replace the credential.',
      );
    });
  });

  it('toggles the token between hidden and shown', () => {
    const { fixture, el } = render();
    const toggle = el.querySelector<HTMLButtonElement>('.package-source-credential-form__toggle-visibility-btn')!;

    expect(toggle.getAttribute('aria-label')).toBe('Show token');
    toggle.click();
    fixture.detectChanges();

    expect(el.querySelector<HTMLInputElement>('#pscf-token')!.type).toBe('text');
    expect(toggle.getAttribute('aria-label')).toBe('Hide token');
  });

  describe('client validation', () => {
    it('blocks an empty save, marks both fields and focuses the first', async () => {
      const { fixture, el, saved } = render();

      saveButton(el).click();
      fixture.detectChanges();
      await fixture.whenStable();

      expect(saved).toEqual([]);
      const username = el.querySelector<HTMLInputElement>('#pscf-username')!;
      expect(username.getAttribute('aria-invalid')).toBe('true');
      expect(username.getAttribute('aria-describedby')).toBe('pscf-username-hint pscf-username-error');
      expect(text(el.querySelector('#pscf-username-error'))).toBe('Username is required.');
      expect(text(el.querySelector('#pscf-token-error'))).toBe('Token is required.');
      expect(el.querySelector('#pscf-token-error')?.getAttribute('role')).toBe('alert');
      expect(text(el.querySelector('.package-source-credential-form__summary'))).toBe('2 fields need attention.');
      expect(document.activeElement).toBe(username);
    });

    it('accepts whitespace without trimming', () => {
      const { el, saved } = render();
      type(el, 'pscf-username', ' ');
      type(el, 'pscf-token', ' ');
      saveButton(el).click();
      expect(saved).toEqual([{ username: ' ', token: ' ' }]);
    });

    it('rejects values over the length limits with one summary per field count', () => {
      const { fixture, el, saved } = render();
      type(el, 'pscf-username', 'ok');
      type(el, 'pscf-token', 't'.repeat(2049));

      saveButton(el).click();
      fixture.detectChanges();

      expect(saved).toEqual([]);
      expect(text(el.querySelector('#pscf-token-error'))).toBe('Token must be 2048 characters or fewer.');
      expect(text(el.querySelector('.package-source-credential-form__summary'))).toBe('1 field needs attention.');
    });

    it('clears a field error when the field is edited', () => {
      const { fixture, el } = render();
      saveButton(el).click();
      fixture.detectChanges();

      type(el, 'pscf-username', 'x');
      fixture.detectChanges();

      expect(text(el.querySelector('#pscf-username-error'))).toBe('');
      expect(el.querySelector('#pscf-username')?.getAttribute('aria-invalid')).toBeNull();
    });

    it('keeps error elements in the DOM so aria-describedby never dangles', () => {
      const { el } = render();
      expect(el.querySelector('#pscf-username-error')).not.toBeNull();
      expect(el.querySelector('#pscf-username-hint')).not.toBeNull();
      expect(el.querySelector('#pscf-token-error')).not.toBeNull();
      expect(el.querySelector('#pscf-token-hint')).not.toBeNull();
    });
  });

  describe('server errors', () => {
    it('shows server field errors and focuses the first invalid field', async () => {
      const { fixture, el } = render();
      type(el, 'pscf-username', 'ci-reader');
      type(el, 'pscf-token', 'secret');

      const errors: CredentialFieldErrors = { token: 'Token must be 2048 characters or fewer.' };
      fixture.componentRef.setInput('fieldErrors', errors);
      fixture.detectChanges();
      await fixture.whenStable();

      expect(text(el.querySelector('#pscf-token-error'))).toBe('Token must be 2048 characters or fewer.');
      expect(text(el.querySelector('.package-source-credential-form__summary'))).toBe('1 field needs attention.');
      expect(document.activeElement).toBe(el.querySelector('#pscf-token'));
    });

    it('asks to clear a server error when its field is edited', () => {
      const { fixture, el, edited } = render();
      fixture.componentRef.setInput('fieldErrors', { username: 'bad' });
      fixture.detectChanges();

      type(el, 'pscf-username', 'x');

      expect(edited).toEqual(['username']);
    });

    it('shows the save error in an alert', () => {
      const { fixture, el } = render();
      fixture.componentRef.setInput('saveError', 'Failed to save credential');
      fixture.detectChanges();

      const alert = el.querySelector('.package-source-credential-form__save-error');
      expect(alert?.getAttribute('role')).toBe('alert');
      expect(text(alert)).toBe('Failed to save credential');
    });
  });

  it('shows the busy state while saving and does not emit again', () => {
    const { fixture, el, saved } = render();
    type(el, 'pscf-username', 'ci-reader');
    type(el, 'pscf-token', 'secret');
    fixture.componentRef.setInput('saving', true);
    fixture.detectChanges();

    saveButton(el).click();

    expect(saved).toEqual([]);
    expect(text(saveButton(el))).toBe('Saving…');
    expect(saveButton(el).querySelector('fd-spinner')).not.toBeNull();
    expect(saveButton(el).getAttribute('aria-disabled')).toBe('true');
    expect(el.querySelector('[role="region"]')?.getAttribute('aria-busy')).toBe('true');
    expect(el.querySelector<HTMLButtonElement>('.package-source-credential-form__cancel-link')!.disabled).toBe(true);
  });

  it('emits cancel from the Cancel link', () => {
    const { el, cancelled } = render();
    el.querySelector<HTMLButtonElement>('.package-source-credential-form__cancel-link')!.click();
    expect(cancelled()).toBe(1);
  });
});

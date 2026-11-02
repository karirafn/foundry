import { AbstractControl, FormArray, FormGroup, ValidationErrors, ValidatorFn } from '@angular/forms';

export const MEMORY_MIN_MB = 512;
export const MEMORY_MAX_MB = 65536;
export const CPU_MIN = 0.5;
export const CPU_MAX = 32;
export const PIDS_MIN = 64;
export const PIDS_MAX = 8192;

const WINDOWS_DRIVE_ROOT = /^[A-Za-z]:[\\/]/;
const WHOLE_NUMBER = /^\d+$/;
const PARENT_SEGMENT = '..';

export type MountPathKind = 'container' | 'host';

export function wholeNumberValidator(control: AbstractControl): ValidationErrors | null {
  const value: unknown = control.value;
  if (value === null || value === '') {
    return null;
  }
  return WHOLE_NUMBER.test(String(value)) ? null : { wholeNumber: true };
}

export function absolutePathValidator(kind: MountPathKind): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    const value: string = control.value;
    if (!value) {
      return null;
    }
    const isAbsolute: boolean = value.startsWith('/') || (kind === 'host' && WINDOWS_DRIVE_ROOT.test(value));
    return isAbsolute ? null : { notAbsolute: true };
  };
}

/** Optional host settings file path; validated trimmed because the server trims it before its own checks. */
export function hostSettingsPathValidator(control: AbstractControl): ValidationErrors | null {
  const value: string = (control.value ?? '').trim();
  if (!value) {
    return null;
  }
  if (!value.startsWith('/') && !WINDOWS_DRIVE_ROOT.test(value)) {
    return { notAbsolute: true };
  }
  return value.replaceAll('\\', '/').split('/').includes(PARENT_SEGMENT) ? { traversal: true } : null;
}

export function notBlankValidator(control: AbstractControl): ValidationErrors | null {
  const value: string = control.value;
  return value && value.trim() === '' ? { blank: true } : null;
}

export function noTraversalValidator(control: AbstractControl): ValidationErrors | null {
  const value: string = control.value;
  if (!value) {
    return null;
  }
  const segments: string[] = value.replaceAll('\\', '/').split('/');
  return segments.includes(PARENT_SEGMENT) ? { traversal: true } : null;
}

function normalizeContainerPath(path: string): string {
  return path.replace(/\/+$/, '');
}

/** Flags every row whose container path repeats an earlier row's (trailing slashes ignored). */
export function uniqueContainerPathsValidator(array: AbstractControl): ValidationErrors | null {
  const rows: FormGroup[] = (array as FormArray).controls as FormGroup[];
  const seen = new Set<string>();
  const duplicateRows: number[] = [];

  rows.forEach((row, index) => {
    const path: string = normalizeContainerPath(row.getRawValue().containerPath);
    if (!path) {
      return;
    }
    if (seen.has(path)) {
      duplicateRows.push(index);
    }
    seen.add(path);
  });

  return duplicateRows.length > 0 ? { duplicateContainerPath: duplicateRows } : null;
}

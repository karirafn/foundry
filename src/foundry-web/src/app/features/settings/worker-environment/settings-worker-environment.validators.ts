import { AbstractControl, ValidationErrors } from '@angular/forms';

export const MEMORY_MIN_MB = 512;
export const MEMORY_MAX_MB = 65536;
export const CPU_MIN = 0.5;
export const CPU_MAX = 32;
export const PIDS_MIN = 64;
export const PIDS_MAX = 8192;

const WHOLE_NUMBER = /^\d+$/;

export function wholeNumberValidator(control: AbstractControl): ValidationErrors | null {
  const value: unknown = control.value;
  if (value === null || value === '') {
    return null;
  }
  return WHOLE_NUMBER.test(String(value)) ? null : { wholeNumber: true };
}

import { HttpErrorResponse } from '@angular/common/http';
import { TimeoutError } from 'rxjs';

const TIMEOUT_MESSAGE = 'The request timed out. Please try again.';

export function extractErrorMessage(err: HttpErrorResponse | TimeoutError): string | null {
  if (err instanceof TimeoutError) {
    return TIMEOUT_MESSAGE;
  }

  if (
    err.error !== null &&
    typeof err.error === 'object' &&
    typeof err.error.detail === 'string' &&
    err.error.detail
  ) {
    return err.error.detail;
  }

  return null;
}

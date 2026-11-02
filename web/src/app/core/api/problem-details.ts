import { HttpErrorResponse } from '@angular/common/http';

/** RFC 7807 body as produced by the backend (plus its `code` extension and validation `errors`). */
interface ProblemDetailsBody {
  title?: string;
  detail?: string;
  status?: number;
  code?: string;
  errors?: Record<string, string[]>;
}

/** What the UI works with: never a raw HttpErrorResponse. */
export interface ApiError {
  status: number;
  message: string;
  /** Machine-readable code, e.g. "auth.email_taken". Use it for logic; show `message` to people. */
  code?: string;
  /** Validation errors keyed by form field name (camelCase, matching the form controls). */
  fieldErrors: Record<string, string[]>;
}

export function toApiError(error: unknown): ApiError {
  if (!(error instanceof HttpErrorResponse)) {
    return { status: 0, message: 'Something went wrong. Please try again.', fieldErrors: {} };
  }

  if (error.status === 0) {
    return {
      status: 0,
      message: 'Cannot reach the server. Check your connection and try again.',
      fieldErrors: {},
    };
  }

  const body = isProblem(error.error) ? error.error : undefined;
  const fieldErrors = body?.errors ?? {};

  if (error.status >= 500) {
    return {
      status: error.status,
      message: 'The server had a problem. Please try again in a moment.',
      code: body?.code,
      fieldErrors,
    };
  }

  const firstFieldMessage = Object.values(fieldErrors)[0]?.[0];
  return {
    status: error.status,
    message:
      body?.title ?? body?.detail ?? firstFieldMessage ?? 'The request could not be completed.',
    code: body?.code,
    fieldErrors,
  };
}

function isProblem(value: unknown): value is ProblemDetailsBody {
  return typeof value === 'object' && value !== null;
}

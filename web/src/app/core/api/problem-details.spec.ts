import { HttpErrorResponse } from '@angular/common/http';
import { toApiError } from './problem-details';

const http = (status: number, error: unknown) => new HttpErrorResponse({ status, error });

describe('toApiError', () => {
  it('uses the title of an RFC 7807 body and keeps the machine-readable code', () => {
    const e = toApiError(
      http(409, { title: 'An account with this email already exists.', code: 'auth.email_taken' }),
    );
    expect(e).toEqual({
      status: 409,
      message: 'An account with this email already exists.',
      code: 'auth.email_taken',
      fieldErrors: {},
    });
  });

  it('keeps per-field validation errors', () => {
    const e = toApiError(
      http(400, {
        title: 'One or more validation errors occurred.',
        errors: { email: ['Bad email'], password: ['Too short'] },
      }),
    );
    expect(e.fieldErrors).toEqual({ email: ['Bad email'], password: ['Too short'] });
  });

  it('explains a network failure (status 0) instead of showing a raw error', () => {
    expect(toApiError(http(0, null)).message).toMatch(/cannot reach the server/i);
  });

  it('never shows server internals for 5xx errors', () => {
    const e = toApiError(http(500, { title: 'NullReferenceException at Foo.Bar line 12' }));
    expect(e.message).not.toMatch(/NullReference/);
    expect(e.message).toMatch(/server had a problem/i);
  });

  it('copes with bodies that are not JSON problems', () => {
    expect(toApiError(http(404, '<html>Not found</html>')).message).toMatch(
      /could not be completed/i,
    );
    expect(toApiError(http(400, null)).message).toMatch(/could not be completed/i);
  });

  it('copes with errors that are not HTTP errors at all', () => {
    expect(toApiError(new Error('boom')).message).toMatch(/something went wrong/i);
    expect(toApiError(undefined).status).toBe(0);
  });
});

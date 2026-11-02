import { safeReturnUrl } from './safe-return-url';

describe('safeReturnUrl (open-redirect protection)', () => {
  it.each(['/lots', '/gate', '/my?tab=history', '/admin/users/12'])(
    'accepts the in-app path %s',
    (url) => {
      expect(safeReturnUrl(url)).toBe(url);
    },
  );

  it.each([
    'https://evil.example',
    'http://evil.example/x',
    '//evil.example',
    '/\\evil.example',
    'javascript:alert(1)',
    'evil.example',
    'lots',
    '/lo\tts',
    '/lots\\..\\x',
    '',
  ])('rejects %j', (url) => {
    expect(safeReturnUrl(url)).toBeNull();
  });

  it('rejects missing values', () => {
    expect(safeReturnUrl(undefined)).toBeNull();
    expect(safeReturnUrl(null)).toBeNull();
  });
});

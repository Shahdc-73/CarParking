/**
 * After login we send people back to where they were headed (?returnUrl=...). That value comes from the URL, so an attacker
 * can craft a link like /login?returnUrl=https://evil.example. Accept ONLY in-app absolute paths ("/lots", "/my?x=1");
 * reject anything that could leave the site ("https://...", "//evil.example", "/\evil.example", "javascript:...").
 */
export function safeReturnUrl(value: string | null | undefined): string | null {
  if (!value || !value.startsWith('/') || value.startsWith('//') || value.startsWith('/\\'))
    return null;
  if (/[\u0000-\u001f\\]/.test(value)) return null; // control characters and backslashes (browsers treat "\" like "/")
  return value;
}

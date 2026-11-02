// Fails the build if the production index.html contains anything our Content-Security-Policy (see security-headers.conf)
// would block. Such a page "works" in `ng serve` but renders unstyled or dead behind nginx, and no unit test notices.
import { readFileSync } from 'node:fs';

const file = process.argv[2] ?? 'dist/web/browser/index.html';
const html = readFileSync(file, 'utf8');

const problems = [];
if (/<script(?![^>]*\bsrc=)[^>]*>/i.test(html)) problems.push('an inline <script> block (blocked: script-src is same-origin only)');
if (/\son[a-z]+\s*=/i.test(html)) problems.push('an inline event handler attribute such as onload= (blocked by CSP)');
if (/media="print"/i.test(html)) problems.push('a media="print" stylesheet swap (needs an inline script to activate; set styles.inlineCritical=false)');

if (problems.length) {
  console.error(`CSP check failed for ${file}:`);
  for (const p of problems) console.error(`  - ${p}`);
  process.exit(1);
}
console.log(`CSP check passed for ${file}.`);

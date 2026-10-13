import { readFile, writeFile } from 'node:fs/promises';

export function orderCsp(html) {
  const policies = [...html.matchAll(/<meta\s+http-equiv="content-security-policy"\s+content="[^"]*"\s*\/?\s*>/gi)];
  if (policies.length !== 1 || !/<head><meta charset="utf-8"\s*\/?\s*>/i.test(html)) {
    throw new Error('Expected one native CSP and an initial UTF-8 declaration.');
  }
  const withoutPolicy = html.replace(policies[0][0], '');
  return withoutPolicy.replace(/(<head><meta charset="utf-8"\s*\/?\s*>)/i, `$1${policies[0][0]}`);
}

// Astro inserts its native policy at the end of explicit head content. Move the
// unchanged policy before protected resources; retain its computed script hashes.
export function earlyCsp() {
  return { name: 'early-static-csp', hooks: {
    'astro:build:done': async ({ dir }) => {
      const page = new URL('index.html', dir);
      await writeFile(page, orderCsp(await readFile(page, 'utf8')));
    },
  } };
}

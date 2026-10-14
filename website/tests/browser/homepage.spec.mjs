import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { mkdir } from 'node:fs/promises';
const evidence = process.env.SITE_CAPTURE_DIRECTORY;

for (const width of [1920, 1440, 1280, 1024, 768, 390, 375, 320]) {
  for (const colorScheme of ['light', 'dark']) {
    test(`${width}px ${colorScheme}: accessibility, semantics, local resources and layout`, async ({ page, context }) => {
      await page.setViewportSize({ width, height: 900 });
      await page.emulateMedia({ colorScheme });
      const requests = [], violations = [], errors = [];
      page.on('request', request => { requests.push(request.url()); });
      page.on('pageerror', error => errors.push(error.message));
      page.on('console', message => { if (message.type() === 'error') errors.push(message.text()); });
      page.on('response', response => { if (response.status() >= 400) errors.push(`HTTP ${response.status()} ${response.url()}`); });
      await page.addInitScript(() => {
        window.cspViolations = [];
        document.addEventListener('securitypolicyviolation', event => window.cspViolations.push({ directive: event.effectiveDirective, uri: event.blockedURI }));
      });
      expect((await page.goto('./')).status()).toBe(200);
      await expect(page.locator('html')).toHaveAttribute('data-effective-theme', colorScheme);
      await page.evaluate(() => document.fonts.ready);
      for (const section of ['#features', '#how-it-works', '#screenshots', '#download']) {
        await page.locator(section).scrollIntoViewIfNeeded();
      }
      await page.waitForFunction(() => [...document.images].filter(image => image.id !== 'viewer-image' && image.getClientRects().length).every(image => image.complete && image.naturalWidth > 0));
      const axe = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze();
      violations.push(...axe.violations.map(item => ({ id: item.id, impact: item.impact, nodes: item.nodes.map(node => node.target) })));
      expect(violations).toEqual([]);
      await expect(page.getByRole('heading', { level: 1 })).toHaveText('Filter. Organize. Archive.');
      expect(await page.evaluate(() => [...document.images].filter(image => image.getClientRects().length && image.naturalWidth)
        .map(image => Math.abs(image.getBoundingClientRect().width / image.getBoundingClientRect().height - image.naturalWidth / image.naturalHeight))
        .every(delta => delta < 0.01))).toBe(true);
      expect(await page.getByRole('heading', { level: 1 }).count()).toBe(1);
      expect(await page.locator('link[rel=canonical]').getAttribute('href')).toBe('https://melnorme6.github.io/ExtSieve/');
      const schema = JSON.parse(await page.locator('script[type="application/ld+json"]').textContent());
      const rows = page.locator('.package-row');
      if (await rows.count()) {
        expect(await rows.count()).toBe(5);
        expect(schema.softwareVersion).toBe((await page.locator('.release-version').textContent()).slice(1));
        expect(schema.downloadUrl).toEqual(await rows.evaluateAll(links => links.map(link => link.href)));
        for (const row of await rows.all()) expect((await row.boundingBox()).height).toBeGreaterThanOrEqual(44);
      } else expect(schema.softwareVersion).toBeUndefined();
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      const hero = page.locator(`.hero-screen.theme-${colorScheme} img`);
      const framing = await hero.evaluate(image => {
        const bounds = image.getBoundingClientRect();
        const frame = image.closest('.hero-window').getBoundingClientRect();
        return {
          width: image.naturalWidth, height: image.naturalHeight,
          ratio: bounds.width / bounds.height,
          inside: bounds.left >= frame.left && bounds.right <= frame.right &&
            bounds.top >= frame.top && bounds.bottom <= frame.bottom,
          source: image.currentSrc,
        };
      });
      expect([framing.width, framing.height]).toEqual([902, 732]);
      expect(framing.ratio).toBeCloseTo(902 / 732, 2);
      expect(framing.inside).toBe(true);
      expect(framing.source).toContain(`${colorScheme}-full.webp`);
      const toggle = await page.locator('.theme-toggle').boundingBox();
      expect(toggle.width).toBe(44);
      expect(toggle.height).toBe(44);
      const icon = await page.locator(`.theme-toggle .theme-${colorScheme} svg`).boundingBox();
      expect(Math.abs(icon.x + icon.width / 2 - toggle.x - toggle.width / 2)).toBeLessThan(0.5);
      expect(Math.abs(icon.y + icon.height / 2 - toggle.y - toggle.height / 2)).toBeLessThan(0.5);
      expect(await page.locator('.site-header').evaluate(element => element.getBoundingClientRect().top)).toBe(0);
      expect(await page.locator('.feature-card').count()).toBe(3);
      for (const card of await page.locator('.step-card').all()) {
        expect(await card.locator('a.image-open:visible').count()).toBe(1);
        expect(await card.locator('.image-expand:visible').count()).toBe(1);
      }
      expect(requests.filter(url => new URL(url).origin !== 'http://127.0.0.1:4335')).toEqual([]);
      expect(await context.cookies()).toEqual([]);
      expect(await page.evaluate(() => Object.keys(localStorage))).toEqual([]);
      expect(await page.evaluate(() => window.cspViolations)).toEqual([]);
      expect(errors).toEqual([]);
      if (evidence && [1920, 390, 320].includes(width)) {
        await mkdir(evidence, { recursive: true });
        await page.evaluate(() => window.scrollTo(0, 0));
        await page.waitForFunction(() => scrollY === 0);
        await page.screenshot({ path: `${evidence}/homepage-${width}-${colorScheme}.png`, fullPage: true });
      }
    });
  }
}

test('only the Latin font loads for all six language samples; complete Inter remains available', async ({ page }) => {
  const fonts = [];
  page.on('request', request => { if (request.url().endsWith('.woff2')) fonts.push(request.url()); });
  await page.goto('./');
  const fontResponse = await page.request.get('fonts/InterVariable-latin.woff2');
  expect(fontResponse.headers().vary ?? '').not.toMatch(/origin/i);
  await page.evaluate(async () => {
    const sample = document.createElement('p');
    sample.id = 'font-sample';
    sample.textContent = 'English: Archive. Italiano: più città. Deutsch: Größe Straße ẞ. Français: cœur déjà où. Español: acción pingüino ¿qué? Português: seleção ação avô. € © —';
    document.body.append(sample);
    await document.fonts.ready;
  });
  expect(fonts.filter(url => url.endsWith('/InterVariable-latin.woff2'))).toHaveLength(1);
  expect(fonts.filter(url => url.endsWith('/InterVariable.woff2'))).toHaveLength(0);
  await page.evaluate(async () => {
    document.getElementById('font-sample').textContent = 'Ω Д';
    await document.fonts.load('16px "Inter Complete"', 'Ω Д');
    await document.fonts.ready;
  });
  expect(fonts.filter(url => url.endsWith('/InterVariable.woff2'))).toHaveLength(1);
});

test('all visible links and controls follow the natural Tab order', async ({ page }) => {
  await page.goto('./');
  const controls = await page.locator('a[href], button, [tabindex="0"]').evaluateAll(elements => elements
    .filter(element => !element.hasAttribute('disabled') && element.getClientRects().length && getComputedStyle(element).visibility !== 'hidden')
    .map(element => ({ tag:element.tagName, href:element.getAttribute('href'), id:element.id })));
  for (const expected of controls) {
    await page.keyboard.press('Tab');
    expect(await page.evaluate(() => ({tag:document.activeElement.tagName,href:document.activeElement.getAttribute('href'),id:document.activeElement.id}))).toEqual(expected);
    expect(await page.evaluate(() => getComputedStyle(document.activeElement).outlineStyle)).toBe('solid');
  }
});

test('both hero themes load eagerly and every image area opens once', async ({ page }) => {
  await page.setViewportSize({ width:390, height:844 });
  await page.emulateMedia({ colorScheme:'light' });
  const images = [];
  page.on('request', request => { if (/(light|dark)-full\.webp/.test(request.url())) images.push(request.url()); });
  await page.goto('./');
  await expect(page.locator('.hero-screen.theme-light img')).toBeVisible();
  await page.waitForFunction(() => document.querySelector('.hero-screen.theme-light img').naturalWidth > 0);
  expect(images.filter(url => /light-full\.webp/.test(url))).toHaveLength(1);
  expect(images.filter(url => /dark-full\.webp/.test(url))).toHaveLength(1);
  await page.getByRole('button', {name:'Switch to dark mode'}).click();
  await page.waitForFunction(() => document.querySelector('.hero-screen.theme-dark img').naturalWidth > 0);
  expect(images.filter(url => /dark-full\.webp/.test(url))).toHaveLength(1);
  for (const selector of ['.hero-screen.theme-dark', '.step-card a.image-open:visible', '#screenshots a.image-open']) {
    for (const opener of await page.locator(selector).all()) {
      await opener.click();
      await expect(page.getByRole('dialog')).toBeVisible();
      await page.waitForFunction(() => document.getElementById('viewer-image').naturalWidth > 0);
      expect(await page.locator('#viewer-image').getAttribute('src')).toBe(new URL(await opener.getAttribute('href'), page.url()).href);
      await page.getByRole('button', { name: 'Actual size' }).click();
      await expect(page.locator('#image-zoom')).toHaveAttribute('aria-pressed', 'true');
      await page.keyboard.press('Escape');
      await expect(opener).toBeFocused();
    }
  }
});

test('implicit System, live preference, Moon/Sun, manual persistence and early bootstrap', async ({ page }) => {
  await page.emulateMedia({ colorScheme: 'light' });
  await page.goto('./');
  await expect(page.getByRole('button', { name: 'Switch to dark mode' })).toBeVisible();
  await expect(page.locator('.theme-toggle .theme-light')).toBeVisible();
  await page.emulateMedia({ colorScheme: 'dark' });
  await expect(page.getByRole('button', { name: 'Switch to light mode' })).toBeVisible();
  await expect(page.locator('.theme-toggle .theme-dark')).toBeVisible();
  await page.getByRole('button', { name: 'Switch to light mode' }).click();
  expect(await page.evaluate(() => localStorage.getItem('extsieve-theme'))).toBe('light');
  await page.reload();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');
  await page.emulateMedia({ colorScheme: 'light' });
  await page.emulateMedia({ colorScheme: 'dark' });
  await expect(page.locator('html')).toHaveAttribute('data-effective-theme', 'light');
  await page.getByRole('button', { name: 'Switch to dark mode' }).click();
  await page.reload();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
});

test('keyboard image viewer: native focus trap, Escape, scroll lock, restoration and axe', async ({ page }) => {
  await page.goto('./');
  const opener = page.locator('#screenshots a.image-open').first();
  await opener.focus();
  await page.keyboard.press('Enter');
  const dialog = page.getByRole('dialog');
  await expect(dialog).toBeVisible();
  await expect(page.getByRole('button', { name: 'Close image' })).toBeFocused();
  expect(await page.evaluate(() => getComputedStyle(document.body).overflow)).toBe('hidden');
  expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
  await page.keyboard.press('Shift+Tab');
  await expect(page.locator('#image-original')).toBeFocused();
  await page.keyboard.press('Tab');
  await expect(page.getByRole('button', { name: 'Close image' })).toBeFocused();
  await page.getByRole('button', { name: 'Actual size' }).click();
  await expect(page.locator('#image-zoom')).toHaveAttribute('aria-pressed', 'true');
  await page.keyboard.press('Escape');
  await expect(dialog).not.toBeVisible();
  await expect(opener).toBeFocused();
  await expect.poll(() => page.evaluate(() => getComputedStyle(document.body).overflow)).not.toBe('hidden');
});

test('mobile menu: keyboard, Escape, anchor close and resize focus', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('./');
  const toggle = page.getByRole('button', { name: 'Menu' });
  await toggle.focus();
  await page.keyboard.press('Enter');
  await expect(toggle).toHaveAttribute('aria-expanded', 'true');
  await page.keyboard.press('Tab');
  await expect(page.locator('#main-navigation a').first()).toBeFocused();
  await page.keyboard.press('Escape');
  await expect(toggle).toBeFocused();
  await expect(toggle).toHaveAttribute('aria-expanded', 'false');
  await toggle.click();
  await page.locator('#main-navigation a[href="#download"]').click();
  await expect(toggle).toHaveAttribute('aria-expanded', 'false');
  await expect(page.locator('#download')).toBeFocused();
  await toggle.focus();
  await page.setViewportSize({ width: 1280, height: 900 });
  await expect(page.locator('#main-navigation a').first()).toBeFocused();
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(toggle).toBeFocused();
});

test('reduced motion and keyboard skip focus remain visible', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.goto('./');
  await page.keyboard.press('Tab');
  await expect(page.getByRole('link', { name: 'Skip to content' })).toBeFocused();
  expect(await page.getByRole('link', { name: 'Skip to content' }).evaluate(element => getComputedStyle(element).outlineStyle)).toBe('solid');
  await page.keyboard.press('Enter');
  await expect(page.locator('#main-content')).toBeFocused();
  expect(await page.locator('.theme-toggle').evaluate(element => getComputedStyle(element).transitionDuration)).toBe('0s');
});

test('JavaScript-disabled System fallback and genuine image-file access', async ({ browser }) => {
  const context = await browser.newContext({ javaScriptEnabled: false, colorScheme: 'dark', viewport: { width: 390, height: 844 } });
  const page = await context.newPage();
  await page.goto('http://127.0.0.1:4335/ExtSieve/');
  await expect(page.locator('.hero-screen.theme-dark')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Menu' })).not.toBeVisible();
  await expect(page.locator('#main-navigation')).toBeVisible();
  expect(await page.locator('#screenshots a.image-open').first().getAttribute('href')).toMatch(/^\/ExtSieve\//);
  await context.close();
});

test('200% and 400% equivalent layout widths preserve content and avoid horizontal scrolling', async ({ page }) => {
  // CSS viewport reduction checks reflow; physical browser zoom remains a manual gate.
  for (const width of [640, 320]) {
    await page.setViewportSize({ width, height: 720 });
    await page.goto('./');
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    for (const selector of ['#hero-title', '#features', '#screenshots', '#download']) await expect(page.locator(selector)).toBeVisible();
  }
});

test('blocked storage keeps System and the manual action usable', async ({ page }) => {
  await page.addInitScript(() => { Storage.prototype.getItem = () => { throw new Error('Synthetic storage denial'); }; Storage.prototype.setItem = () => { throw new Error('Synthetic storage denial'); }; });
  await page.emulateMedia({ colorScheme: 'dark' });
  await page.goto('./');
  await page.getByRole('button', { name: 'Switch to light mode' }).click();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');
});

test('CSP actually rejects an unhashed inline script and style attribute', async ({ page }) => {
  await page.addInitScript(() => {
    window.expectedCspViolations = [];
    document.addEventListener('securitypolicyviolation', event => window.expectedCspViolations.push(event.effectiveDirective));
  });
  await page.goto('./');
  await page.evaluate(() => {
    const script = document.createElement('script');
    script.textContent = 'window.unapprovedInlineScriptRan = true';
    document.body.append(script);
    document.getElementById('hero-title').setAttribute('style', 'color: rgb(255, 0, 0)');
  });
  await expect.poll(() => page.evaluate(() => window.expectedCspViolations)).toContain('script-src-elem');
  await expect.poll(() => page.evaluate(() => window.expectedCspViolations)).toContain('style-src-attr');
  expect(await page.evaluate(() => window.unapprovedInlineScriptRan)).toBeUndefined();
  expect(await page.locator('#hero-title').evaluate(element => getComputedStyle(element).color)).not.toBe('rgb(255, 0, 0)');
});

test('touch image opening, actual size and close restore the visible trigger', async ({ browser }) => {
  const context = await browser.newContext({ hasTouch: true, viewport: { width: 390, height: 844 } });
  const page = await context.newPage();
  await page.goto('http://127.0.0.1:4335/ExtSieve/');
  const opener = page.locator('#screenshots a.image-open').first();
  await opener.tap();
  await expect(page.getByRole('dialog')).toBeVisible();
  await page.getByRole('button', { name: 'Actual size' }).tap();
  await expect(page.locator('#image-zoom')).toHaveAttribute('aria-pressed', 'true');
  await page.getByRole('button', { name: 'Close image' }).tap();
  await expect(opener).toBeFocused();
  await expect.poll(() => page.evaluate(() => getComputedStyle(document.body).overflow)).not.toBe('hidden');
  await context.close();
});

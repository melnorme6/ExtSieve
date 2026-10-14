import { test, expect } from '@playwright/test';

async function expectStepTheme(page, theme) {
  for (const card of await page.locator('.step-card').all()) {
    const active = card.locator(`.step-images.theme-${theme}`);
    await expect(active).toBeVisible();
    await expect(card.locator(`.step-images.theme-${theme === 'light' ? 'dark' : 'light'}`)).not.toBeVisible();
    await expect(card.locator('.image-expand:visible')).toHaveCount(1);
    for (const image of await active.locator('img:visible').all()) {
      await expect.poll(() => image.evaluate(image => image.complete && image.naturalWidth > 0)).toBe(true);
      expect((await image.evaluate(image => image.currentSrc)).endsWith('-dark.webp')).toBe(theme === 'dark');
    }
    expect((await active.getAttribute('href')).endsWith('-dark.png')).toBe(theme === 'dark');
  }
}

for (const width of [390, 1440]) {
  for (const theme of ['light', 'dark']) {
    test(`${width}px ${theme}: three step previews and expand controls open the matching image`, async ({ browser }) => {
      const context = await browser.newContext({ viewport: { width, height: 900 }, colorScheme: theme, hasTouch: width === 390 });
      const page = await context.newPage();
      await page.goto('http://127.0.0.1:4335/ExtSieve/');
      await page.locator('#how-it-works').scrollIntoViewIfNeeded();
      await expectStepTheme(page, theme);
      for (const card of await page.locator('.step-card').all()) {
        const opener = card.locator(`.step-images.theme-${theme}`);
        await opener.scrollIntoViewIfNeeded();
        const expand = await opener.locator('.image-expand').boundingBox();
        const point = [expand.x + expand.width / 2, expand.y + expand.height / 2];
        if (width === 390) await page.touchscreen.tap(...point);
        else await page.mouse.click(...point);
        await expect(page.getByRole('dialog')).toBeVisible();
        const expected = new URL(await opener.getAttribute('href'), page.url()).href;
        await expect(page.locator('#viewer-image')).toHaveAttribute('src', expected);
        await expect(page.locator('#image-original')).toHaveAttribute('href', expected);
        await expect(page.locator('#viewer-image')).toHaveAttribute('alt', await opener.getAttribute('data-image-alt'));
        await expect.poll(() => page.locator('#viewer-image').evaluate(image => image.complete && image.naturalWidth > 0)).toBe(true);
        await page.getByRole('button', { name: 'Actual size' }).click();
        await expect(page.locator('#image-zoom')).toHaveAttribute('aria-pressed', 'true');
        await page.getByRole('button', { name: 'Close image' }).click();
        await expect(opener).toBeFocused();
      }
      await page.getByRole('button', { name: `Switch to ${theme === 'light' ? 'dark' : 'light'} mode` }).click();
      await expectStepTheme(page, theme === 'light' ? 'dark' : 'light');
      await context.close();
    });
  }
}

test('live System changes keep every open step modal and restored focus in the current theme', async ({ page }) => {
  await page.goto('./');
  await page.locator('#how-it-works').scrollIntoViewIfNeeded();
  for (const card of await page.locator('.step-card').all()) {
    for (const initial of ['light', 'dark']) {
      const next = initial === 'light' ? 'dark' : 'light';
      await page.emulateMedia({ colorScheme: initial });
      await card.locator(`.step-images.theme-${initial}`).click();
      await expect(page.getByRole('dialog')).toBeVisible();
      await page.emulateMedia({ colorScheme: next });
      const matching = card.locator(`.step-images.theme-${next}`);
      const expected = new URL(await matching.getAttribute('href'), page.url()).href;
      await expect(page.locator('#viewer-image')).toHaveAttribute('src', expected);
      await expect(page.locator('#image-original')).toHaveAttribute('href', expected);
      await expect.poll(() => page.locator('#viewer-image').evaluate(image => image.complete && image.naturalWidth > 0)).toBe(true);
      await page.keyboard.press('Escape');
      await expect(matching).toBeFocused();
    }
  }
});

test('JavaScript-disabled dark step previews link directly to dark image files', async ({ browser }) => {
  const context = await browser.newContext({ javaScriptEnabled: false, colorScheme: 'dark', viewport: { width: 390, height: 900 } });
  const page = await context.newPage();
  await page.goto('http://127.0.0.1:4335/ExtSieve/');
  await page.locator('#how-it-works').scrollIntoViewIfNeeded();
  await expectStepTheme(page, 'dark');
  const source = await page.locator('.step-images.theme-dark').first().getAttribute('href');
  expect((await page.request.get(source)).status()).toBe(200);
  await context.close();
});

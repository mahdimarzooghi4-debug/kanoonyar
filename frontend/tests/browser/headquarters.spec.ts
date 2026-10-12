import { test, expect } from '@playwright/test';

const titles: Record<string, string> = {
  headquarters: 'پنل ستاد',
  center: 'پنل کانون',
  audience: 'پرتال مخاطبان',
};

test('selected experience renders its Persian RTL React shell', async ({ page }) => {
  const experience = process.env.KANOONYAR_BROWSER_EXPERIENCE ?? 'headquarters';
  await page.goto('/');
  await expect(page.locator('html')).toHaveAttribute('lang', 'fa');
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
  await expect(page.getByRole('heading', { name: titles[experience] })).toBeVisible();
});

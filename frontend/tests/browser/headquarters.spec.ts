import { test, expect } from '@playwright/test';

test('headquarters loads Persian RTL React shell', async ({ page }) => {
  await page.goto('/');
  await expect(page.locator('html')).toHaveAttribute('lang', 'fa');
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
  await expect(page.getByRole('heading', { name: 'پنل ستاد' })).toBeVisible();
});

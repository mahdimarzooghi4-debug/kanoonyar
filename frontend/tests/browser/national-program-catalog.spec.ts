import { test, expect } from '@playwright/test';

test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE !== 'headquarters', 'Headquarters-only catalog');

test('published national programs render goals and mandatory requirements', async ({ page }) => {
  await page.route('**/api/v1/programs/published', route => route.fulfill({
    status: 200, contentType: 'application/json', body: JSON.stringify({ programs: [{
      programId: 'national-1', version: 2, title: 'برنامه ملی نمونه',
      nationalObjectives: ['هدف ثابت'], mandatoryRequirements: ['الزام ثابت']
    }] })
  }));
  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'برنامه ملی نمونه — نسخه 2' })).toBeVisible();
  await expect(page.getByText('هدف ثابت')).toBeVisible();
  await expect(page.getByText('الزام ثابت')).toBeVisible();
});

test('catalog handles no published programs', async ({ page }) => {
  await page.route('**/api/v1/programs/published', route => route.fulfill({
    status: 200, contentType: 'application/json', body: '{"programs":[]}'
  }));
  await page.goto('/');
  await expect(page.getByText('برنامه منتشرشده‌ای وجود ندارد.')).toBeVisible();
});

test('catalog rejects invalid API responses without displaying untrusted entries', async ({ page }) => {
  await page.route('**/api/v1/programs/published', route => route.fulfill({
    status: 200, contentType: 'application/json', body: '{"programs":[{"title":"ناقص"}]}'
  }));
  await page.goto('/');
  await expect(page.getByRole('alert')).toContainText('فهرست برنامه‌های ملی در دسترس نیست.');
  await expect(page.getByText('ناقص')).toHaveCount(0);
});

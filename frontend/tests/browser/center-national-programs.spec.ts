import { test, expect } from '@playwright/test';

test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE !== 'center', 'Center-only national program discovery');

test('center can inspect national objectives and mandatory requirements without adoption commands', async ({ page }) => {
  await page.route('**/api/v1/programs/published', route => route.fulfill({ status: 200, contentType: 'application/json',
    body: JSON.stringify({ programs: [{ programId: 'national-1', version: 1, title: 'برنامه ملی آزمایشی',
      nationalObjectives: ['هدف ملی'], mandatoryRequirements: ['الزام اجباری'] }] }) }));
  await page.goto('/');
  await page.getByRole('button', { name: 'نمایش برنامه‌های ملی' }).click();
  await expect(page.getByRole('heading', { name: 'برنامه ملی آزمایشی — نسخه 1' })).toBeVisible();
  await expect(page.getByText('هدف ملی', { exact: true })).toBeVisible();
  await expect(page.getByText('الزام اجباری', { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: /ثبت برنامه اجرایی/ })).toHaveCount(0);
});

test('center shows safe error and clears catalog after failed refresh', async ({ page }) => {
  let failed = false;
  await page.route('**/api/v1/programs/published', route => route.fulfill({ status: failed ? 503 : 200,
    contentType: 'application/json', body: failed ? '{}' : '{"programs":[{"programId":"p","version":1,"title":"برنامه اول","nationalObjectives":[],"mandatoryRequirements":[]}]}' }));
  await page.goto('/');
  await page.getByRole('button', { name: 'نمایش برنامه‌های ملی' }).click();
  await expect(page.getByText('برنامه اول')).toBeVisible();
  failed = true;
  await page.getByRole('button', { name: 'نمایش برنامه‌های ملی' }).click();
  await expect(page.getByRole('alert')).toContainText('دریافت برنامه‌های ملی ممکن نشد.');
  await expect(page.getByText('برنامه اول')).toHaveCount(0);
});

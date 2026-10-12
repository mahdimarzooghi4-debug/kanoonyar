import { test, expect } from '@playwright/test';

test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE !== 'center', 'Center-only local planning');

test('local plan draft keeps national constraints read-only and does not submit', async ({ page }) => {
  let writes = 0;
  await page.route('**/api/v1/**', async route => {
    if (route.request().method() !== 'GET') writes++;
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ programs: [{
      programId: 'p1', version: 3, title: 'برنامه ملی', nationalObjectives: ['هدف ثابت'], mandatoryRequirements: ['الزام ثابت']
    }] }) });
  });
  await page.goto('/');
  await page.getByRole('button', { name: 'نمایش برنامه‌های ملی' }).click();
  await expect(page.getByText('هدف ثابت')).toBeVisible();
  await expect(page.getByText('الزام ثابت')).toBeVisible();
  await page.getByRole('textbox', { name: 'عنوان محلی p1' }).fill('برنامه محلی کانون');
  await page.getByRole('textbox', { name: 'فعالیت محلی p1' }).fill('کارگاه محلی');
  await page.getByRole('button', { name: 'افزودن فعالیت' }).click();
  await expect(page.getByText('کارگاه محلی')).toBeVisible();
  await expect(page.getByRole('button', { name: 'ثبت برنامه اجرایی (نیازمند مجوز و اتصال امن)' })).toBeDisabled();
  expect(writes).toBe(0);
});

test('local draft activities can be removed without changing national constraints', async ({ page }) => {
  await page.route('**/api/v1/programs/published', route => route.fulfill({ status: 200, contentType: 'application/json',
    body: '{"programs":[{"programId":"p1","version":1,"title":"ملی","nationalObjectives":[],"mandatoryRequirements":["الزام"]}]}' }));
  await page.goto('/');
  await page.getByRole('button', { name: 'نمایش برنامه‌های ملی' }).click();
  await page.getByRole('textbox', { name: 'فعالیت محلی p1' }).fill('فعالیت آزمایشی');
  await page.getByRole('button', { name: 'افزودن فعالیت' }).click();
  await page.getByRole('button', { name: 'حذف فعالیت 1' }).click();
  await expect(page.getByText('فعالیت آزمایشی')).toHaveCount(0);
  await expect(page.getByText('الزام', { exact: true })).toBeVisible();
});

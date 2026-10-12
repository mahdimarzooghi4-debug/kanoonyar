import { test, expect } from '@playwright/test';

test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE !== 'center', 'Center-only drafts');

test('center member can read latest stored drafts without write operations', async ({ page }) => {
  let writes = 0;
  await page.route('**/api/v1/identity/center-memberships', route => route.fulfill({ status: 200,
    contentType: 'application/json', body: '{"memberships":[{"centerId":"c1","displayName":"کانون یک","roleCode":"member"}]}' }));
  await page.route('**/api/v1/centers/*/local-plan-drafts', route => {
    if (route.request().method() !== 'GET') writes++;
    return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ drafts: [{
      draftId: 'draft-1', revision: 2, programId: 'national-1', nationalVersion: 3,
      localTitle: 'طرح محلی ثبت‌شده', localActivities: ['کارگاه'] }] }) });
  });
  await page.goto('/');
  await page.getByRole('textbox', { name: 'توکن دسترسی' }).fill('test-token');
  await page.getByRole('button', { name: 'دریافت عضویت‌ها' }).click();
  await page.getByRole('combobox', { name: 'انتخاب کانون برای پیش‌نویس‌ها' }).selectOption('c1');
  await page.getByRole('button', { name: 'دریافت پیش‌نویس‌های ثبت‌شده' }).click();
  await expect(page.getByText('طرح محلی ثبت‌شده')).toBeVisible();
  await expect(page.getByText('کارگاه', { exact: true })).toBeVisible();
  expect(writes).toBe(0);
});

test('stored draft viewer rejects malformed response', async ({ page }) => {
  await page.route('**/api/v1/identity/center-memberships', route => route.fulfill({ status: 200,
    contentType: 'application/json', body: '{"memberships":[{"centerId":"c1","displayName":"کانون یک","roleCode":"member"}]}' }));
  await page.route('**/api/v1/centers/*/local-plan-drafts', route => route.fulfill({ status: 200,
    contentType: 'application/json', body: '{"drafts":[{"localTitle":"ناقص"}]}' }));
  await page.goto('/');
  await page.getByRole('textbox', { name: 'توکن دسترسی' }).fill('test-token');
  await page.getByRole('button', { name: 'دریافت عضویت‌ها' }).click();
  await page.getByRole('combobox', { name: 'انتخاب کانون برای پیش‌نویس‌ها' }).selectOption('c1');
  await page.getByRole('button', { name: 'دریافت پیش‌نویس‌های ثبت‌شده' }).click();
  await expect(page.getByRole('alert')).toContainText('دریافت پیش‌نویس‌ها ممکن نشد.');
  await expect(page.getByText('ناقص')).toHaveCount(0);
});


test('membership alone cannot display center drafts when read grant is denied', async ({ page }) => {
  await page.route('**/api/v1/identity/center-memberships', route => route.fulfill({ status: 200,
    contentType: 'application/json',
    body: '{"memberships":[{"centerId":"c1","displayName":"کانون یک","roleCode":"member"}]}' }));
  await page.route('**/api/v1/centers/*/local-plan-drafts', route => route.fulfill({
    status: 403, contentType: 'application/problem+json',
    body: '{"title":"Local draft read permission not granted"}'
  }));
  await page.goto('/');
  await page.getByRole('textbox', { name: 'توکن دسترسی' }).fill('test-token');
  await page.getByRole('button', { name: 'دریافت عضویت‌ها' }).click();
  await page.getByRole('combobox', { name: 'انتخاب کانون برای پیش‌نویس‌ها' }).selectOption('c1');
  await page.getByRole('button', { name: 'دریافت پیش‌نویس‌های ثبت‌شده' }).click();
  await expect(page.getByRole('alert')).toContainText('مجوز مستقل مشاهده');
  await expect(page.getByRole('button', { name: 'ویرایش پیش‌نویس' })).toHaveCount(0);
});

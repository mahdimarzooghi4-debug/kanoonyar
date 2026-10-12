import { test, expect } from '@playwright/test';

test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE !== 'center', 'Center-only browser contract');

test('center workspace renders active memberships returned by the API', async ({ page }) => {
  await page.route('**/api/v1/identity/center-memberships', async route => {
    expect(route.request().headers()['authorization']).toBe('Bearer test-token');
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ memberships: [
      { centerId: 'center-01', displayName: 'کانون نمونه', roleCode: 'uninterpreted-role' }
    ] }) });
  });
  await page.goto('/');
  await page.getByRole('textbox', { name: 'توکن دسترسی' }).fill('test-token');
  await page.getByRole('button', { name: 'دریافت عضویت‌ها' }).click();
  await expect(page.getByText(/کانون نمونه/)).toBeVisible();
});

test('center workspace handles empty memberships and rejects malformed responses', async ({ page }) => {
  await page.route('**/api/v1/identity/center-memberships', route =>
    route.fulfill({ status: 200, contentType: 'application/json', body: '{"memberships":[]}' }));
  await page.goto('/');
  await page.getByRole('textbox', { name: 'توکن دسترسی' }).fill('test-token');
  await page.getByRole('button', { name: 'دریافت عضویت‌ها' }).click();
  await expect(page.getByText('عضویت فعالی ثبت نشده است.')).toBeVisible();
  await page.unroute('**/api/v1/identity/center-memberships');
  await page.route('**/api/v1/identity/center-memberships', route =>
    route.fulfill({ status: 200, contentType: 'application/json', body: '{"memberships":"invalid"}' }));
  await page.getByRole('button', { name: 'دریافت عضویت‌ها' }).click();
  await expect(page.getByRole('alert')).toContainText('ارتباط با سرویس برقرار نشد.');
});

test('center workspace does not show previous membership data after unauthorized response', async ({ page }) => {
  let allowed = true;
  await page.route('**/api/v1/identity/center-memberships', route =>
    route.fulfill({ status: allowed ? 200 : 401, contentType: 'application/json',
      body: allowed ? '{"memberships":[{"centerId":"center-1","displayName":"مرکز اول","roleCode":"role"}]}' : '{}' }));
  await page.goto('/');
  await page.getByRole('textbox', { name: 'توکن دسترسی' }).fill('test-token');
  await page.getByRole('button', { name: 'دریافت عضویت‌ها' }).click();
  await expect(page.getByText(/مرکز اول/)).toBeVisible();
  allowed = false;
  await page.getByRole('button', { name: 'دریافت عضویت‌ها' }).click();
  await expect(page.getByRole('alert')).toContainText('نشست معتبر نیست.');
  await expect(page.getByText(/مرکز اول/)).toHaveCount(0);
});

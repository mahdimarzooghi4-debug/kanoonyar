import { test, expect } from '@playwright/test';

test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE !== 'center', 'Center-only history viewer');

const draftId = '11111111-1111-4111-8111-111111111111';

async function openHistory(page: import('@playwright/test').Page) {
  await page.route('**/api/v1/identity/center-memberships', route => route.fulfill({
    status: 200, contentType: 'application/json',
    body: '{"memberships":[{"centerId":"c1","displayName":"کانون یک","roleCode":"member"},{"centerId":"c2","displayName":"کانون دو","roleCode":"member"}]}'
  }));
  await page.route('**/api/v1/centers/*/local-plan-drafts', route => route.fulfill({
    status: 200, contentType: 'application/json',
    body: JSON.stringify({ drafts: [{ draftId, revision: 22,
      programId: 'national-1', nationalVersion: 1,
      localTitle: 'پیش‌نویس', localActivities: ['کارگاه'] }] })
  }));
  await page.goto('/');
  await page.getByRole('textbox', { name: 'توکن دسترسی' }).fill('token');
  await page.getByRole('button', { name: 'دریافت عضویت‌ها' }).click();
  await page.getByRole('combobox', { name: 'انتخاب کانون برای پیش‌نویس‌ها' }).selectOption('c1');
  await page.getByRole('button', { name: 'دریافت پیش‌نویس‌های ثبت‌شده' }).click();
  await page.getByRole('button', { name: 'تاریخچه نسخه‌ها' }).click();
}

test('history is read-only, loads older revisions with exclusive cursor and no write call', async ({ page }) => {
  const urls: string[] = [];
  await page.route('**/api/v1/centers/*/local-plan-drafts/*/revisions*', route => {
    urls.push(route.request().url());
    expect(route.request().method()).toBe('GET');
    const older = route.request().url().includes('beforeRevision=3');
    const numbers = older ? [2, 1] : Array.from({ length: 20 }, (_, i) => 22 - i);
    return route.fulfill({ status: 200, contentType: 'application/json',
      body: JSON.stringify({ draftId,
        revisions: numbers.map(n => ({
          revision: n, programId: 'national-1', nationalVersion: 1,
          localTitle: 'نسخه ذخیره‌شده', localActivities: ['فعالیت ' + n],
          recordedAt: '2026-10-11T12:00:00Z'
        })),
        nextBeforeRevision: older ? null : 3
      })
    });
  });
  await openHistory(page);
  await page.getByRole('button', { name: 'دریافت تاریخچه' }).click();
  await expect(page.getByText('نسخه 22 — نسخه ذخیره‌شده')).toBeVisible();
  await expect(page.getByText('نسخه 3 — نسخه ذخیره‌شده')).toBeVisible();
  await page.getByRole('button', { name: 'نسخه‌های قدیمی‌تر' }).click();
  await expect(page.getByText('نسخه 1 — نسخه ذخیره‌شده')).toBeVisible();
  expect(urls).toHaveLength(2);
  expect(urls[0]).toContain('/centers/c1/');
  expect(urls[1]).toContain('beforeRevision=3');
  await expect(page.getByRole('button', { name: 'نسخه‌های قدیمی‌تر' })).toHaveCount(0);
});

test('independent history-read denial reveals no revisions', async ({ page }) => {
  await page.route('**/api/v1/centers/*/local-plan-drafts/*/revisions*', route =>
    route.fulfill({ status: 403, contentType: 'application/problem+json', body: '{}' }));
  await openHistory(page);
  await page.getByRole('button', { name: 'دریافت تاریخچه' }).click();
  await expect(page.getByRole('alert')).toContainText('مجوز مستقل مشاهده تاریخچه');
  await expect(page.getByText('نسخه 22 — نسخه ذخیره‌شده')).toHaveCount(0);
});

test('invalid history payload is rejected and changing center clears history', async ({ page }) => {
  await page.route('**/api/v1/centers/*/local-plan-drafts/*/revisions*', route =>
    route.fulfill({ status: 200, contentType: 'application/json',
      body: JSON.stringify({ draftId, revisions: [{ revision: 1 }], nextBeforeRevision: null }) }));
  await openHistory(page);
  await page.getByRole('button', { name: 'دریافت تاریخچه' }).click();
  await expect(page.getByRole('alert')).toContainText('تاریخچه قابل دریافت نیست');
  await page.getByRole('combobox', { name: 'انتخاب کانون برای پیش‌نویس‌ها' }).selectOption('c2');
  await expect(page.getByRole('region', { name: 'تاریخچه پیش‌نویس' })).toHaveCount(0);
});

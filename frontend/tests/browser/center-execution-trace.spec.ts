import { test, expect } from '@playwright/test';

test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE !== 'center', 'Center-only execution trace');

const draft = '11111111-1111-4111-8111-111111111111';
async function open(page: import('@playwright/test').Page) {
  await page.route('**/api/v1/identity/center-memberships', route => route.fulfill({
    status: 200, contentType: 'application/json',
    body: '{"memberships":[{"centerId":"c1","displayName":"کانون یک","roleCode":"member"},{"centerId":"c2","displayName":"کانون دو","roleCode":"member"}]}'
  }));
  await page.route('**/api/v1/centers/*/local-plan-drafts', route => route.fulfill({
    status: 200, contentType: 'application/json',
    body: JSON.stringify({ drafts: [{
      draftId: draft, revision: 2, programId: 'national', nationalVersion: 1,
      localTitle: 'طرح محلی', localActivities: ['کارگاه'] }] })
  }));
  await page.goto('/');
  await page.getByRole('textbox', { name: 'توکن دسترسی' }).fill('test-token');
  await page.getByRole('button', { name: 'دریافت عضویت‌ها' }).click();
  await page.getByRole('combobox', { name: 'انتخاب کانون برای پیش‌نویس‌ها' }).selectOption('c1');
  await page.getByRole('button', { name: 'دریافت پیش‌نویس‌های ثبت‌شده' }).click();
  await page.getByRole('button', { name: 'پیوند ابلاغ ملی' }).click();
}

test('shows version pinned national issuance and planned activities without a mutation', async ({ page }) => {
  let requests = 0;
  await page.route('**/api/v1/centers/*/local-plan-drafts/*/execution-trace', route => {
    requests++;
    expect(route.request().method()).toBe('GET');
    expect(route.request().url()).toContain('/centers/c1/');
    return route.fulfill({ status: 200, contentType: 'application/json',
      body: JSON.stringify({ draftId: draft, trace: {
        linkId: 'link-a', issuanceId: 'issued-a', programId: 'national',
        nationalVersion: 1, pinnedRevision: 1, latestRevision: 2,
        localTitle: 'طرح محلی', plannedActivities: ['کارگاه'] } })
    });
  });
  await open(page);
  await page.getByRole('button', { name: 'دریافت پیوند برنامه' }).click();
  await expect(page.getByText('نسخه اولیه متصل: 1 — آخرین نسخه طرح: 2')).toBeVisible();
  await expect(page.getByText('فعالیت‌های برنامه‌ریزی‌شده، نه اجرای قطعی')).toBeVisible();
  expect(requests).toBe(1);
  await page.getByRole('combobox', { name: 'انتخاب کانون برای پیش‌نویس‌ها' }).selectOption('c2');
  await expect(page.getByRole('region', { name: 'پیوند برنامه ملی و طرح محلی' })).toHaveCount(0);
});

test('denied or malformed trace never reveals local data', async ({ page }) => {
  await page.route('**/api/v1/centers/*/local-plan-drafts/*/execution-trace', route =>
    route.fulfill({ status: 403, contentType: 'application/problem+json', body: '{}' }));
  await open(page);
  await page.getByRole('button', { name: 'دریافت پیوند برنامه' }).click();
  await expect(page.getByRole('alert')).toContainText('مجوز مشاهده پیوند برنامه تأیید نشده');
  await expect(page.getByText('نسخه اولیه متصل:')).toHaveCount(0);
});

test('inconsistent server lineage is rejected', async ({ page }) => {
  await page.route('**/api/v1/centers/*/local-plan-drafts/*/execution-trace', route =>
    route.fulfill({ status: 200, contentType: 'application/json',
      body: JSON.stringify({ draftId: draft, trace: {
        linkId: 'link-b', issuanceId: 'issue-b', programId: 'national',
        nationalVersion: 1, pinnedRevision: 3, latestRevision: 2,
        localTitle: 'Bad', plannedActivities: [] } }) }));
  await open(page);
  await page.getByRole('button', { name: 'دریافت پیوند برنامه' }).click();
  await expect(page.getByRole('alert')).toContainText('خواندن پیوند برنامه ممکن نشد');
  await expect(page.getByText('Bad')).toHaveCount(0);
});

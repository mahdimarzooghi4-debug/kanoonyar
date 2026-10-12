import { test, expect } from '@playwright/test';

test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE !== 'center', 'Center-only reports');

const draft = '11111111-1111-4111-8111-111111111111';
async function start(page: import('@playwright/test').Page) {
  await page.route('**/api/v1/identity/center-memberships', route => route.fulfill({
    status: 200, contentType: 'application/json',
    body: '{"memberships":[{"centerId":"c1","displayName":"کانون یک","roleCode":"member"},{"centerId":"c2","displayName":"کانون دو","roleCode":"member"}]}'
  }));
  await page.route('**/api/v1/centers/*/local-plan-drafts', route => route.fulfill({
    status: 200, contentType: 'application/json', body: JSON.stringify({ drafts: [{
      draftId: draft, revision: 1, programId: 'national', nationalVersion: 1,
      localTitle: 'طرح محلی', localActivities: ['کارگاه'] }] })
  }));
  await page.goto('/');
  await page.getByRole('textbox', { name: 'توکن دسترسی' }).fill('test-token');
  await page.getByRole('button', { name: 'دریافت عضویت‌ها' }).click();
  await page.getByRole('combobox', { name: 'انتخاب کانون برای پیش‌نویس‌ها' }).selectOption('c1');
  await page.getByRole('button', { name: 'دریافت پیش‌نویس‌های ثبت‌شده' }).click();
  await page.getByRole('button', { name: 'گزارش‌های فعالیت' }).click();
}

test('paginates unverified reports and does not imply execution', async ({ page }) => {
  const calls: string[] = [];
  await page.route('**/api/v1/centers/*/local-plan-drafts/*/activity-reports*', route => {
    calls.push(route.request().url());
    expect(route.request().method()).toBe('GET');
    const older = route.request().url().includes('beforeSequence=3');
    const numbers = older ? [2,1] : Array.from({ length: 20 }, (_, i) => 22-i);
    return route.fulfill({ status: 200, contentType: 'application/json',
      body: JSON.stringify({ draftId: draft,
        reports: numbers.map(n => ({
          reportSequence:n, reportId:'report-'+n,linkId:'link',
          sourceKind:'class',sourceRecordRef:'source-'+n,evidenceRef:'evidence-'+n,
          reportedSummary:'Unverified '+n,claimedOccurredAt:null,
          recordedAt:'2026-10-12T06:00:00Z',verificationStatus:'UNVERIFIED'
        })),
        nextBeforeSequence:older ? null : 3 })
    });
  });
  await start(page);
  await page.getByRole('button', { name: 'دریافت گزارش‌ها' }).click();
  await expect(page.getByText('Unverified 22 — راستی‌آزمایی‌نشده')).toBeVisible();
  await expect(page.getByText('زمان ادعاشده: نامشخص').first()).toBeVisible();
  await page.getByRole('button', { name: 'گزارش‌های قدیمی‌تر' }).click();
  await expect(page.getByText('Unverified 1 — راستی‌آزمایی‌نشده')).toBeVisible();
  expect(calls).toHaveLength(2);
  expect(calls[1]).toContain('beforeSequence=3');
  await expect(page.getByRole('button', { name: 'گزارش‌های قدیمی‌تر' })).toHaveCount(0);
  await page.getByRole('combobox', { name: 'انتخاب کانون برای پیش‌نویس‌ها' }).selectOption('c2');
  await expect(page.getByRole('region', { name: 'گزارش فعالیت و ارجاع شواهد' })).toHaveCount(0);
});

test('denied reports reveal no reference or summary', async ({ page }) => {
  await page.route('**/api/v1/centers/*/local-plan-drafts/*/activity-reports*',
    route => route.fulfill({ status:403, contentType:'application/problem+json',body:'{}' }));
  await start(page);
  await page.getByRole('button', { name: 'دریافت گزارش‌ها' }).click();
  await expect(page.getByRole('alert')).toContainText('مجوز مستقل مشاهده گزارش فعالیت');
  await expect(page.getByText(/ارجاع مدرک \(تأییدنشده\):/)).toHaveCount(0);
});

test('rejects server payload that falsely asserts verification', async ({ page }) => {
  await page.route('**/api/v1/centers/*/local-plan-drafts/*/activity-reports*',
    route => route.fulfill({ status:200, contentType:'application/json',
      body: JSON.stringify({ draftId: draft, reports:[{
        reportSequence:1, reportId:'forged',linkId:'link',
        sourceKind:'class',sourceRecordRef:'source',evidenceRef:'evidence',
        reportedSummary:'False verification',claimedOccurredAt:null,
        recordedAt:'2026-10-12T06:00:00Z',verificationStatus:'VERIFIED'
      }],nextBeforeSequence:null }) }));
  await start(page);
  await page.getByRole('button', { name: 'دریافت گزارش‌ها' }).click();
  await expect(page.getByRole('alert')).toContainText('دریافت گزارش‌های فعالیت ممکن نشد');
  await expect(page.getByText('False verification')).toHaveCount(0);
});

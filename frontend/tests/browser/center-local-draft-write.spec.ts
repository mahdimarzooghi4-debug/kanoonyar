import { test, expect } from '@playwright/test';

test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE !== 'center', 'Center-only local plan write adapter');

async function start(page: import('@playwright/test').Page) {
  await page.route('**/api/v1/identity/center-memberships', route => route.fulfill({
    status: 200, contentType: 'application/json',
    body: '{"memberships":[{"centerId":"c1","displayName":"کانون یک","roleCode":"member"}]}'
  }));
  await page.route('**/api/v1/programs/published', route => route.fulfill({
    status: 200, contentType: 'application/json',
    body: '{"programs":[{"programId":"p1","version":3,"title":"برنامه ملی","nationalObjectives":["هدف"],"mandatoryRequirements":["الزام"]}]}'
  }));
  await page.goto('/');
  await page.getByRole('textbox', { name: 'توکن دسترسی' }).fill('test-token');
  await page.getByRole('button', { name: 'دریافت عضویت‌ها' }).click();
  await page.getByRole('combobox', { name: 'انتخاب کانون برای پیش‌نویس‌ها' }).selectOption('c1');
  await page.getByRole('button', { name: 'نمایش برنامه‌های ملی' }).click();
}

test('server denial never claims a stored draft even for a member', async ({ page }) => {
  let writes = 0;
  await page.route('**/api/v1/centers/*/local-plan-drafts/revisions', route => {
    writes++;
    expect(route.request().method()).toBe('POST');
    expect(route.request().url()).toContain('/centers/c1/');
    const body = route.request().postDataJSON();
    expect(body).toMatchObject({ expectedRevision: 0, programId: 'p1', nationalVersion: 3, localTitle: 'عنوان محلی' });
    expect(body).not.toHaveProperty('actorSubject');
    expect(body).not.toHaveProperty('centerId');
    return route.fulfill({ status: 403, contentType: 'application/problem+json',
      body: '{"title":"Local draft write permission not granted"}' });
  });
  await start(page);
  await page.getByRole('textbox', { name: 'عنوان محلی p1' }).fill('عنوان محلی');
  await page.getByRole('button', { name: 'ثبت پیش‌نویس نسخه‌دار' }).click();
  await expect(page.getByRole('alert')).toContainText('هیچ نسخه‌ای ثبت نشد');
  expect(writes).toBe(1);
});

test('stale stored draft revision receives explicit conflict, not a silent overwrite', async ({ page }) => {
  const draftId = '11111111-1111-4111-8111-111111111111';
  await page.route('**/api/v1/centers/*/local-plan-drafts', route => route.fulfill({
    status: 200, contentType: 'application/json',
    body: JSON.stringify({ drafts: [{ draftId, revision: 2, programId: 'p1', nationalVersion: 3,
      localTitle: 'نسخه پیشین', localActivities: ['کارگاه'] }] })
  }));
  await page.route('**/api/v1/centers/*/local-plan-drafts/revisions', route => {
    expect(route.request().postDataJSON()).toMatchObject({ draftId, expectedRevision: 2 });
    return route.fulfill({ status: 409, contentType: 'application/problem+json', body: '{"title":"Conflict"}' });
  });
  await start(page);
  await page.getByRole('button', { name: 'دریافت پیش‌نویس‌های ثبت‌شده' }).click();
  await page.getByRole('button', { name: 'ویرایش پیش‌نویس' }).click();
  await expect(page.getByRole('textbox', { name: 'عنوان محلی p1' })).toHaveValue('نسخه پیشین');
  await page.getByRole('button', { name: 'ثبت پیش‌نویس نسخه‌دار' }).click();
  await expect(page.getByRole('alert')).toContainText('تعارض ویرایش هم‌زمان');
});

test('mocked server acknowledgments advance expected revision only after valid response', async ({ page }) => {
  const expected: number[] = [];
  await page.route('**/api/v1/centers/*/local-plan-drafts/revisions', route => {
    const data = route.request().postDataJSON();
    expected.push(data.expectedRevision);
    return route.fulfill({ status: 200, contentType: 'application/json',
      body: JSON.stringify({ draftId: data.draftId, revision: data.expectedRevision + 1,
        programId: data.programId, nationalVersion: data.nationalVersion }) });
  });
  await start(page);
  await page.getByRole('textbox', { name: 'عنوان محلی p1' }).fill('طرح');
  await page.getByRole('button', { name: 'ثبت پیش‌نویس نسخه‌دار' }).click();
  await expect(page.getByRole('status').last()).toContainText('نسخه 1 توسط API ثبت شد');
  await page.getByRole('button', { name: 'ثبت پیش‌نویس نسخه‌دار' }).click();
  await expect(page.getByRole('status').last()).toContainText('نسخه 2 توسط API ثبت شد');
  expect(expected).toEqual([0, 1]);
});

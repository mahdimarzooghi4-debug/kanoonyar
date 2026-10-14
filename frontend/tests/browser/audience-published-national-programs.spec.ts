import { test, expect } from '@playwright/test';

test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE !== 'audience',
  'Public national program explorer is only available in audience experience');

const publicProgram = (id: string, version: number, title: string, goal: string, requirement: string) => ({
  programId: id, version, title, nationalObjectives: [goal], mandatoryRequirements: [requirement],
});
const published = [
  publicProgram('national-a', 2, 'برنامه رشد فرهنگی', 'هدف واقعی نسخه دوم', 'الزام نسخه دوم'),
  publicProgram('national-b', 1, 'برنامه محلی مرتبط', 'هدف نسخه ب', 'الزام نسخه ب'),
  publicProgram('national-a', 1, 'برنامه رشد فرهنگی', 'هدف تاریخی نسخه اول', 'الزام تاریخی نسخه اول'),
];

test('audience reads only published national records, searches, opens requirements and compares original versions', async ({ page }) => {
  const paths: string[] = [];
  await page.route('**/api/**', async route => {
    paths.push(route.request().url());
    if (!route.request().url().endsWith('/api/v1/programs/published'))
      return route.fulfill({status: 500, body: 'UNEXPECTED PRIVATE API CALL'});
    return route.fulfill({
      status: 200, contentType: 'application/json', body: JSON.stringify({programs: published}),
    });
  });
  await page.goto('/');
  await expect(page.getByRole('heading', {name: 'پرتال مخاطبان'})).toBeVisible();
  await expect(page.getByText('2 برنامه از 2 برنامه منتشرشده')).toBeVisible();
  const cards = page.locator('section[aria-label="فهرست برنامه‌های ملی منتشرشده"] li');
  await expect(cards).toHaveCount(2);
  await expect(cards.filter({hasText: 'برنامه رشد فرهنگی'})).toContainText('آخرین نسخه منتشرشده: 2');
  await cards.filter({hasText: 'برنامه رشد فرهنگی'})
    .getByRole('button', {name: 'مشاهده نسخه‌ها و الزامات برنامه'}).click();
  const detail = page.getByRole('region', {name: 'جزئیات برنامه ملی انتخاب‌شده'});
  await expect(detail).toContainText('هدف واقعی نسخه دوم');
  await expect(detail).toContainText('الزام نسخه دوم');
  await expect(detail).not.toContainText('هدف تاریخی نسخه اول');
  await detail.getByRole('combobox',{name:'نسخه منتشرشده پیشین برای مقایسه'}).selectOption('1');
  await expect(detail).toContainText('هدف تاریخی نسخه اول');
  await expect(detail).toContainText('الزام تاریخی نسخه اول');
  await expect(detail).toContainText('هدف واقعی نسخه دوم');

  await page.getByRole('searchbox', {name:'جست‌وجوی نام یا شناسه برنامه منتشرشده'}).fill('national-b');
  await expect(page.getByText('1 برنامه از 2 برنامه منتشرشده')).toBeVisible();
  await expect(detail).toHaveCount(0);
  await page.getByRole('searchbox', {name:'جست‌وجوی نام یا شناسه برنامه منتشرشده'}).fill('');
  await cards.filter({hasText: 'برنامه محلی مرتبط'})
    .getByRole('button', {name: 'مشاهده نسخه‌ها و الزامات برنامه'}).click();
  await expect(detail).toContainText('هدف نسخه ب');
  await expect(detail).not.toContainText('هدف تاریخی نسخه اول');
  await expect(page.getByText('خدمات ثبت‌نام، حساب شخصی و پیگیری مخاطب هنوز فعال نشده‌اند؛', {exact:false})).toBeVisible();
  expect(paths).toHaveLength(1);
  expect(paths[0]).toContain('/api/v1/programs/published');
  await expect(page.getByRole('button',{name:/ثبت‌نام|پرداخت|رزرو|پذیرش/})).toHaveCount(0);
});

test('absence of national publications is an explicit empty state, never invented center offerings', async ({ page }) => {
  await page.route('**/api/v1/programs/published', route => route.fulfill({
    status:200,contentType:'application/json',body:JSON.stringify({programs:[]}),
  }));
  await page.goto('/');
  await expect(page.getByText('هنوز برنامه ملی منتشرشده‌ای در منبع سامانه وجود ندارد.')).toBeVisible();
  await expect(page.locator('section[aria-label="فهرست برنامه‌های ملی منتشرشده"] li')).toHaveCount(0);
  await expect(page.getByRole('button',{name:/ثبت‌نام|پرداخت|رزرو/})).toHaveCount(0);
});

test('malformed duplicate or forged private fields never become public audience data', async ({page}) => {
  let payload:unknown = {programs:[{...published[0],nationalObjectives:['معتبر'],mandatoryRequirements:null}]};
  await page.route('**/api/v1/programs/published',route=>route.fulfill({
    status:200,contentType:'application/json',body:JSON.stringify(payload),
  }));
  await page.goto('/');
  await expect(page.getByRole('alert')).toContainText('اطلاعات معتبر برنامه‌های منتشرشده در دسترس نیست.');
  await expect(page.getByText('معتبر',{exact:true})).toHaveCount(0);
  payload = {programs:[published[0],published[0]]};
  await page.getByRole('button',{name:'بازخوانی نسخه‌های منتشرشده'}).click();
  await expect(page.getByRole('alert')).toBeVisible();
  payload = {programs:[{...published[0],privateCenterDraftTitle:'محتوای خصوصی نباید نمایش داده شود', 
    createdBySubject:'خصوصی',enrollmentStatus:'APPROVED'}]};
  await page.getByRole('button',{name:'بازخوانی نسخه‌های منتشرشده'}).click();
  await expect(page.getByText('هدف واقعی نسخه دوم')).toHaveCount(0);
  await page.getByRole('button',{name:'مشاهده نسخه‌ها و الزامات برنامه'}).click();
  await expect(page.getByText('هدف واقعی نسخه دوم')).toBeVisible();
  await expect(page.getByText('محتوای خصوصی نباید نمایش داده شود')).toHaveCount(0);
  await expect(page.getByText('خصوصی',{exact:true})).toHaveCount(0);
  await expect(page.getByText('APPROVED')).toHaveCount(0);
});

test('HTTP denial, failure and invalid response clear previously visible publication details', async ({ page }) => {
  let denied = false;
  await page.route('**/api/v1/programs/published', route=>route.fulfill(denied ?
    {status:503,contentType:'application/problem+json',body:'{}'}:
    {status:200,contentType:'application/json',body:JSON.stringify({programs:[published[0]]})}));
  await page.goto('/');
  await page.getByRole('button',{name:'مشاهده نسخه‌ها و الزامات برنامه'}).click();
  await expect(page.getByText('هدف واقعی نسخه دوم')).toBeVisible();
  denied = true;
  await page.getByRole('button',{name:'بازخوانی نسخه‌های منتشرشده'}).click();
  await expect(page.getByRole('alert')).toContainText('اطلاعات معتبر برنامه‌های منتشرشده در دسترس نیست.');
  await expect(page.getByText('هدف واقعی نسخه دوم')).toHaveCount(0);
  await expect(page.getByText('برنامه رشد فرهنگی')).toHaveCount(0);
});

test('outdated fetch after refetch cannot reinstate superseded publications', async ({page})=>{
  let fetches=0;
  await page.route('**/api/v1/programs/published',async route=>{
    fetches++;
    if(fetches===1){
      await new Promise(resolve=>setTimeout(resolve,350));
      try { await route.fulfill({status:200,contentType:'application/json',
        body:JSON.stringify({programs:[published[0]]})}); } catch { /* aborted */ }
    }else{
      await route.fulfill({status:200,contentType:'application/json',
        body:JSON.stringify({programs:[published[1]]})});
    }
  });
  await page.goto('/');
  await page.getByRole('button',{name:'بازخوانی نسخه‌های منتشرشده'}).click();
  await expect(page.getByText('برنامه محلی مرتبط')).toBeVisible();
  await page.waitForTimeout(450);
  await expect(page.getByText('برنامه رشد فرهنگی')).toHaveCount(0);
  await expect(page.getByText('برنامه محلی مرتبط')).toBeVisible();
});

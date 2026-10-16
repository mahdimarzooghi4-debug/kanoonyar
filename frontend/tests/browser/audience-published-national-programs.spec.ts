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
  const objectives = detail.getByRole('region', {name:'اهداف ملی'});
  const requirements = detail.getByRole('region', {name:'الزامات اجباری'});
  await expect(objectives).toContainText('عبارت موجود در نسخه پیشین و غایب از نسخه جاری: هدف تاریخی نسخه اول');
  await expect(objectives).toContainText('عبارت افزوده‌شده در نسخه جاری: هدف واقعی نسخه دوم');
  await expect(requirements).toContainText('عبارت موجود در نسخه پیشین و غایب از نسخه جاری: الزام تاریخی نسخه اول');
  await expect(requirements).toContainText('عبارت افزوده‌شده در نسخه جاری: الزام نسخه دوم');
  await expect(detail).toContainText('معنای حقوقی، اولویت اجرا یا اثر عملی را تعیین نمی‌کند');

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


test('published text comparison preserves duplicate source entries and exact text only', async ({page}) => {
  const rows = [
    {programId:'duplicate-case',version:1,title:'مقایسه دقیق',
      nationalObjectives:['هدف ثابت','هدف حذف','هدف تکراری','هدف تکراری'],
      mandatoryRequirements:['الزام ثابت']},
    {programId:'duplicate-case',version:2,title:'مقایسه دقیق',
      nationalObjectives:['هدف ثابت','هدف اضافه','هدف تکراری'],
      mandatoryRequirements:['الزام ثابت']},
  ];
  await page.route('**/api/v1/programs/published', route=>route.fulfill({
    status:200,contentType:'application/json',body:JSON.stringify({programs:rows}),
  }));
  await page.goto('/');
  await page.getByRole('button',{name:'مشاهده نسخه‌ها و الزامات برنامه'}).click();
  const detail=page.getByRole('region',{name:'جزئیات برنامه ملی انتخاب‌شده'});
  await detail.getByRole('combobox',{name:'نسخه منتشرشده پیشین برای مقایسه'}).selectOption('1');
  const objectives=detail.getByRole('region',{name:'اهداف ملی'});
  const entries=objectives.locator('li');
  await expect(entries).toHaveCount(5);
  await expect(entries.filter({hasText:'عبارت بدون تغییر: هدف ثابت'})).toHaveCount(1);
  await expect(entries.filter({hasText:'عبارت بدون تغییر: هدف تکراری'})).toHaveCount(1);
  await expect(entries.filter({hasText:'عبارت موجود در نسخه پیشین و غایب از نسخه جاری: هدف تکراری'})).toHaveCount(1);
  await expect(entries.filter({hasText:'عبارت افزوده‌شده در نسخه جاری: هدف اضافه'})).toHaveCount(1);
  await expect(entries.filter({hasText:'عبارت موجود در نسخه پیشین و غایب از نسخه جاری: هدف حذف'})).toHaveCount(1);
});


test('shareable published comparison reads and verifies both exact public versions', async ({page}) => {
  const reads: URL[] = [];
  await page.route('**/api/**', async route => {
    const url = new URL(route.request().url());
    if (url.pathname === '/api/v1/programs/published/version') {
      reads.push(url);
      const version = Number(url.searchParams.get('version'));
      const row = version === 1 ? published[2] : published[0];
      return route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(row)});
    }
    if (url.pathname === '/api/v1/programs/published')
      return route.fulfill({status:200,contentType:'application/json',body:JSON.stringify({programs:published})});
    return route.fulfill({status:404,body:'not found'});
  });
  await page.goto('/?compareProgramId=national-a&earlierVersion=1&currentVersion=2');
  const comparison = page.getByRole('region',{name:'مقایسه قابل اشتراک نسخه‌های منتشرشده'});
  await expect(comparison.getByRole('heading',{name:'مقایسه دقیق دو نسخه منتشرشده برنامه ملی'})).toBeVisible();
  await expect(comparison).toContainText('شناسه و شماره هر دو نسخه با منبع رسمی انتشار تطبیق داده شد.');
  await expect(comparison).toContainText('هدف تاریخی نسخه اول');
  await expect(comparison).toContainText('هدف واقعی نسخه دوم');
  await expect(comparison).toContainText('الزام تاریخی نسخه اول');
  await expect(comparison).toContainText('الزام نسخه دوم');
  expect(reads).toHaveLength(2);
  expect(reads.map(url => url.searchParams.get('programId')).sort()).toEqual(['national-a','national-a']);
  expect(reads.map(url => Number(url.searchParams.get('version'))).sort()).toEqual([1,2]);
});

test('comparison links reject ambiguous or malformed references without exact reads', async ({page}) => {
  let versionReads = 0;
  await page.route('**/api/**', async route => {
    const url = new URL(route.request().url());
    if (url.pathname === '/api/v1/programs/published/version') {
      versionReads++;
      return route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(published[0])});
    }
    if (url.pathname === '/api/v1/programs/published')
      return route.fulfill({status:200,contentType:'application/json',body:JSON.stringify({programs:[]})});
    return route.fulfill({status:404,body:'not found'});
  });
  for (const query of [
    'compareProgramId=national-a&earlierVersion=1&currentVersion=2&extra=1',
    'compareProgramId=national-a&compareProgramId=national-b&earlierVersion=1&currentVersion=2',
    'compareProgramId=national-a&earlierVersion=2&currentVersion=1',
    'compareProgramId=national-a&earlierVersion=01&currentVersion=2',
  ]) {
    await page.goto('/?' + query);
    await expect(page.getByRole('alert')).toContainText('پیوند مقایسه معتبر نیست');
  }
  expect(versionReads).toBe(0);
});

test('a mismatched or unavailable exact version suppresses the whole shared comparison', async ({page}) => {
  let mode: 'mismatch' | 'unavailable' = 'mismatch';
  await page.route('**/api/**', async route => {
    const url = new URL(route.request().url());
    if (url.pathname === '/api/v1/programs/published/version') {
      const version = Number(url.searchParams.get('version'));
      if (mode === 'unavailable' && version === 2)
        return route.fulfill({status:503,contentType:'application/problem+json',body:'{}'});
      const row = version === 1 ? published[2] : mode === 'mismatch'
        ? {...published[0],version:3} : published[0];
      return route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(row)});
    }
    if (url.pathname === '/api/v1/programs/published')
      return route.fulfill({status:200,contentType:'application/json',body:JSON.stringify({programs:[]})});
    return route.fulfill({status:404,body:'not found'});
  });
  const url = '/?compareProgramId=national-a&earlierVersion=1&currentVersion=2';
  await page.goto(url);
  await expect(page.getByRole('alert')).toContainText('انتشار رسمی هر دو نسخه تأیید نشد');
  await expect(page.getByText('هدف تاریخی نسخه اول')).toHaveCount(0);
  await expect(page.getByText('هدف واقعی نسخه دوم')).toHaveCount(0);
  mode = 'unavailable';
  await page.goto(url);
  await expect(page.getByRole('alert')).toContainText('انتشار رسمی هر دو نسخه تأیید نشد');
  await expect(page.getByText('هدف تاریخی نسخه اول')).toHaveCount(0);
  await expect(page.getByText('هدف واقعی نسخه دوم')).toHaveCount(0);
});

test('selected published versions produce a share link pinned to that program and version pair', async ({page}) => {
  await page.route('**/api/v1/programs/published',route=>route.fulfill({
    status:200,contentType:'application/json',body:JSON.stringify({programs:published}),
  }));
  await page.goto('/');
  const cards=page.locator('section[aria-label="فهرست برنامه‌های ملی منتشرشده"] li');
  await cards.filter({hasText:'شناسه برنامه: national-a'})
    .getByRole('button',{name:'مشاهده نسخه‌ها و الزامات برنامه'}).click();
  const detail=page.getByRole('region',{name:'جزئیات برنامه ملی انتخاب‌شده'});
  await detail.getByRole('combobox',{name:'نسخه منتشرشده پیشین برای مقایسه'}).selectOption('1');
  const link=detail.getByRole('link',{name:'پیوند قابل اشتراک مقایسه نسخه 1 و 2'});
  const href=new URL(await link.getAttribute('href')!, 'http://localhost');
  expect(href.searchParams.get('compareProgramId')).toBe('national-a');
  expect(href.searchParams.get('earlierVersion')).toBe('1');
  expect(href.searchParams.get('currentVersion')).toBe('2');
  expect([...href.searchParams.keys()].sort()).toEqual(['compareProgramId','currentVersion','earlierVersion']);
});

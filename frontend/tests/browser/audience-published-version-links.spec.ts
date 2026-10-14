import { test, expect } from '@playwright/test';

test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE !== 'audience',
  'Public source-pinned national-program permalinks belong only to Audience.');

const program=(version:number)=>({
  programId:'national-pinned',version,title:'برنامه ملی نسخه‌دار',
  nationalObjectives:['هدف رسمی نسخه '+version],
  mandatoryRequirements:['الزام منتشرشده نسخه '+version],
});

async function catalogue(page:import('@playwright/test').Page) {
  await page.route('**/api/v1/programs/published',route=>route.fulfill({
    status:200,contentType:'application/json',
    body:JSON.stringify({programs:[program(1),program(3)],nextCursor:null}),
  }));
}

test('canonical public permalink opens exact published version, never silently newer version',async({page})=>{
  const requests:string[]=[];
  await catalogue(page);
  await page.route('**/api/v1/programs/published/version?*',route=>{
    requests.push(route.request().url());
    return route.fulfill({status:200,contentType:'application/json',
      body:JSON.stringify({...program(1),privateCenterDraft:'HIDDEN',publisherSubject:'PRIVATE'})});
  });
  await page.goto('/?programId=national-pinned&version=1');
  const pinned=page.getByRole('region',{name:'ارجاع به نسخه منتشرشده'});
  await expect(pinned).toContainText('نسخه مشخص‌شده از منبع رسمی انتشار تأیید شد.');
  await expect(pinned).toContainText('هدف رسمی نسخه 1');
  await expect(pinned).not.toContainText('هدف رسمی نسخه 3');
  await expect(pinned).not.toContainText('HIDDEN');
  await expect(pinned).not.toContainText('PRIVATE');
  await expect(page.getByText('آخرین نسخه منتشرشده: 3')).toBeVisible();
  expect(requests.length).toBeGreaterThan(0);
  for(const url of requests) {
    const parsed=new URL(url);
    expect(parsed.searchParams.get('programId')).toBe('national-pinned');
    expect(parsed.searchParams.get('version')).toBe('1');
  }
});

test('existing published version UI offers native shareable direct link',async({page})=>{
  await catalogue(page);
  await page.route('**/api/v1/programs/published/version?*',route=>route.fulfill({
    status:200,contentType:'application/json',body:JSON.stringify(program(3))}));
  await page.goto('/');
  await page.getByRole('button',{name:'مشاهده نسخه‌ها و الزامات برنامه'}).click();
  const link=page.getByRole('link',{name:'پیوند مستقیم همین نسخه منتشرشده'}).first();
  await expect(link).toHaveAttribute('href',/programId=national-pinned/);
  await expect(link).toHaveAttribute('href',/version=3/);
  await link.click();
  const pinned=page.getByRole('region',{name:'ارجاع به نسخه منتشرشده'});
  await expect(pinned).toContainText('هدف رسمی نسخه 3');
});

test('unpublished or missing pinned version is never replaced by a published neighbour',async({page})=>{
  await catalogue(page);
  await page.route('**/api/v1/programs/published/version?*',route=>route.fulfill({
    status:404,contentType:'application/problem+json',body:'{}'}));
  await page.goto('/?programId=national-pinned&version=2');
  const pinned=page.getByRole('region',{name:'ارجاع به نسخه منتشرشده'});
  await expect(pinned.getByRole('alert')).toContainText('نسخه دیگری جایگزین نمی‌شود');
  await expect(pinned).not.toContainText('هدف رسمی نسخه 1');
  await expect(pinned).not.toContainText('هدف رسمی نسخه 3');
  await expect(page.getByText('آخرین نسخه منتشرشده: 3')).toBeVisible();
});

test('forged 200 from wrong exact version fails closed',async({page})=>{
  await catalogue(page);
  await page.route('**/api/v1/programs/published/version?*',route=>route.fulfill({
    status:200,contentType:'application/json',body:JSON.stringify(program(3))}));
  await page.goto('/?programId=national-pinned&version=1');
  const pinned=page.getByRole('region',{name:'ارجاع به نسخه منتشرشده'});
  await expect(pinned.getByRole('alert')).toBeVisible();
  await expect(pinned).not.toContainText('هدف رسمی نسخه 3');
});

test('invalid or ambiguous shareable reference never calls the direct source',async({page})=>{
  let called=0;
  await catalogue(page);
  await page.route('**/api/v1/programs/published/version?*',route=>{
    called++;
    return route.fulfill({status:200,contentType:'application/json',
      body:JSON.stringify(program(1))});
  });
  for(const url of [
    '/?programId=national-pinned&version=01',
    '/?programId=national-pinned&version=0',
    '/?programId=national-pinned&programId=other&version=1',
    '/?programId=national-pinned&version=1&version=3',
    '/?programId=national-pinned',
  ]) {
    await page.goto(url);
    await expect(page.getByRole('region',{name:'ارجاع به نسخه منتشرشده'})
      .getByRole('alert')).toBeVisible();
  }
  expect(called).toBe(0);
});

test('unavailable pinned source never displays a fabricated public record',async({page})=>{
  await catalogue(page);
  await page.route('**/api/v1/programs/published/version?*',route=>route.fulfill({
    status:503,contentType:'application/problem+json',body:'{}'}));
  await page.goto('/?programId=national-pinned&version=1');
  const pinned=page.getByRole('region',{name:'ارجاع به نسخه منتشرشده'});
  await expect(pinned.getByRole('alert')).toBeVisible();
  await expect(pinned).not.toContainText('هدف رسمی نسخه 1');
});

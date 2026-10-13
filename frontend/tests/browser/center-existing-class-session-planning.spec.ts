import { test, expect } from '@playwright/test';
test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE !== 'center', 'S44 genuine existing-class scheduling');

const classId='aaaaaaaa-aaaa-4aaa-8aaa-000000000044';
const sourceId='bbbbbbbb-bbbb-4bbb-8bbb-000000000044';
const source={sourceId,classId,title:'جلسه قبلی',classTitle:'کلاس ثبت‌شده قدیمی',scheduledAt:null};
async function enter(page:import('@playwright/test').Page){
 await page.route('**/api/v1/identity/center-memberships',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({memberships:[
   {centerId:'c1',displayName:'کانون اول',roleCode:'member'},
   {centerId:'c2',displayName:'کانون دوم',roleCode:'member'}]})}));
 await page.goto('/');
 await page.getByRole('textbox',{name:'توکن دسترسی'}).fill('synthetic-s44');
 await page.getByRole('button',{name:'دریافت عضویت‌ها'}).click();
 await page.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c1');
}
async function open(page:import('@playwright/test').Page){
 const cat=page.locator('section[aria-label="مدیریت درخواست کلاس‌های موجود"]');
 await cat.getByRole('button',{name:'دریافت کلاس‌ها از جلسات واقعی'}).click();
 await cat.getByRole('button',{name:'برنامه‌ریزی جلسه جدید برای همین کلاس'}).click();
 return cat.locator('section[aria-label="برنامه‌ریزی جلسه کلاس موجود"]');
}
function allowSources(page:import('@playwright/test').Page){
 return page.route(/\/activity-sources\/class-sessions(?:\?.*)?$/,r=>r.fulfill({
  status:200,contentType:'application/json',
  body:JSON.stringify({sourceKind:'CLASS_SESSION',records:[source],nextBeforeSourceId:null})}));
}
test('existing class -> two original S13 planned sessions, without duplicate class or delivery',async({page})=>{
 const posts:Array<{url:string;body:any;auth:string|undefined}>=[];
 await allowSources(page);
 await page.route('**/api/v1/centers/*/classes**',r=>{
  const body=r.request().postDataJSON();
  posts.push({url:r.request().url(),body,auth:r.request().headers().authorization});
  return r.fulfill({status:200,contentType:'application/json',
   body:JSON.stringify({sessionId:body.sessionId,classId:body.classId,scheduledAt:body.scheduledAt})});
 });
 await enter(page);
 const panel=await open(page);
 await expect(panel.getByText('کلاس ثبت‌شده قدیمی',{exact:true})).toBeVisible();
 await expect(panel.getByRole('button',{name:'ثبت کلاس',exact:true})).toHaveCount(0);
 await panel.getByRole('textbox',{name:'عنوان جلسه جدید'}).fill('جلسه بعدی اول');
 await panel.getByRole('button',{name:'ثبت جلسه برنامه‌ریزی‌شده'}).click();
 await expect(panel.getByText('جلسه برنامه‌ریزی‌شده در API ثبت شد؛ برگزاری تأیید نشده است.',{exact:false})).toBeVisible();
 await panel.getByRole('textbox',{name:'عنوان جلسه جدید'}).fill('جلسه بعدی دوم');
 await panel.getByRole('button',{name:'ثبت جلسه برنامه‌ریزی‌شده'}).click();
 await expect.poll(()=>posts.length).toBe(2);
 expect(posts.every(x=>x.url.endsWith('/classes/sessions'))).toBe(true);
 expect(posts.every(x=>x.auth==='Bearer synthetic-s44')).toBe(true);
 expect(posts.map(x=>x.body.classId)).toEqual([classId,classId]);
 expect(posts.map(x=>x.body.title)).toEqual(['جلسه بعدی اول','جلسه بعدی دوم']);
 expect(posts[0].body.sessionId).not.toBe(posts[1].body.sessionId);
 expect(posts[0].body.scheduledAt).toBeNull();
 await page.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c2');
 await expect(page.locator('section[aria-label="برنامه‌ریزی جلسه کلاس موجود"]')).toHaveCount(0);
});
test('original independent S13 write grant denial is not a scheduled session',async({page})=>{
 await allowSources(page);
 await page.route('**/api/v1/centers/*/classes/sessions',r=>r.fulfill({
  status:403,contentType:'application/problem+json',body:'{}'}));
 await enter(page);
 const panel=await open(page);
 await panel.getByRole('textbox',{name:'عنوان جلسه جدید'}).fill('بدون مجوز');
 await panel.getByRole('button',{name:'ثبت جلسه برنامه‌ریزی‌شده'}).click();
 await expect(panel.getByRole('alert')).toContainText('مجوز مستقل برنامه‌ریزی جلسه تأیید نشده است.');
 await expect(panel.getByText('جلسه برنامه‌ریزی‌شده در API ثبت شد',{exact:false})).toHaveCount(0);
});
test('forged class or scheduled instant receipt fails closed',async({page})=>{
 await allowSources(page);
 let mismatch='class';
 await page.route('**/api/v1/centers/*/classes/sessions',r=>{
  const body=r.request().postDataJSON();
  return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
   sessionId:body.sessionId,classId:mismatch==='class'?sourceId:classId,
   scheduledAt:mismatch==='class'?body.scheduledAt:'2026-11-01T00:00:00Z'})});
 });
 await enter(page);
 const panel=await open(page);
 await panel.getByRole('textbox',{name:'عنوان جلسه جدید'}).fill('جلسه نامعتبر');
 await panel.getByRole('button',{name:'ثبت جلسه برنامه‌ریزی‌شده'}).click();
 await expect(panel.getByRole('alert')).toContainText('ثبت جلسه تأیید نشد.');
 mismatch='time';
 await panel.getByRole('textbox',{name:'عنوان جلسه جدید'}).fill('جلسه با زمان');
 await panel.getByRole('textbox',{name:'زمان برنامه‌ریزی جلسه'}).fill('2026-11-20T10:00');
 await panel.getByRole('button',{name:'ثبت جلسه برنامه‌ریزی‌شده'}).click();
 await expect(panel.getByRole('alert')).toContainText('ثبت جلسه تأیید نشد.');
 await expect(panel.getByText('جلسه برنامه‌ریزی‌شده در API ثبت شد',{exact:false})).toHaveCount(0);
});
test('source read 403 never exposes existing-class session-write entry',async({page})=>{
 let posts=0;
 await page.route(/\/activity-sources\/class-sessions(?:\?.*)?$/,r=>r.fulfill({
  status:403,contentType:'application/problem+json',body:'{}'}));
 await page.route('**/api/v1/centers/*/classes/sessions',r=>{
  posts++;return r.fulfill({status:500,body:'{}'});
 });
 await enter(page);
 const cat=page.locator('section[aria-label="مدیریت درخواست کلاس‌های موجود"]');
 await cat.getByRole('button',{name:'دریافت کلاس‌ها از جلسات واقعی'}).click();
 await expect(cat.getByRole('alert')).toContainText('مجوز مستقل کاتالوگ کلاس و جلسه وجود ندارد.');
 await expect(cat.getByRole('button',{name:'برنامه‌ریزی جلسه جدید برای همین کلاس'})).toHaveCount(0);
 expect(posts).toBe(0);
});

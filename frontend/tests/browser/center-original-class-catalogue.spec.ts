import {test,expect} from '@playwright/test';
test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE!=='center','S46 original class owner directory');

const noSession='aaaaaaaa-aaaa-4aaa-8aaa-000000000046';
const classRecord={classId:noSession,title:'کلاس اصلی بدون هیچ جلسه'};
async function enter(page:import('@playwright/test').Page){
 await page.route('**/api/v1/identity/center-memberships',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({memberships:[
   {centerId:'c1',displayName:'کانون یک',roleCode:'member'},
   {centerId:'c2',displayName:'کانون دو',roleCode:'member'}]})}));
 await page.goto('/');
 await page.getByRole('textbox',{name:'توکن دسترسی'}).fill('synthetic-s46');
 await page.getByRole('button',{name:'دریافت عضویت‌ها'}).click();
 await page.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c1');
}
const directory=(p:import('@playwright/test').Page)=>
 p.locator('section[aria-label="مدیریت درخواست کلاس‌های موجود"]');

test('sessionless original class -> original S13 session planning and separate S18 intake',async({page:p})=>{
 const writes:any[]=[];
 await p.route(/\/classes\/catalogue(?:\?.*)?$/,r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
   classes:[classRecord],nextBeforeClassId:null})}));
 // No permission to S12 session catalogue is needed to list real class identities.
 await p.route(/\/activity-sources\/class-sessions(?:\?.*)?$/,r=>r.fulfill({
  status:403,contentType:'application/problem+json',body:'{}'}));
 await p.route('**/api/v1/centers/*/classes/sessions',r=>{
  const body=r.request().postDataJSON();writes.push(body);
  return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
   sessionId:body.sessionId,classId:body.classId,scheduledAt:body.scheduledAt})});
 });
 await p.route(/\/classes\/[^/]+\/intake(?:\?.*)?$/,r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
   classId:noSession,currentRevision:1,configuredCapacity:8,
   confirmedEnrollmentCount:null,registrationIntents:[],nextBeforeRequestId:null})}));
 await enter(p);
 const x=directory(p);
 await x.getByRole('button',{name:'دریافت کاتالوگ اصلی همه کلاس‌ها'}).click();
 await expect(x.getByText(classRecord.title,{exact:true})).toBeVisible();
 await expect(x.getByRole('button',{name:'عملیات جلسات واقعی همین کلاس'})).toHaveCount(0);
 await x.getByRole('button',{name:'برنامه‌ریزی جلسه جدید برای همین کلاس'}).click();
 const planner=x.locator('section[aria-label="برنامه‌ریزی جلسه کلاس موجود"]');
 await expect(planner.getByRole('button',{name:'ثبت کلاس',exact:true})).toHaveCount(0);
 await planner.getByRole('textbox',{name:'عنوان جلسه جدید'}).fill('اولین جلسه این کلاس');
 await planner.getByRole('button',{name:'ثبت جلسه برنامه‌ریزی‌شده'}).click();
 await expect(planner.getByRole('status')).toContainText('برگزاری تأیید نشده است');
 expect(writes).toHaveLength(1);
 expect(writes[0].classId).toBe(noSession);
 expect(writes[0].title).toBe('اولین جلسه این کلاس');
 expect(writes[0].scheduledAt).toBeNull();
 await x.getByRole('button',{name:'مدیریت ظرفیت و درخواست‌های همین کلاس'}).click();
 const intake=x.locator('section[aria-label="ظرفیت و درخواست اولیه کلاس"]');
 await intake.getByRole('button',{name:'دریافت وضعیت ظرفیت'}).click();
 await expect(intake.getByText('آمار ثبت‌نام قطعی: نامعلوم؛ از درخواست‌های اولیه استنتاج نمی‌شود.')).toBeVisible();
 await p.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c2');
 await expect(directory(p).getByText(classRecord.title,{exact:true})).toHaveCount(0);
});
test('original class directory paginates 22 immutable owners with no synthetic sessions',async({page:p})=>{
 const rows=Array.from({length:22},(_,i)=>({
  classId:'00000000-0000-4000-8000-'+String(22-i).padStart(12,'0'),
  title:'اصلی '+(22-i)}));
 await p.route(/\/classes\/catalogue(?:\?.*)?$/,r=>{
  const page2=r.request().url().includes('beforeClassId=');
  return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
   classes:page2?rows.slice(20):rows.slice(0,20),
   nextBeforeClassId:page2?null:rows[19].classId})});
 });
 await enter(p);
 const x=directory(p);
 await x.getByRole('button',{name:'دریافت کاتالوگ اصلی همه کلاس‌ها'}).click();
 await expect(x.getByRole('button',{name:'برنامه‌ریزی جلسه جدید برای همین کلاس'})).toHaveCount(20);
 await x.getByRole('button',{name:'کلاس‌های اصلی بیشتر'}).click();
 await expect(x.getByRole('button',{name:'برنامه‌ریزی جلسه جدید برای همین کلاس'})).toHaveCount(22);
 await expect(x.getByText('اصلی 1',{exact:true})).toBeVisible();
});
test('independent directory denial and forged cursor never disclose class or schedule',async({page:p})=>{
 await p.route(/\/classes\/catalogue(?:\?.*)?$/,r=>r.fulfill({
  status:403,contentType:'application/problem+json',body:'{}'}));
 await enter(p);
 const x=directory(p);
 await x.getByRole('button',{name:'دریافت کاتالوگ اصلی همه کلاس‌ها'}).click();
 await expect(x.getByRole('alert')).toContainText('مجوز مستقل مشاهده کاتالوگ اصلی کلاس‌ها وجود ندارد.');
 await expect(x.getByRole('button',{name:'برنامه‌ریزی جلسه جدید برای همین کلاس'})).toHaveCount(0);
 await p.unroute(/\/classes\/catalogue(?:\?.*)?$/);
 await p.route(/\/classes\/catalogue(?:\?.*)?$/,r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
   classes:[classRecord],nextBeforeClassId:noSession})}));
 await x.getByRole('button',{name:'دریافت کاتالوگ اصلی همه کلاس‌ها'}).click();
 await expect(x.getByRole('alert')).toContainText('کاتالوگ اصلی معتبر کلاس‌ها دریافت نشد.');
 await expect(x.getByText(classRecord.title,{exact:true})).toHaveCount(0);
});

import {test,expect} from '@playwright/test';
test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE!=='center','Private center event planning');
const draft='11111111-1111-4111-8111-111111111111';
async function enter(p:import('@playwright/test').Page){
 await p.route('**/api/v1/identity/center-memberships',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({memberships:[
   {centerId:'c1',displayName:'کانون ۱',roleCode:'member'},
   {centerId:'c2',displayName:'کانون ۲',roleCode:'member'}]})}));
 await p.goto('/');
 await p.getByRole('textbox',{name:'توکن دسترسی'}).fill('synthetic');
 await p.getByRole('button',{name:'دریافت عضویت‌ها'}).click();
 await p.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c1');
}
test('creates and revises private scheduled event against a real pinned local plan',async({page:p})=>{
 const requests:Record<string,any>[]=[];
 await p.route('**/api/v1/centers/*/local-plan-drafts',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({drafts:[
   {draftId:draft,revision:2,localTitle:'طرح معتبر',programId:'national-1',nationalVersion:1}]})
 }));
 await p.route('**/api/v1/centers/*/events/plans/revisions',async r=>{
  const x=r.request().postDataJSON();requests.push(x);
  await r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
   eventId:x.eventId,draftId:x.draftId,pinnedPlanRevision:x.pinnedPlanRevision,
   revision:x.expectedRevision+1,visibility:'PRIVATE_DRAFT',isPublished:false,
   isExecuted:false,isRegistrationOpen:false})});
 });
 await enter(p);
 await p.getByRole('button',{name:'دریافت طرح‌های محلی رویداد'}).click();
 await p.getByRole('combobox',{name:'طرح محلی رویداد'}).selectOption(draft+':2');
 await p.getByRole('textbox',{name:'عنوان رویداد'}).fill('گردهمایی فرهنگی');
 await p.getByRole('textbox',{name:'هدف رویداد'}).fill('آشنایی فرهنگی');
 await p.getByRole('textbox',{name:'توصیف محل برگزاری'}).fill('سالن');
 await p.getByRole('textbox',{name:'زمان رویداد UTC'}).fill('2026-11-10T10:00');
 await p.getByRole('button',{name:'ذخیره نسخه برنامه رویداد'}).click();
 await expect(p.getByText('نسخه برنامه رویداد ذخیره شد؛ انتشار یا برگزاری تأیید نشده است.')).toBeVisible();
 expect(requests[0]).toMatchObject({expectedRevision:0,draftId:draft,pinnedPlanRevision:2,
  scheduledAt:'2026-11-10T10:00:00.000Z'});
 await p.getByRole('textbox',{name:'عنوان رویداد'}).fill('عنوان اصلاح‌شده');
 await p.getByRole('button',{name:'ذخیره نسخه برنامه رویداد'}).click();
 expect(requests[1].eventId).toBe(requests[0].eventId);
 expect(requests[1].expectedRevision).toBe(1);
 await p.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c2');
 await expect(p.getByText('نسخه برنامه رویداد ذخیره شد؛ انتشار یا برگزاری تأیید نشده است.')).toHaveCount(0);
});
test('no local plan cannot become an invented event',async({page:p})=>{
 await p.route('**/api/v1/centers/*/local-plan-drafts',r=>r.fulfill({
  status:200,contentType:'application/json',body:'{"drafts":[]}'}));
 await enter(p);
 await p.getByRole('button',{name:'دریافت طرح‌های محلی رویداد'}).click();
 await expect(p.getByText('بدون طرح محلی معتبر نمی‌توان رویداد ایجاد کرد.')).toBeVisible();
 await expect(p.getByRole('button',{name:'ذخیره نسخه برنامه رویداد'})).toBeDisabled();
});
test('forged published or executed event is never rendered',async({page:p})=>{
 await p.route('**/api/v1/centers/*/events/plans',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({events:[{
   eventId:'22222222-2222-4222-8222-222222222222',draftId:draft,pinnedPlanRevision:2,
   revision:1,title:'forged published event',purpose:'secret',locationDescription:'venue',
   scheduledAt:'2026-11-10T10:00:00Z',recordedAt:'2026-10-13T10:00:00Z',
   visibility:'PUBLIC',isPublished:true,isExecuted:true,isRegistrationOpen:true}],
   nextBeforeEventId:null})}));
 await enter(p);
 await p.getByRole('button',{name:'دریافت رویدادهای خصوصی'}).click();
 await expect(p.getByRole('alert')).toContainText('دریافت رویداد خصوصی ناموفق بود.');
 await expect(p.getByText('forged published event')).toHaveCount(0);
});
test('independent denial does not expose event lists',async({page:p})=>{
 await p.route('**/api/v1/centers/*/events/plans',r=>r.fulfill({
  status:403,contentType:'application/problem+json',body:'{"title":"Forbidden"}'}));
 await enter(p);
 await p.getByRole('button',{name:'دریافت رویدادهای خصوصی'}).click();
 await expect(p.getByRole('alert')).toContainText('مجوز مستقل خواندن رویداد وجود ندارد.');
});

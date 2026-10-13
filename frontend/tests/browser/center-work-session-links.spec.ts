import {test,expect} from '@playwright/test';
test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE!=='center','S41 center-private class session links');
const draft='aaaaaaaa-aaaa-4aaa-8aaa-000000000041';
const card='bbbbbbbb-bbbb-4bbb-8bbb-000000000041';
const session='cccccccc-cccc-4ccc-8ccc-000000000041';
const clazz='dddddddd-dddd-4ddd-8ddd-000000000041';
const plan={draftId:draft,revision:1,programId:'national-1',nationalVersion:1,
 localTitle:'طرح واقعی مرتبط',localActivities:[]};
const work={workCardId:card,draftId:draft,pinnedPlanRevision:1,revision:1,
 title:'کارت برنامه‌ریزی کانون',planningNote:'یادداشت خصوصی',plannedDueAt:null,
 recordedAt:'2026-10-13T10:00:00Z',needs:[],needsPreviewLimit:5,
 nextBeforeNeedId:null,visibility:'PRIVATE_DRAFT',executionStatus:'UNKNOWN',isExecuted:false};
const base={sourcePlan:{draftId:draft,pinnedPlanRevision:1,localTitle:plan.localTitle,
 programId:plan.programId,nationalVersion:1,visibility:'PRIVATE_DRAFT',isPublished:false},
 cards:[work],nextBeforeCardId:null,visibility:'PRIVATE_PLANNING',
 executionStatus:'UNKNOWN',fulfillmentStatus:'UNKNOWN',
 isExecuted:false,isAllocated:false,isPurchased:false,isFulfilled:false};
const sessionSource={sourceId:session,classId:clazz,title:'جلسه واقعی آموزشی',
 classTitle:'کلاس واقعی',scheduledAt:null};
const state={visibility:'PRIVATE_PLANNING',isPublished:false,isExecuted:false,
 isAttendanceVerified:false,isDeliveryVerified:false};
const item={sessionId:session,classId:clazz,sessionTitle:'جلسه واقعی آموزشی',
 classTitle:'کلاس واقعی',scheduledAt:null,recordedAt:'2026-10-13T10:00:00Z',...state};
async function enter(p:import('@playwright/test').Page){
 await p.route('**/api/v1/identity/center-memberships',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({memberships:[
   {centerId:'c1',displayName:'کانون یک',roleCode:'member'},
   {centerId:'c2',displayName:'کانون دو',roleCode:'member'}]})}));
 await p.route('**/api/v1/centers/*/local-plan-drafts',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({drafts:[plan]})}));
 await p.route('**/api/v1/centers/*/operations/local-plan-workspace/*/*',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify(base)}));
 await p.goto('/');
 await p.getByRole('textbox',{name:'توکن دسترسی'}).fill('synthetic');
 await p.getByRole('button',{name:'دریافت عضویت‌ها'}).click();
 await p.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c1');
 const workspace=p.locator('section[aria-label="میزکار یکپارچه طرح بومی"]');
 await workspace.getByRole('button',{name:'دریافت طرح‌های واقعی کانون'}).click();
 await workspace.getByRole('combobox',{name:'انتخاب نسخه طرح برای میزکار'}).selectOption(draft+':1');
 await workspace.getByRole('button',{name:'نمایش کارت‌ها و نیازهای این نسخه طرح'}).click();
 return p.locator('section[aria-label="جلسات آموزشی مرتبط با کارت '+card+'"]');
}
test('genuine session selected and linked to original work card, read and preparation available',async({page:p})=>{
 const posts:any[]=[];
 await p.route(/\/activity-sources\/class-sessions(?:\?.*)?$/,r=>r.fulfill({
  status:200,contentType:'application/json',
  body:JSON.stringify({sourceKind:'CLASS_SESSION',records:[sessionSource],nextBeforeSourceId:null})}));
 await p.route('**/api/v1/centers/*/operations/work-cards/*/session-links',r=>{
  if(r.request().method()==='POST'){
   const body=r.request().postDataJSON();posts.push(body);
   return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
    workCardId:card,sessionId:body.sessionId,...state})});
  }
  return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
   workCardId:card,links:[item],nextBeforeSessionId:null,...state})});
 });
 const panel=await enter(p);
 await panel.getByRole('button',{name:'دریافت جلسه‌های واقعی برای پیوند'}).click();
 await expect.poll(async()=>{
  const errors=await panel.getByRole('alert').allTextContents();
  const options=await panel.locator('select option').allTextContents();
  return JSON.stringify({errors,options});
 }).toContain('جلسه واقعی آموزشی');
 await panel.locator('select').selectOption(session);
 await panel.getByRole('button',{name:'ثبت پیوند خصوصی جلسه به کارت کار'}).click();
 await expect(panel.getByRole('status')).toContainText('برگزاری یا حضور تأیید نشده');
 expect(posts).toEqual([{sessionId:session}]);
 await panel.getByRole('button',{name:'مشاهده جلسات پیوندخورده'}).click();
 await expect(panel.getByText('کلاس واقعی — جلسه واقعی آموزشی')).toBeVisible();
 await panel.getByRole('button',{name:'باز کردن دفتر اصلی آماده‌سازی جلسات'}).click();
 await expect(panel.getByRole('heading',{name:'دفتر آماده‌سازی جلسات کلاس'})).toBeVisible();
 await p.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c2');
 await expect(p.locator('section[aria-label="جلسات آموزشی مرتبط با کارت '+card+'"]')).toHaveCount(0);
});
test('forged delivery, attendance or publication is rejected without leaking title',async({page:p})=>{
 await p.route('**/api/v1/centers/*/operations/work-cards/*/session-links',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
   workCardId:card,links:[{...item,isAttendanceVerified:true,isDeliveryVerified:true}],
   nextBeforeSessionId:null,...state})}));
 const panel=await enter(p);
 await panel.getByRole('button',{name:'مشاهده جلسات پیوندخورده'}).click();
 await expect(panel.getByText('فهرست معتبر پیوندها دریافت نشد.')).toBeVisible();
 await expect(panel.getByText('کلاس واقعی — جلسه واقعی آموزشی')).toHaveCount(0);
});
test('separate read/write link grants are visibly denied',async({page:p})=>{
 await p.route(/\/activity-sources\/class-sessions(?:\?.*)?$/,r=>r.fulfill({
  status:200,contentType:'application/json',
  body:JSON.stringify({sourceKind:'CLASS_SESSION',records:[sessionSource],nextBeforeSourceId:null})}));
 await p.route('**/api/v1/centers/*/operations/work-cards/*/session-links',r=>r.fulfill({
  status:403,contentType:'application/problem+json',body:'{}'}));
 const panel=await enter(p);
 await panel.getByRole('button',{name:'مشاهده جلسات پیوندخورده'}).click();
 await expect(panel.getByText('مجوز مستقل خواندن پیوند جلسات وجود ندارد.')).toBeVisible();
 await panel.getByRole('button',{name:'دریافت جلسه‌های واقعی برای پیوند'}).click();
 await expect.poll(async()=>{
  const errors=await panel.getByRole('alert').allTextContents();
  const options=await panel.locator('select option').allTextContents();
  return JSON.stringify({errors,options});
 }).toContain('جلسه واقعی آموزشی');
 await panel.locator('select').selectOption(session);
 await panel.getByRole('button',{name:'ثبت پیوند خصوصی جلسه به کارت کار'}).click();
 await expect(panel.getByText('مجوز مستقل ثبت پیوند وجود ندارد.')).toBeVisible();
});

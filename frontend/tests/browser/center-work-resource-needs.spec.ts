import {test,expect} from '@playwright/test';
test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE!=='center','S39 private resource needs');
const card='aaaaaaaa-aaaa-4aaa-8aaa-000000000039';
const draft='bbbbbbbb-bbbb-4bbb-8bbb-000000000039';
const need='cccccccc-cccc-4ccc-8ccc-000000000039';
const flags={visibility:'PRIVATE_PLANNING',fulfillmentStatus:'UNKNOWN',
 isAllocated:false,isPurchased:false,isFulfilled:false};
const source={workCardId:card,draftId:draft,pinnedPlanRevision:1,revision:1,
 title:'کارت واقعی',planningNote:'طرح محلی',plannedDueAt:null,
 recordedAt:'2026-10-13T10:00:00Z',visibility:'PRIVATE_DRAFT',
 executionStatus:'UNKNOWN',isExecuted:false};
const record={needId:need,workCardId:card,revision:1,resourceLabel:'تجهیزات آموزشی',
 planningDetail:'نیاز پیشنهادی',desiredBy:null,recordedAt:'2026-10-13T11:00:00Z',...flags};
async function setup(p:import('@playwright/test').Page){
 await p.route('**/api/v1/identity/center-memberships',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({memberships:[
   {centerId:'c1',displayName:'اول',roleCode:'member'},
   {centerId:'c2',displayName:'دوم',roleCode:'member'}]})}));
 await p.route('**/api/v1/centers/*/operations/work-cards',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
   cards:[source],nextBeforeCardId:null,visibility:'PRIVATE_DRAFT',
   executionStatus:'UNKNOWN',isExecuted:false})}));
 await p.goto('/');
 await p.getByRole('textbox',{name:'توکن دسترسی'}).fill('synthetic');
 await p.getByRole('button',{name:'دریافت عضویت‌ها'}).click();
 await p.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c1');
 await p.locator('section[aria-label="کارت‌های کار خصوصی کانون"]')
  .getByRole('button',{name:'دریافت کارت‌های خصوصی'}).click();
}
test('real card bound resource need, history and clear center switch',async({page:p})=>{
 const submitted:any[]=[];
 await p.route('**/api/v1/centers/*/operations/work-cards/*/resource-needs/revisions',r=>{
  const body=r.request().postDataJSON();submitted.push(body);
  return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
   needId:body.needId,workCardId:card,revision:body.expectedRevision+1,...flags})});
 });
 await p.route('**/api/v1/centers/*/operations/work-cards/*/resource-needs',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
   workCardId:card,needs:[record],nextBeforeNeedId:null,...flags})}));
 await p.route('**/api/v1/centers/*/operations/work-cards/*/resource-needs/*/revisions',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
   workCardId:card,needId:need,revisions:[{revision:1,resourceLabel:record.resourceLabel,
    planningDetail:record.planningDetail,desiredBy:null,recordedAt:record.recordedAt,...flags}],
   nextBeforeRevision:null,...flags})}));
 await setup(p);
 const x=p.locator('section[aria-label="نیازهای منابع کارت '+card+'"]');
 await x.getByRole('textbox',{name:'نام نیاز منابع '+card}).fill('تجهیزات آموزشی');
 await x.getByRole('button',{name:'ثبت نسخه نیاز منابع'}).click();
 await expect(x.getByRole('status')).toContainText('تأمین یا خرید تأیید نشده');
 expect(submitted).toHaveLength(1);
 expect(submitted[0].expectedRevision).toBe(0);
 expect(submitted[0].isFulfilled).toBeUndefined();
 await x.getByRole('button',{name:'دریافت نیازهای این کارت کار'}).click();
 await expect(x.getByText('تجهیزات آموزشی',{exact:true})).toBeVisible();
 await x.getByRole('button',{name:'ویرایش نیاز و تاریخچه'}).click();
 await x.getByRole('button',{name:'تاریخچه نیاز منابع'}).click();
 await expect(x.getByText('نسخه 1 — تجهیزات آموزشی — تأمین نامشخص')).toBeVisible();
 await p.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c2');
 await expect(p.locator('section[aria-label="نیازهای منابع کارت '+card+'"]')).toHaveCount(0);
});
test('forged allocation, purchase or fulfillment is never shown',async({page:p})=>{
 await p.route('**/api/v1/centers/*/operations/work-cards/*/resource-needs',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
   workCardId:card,needs:[{...record,isPurchased:true,isFulfilled:true}],
   nextBeforeNeedId:null,...flags})}));
 await setup(p);
 const x=p.locator('section[aria-label="نیازهای منابع کارت '+card+'"]');
 await x.getByRole('button',{name:'دریافت نیازهای این کارت کار'}).click();
 await expect(x.getByText('فهرست معتبر نیازهای منابع دریافت نشد.')).toBeVisible();
 await expect(x.getByText('تجهیزات آموزشی',{exact:true})).toHaveCount(0);
});
test('separate resource-read and write grants default deny',async({page:p})=>{
 await p.route('**/api/v1/centers/*/operations/work-cards/*/resource-needs*',r=>r.fulfill({
  status:403,contentType:'application/problem+json',body:'{}'}));
 await setup(p);
 const x=p.locator('section[aria-label="نیازهای منابع کارت '+card+'"]');
 await x.getByRole('button',{name:'دریافت نیازهای این کارت کار'}).click();
 await expect(x.getByText('مجوز مستقل مشاهده نیاز منابع موجود نیست.')).toBeVisible();
 await x.getByRole('textbox',{name:'نام نیاز منابع '+card}).fill('نیاز داخلی');
 await x.getByRole('button',{name:'ثبت نسخه نیاز منابع'}).click();
 await expect(x.getByText('مجوز مستقل ثبت نیاز منابع موجود نیست.')).toBeVisible();
});

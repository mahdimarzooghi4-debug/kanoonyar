import {test,expect} from '@playwright/test';
test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE!=='center','Private integrated local plan workspace');
const draftId='aaaaaaaa-aaaa-4aaa-8aaa-000000000040';
const cardId='bbbbbbbb-bbbb-4bbb-8bbb-000000000040';
const needId='cccccccc-cccc-4ccc-8ccc-000000000040';
const plan={draftId,revision:1,programId:'national-1',nationalVersion:1,
 localTitle:'طرح بومی واقعی',localActivities:[]};
const needs=[{needId,workCardId:cardId,revision:2,resourceLabel:'تجهیزات آموزشی پیشنهادی',
 planningDetail:'در حال برنامه‌ریزی',desiredBy:null,recordedAt:'2026-10-13T09:00:00Z',
 visibility:'PRIVATE_PLANNING',fulfillmentStatus:'UNKNOWN',isAllocated:false,
 isPurchased:false,isFulfilled:false}];
const card={workCardId:cardId,draftId,pinnedPlanRevision:1,revision:3,title:'کار واقعی طرح بومی',
 planningNote:'یادداشت کار اصلی',plannedDueAt:null,recordedAt:'2026-10-13T09:00:00Z',
 needs,needsPreviewLimit:5,nextBeforeNeedId:null,visibility:'PRIVATE_DRAFT',
 executionStatus:'UNKNOWN',isExecuted:false};
const response={sourcePlan:{draftId,pinnedPlanRevision:1,localTitle:plan.localTitle,
 programId:plan.programId,nationalVersion:1,visibility:'PRIVATE_DRAFT',isPublished:false},
 cards:[card],nextBeforeCardId:null,visibility:'PRIVATE_PLANNING',
 executionStatus:'UNKNOWN',fulfillmentStatus:'UNKNOWN',isExecuted:false,
 isAllocated:false,isPurchased:false,isFulfilled:false};
async function enter(p:import('@playwright/test').Page){
 await p.route('**/api/v1/identity/center-memberships',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({memberships:[
   {centerId:'c1',displayName:'کانون یک',roleCode:'member'},
   {centerId:'c2',displayName:'کانون دو',roleCode:'member'}]})}));
 await p.route('**/api/v1/centers/*/local-plan-drafts',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({drafts:[plan]})}));
 await p.goto('/');
 await p.getByRole('textbox',{name:'توکن دسترسی'}).fill('synthetic');
 await p.getByRole('button',{name:'دریافت عضویت‌ها'}).click();
 await p.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c1');
}
test('real plan -> pinned work card -> real planning needs -> original need editor',async({page:p})=>{
 const requested:string[]=[];
 await p.route('**/api/v1/centers/*/operations/local-plan-workspace/*/*',r=>{
  requested.push(r.request().url());
  return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify(response)});
 });
 await p.route('**/api/v1/centers/*/operations/work-cards/*/resource-needs',r=>r.fulfill({
  status:200,contentType:'application/json',
  body:JSON.stringify({workCardId:cardId,needs,nextBeforeNeedId:null,
   visibility:'PRIVATE_PLANNING',fulfillmentStatus:'UNKNOWN',
   isAllocated:false,isPurchased:false,isFulfilled:false})}));
 await enter(p);
 let x=p.locator('section[aria-label="میزکار یکپارچه طرح بومی"]');
 await x.getByRole('button',{name:'دریافت طرح‌های واقعی کانون'}).click();
 await x.getByRole('combobox',{name:'انتخاب نسخه طرح برای میزکار'}).selectOption(draftId+':1');
 await x.getByRole('button',{name:'نمایش کارت‌ها و نیازهای این نسخه طرح'}).click();
 await expect(x.getByText('طرح بومی واقعی',{exact:true})).toBeVisible();
 await expect(x.getByText('کار واقعی طرح بومی',{exact:true})).toBeVisible();
 await expect(x.getByText('تجهیزات آموزشی پیشنهادی — نسخه 2 / تأمین نامشخص')).toBeVisible();
 expect(requested).toHaveLength(1);
 expect(requested[0]).toContain('/operations/local-plan-workspace/'+draftId+'/1');
 await x.getByRole('button',{name:'مدیریت کامل نیازهای همین کارت'}).click();
 const original=x.locator('section[aria-label="نیازهای منابع کارت '+cardId+'"]');
 await original.getByRole('button',{name:'دریافت نیازهای این کارت کار'}).click();
 await expect(original.getByText('تجهیزات آموزشی پیشنهادی',{exact:true})).toBeVisible();
 await p.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c2');
 x=p.locator('section[aria-label="میزکار یکپارچه طرح بومی"]');
 await expect(x.getByText('کار واقعی طرح بومی',{exact:true})).toHaveCount(0);
});
test('untrusted purchased or executed workspace is not rendered',async({page:p})=>{
 await p.route('**/api/v1/centers/*/operations/local-plan-workspace/*/*',r=>r.fulfill({
  status:200,contentType:'application/json',
  body:JSON.stringify({...response,cards:[{...card,needs:[{...needs[0],isPurchased:true}]}]})}));
 await enter(p);
 const x=p.locator('section[aria-label="میزکار یکپارچه طرح بومی"]');
 await x.getByRole('button',{name:'دریافت طرح‌های واقعی کانون'}).click();
 await x.getByRole('combobox',{name:'انتخاب نسخه طرح برای میزکار'}).selectOption(draftId+':1');
 await x.getByRole('button',{name:'نمایش کارت‌ها و نیازهای این نسخه طرح'}).click();
 await expect(x.getByRole('alert')).toContainText('نمای یکپارچه معتبر از منابع اصلی دریافت نشد.');
 await expect(x.getByText('تجهیزات آموزشی پیشنهادی — نسخه 2 / تأمین نامشخص')).toHaveCount(0);
});
test('denied aggregate read does not leak plan or work card details',async({page:p})=>{
 await p.route('**/api/v1/centers/*/operations/local-plan-workspace/*/*',r=>r.fulfill({
  status:403,contentType:'application/problem+json',body:'{}'}));
 await enter(p);
 const x=p.locator('section[aria-label="میزکار یکپارچه طرح بومی"]');
 await x.getByRole('button',{name:'دریافت طرح‌های واقعی کانون'}).click();
 await x.getByRole('combobox',{name:'انتخاب نسخه طرح برای میزکار'}).selectOption(draftId+':1');
 await x.getByRole('button',{name:'نمایش کارت‌ها و نیازهای این نسخه طرح'}).click();
 await expect(x.getByRole('alert')).toContainText('مجوز مستقل خواندن طرح، کارت یا نیازهای هر کارت وجود ندارد.');
 await expect(x.getByText('کار واقعی طرح بومی',{exact:true})).toHaveCount(0);
});

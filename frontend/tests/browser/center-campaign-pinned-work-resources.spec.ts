import {test,expect} from '@playwright/test';
test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE!=='center','S49 campaign to exact original work/resource plan');

const campaignId='11111111-1111-4111-8111-000000000049';
const draftId='22222222-2222-4222-8222-000000000049';
const otherDraft='33333333-3333-4333-8333-000000000049';
const cardId='44444444-4444-4444-8444-000000000049';
const needId='55555555-5555-4555-8555-000000000049';
const campaign={campaignId,slug:'campaign-source-49',draftId,pinnedPlanRevision:3,revision:2,
 title:'پویش منبع‌دار واقعی',goalText:'هدف خصوصی',audienceDescription:'توصیف عمومی مخاطب',
 plannedActivities:[{activityKey:'step_one',title:'گام آزمایشی'}],
 recordedAt:'2026-10-13T09:00:00Z',visibility:'PRIVATE_DRAFT',
 isPublished:false,isExecuted:false};
const need={needId,workCardId:cardId,revision:2,resourceLabel:'ابزار آموزشی موردنیاز',
 planningDetail:'برنامه‌ریزی نشده برای خرید',desiredBy:null,
 recordedAt:'2026-10-13T09:00:00Z',visibility:'PRIVATE_PLANNING',
 fulfillmentStatus:'UNKNOWN',isAllocated:false,isPurchased:false,isFulfilled:false};
const card={workCardId:cardId,draftId,pinnedPlanRevision:3,revision:2,
 title:'کارت کار اصلی طرح پویش',planningNote:'شرح صرفاً داخلی',
 plannedDueAt:null,recordedAt:'2026-10-13T09:00:00Z',
 needs:[need],needsPreviewLimit:5,nextBeforeNeedId:null,
 visibility:'PRIVATE_DRAFT',executionStatus:'UNKNOWN',isExecuted:false};
function snapshot(overrides:Record<string,unknown>={}){
 return {sourcePlan:{draftId,pinnedPlanRevision:3,localTitle:'طرح محلی اصلی',
  programId:'national-main',nationalVersion:4,visibility:'PRIVATE_DRAFT',isPublished:false},
  cards:[card],nextBeforeCardId:null,visibility:'PRIVATE_PLANNING',
  executionStatus:'UNKNOWN',fulfillmentStatus:'UNKNOWN',
  isExecuted:false,isAllocated:false,isPurchased:false,isFulfilled:false,...overrides};
}
async function enter(p:import('@playwright/test').Page){
 await p.route('**/api/v1/identity/center-memberships',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({memberships:[
   {centerId:'c1',displayName:'کانون یک',roleCode:'member'},
   {centerId:'c2',displayName:'کانون دیگر',roleCode:'member'}]})}));
 await p.goto('/');
 await p.getByRole('textbox',{name:'توکن دسترسی'}).fill('synthetic-s49');
 await p.getByRole('button',{name:'دریافت عضویت‌ها'}).click();
 await p.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c1');
}
function campaignRoute(p:import('@playwright/test').Page){
 return p.route('**/api/v1/centers/*/campaigns/drafts',r=>r.fulfill({
  status:200,contentType:'application/json',
  body:JSON.stringify({campaigns:[campaign],nextBeforeCampaignId:null})}));
}
async function open(p:import('@playwright/test').Page){
 const c=p.locator('section[aria-label="پویش‌ساز خصوصی کانون"]');
 await c.getByRole('button',{name:'دریافت پویش‌های خصوصی'}).click();
 await c.getByRole('button',{name:'ویرایش پویش'}).click();
 await c.getByRole('button',{name:'کارت‌های کار و نیازهای طرح مبنای همین پویش'}).click();
 return c.locator('section[aria-label="میزکار یکپارچه طرح بومی"]');
}
test('authentic S23 campaign -> exact pinned S40 plan cards -> original S39 resource editor',async({page:p})=>{
 const requests:string[]=[];let broadPlanCalls=0;
 await campaignRoute(p);
 await p.route('**/api/v1/centers/*/local-plan-drafts',r=>{
  broadPlanCalls++;return r.fulfill({status:403,body:'{}'});
 });
 await p.route('**/api/v1/centers/*/operations/local-plan-workspace/*/*',r=>{
  requests.push(r.request().url());return r.fulfill({
   status:200,contentType:'application/json',body:JSON.stringify(snapshot())});
 });
 await p.route('**/api/v1/centers/*/operations/work-cards/*/resource-needs',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
   workCardId:cardId,needs:[need],nextBeforeNeedId:null,visibility:'PRIVATE_PLANNING',
   fulfillmentStatus:'UNKNOWN',isAllocated:false,isPurchased:false,isFulfilled:false})}));
 await enter(p);
 const workspace=await open(p);
 await expect(workspace.getByText('نسخه طرح مبنای پویش:',{exact:false})).toBeVisible();
 await expect(workspace.getByRole('combobox',{name:'انتخاب نسخه طرح برای میزکار'})).toHaveCount(0);
 await expect(workspace.getByRole('button',{name:'دریافت طرح‌های واقعی کانون'})).toHaveCount(0);
 await workspace.getByRole('button',{name:'نمایش کارت‌ها و نیازهای این نسخه طرح'}).click();
 await expect(workspace.getByText('کارت کار اصلی طرح پویش',{exact:true})).toBeVisible();
 await expect(workspace.getByText('ابزار آموزشی موردنیاز — نسخه 2 / تأمین نامشخص')).toBeVisible();
 expect(requests).toHaveLength(1);
 expect(requests[0]).toContain('/operations/local-plan-workspace/'+draftId+'/3');
 expect(broadPlanCalls).toBe(0);
 await workspace.getByRole('button',{name:'مدیریت کامل نیازهای همین کارت'}).click();
 const original=workspace.locator('section[aria-label="نیازهای منابع کارت '+cardId+'"]');
 await original.getByRole('button',{name:'دریافت نیازهای این کارت کار'}).click();
 await expect(original.getByText(need.resourceLabel,{exact:true})).toBeVisible();
 await p.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c2');
 await expect(p.locator('section[aria-label="پویش‌ساز خصوصی کانون"]')
  .getByText('کارت کار اصلی طرح پویش',{exact:true})).toHaveCount(0);
});
test('campaign visibility never delegates original S40 plan/work/resource read grant',async({page:p})=>{
 await campaignRoute(p);
 await p.route('**/api/v1/centers/*/operations/local-plan-workspace/*/*',r=>r.fulfill({
  status:403,contentType:'application/problem+json',body:'{}'}));
 await enter(p);
 const workspace=await open(p);
 await workspace.getByRole('button',{name:'نمایش کارت‌ها و نیازهای این نسخه طرح'}).click();
 await expect(workspace.getByRole('alert')).toContainText('مجوز مستقل خواندن طرح، کارت یا نیازهای هر کارت وجود ندارد.');
 await expect(workspace.getByText('کارت کار اصلی طرح پویش',{exact:true})).toHaveCount(0);
});
test('forged different plan revision or completed procurement in S40 response fails closed',async({page:p})=>{
 await campaignRoute(p);
 let forged:'plan'|'purchase'='plan';
 await p.route('**/api/v1/centers/*/operations/local-plan-workspace/*/*',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify(
   forged==='plan'?snapshot({sourcePlan:{...snapshot().sourcePlan,draftId:otherDraft}}):
   snapshot({cards:[{...card,needs:[{...need,isPurchased:true}]}]})
  )}));
 await enter(p);
 const workspace=await open(p);
 for(const type of ['plan','purchase'] as const){
  forged=type;
  await workspace.getByRole('button',{name:'نمایش کارت‌ها و نیازهای این نسخه طرح'}).click();
  await expect(workspace.getByRole('alert')).toContainText('نمای یکپارچه معتبر از منابع اصلی دریافت نشد.');
  await expect(workspace.getByText('ابزار آموزشی موردنیاز — نسخه 2 / تأمین نامشخص')).toHaveCount(0);
 }
});
test('another real campaign plan version cannot be substituted without selecting its campaign',async({page:p})=>{
 const next={...campaign,campaignId:'66666666-6666-4666-8666-000000000049',
  draftId:otherDraft,pinnedPlanRevision:1,slug:'other-campaign-49',title:'پویش دوم'};
 await p.route('**/api/v1/centers/*/campaigns/drafts',r=>r.fulfill({
  status:200,contentType:'application/json',
  body:JSON.stringify({campaigns:[next,campaign],nextBeforeCampaignId:null})}));
 await enter(p);
 const c=p.locator('section[aria-label="پویش‌ساز خصوصی کانون"]');
 await c.getByRole('button',{name:'دریافت پویش‌های خصوصی'}).click();
 const entries=c.locator('li');
 await entries.filter({hasText:next.title}).getByRole('button',{name:'ویرایش پویش'}).click();
 await c.getByRole('button',{name:'کارت‌های کار و نیازهای طرح مبنای همین پویش'}).click();
 const ws=c.locator('section[aria-label="میزکار یکپارچه طرح بومی"]');
 await expect(ws.getByText('نسخه طرح مبنای پویش:',{exact:false})).toContainText(otherDraft);
 await entries.filter({hasText:campaign.title}).getByRole('button',{name:'ویرایش پویش'}).click();
 await expect(ws).toHaveCount(0);
});

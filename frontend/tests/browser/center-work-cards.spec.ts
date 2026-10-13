import {test,expect} from '@playwright/test';
test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE!=='center','Private work cards');
const draft='11111111-1111-4111-8111-111111111111';
const id='22222222-2222-4222-8222-222222222222';
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
const task=(title='برنامه تمرین')=>({workCardId:id,draftId:draft,pinnedPlanRevision:2,revision:1,
 title,planningNote:'یادداشت',plannedDueAt:null,recordedAt:'2026-10-13T08:00:00Z',
 visibility:'PRIVATE_DRAFT',executionStatus:'UNKNOWN',isExecuted:false});
test('source planning choose and create, list and immutable history',async({page:p})=>{
 const posted:any[]=[];
 await p.route('**/api/v1/centers/*/local-plan-drafts',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({drafts:[{
   draftId:draft,revision:2,programId:'national-1',nationalVersion:1,
   localTitle:'طرح محلی معتبر',localActivities:[]}]})}));
 await p.route('**/api/v1/centers/*/operations/work-cards',r=>r.fulfill({
  status:200,contentType:'application/json',
  body:JSON.stringify({cards:[task()],nextBeforeCardId:null,
   visibility:'PRIVATE_DRAFT',executionStatus:'UNKNOWN',isExecuted:false})}));
 await p.route('**/api/v1/centers/*/operations/work-cards/revisions',r=>{
  posted.push(r.request().postDataJSON());
  const body=r.request().postDataJSON();
  return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
   workCardId:body.workCardId,draftId:body.draftId,pinnedPlanRevision:body.pinnedPlanRevision,
   revision:body.expectedRevision+1,visibility:'PRIVATE_DRAFT',executionStatus:'UNKNOWN',
   isExecuted:false})});
 });
 await p.route('**/api/v1/centers/*/operations/work-cards/*/revisions',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
   workCardId:id,draftId:draft,pinnedPlanRevision:2,
   revisions:[{revision:1,title:'برنامه تمرین',planningNote:'یادداشت',
    plannedDueAt:null,recordedAt:'2026-10-13T08:00:00Z',
    visibility:'PRIVATE_DRAFT',executionStatus:'UNKNOWN',isExecuted:false}],
   nextBeforeRevision:null,visibility:'PRIVATE_DRAFT',executionStatus:'UNKNOWN',isExecuted:false})}));
 await enter(p);
 const panel=p.locator('section[aria-label="کارت‌های کار خصوصی کانون"]');
 await panel.getByRole('button',{name:'دریافت طرح‌های محلی واقعی'}).click();
 await panel.getByRole('combobox',{name:'انتخاب طرح برای کارت'}).selectOption(draft+':2');
 await panel.getByRole('textbox',{name:'عنوان کارت خصوصی'}).fill('برنامه تمرین');
 await panel.getByRole('button',{name:'ذخیره نسخه کارت کار'}).click();
 await expect(panel.getByRole('status')).toContainText('انجام کار تأیید نشده است');
 expect(posted).toHaveLength(1);
 expect(posted[0].draftId).toBe(draft);
 expect(posted[0].pinnedPlanRevision).toBe(2);
 expect(posted[0].expectedRevision).toBe(0);
 expect(posted[0].isExecuted).toBeUndefined();
 await panel.getByRole('button',{name:'دریافت کارت‌های خصوصی'}).click();
 await expect(panel.getByText('برنامه تمرین',{exact:true})).toBeVisible();
 await panel.getByRole('button',{name:'ویرایش کارت برنامه‌ریزی‌شده'}).click();
 await panel.getByRole('button',{name:'مشاهده تاریخچه کارت'}).click();
 await expect(panel.getByText('نسخه 1 — برنامه تمرین — UNKNOWN')).toBeVisible();
 await p.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c2');
 await expect(p.getByText('نسخه 1 — برنامه تمرین — UNKNOWN')).toHaveCount(0);
});
test('rejects fake executed source and refuses to display it',async({page:p})=>{
 await p.route('**/api/v1/centers/*/operations/work-cards',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({cards:[{
   ...task('نسخه جعلی'),executionStatus:'COMPLETE',isExecuted:true}],
   nextBeforeCardId:null,visibility:'PRIVATE_DRAFT',executionStatus:'UNKNOWN',isExecuted:false})}));
 await enter(p);
 const panel=p.locator('section[aria-label="کارت‌های کار خصوصی کانون"]');
 await panel.getByRole('button',{name:'دریافت کارت‌های خصوصی'}).click();
 await expect(panel.getByRole('alert')).toContainText('کارت کار معتبر دریافت نشد.');
 await expect(panel.getByText('نسخه جعلی')).toHaveCount(0);
});
test('revoked source and task grants fail closed',async({page:p})=>{
 await p.route('**/api/v1/centers/*/operations/work-cards',r=>r.fulfill({
  status:403,contentType:'application/problem+json',body:'{}'}));
 await p.route('**/api/v1/centers/*/local-plan-drafts',r=>r.fulfill({
  status:403,contentType:'application/problem+json',body:'{}'}));
 await enter(p);
 const panel=p.locator('section[aria-label="کارت‌های کار خصوصی کانون"]');
 await panel.getByRole('button',{name:'دریافت کارت‌های خصوصی'}).click();
 await expect(panel.getByText('مجوز مستقل مشاهده کارت‌های کار وجود ندارد.')).toBeVisible();
 await panel.getByRole('button',{name:'دریافت طرح‌های محلی واقعی'}).click();
 await expect(panel.getByText('مجوز مستقل مشاهده طرح محلی وجود ندارد.')).toBeVisible();
});

import {test,expect} from '@playwright/test';
test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE!=='center','Sprint 42 linked session report handoff');
const draft='aaaaaaaa-aaaa-4aaa-8aaa-000000000042';
const card='bbbbbbbb-bbbb-4bbb-8bbb-000000000042';
const session='cccccccc-cccc-4ccc-8ccc-000000000042';
const clazz='dddddddd-dddd-4ddd-8ddd-000000000042';
const issuance='eeeeeeee-eeee-4eee-8eee-000000000042';
const linkId='ffffffff-ffff-4fff-8fff-000000000042';
const flags={visibility:'PRIVATE_PLANNING',isPublished:false,isExecuted:false,
 isAttendanceVerified:false,isDeliveryVerified:false};
const plan={draftId:draft,revision:1,programId:'national-1',nationalVersion:1,
 localTitle:'برنامه بومی آزمون',localActivities:[]};
const work={workCardId:card,draftId:draft,pinnedPlanRevision:1,revision:1,
 title:'کارت اصلی برنامه',planningNote:'بدون ادعای اجرا',plannedDueAt:null,
 recordedAt:'2026-10-13T10:00:00Z',needs:[],needsPreviewLimit:5,
 nextBeforeNeedId:null,visibility:'PRIVATE_DRAFT',executionStatus:'UNKNOWN',isExecuted:false};
const overview={sourcePlan:{draftId:draft,pinnedPlanRevision:1,localTitle:plan.localTitle,
 programId:plan.programId,nationalVersion:1,visibility:'PRIVATE_DRAFT',isPublished:false},
 cards:[work],nextBeforeCardId:null,visibility:'PRIVATE_PLANNING',
 executionStatus:'UNKNOWN',fulfillmentStatus:'UNKNOWN',isExecuted:false,
 isAllocated:false,isPurchased:false,isFulfilled:false};
const sessionRecord={sessionId:session,classId:clazz,sessionTitle:'جلسه اصیل',
 classTitle:'کلاس اصیل',scheduledAt:null,recordedAt:'2026-10-13T10:00:00Z',...flags};
const trace={linkId,issuanceId:issuance,pinnedRevision:1,latestRevision:1,
 programId:'national-1',nationalVersion:1,localTitle:plan.localTitle,plannedActivities:[]};
const report={reportSequence:1,reportId:'12345678-1234-4234-8234-123456789042',
 draftId:draft,linkId,issuanceId:issuance,programId:'national-1',nationalVersion:1,
 pinnedRevision:1,reportedSummary:'گزارش انسانی اعلامی',
 evidenceRef:'opaque-evidence-ref',claimedOccurredAt:null,
 recordedAt:'2026-10-13T10:00:00Z',verificationStatus:'UNVERIFIED'};
async function enter(p:import('@playwright/test').Page){
 await p.route('**/api/v1/identity/center-memberships',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({memberships:[
   {centerId:'c1',displayName:'کانون ۱',roleCode:'member'},
   {centerId:'c2',displayName:'کانون ۲',roleCode:'member'}]})}));
 await p.route('**/api/v1/centers/*/local-plan-drafts',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({drafts:[plan]})}));
 await p.route('**/api/v1/centers/*/operations/local-plan-workspace/*/*',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify(overview)}));
 await p.route('**/api/v1/centers/*/operations/work-cards/*/session-links',r=>r.fulfill({
  status:200,contentType:'application/json',
  body:JSON.stringify({workCardId:card,links:[sessionRecord],
   nextBeforeSessionId:null,...flags})}));
 await p.goto('/');
 await p.getByRole('textbox',{name:'توکن دسترسی'}).fill('synthetic');
 await p.getByRole('button',{name:'دریافت عضویت‌ها'}).click();
 await p.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c1');
 const workspace=p.locator('section[aria-label="میزکار یکپارچه طرح بومی"]');
 await workspace.getByRole('button',{name:'دریافت طرح‌های واقعی کانون'}).click();
 await workspace.getByRole('combobox',{name:'انتخاب نسخه طرح برای میزکار'}).selectOption(draft+':1');
 await workspace.getByRole('button',{name:'نمایش کارت‌ها و نیازهای این نسخه طرح'}).click();
 await p.locator('section[aria-label="جلسات آموزشی مرتبط با کارت '+card+'"]')
  .getByRole('button',{name:'مشاهده جلسات پیوندخورده'}).click();
 return p.locator('section[aria-label="گزارش خصوصی جلسه پیوندخورده '+session+'"]');
}
const traceRoute='**/api/v1/centers/*/local-plan-drafts/*/execution-trace';
const reportRoute='**/api/v1/centers/*/activity-reports/sourced';
test('real S41 linked session → exact S10 issuance → original S17 UNVERIFIED report → S16 dossier',async({page:p})=>{
 const received:any[]=[];
 await p.route(traceRoute,r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({draftId:draft,trace})}));
 await p.route(reportRoute,r=>{
  const data=r.request().postDataJSON();received.push(data);
  return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
   reportId:data.reportId,draftId:data.draftId,linkId:data.linkId,
   sourceKind:data.sourceKind,sourceId:data.sourceId,verificationStatus:'UNVERIFIED'})});
 });
 await p.route(/\/class-sessions\/[^/]+\/report-lineage(?:\?.*)?$/,r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
   sessionId:session,classId:clazz,sessionTitle:'جلسه اصیل',classTitle:'کلاس اصیل',
   scheduledAt:null,activityReports:[report],nextBeforeSequence:null})}));
 const x=await enter(p);
 await x.getByRole('button',{name:'بررسی پیوند ابلاغ معتبر همین نسخه طرح'}).click();
 await expect(x.getByText('ابلاغ '+issuance,{exact:false})).toBeVisible();
 await x.getByRole('textbox',{name:'شرح گزارش جلسه '+session}).fill('گزارش انسانی اعلامی');
 await x.getByRole('textbox',{name:'ارجاع شواهد جلسه '+session}).fill('opaque-evidence-ref');
 await x.getByRole('button',{name:'ثبت گزارش جلسه به صورت راستی‌آزمایی‌نشده'}).click();
 await expect(x.getByRole('status')).toContainText('همچنان UNVERIFIED');
 expect(received).toHaveLength(1);
 expect(received[0]).toMatchObject({draftId:draft,linkId,sourceKind:'CLASS_SESSION',
  sourceId:session,reportedSummary:'گزارش انسانی اعلامی',evidenceRef:'opaque-evidence-ref',
  claimedOccurredAt:null});
 expect(received[0].reportId).toMatch(/^[0-9a-f-]{36}$/);
 await x.getByRole('button',{name:'مشاهده پرونده اصلی گزارش جلسه'}).click();
 await x.getByRole('button',{name:'دریافت پرونده گزارش جلسه'}).click();
 await expect(x.getByText('گزارش انسانی اعلامی — راستی‌آزمایی‌نشده')).toBeVisible();
 await p.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c2');
 await expect(p.locator('section[aria-label="گزارش خصوصی جلسه پیوندخورده '+session+'"]')).toHaveCount(0);
});
test('no original issuance or wrong pinned version means no report writer and no POST',async({page:p})=>{
 const attempted:any[]=[];
 await p.route(reportRoute,r=>{attempted.push(r.request().postDataJSON());return r.abort();});
 await p.route(traceRoute,r=>r.fulfill({status:200,contentType:'application/json',
  body:JSON.stringify({draftId:draft,trace:null})}));
 const x=await enter(p);
 await x.getByRole('button',{name:'بررسی پیوند ابلاغ معتبر همین نسخه طرح'}).click();
 await expect(x.getByText('برای این طرح پیوند ابلاغ تثبیت‌شده موجود نیست',{exact:false})).toBeVisible();
 await expect(x.getByRole('button',{name:'ثبت گزارش جلسه به صورت راستی‌آزمایی‌نشده'})).toHaveCount(0);
 await p.unroute(traceRoute);
 await p.route(traceRoute,r=>r.fulfill({status:200,contentType:'application/json',
  body:JSON.stringify({draftId:draft,trace:{...trace,pinnedRevision:2}})}));
 await x.getByRole('button',{name:'بررسی پیوند ابلاغ معتبر همین نسخه طرح'}).click();
 await expect(x.getByText('نسخه یا برنامه ملی پیوند ابلاغ',{exact:false})).toBeVisible();
 expect(attempted).toHaveLength(0);
});
test('separate S17 write denial and forged VERIFIED receipt cannot become execution',async({page:p})=>{
 await p.route(traceRoute,r=>r.fulfill({status:200,contentType:'application/json',
  body:JSON.stringify({draftId:draft,trace})}));
 await p.route(reportRoute,r=>r.fulfill({
  status:403,contentType:'application/problem+json',body:'{}'}));
 const x=await enter(p);
 await x.getByRole('button',{name:'بررسی پیوند ابلاغ معتبر همین نسخه طرح'}).click();
 await x.getByRole('textbox',{name:'شرح گزارش جلسه '+session}).fill('ادعای یک');
 await x.getByRole('textbox',{name:'ارجاع شواهد جلسه '+session}).fill('opaque-ref');
 await x.getByRole('button',{name:'ثبت گزارش جلسه به صورت راستی‌آزمایی‌نشده'}).click();
 await expect(x.getByText('مجوز مستقل ثبت گزارش ادعایی وجود ندارد.')).toBeVisible();
 await p.unroute(reportRoute);
 await p.route(reportRoute,r=>{
  const data=r.request().postDataJSON();
  return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
   reportId:data.reportId,draftId:draft,linkId,sourceKind:'CLASS_SESSION',
   sourceId:session,verificationStatus:'VERIFIED'})});
 });
 await x.getByRole('button',{name:'ثبت گزارش جلسه به صورت راستی‌آزمایی‌نشده'}).click();
 await expect(x.getByText('تأیید معتبر ذخیره گزارش دریافت نشد',{exact:false})).toBeVisible();
 await expect(x.getByText('گزارش اصلی ثبت شد',{exact:false})).toHaveCount(0);
});
test('independent S16 dossier denial does not create S17 report rights',async({page:p})=>{
 await p.route(traceRoute,r=>r.fulfill({status:403,contentType:'application/problem+json',body:'{}'}));
 await p.route(/\/class-sessions\/[^/]+\/report-lineage(?:\?.*)?$/,r=>r.fulfill({
  status:403,contentType:'application/problem+json',body:'{}'}));
 const x=await enter(p);
 await x.getByRole('button',{name:'بررسی پیوند ابلاغ معتبر همین نسخه طرح'}).click();
 await expect(x.getByText('مجوز مستقل مشاهده پیوند ابلاغ وجود ندارد.')).toBeVisible();
 await x.getByRole('button',{name:'مشاهده پرونده اصلی گزارش جلسه'}).click();
 await x.getByRole('button',{name:'دریافت پرونده گزارش جلسه'}).click();
 await expect(x.getByText('مجوز مستقل پرونده گزارش جلسه تأیید نشده است.')).toBeVisible();
 await expect(x.getByRole('button',{name:'ثبت گزارش جلسه به صورت راستی‌آزمایی‌نشده'})).toHaveCount(0);
});

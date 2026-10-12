import { test, expect } from '@playwright/test';
test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE!=='center','Center source report create only');
const id=(n:number)=>'10000000-0000-4000-8000-'+n.toString().padStart(12,'0');
const draft=id(90),link=id(91),session=id(92);
async function open(page:import('@playwright/test').Page) {
 await page.route('**/api/v1/identity/center-memberships',r=>r.fulfill({
   status:200,contentType:'application/json',body:JSON.stringify({memberships:[
     {centerId:'c1',displayName:'کانون ۱',roleCode:'member'},
     {centerId:'c2',displayName:'کانون ۲',roleCode:'member'}]})}));
 await page.route('**/api/v1/centers/*/local-plan-drafts',r=>r.fulfill({
   status:200,contentType:'application/json',body:JSON.stringify({
     drafts:[{draftId:draft,revision:1,programId:'national-1',
       nationalVersion:1,localTitle:'طرح آزمایشی',localActivities:['کلاس']}]
   })}));
 await page.route('**/api/v1/centers/*/local-plan-drafts/*/execution-trace',r=>r.fulfill({
   status:200,contentType:'application/json',body:JSON.stringify({draftId:draft,trace:{
     linkId:link,issuanceId:id(93),pinnedRevision:1,latestRevision:1,
     programId:'national-1',nationalVersion:1,localTitle:'طرح آزمایشی',plannedActivities:['کلاس']
   }})}));
 await page.route('**/api/v1/centers/*/activity-sources/class-sessions**',r=>r.fulfill({
   status:200,contentType:'application/json',body:JSON.stringify({
     sourceKind:'CLASS_SESSION',records:[{sourceId:session,title:'جلسه آزمایشی',
       classId:id(94),classTitle:'کلاس',scheduledAt:null}],nextBeforeSourceId:null
   })}));
 await page.goto('/');
 await page.getByRole('textbox',{name:'توکن دسترسی'}).fill('synthetic');
 await page.getByRole('button',{name:'دریافت عضویت‌ها'}).click();
 await page.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c1');
 await page.getByRole('button',{name:'دریافت پیش‌نویس‌های ثبت‌شده'}).click();
 await page.getByRole('button',{name:'پیوند ابلاغ ملی'}).click();
 await page.getByRole('button',{name:'دریافت پیوند برنامه'}).click();
 await page.getByRole('button',{name:'دریافت منابع برای گزارش فعالیت'}).click();
}
test('submits immutable typed source with exact pinned plan and explicit UNVERIFIED acknowledgment',async({page})=>{
 let body:Record<string,unknown>|null=null;
 await page.route('**/api/v1/centers/*/activity-reports/sourced',async r=>{
   body=r.request().postDataJSON();
   await r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
      reportId:body!.reportId,draftId:draft,linkId:link,sourceKind:'CLASS_SESSION',
      sourceId:session,verificationStatus:'UNVERIFIED'
   })});
 });
 await open(page);
 await page.getByRole('combobox',{name:'منبع گزارش فعالیت'}).selectOption(session);
 await page.getByRole('textbox',{name:'شرح گزارش فعالیت'}).fill('صرفاً گزارش آزمایشی');
 await page.getByRole('textbox',{name:'ارجاع شواهد گزارش'}).fill('synthetic-evidence');
 await page.getByRole('button',{name:'ثبت گزارش فعالیت تأییدنشده'}).click();
 await expect(page.getByText('گزارش فعالیت ثبت شد؛ همچنان راستی‌آزمایی‌نشده است.')).toBeVisible();
 expect(body).toMatchObject({draftId:draft,linkId:link,sourceKind:'CLASS_SESSION',
     sourceId:session,reportedSummary:'صرفاً گزارش آزمایشی',evidenceRef:'synthetic-evidence',
     claimedOccurredAt:null});
 expect(typeof body!.reportId).toBe('string');
 await page.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c2');
 await expect(page.getByText('گزارش فعالیت ثبت شد؛ همچنان راستی‌آزمایی‌نشده است.')).toHaveCount(0);
});
test('write permission denial is visible and never reports success',async({page})=>{
 await page.route('**/api/v1/centers/*/activity-reports/sourced',r=>
   r.fulfill({status:403,contentType:'application/problem+json',body:'{}'}));
 await open(page);
 await page.getByRole('combobox',{name:'منبع گزارش فعالیت'}).selectOption(session);
 await page.getByRole('textbox',{name:'شرح گزارش فعالیت'}).fill('گزارش');
 await page.getByRole('textbox',{name:'ارجاع شواهد گزارش'}).fill('unverified-ref');
 await page.getByRole('button',{name:'ثبت گزارش فعالیت تأییدنشده'}).click();
 await expect(page.getByRole('alert')).toContainText('مجوز مستقل ثبت گزارش فعالیت');
 await expect(page.getByText(/گزارش فعالیت ثبت شد؛/)).toHaveCount(0);
});
test('forged verified storage acknowledgment fails closed',async({page})=>{
 await page.route('**/api/v1/centers/*/activity-reports/sourced',r=>
   r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
     reportId:id(95),draftId:draft,linkId:link,sourceKind:'CLASS_SESSION',
     sourceId:session,verificationStatus:'VERIFIED'
   })}));
 await open(page);
 await page.getByRole('combobox',{name:'منبع گزارش فعالیت'}).selectOption(session);
 await page.getByRole('textbox',{name:'شرح گزارش فعالیت'}).fill('گزارش');
 await page.getByRole('textbox',{name:'ارجاع شواهد گزارش'}).fill('unverified-ref');
 await page.getByRole('button',{name:'ثبت گزارش فعالیت تأییدنشده'}).click();
 await expect(page.getByRole('alert')).toContainText('تأیید نشد');
 await expect(page.getByText(/گزارش فعالیت ثبت شد؛/)).toHaveCount(0);
});

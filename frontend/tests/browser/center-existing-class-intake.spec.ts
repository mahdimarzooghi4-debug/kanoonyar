import {test,expect} from '@playwright/test';
test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE!=='center','S43 original S18/S19 class intake');
const cls='aaaaaaaa-aaaa-4aaa-8aaa-000000000043';
const other='bbbbbbbb-bbbb-4bbb-8bbb-000000000043';
const session='cccccccc-cccc-4ccc-8ccc-000000000043';
const draft='dddddddd-dddd-4ddd-8ddd-000000000043';
const card='eeeeeeee-eeee-4eee-8eee-000000000043';
const request='ffffffff-ffff-4fff-8fff-000000000043';
const applicant='12345678-1234-4234-8234-123456789043';
const source={sourceId:session,classId:cls,title:'جلسه واقعی',classTitle:'کلاس واقعی قدیمی',scheduledAt:null};
const queue={requestId:request,applicantRef:applicant,capacityRevision:1,
 recordedAt:'2026-10-13T09:00:00Z',state:'REQUESTED',seatReserved:false};
const intake={classId:cls,currentRevision:1,configuredCapacity:8,
 confirmedEnrollmentCount:null,registrationIntents:[queue],nextBeforeRequestId:null};
async function enter(p:import('@playwright/test').Page){
 await p.route('**/api/v1/identity/center-memberships',r=>r.fulfill({status:200,
  contentType:'application/json',body:JSON.stringify({memberships:[
  {centerId:'c1',displayName:'کانون نخست',roleCode:'member'},
  {centerId:'c2',displayName:'کانون دوم',roleCode:'member'}]})}));
 await p.goto('/');
 await p.getByRole('textbox',{name:'توکن دسترسی'}).fill('synthetic');
 await p.getByRole('button',{name:'دریافت عضویت‌ها'}).click();
 await p.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c1');
}
async function linked(p:import('@playwright/test').Page){
 const local={draftId:draft,revision:1,programId:'national-1',nationalVersion:1,
  localTitle:'طرح اصیل',localActivities:[]};
 const work={workCardId:card,draftId:draft,pinnedPlanRevision:1,revision:1,
  title:'کار اصلی',planningNote:'فقط برنامه',plannedDueAt:null,
  recordedAt:'2026-10-13T09:00:00Z',needs:[],needsPreviewLimit:5,
  nextBeforeNeedId:null,visibility:'PRIVATE_DRAFT',executionStatus:'UNKNOWN',isExecuted:false};
 const flags={visibility:'PRIVATE_PLANNING',isPublished:false,isExecuted:false,
  isAttendanceVerified:false,isDeliveryVerified:false};
 await p.route('**/api/v1/centers/*/local-plan-drafts',r=>r.fulfill({status:200,
  contentType:'application/json',body:JSON.stringify({drafts:[local]})}));
 await p.route('**/api/v1/centers/*/operations/local-plan-workspace/*/*',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
   sourcePlan:{draftId:draft,pinnedPlanRevision:1,localTitle:'طرح اصیل',
    programId:'national-1',nationalVersion:1,visibility:'PRIVATE_DRAFT',isPublished:false},
   cards:[work],nextBeforeCardId:null,visibility:'PRIVATE_PLANNING',
   executionStatus:'UNKNOWN',fulfillmentStatus:'UNKNOWN',isExecuted:false,
   isAllocated:false,isPurchased:false,isFulfilled:false})}));
 await p.route('**/api/v1/centers/*/operations/work-cards/*/session-links',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({workCardId:card,
   links:[{sessionId:session,classId:cls,sessionTitle:'جلسه واقعی',
    classTitle:'کلاس واقعی قدیمی',scheduledAt:null,recordedAt:'2026-10-13T09:00:00Z',
    ...flags}],nextBeforeSessionId:null,...flags})}));
 const workspace=p.locator('section[aria-label="میزکار یکپارچه طرح بومی"]');
 await workspace.getByRole('button',{name:'دریافت طرح‌های واقعی کانون'}).click();
 await workspace.getByRole('combobox',{name:'انتخاب نسخه طرح برای میزکار'}).selectOption(draft+':1');
 await workspace.getByRole('button',{name:'نمایش کارت‌ها و نیازهای این نسخه طرح'}).click();
 const panel=p.locator('section[aria-label="جلسات آموزشی مرتبط با کارت '+card+'"]');
 await panel.getByRole('button',{name:'مشاهده جلسات پیوندخورده'}).click();
 return panel;
}
test('existing class -> original capacity -> REQUESTED queue -> independent human-review question',async({page:p})=>{
 const capacityWrites:any[]=[],requests:any[]=[],reviews:any[]=[];
 await p.route(/\/activity-sources\/class-sessions(?:\?.*)?$/,r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
   sourceKind:'CLASS_SESSION',records:[source],nextBeforeSourceId:null})}));
 await p.route(/\/classes\/[^/]+\/intake(?:\?.*)?$/,r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify(intake)}));
 await p.route(/\/classes\/[^/]+\/capacity-revisions$/,r=>{
  const v=r.request().postDataJSON();capacityWrites.push(v);
  return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
   classId:cls,revision:v.expectedRevision+1,capacity:v.capacity,confirmedEnrollmentCount:null})});
 });
 await p.route(/\/classes\/[^/]+\/registration-intents$/,r=>{
  const v=r.request().postDataJSON();requests.push(v);
  return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
   classId:cls,requestId:v.requestId,capacityRevision:2,state:'REQUESTED',seatReserved:false})});
 });
 await p.route(/\/classes\/[^/]+\/registration-intents\/[^/]+\/review-requests$/,r=>{
  if(r.request().method()==='POST'){
   const v=r.request().postDataJSON();reviews.push(v);
   return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
    classId:cls,requestId:request,reviewRequestId:v.reviewRequestId,reviewTopic:v.reviewTopic,
    reviewStatus:'REVIEW_REQUIRED',enrollmentDecision:null,seatReserved:false})});
  }
  return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
   classId:cls,requestId:request,registrationState:'REQUESTED',enrollmentDecision:null,
   seatReserved:false,reviewRequests:[]})});
 });
 await enter(p);
 const catalogue=p.locator('section[aria-label="مدیریت درخواست کلاس‌های موجود"]');
 await catalogue.getByRole('button',{name:'دریافت کلاس‌ها از جلسات واقعی'}).click();
 await expect(catalogue.getByText('کلاس واقعی قدیمی',{exact:true})).toBeVisible();
 await catalogue.getByRole('button',{name:'مدیریت ظرفیت و درخواست‌های همین کلاس'}).click();
 const intakeView=catalogue.locator('section[aria-label="ظرفیت و درخواست اولیه کلاس"]');
 await intakeView.getByRole('button',{name:'دریافت وضعیت ظرفیت'}).click();
 await expect(intakeView.getByText('آمار ثبت‌نام قطعی: نامعلوم؛ از درخواست‌های اولیه استنتاج نمی‌شود.')).toBeVisible();
 await intakeView.getByRole('spinbutton',{name:'ظرفیت جدید کلاس'}).fill('9');
 await intakeView.getByRole('button',{name:'ثبت نسخه ظرفیت کلاس'}).click();
 expect(capacityWrites).toEqual([{expectedRevision:1,capacity:9}]);
 await expect(intakeView.getByText('هیچ ثبت‌نامی تأیید نشد',{exact:false})).toBeVisible();
 await intakeView.getByRole('textbox',{name:'شناسه UUID مخاطب'}).fill(applicant);
 await intakeView.getByRole('button',{name:'ثبت درخواست اولیه بدون رزرو'}).click();
 expect(requests).toHaveLength(1);
 expect(requests[0]).toMatchObject({applicantRef:applicant});
 await expect(intakeView.getByText('درخواست در حالت REQUESTED ذخیره شد',{exact:false})).toBeVisible();
 await intakeView.getByRole('button',{name:'بررسی انسانی درخواست'}).click();
 const review=intakeView.locator('section[aria-label="نیازهای بررسی انسانی ثبت‌نام"]');
 await review.getByRole('button',{name:'دریافت نیازهای بررسی'}).click();
 await expect(review.getByText('هیچ موضوعی ثبت نشده؛ این به معنی تأیید هیچ شرطی نیست.')).toBeVisible();
 await review.getByRole('combobox',{name:'موضوع بررسی انسانی'}).selectOption('CONSENT_AUTHORITY');
 await review.getByRole('button',{name:'ثبت نیاز به بررسی انسانی'}).click();
 expect(reviews).toHaveLength(1);
 expect(reviews[0].reviewTopic).toBe('CONSENT_AUTHORITY');
 await expect(review.getByText('هیچ تأیید یا پذیرشی انجام نشد',{exact:false})).toBeVisible();
 await p.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c2');
 await expect(p.locator('section[aria-label="مدیریت درخواست کلاس‌های موجود"]')
  .getByText('کلاس واقعی قدیمی',{exact:true})).toHaveCount(0);
});
test('linked S41 session opens original class intake without any derived enrollment right',async({page:p})=>{
 await p.route(/\/classes\/[^/]+\/intake(?:\?.*)?$/,r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify(intake)}));
 await enter(p);
 const panel=await linked(p);
 await panel.getByRole('button',{name:'درخواست‌ها و بررسی انسانی این کلاس'}).click();
 const original=panel.locator('section[aria-label="ظرفیت و درخواست اولیه کلاس"]');
 await original.getByRole('button',{name:'دریافت وضعیت ظرفیت'}).click();
 await expect(original.getByText('شناسه درخواست: '+request,{exact:false})).toBeVisible();
 await expect(original.getByText('آمار ثبت‌نام قطعی: نامعلوم؛ از درخواست‌های اولیه استنتاج نمی‌شود.')).toBeVisible();
});
test('forged confirmed enrollment in S18 and forged consent in S19 are rejected',async({page:p})=>{
 await p.route(/\/activity-sources\/class-sessions(?:\?.*)?$/,r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
   sourceKind:'CLASS_SESSION',records:[source],nextBeforeSourceId:null})}));
 await p.route(/\/classes\/[^/]+\/intake(?:\?.*)?$/,r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
   ...intake,confirmedEnrollmentCount:2})}));
 await enter(p);
 const catalogue=p.locator('section[aria-label="مدیریت درخواست کلاس‌های موجود"]');
 await catalogue.getByRole('button',{name:'دریافت کلاس‌ها از جلسات واقعی'}).click();
 await catalogue.getByRole('button',{name:'مدیریت ظرفیت و درخواست‌های همین کلاس'}).click();
 let original=catalogue.locator('section[aria-label="ظرفیت و درخواست اولیه کلاس"]');
 await original.getByRole('button',{name:'دریافت وضعیت ظرفیت'}).click();
 await expect(original.getByText('اطلاعات ظرفیت در دسترس نیست.')).toBeVisible();
 await expect(original.getByRole('button',{name:'بررسی انسانی درخواست'})).toHaveCount(0);
 await p.unroute(/\/classes\/[^/]+\/intake(?:\?.*)?$/);
 await p.route(/\/classes\/[^/]+\/intake(?:\?.*)?$/,r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify(intake)}));
 await original.getByRole('button',{name:'دریافت وضعیت ظرفیت'}).click();
 await original.getByRole('button',{name:'بررسی انسانی درخواست'}).click();
 const review=original.locator('section[aria-label="نیازهای بررسی انسانی ثبت‌نام"]');
 await p.route(/\/classes\/[^/]+\/registration-intents\/[^/]+\/review-requests$/,r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
   classId:cls,requestId:request,registrationState:'REQUESTED',
   enrollmentDecision:'APPROVED',seatReserved:true,reviewRequests:[]})}));
 await review.getByRole('button',{name:'دریافت نیازهای بررسی'}).click();
 await expect(review.getByText('نمای بررسی معتبر دریافت نشد.')).toBeVisible();
 await expect(review.getByRole('button',{name:'ثبت نیاز به بررسی انسانی'})).toHaveCount(0);
});
test('source catalog permission and S18 intake permission are independent and fail closed',async({page:p})=>{
 await p.route(/\/activity-sources\/class-sessions(?:\?.*)?$/,r=>r.fulfill({
  status:403,contentType:'application/problem+json',body:'{}'}));
 await enter(p);
 const catalogue=p.locator('section[aria-label="مدیریت درخواست کلاس‌های موجود"]');
 await catalogue.getByRole('button',{name:'دریافت کلاس‌ها از جلسات واقعی'}).click();
 await expect(catalogue.getByText('مجوز مستقل کاتالوگ کلاس و جلسه وجود ندارد.')).toBeVisible();
 await expect(catalogue.getByRole('button',{name:'مدیریت ظرفیت و درخواست‌های همین کلاس'})).toHaveCount(0);
 await p.unroute(/\/activity-sources\/class-sessions(?:\?.*)?$/);
 await p.route(/\/activity-sources\/class-sessions(?:\?.*)?$/,r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
   sourceKind:'CLASS_SESSION',records:[source],nextBeforeSourceId:null})}));
 await p.route(/\/classes\/[^/]+\/intake(?:\?.*)?$/,r=>r.fulfill({
  status:403,contentType:'application/problem+json',body:'{}'}));
 await catalogue.getByRole('button',{name:'دریافت کلاس‌ها از جلسات واقعی'}).click();
 await catalogue.getByRole('button',{name:'مدیریت ظرفیت و درخواست‌های همین کلاس'}).click();
 const intakeView=catalogue.locator('section[aria-label="ظرفیت و درخواست اولیه کلاس"]');
 await intakeView.getByRole('button',{name:'دریافت وضعیت ظرفیت'}).click();
 await expect(intakeView.getByText('مجوز مستقل مشاهده ظرفیت و درخواست‌ها تأیید نشده است.')).toBeVisible();
 await expect(intakeView.getByRole('button',{name:'بررسی انسانی درخواست'})).toHaveCount(0);
});
test('21 session cursor shows classes discovered in page2 without duplicating original class',async({page:p})=>{
 const sessions=Array.from({length:21},(_,i)=>({
  sourceId:'00000000-0000-4000-8000-'+String(21-i).padStart(12,'0'),
  classId:i===20?other:cls,title:'جلسه '+i,classTitle:i===20?'کلاس دوم':'کلاس واقعی قدیمی',scheduledAt:null}));
 await p.route(/\/activity-sources\/class-sessions(?:\?.*)?$/,r=>{
  const page2=r.request().url().includes('beforeSourceId=');
  return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
   sourceKind:'CLASS_SESSION',records:page2?sessions.slice(20):sessions.slice(0,20),
   nextBeforeSourceId:page2?null:sessions[19].sourceId})});
 });
 await enter(p);
 const catalogue=p.locator('section[aria-label="مدیریت درخواست کلاس‌های موجود"]');
 await catalogue.getByRole('button',{name:'دریافت کلاس‌ها از جلسات واقعی'}).click();
 await expect(catalogue.getByRole('button',{name:'مدیریت ظرفیت و درخواست‌های همین کلاس'})).toHaveCount(1);
 await catalogue.getByRole('button',{name:'جلسات بعدی برای یافتن کلاس‌های بیشتر'}).click();
 await expect(catalogue.getByRole('button',{name:'مدیریت ظرفیت و درخواست‌های همین کلاس'})).toHaveCount(2);
 await expect(catalogue.getByText('کلاس دوم',{exact:true})).toBeVisible();
});

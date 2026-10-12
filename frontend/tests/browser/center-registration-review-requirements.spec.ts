import{test,expect}from'@playwright/test';
test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE!=='center','Review intake only');
const uuid=(n:number)=>'10000000-0000-4000-8000-'+n.toString().padStart(12,'0');
const cls=uuid(401),session=uuid(402),request=uuid(403),applicant=uuid(404);
async function open(page:import('@playwright/test').Page){
 await page.route('**/api/v1/identity/center-memberships',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({memberships:[
   {centerId:'c1',displayName:'کانون ۱',roleCode:'member'},
   {centerId:'c2',displayName:'کانون ۲',roleCode:'member'}]})}));
 await page.route('**/api/v1/centers/*/activity-sources/class-sessions**',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
   sourceKind:'CLASS_SESSION',records:[{sourceId:session,title:'جلسه تست',
    classId:cls,classTitle:'کلاس تست',scheduledAt:null}],nextBeforeSourceId:null})}));
 await page.route('**/api/v1/centers/*/classes/*/intake**',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
   classId:cls,currentRevision:1,configuredCapacity:2,confirmedEnrollmentCount:null,
   registrationIntents:[{requestId:request,applicantRef:applicant,capacityRevision:1,
     recordedAt:'2026-10-12T10:00:00Z',state:'REQUESTED',seatReserved:false}],
   nextBeforeRequestId:null})}));
 await page.goto('/');
 await page.getByRole('textbox',{name:'توکن دسترسی'}).fill('synthetic');
 await page.getByRole('button',{name:'دریافت عضویت‌ها'}).click();
 await page.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c1');
 await page.getByRole('button',{name:'دریافت جلسات کلاس'}).click();
 await page.getByRole('button',{name:'ظرفیت و درخواست‌های کلاس'}).click();
 await page.getByRole('button',{name:'دریافت وضعیت ظرفیت'}).click();
 await page.getByRole('button',{name:'بررسی انسانی درخواست'}).click();
}
test('raises non-decisional review question with matching identities and clears across centers',async({page})=>{
 let submitted:any=null;
 await page.route('**/api/v1/centers/*/classes/*/registration-intents/*/review-requests',async r=>{
  if(r.request().method()==='GET')return r.fulfill({status:200,contentType:'application/json',
    body:JSON.stringify({classId:cls,requestId:request,registrationState:'REQUESTED',
       enrollmentDecision:null,seatReserved:false,reviewRequests:[]})});
  submitted=r.request().postDataJSON();
  await r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
    classId:cls,requestId:request,reviewRequestId:submitted.reviewRequestId,
    reviewTopic:submitted.reviewTopic,reviewStatus:'REVIEW_REQUIRED',
    enrollmentDecision:null,seatReserved:false
  })});
 });
 await open(page);
 await page.getByRole('button',{name:'دریافت نیازهای بررسی'}).click();
 await page.getByRole('combobox',{name:'موضوع بررسی انسانی'}).selectOption('CONSENT_AUTHORITY');
 await page.getByRole('button',{name:'ثبت نیاز به بررسی انسانی'}).click();
 await expect(page.getByText('نیاز به بررسی ثبت شد؛ هیچ تأیید یا پذیرشی انجام نشد.')).toBeVisible();
 expect(submitted.reviewTopic).toBe('CONSENT_AUTHORITY');
 expect(typeof submitted.reviewRequestId).toBe('string');
 await page.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c2');
 await expect(page.getByText('نیاز به بررسی ثبت شد؛ هیچ تأیید یا پذیرشی انجام نشد.')).toHaveCount(0);
});
test('a denied independent read grant exposes no review topic',async({page})=>{
 await page.route('**/api/v1/centers/*/classes/*/registration-intents/*/review-requests',r=>
  r.fulfill({status:403,contentType:'application/problem+json',body:'{}'}));
 await open(page);
 await page.getByRole('button',{name:'دریافت نیازهای بررسی'}).click();
 await expect(page.getByRole('alert')).toContainText('مجوز مستقل مشاهده بررسی انسانی');
});
test('a forged VERIFIED review result cannot be displayed',async({page})=>{
 await page.route('**/api/v1/centers/*/classes/*/registration-intents/*/review-requests',r=>
  r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
    classId:cls,requestId:request,registrationState:'REQUESTED',
    enrollmentDecision:null,seatReserved:false,reviewRequests:[{
      reviewRequestId:uuid(405),reviewTopic:'CONSENT_AUTHORITY',
      reviewStatus:'VERIFIED',recordedAt:'2026-10-12T10:00:00Z'
    }]
  })}));
 await open(page);
 await page.getByRole('button',{name:'دریافت نیازهای بررسی'}).click();
 await expect(page.getByRole('alert')).toContainText('نمای بررسی معتبر دریافت نشد.');
 await expect(page.getByText('نیازمند بررسی (REVIEW_REQUIRED)')).toHaveCount(0);
});

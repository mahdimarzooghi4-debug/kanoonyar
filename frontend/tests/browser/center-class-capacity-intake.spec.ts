import {test,expect} from '@playwright/test';
test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE!=='center','Center class intake only');
const id=(n:number)=>'10000000-0000-4000-8000-'+n.toString().padStart(12,'0');
const cls=id(301),session=id(302),applicant=id(303);
async function open(page:import('@playwright/test').Page){
 await page.route('**/api/v1/identity/center-memberships',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({memberships:[
    {centerId:'c1',displayName:'کانون ۱',roleCode:'member'},
    {centerId:'c2',displayName:'کانون ۲',roleCode:'member'}]})}));
 await page.route('**/api/v1/centers/*/activity-sources/class-sessions**',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
    sourceKind:'CLASS_SESSION',records:[{sourceId:session,title:'جلسه آزمایشی',
      classId:cls,classTitle:'کلاس آزمایشی',scheduledAt:null}],nextBeforeSourceId:null
  })}));
 await page.goto('/');
 await page.getByRole('textbox',{name:'توکن دسترسی'}).fill('synthetic');
 await page.getByRole('button',{name:'دریافت عضویت‌ها'}).click();
 await page.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c1');
 await page.getByRole('button',{name:'دریافت جلسات کلاس'}).click();
 await page.getByRole('button',{name:'ظرفیت و درخواست‌های کلاس'}).click();
}
const intake=(revision:number|null,capacity:number|null)=>({
 classId:cls,currentRevision:revision,configuredCapacity:capacity,
 confirmedEnrollmentCount:null,registrationIntents:[],nextBeforeRequestId:null
});
test('can configure a version and submit only a REQUESTED intake, then clear on center change',async({page})=>{
 let revision:number|null=null,capacity:number|null=null;let submitted:any=null;
 await page.route('**/api/v1/centers/*/classes/*/intake**',r=>r.fulfill({
   status:200,contentType:'application/json',body:JSON.stringify(intake(revision,capacity))
 }));
 await page.route('**/api/v1/centers/*/classes/*/capacity-revisions',async r=>{
   const input=r.request().postDataJSON();
   expect(input).toMatchObject({expectedRevision:0,capacity:2});
   revision=1;capacity=2;
   await r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
     classId:cls,revision,capacity,confirmedEnrollmentCount:null
   })});
 });
 await page.route('**/api/v1/centers/*/classes/*/registration-intents',async r=>{
   submitted=r.request().postDataJSON();
   await r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
     classId:cls,requestId:submitted.requestId,capacityRevision:1,state:'REQUESTED',seatReserved:false
   })});
 });
 await open(page);
 await page.getByRole('button',{name:'دریافت وضعیت ظرفیت'}).click();
 await expect(page.getByText(/ظرفیت تعریف‌شده: نامشخص/)).toBeVisible();
 await page.getByRole('spinbutton',{name:'ظرفیت جدید کلاس'}).fill('2');
 await page.getByRole('button',{name:'ثبت نسخه ظرفیت کلاس'}).click();
 await expect(page.getByText('نسخه جدید ظرفیت ثبت شد؛ هیچ ثبت‌نامی تأیید نشد.')).toBeVisible();
 await page.getByRole('textbox',{name:'شناسه UUID مخاطب'}).fill(applicant);
 await page.getByRole('button',{name:'ثبت درخواست اولیه بدون رزرو'}).click();
 await expect(page.getByText('درخواست در حالت REQUESTED ذخیره شد؛ ثبت‌نام قطعی نیست.')).toBeVisible();
 expect(submitted.applicantRef).toBe(applicant);
 expect(typeof submitted.requestId).toBe('string');
 await page.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c2');
 await expect(page.getByText('درخواست در حالت REQUESTED ذخیره شد؛ ثبت‌نام قطعی نیست.')).toHaveCount(0);
});
test('unauthorized class intake never exposes queue',async({page})=>{
 await page.route('**/api/v1/centers/*/classes/*/intake**',r=>
  r.fulfill({status:403,contentType:'application/problem+json',body:'{}'}));
 await open(page);
 await page.getByRole('button',{name:'دریافت وضعیت ظرفیت'}).click();
 await expect(page.getByRole('alert')).toContainText('مجوز مستقل مشاهده ظرفیت');
 await expect(page.getByText(/ظرفیت تعریف‌شده:/)).toHaveCount(0);
});
test('forged confirmed count or seat allocation fails closed',async({page})=>{
 await page.route('**/api/v1/centers/*/classes/*/intake**',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
     ...intake(1,2),confirmedEnrollmentCount:1
  })
 }));
 await open(page);
 await page.getByRole('button',{name:'دریافت وضعیت ظرفیت'}).click();
 await expect(page.getByRole('alert')).toContainText('اطلاعات ظرفیت در دسترس نیست.');
});

import {test,expect} from '@playwright/test';
test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE!=='center','Center-only history');
const session='10000000-0000-4000-8000-000000000019';
const id=(n:number)=>'10000000-0000-4000-8000-'+n.toString().padStart(12,'0');
const at='2026-10-12T10:00:00Z';
async function open(page:import('@playwright/test').Page){
  await page.route('**/api/v1/identity/center-memberships',r=>r.fulfill({
    status:200,contentType:'application/json',body:JSON.stringify({memberships:[
      {centerId:'c1',displayName:'کانون ۱',roleCode:'member'},
      {centerId:'c2',displayName:'کانون ۲',roleCode:'member'}]})
  }));
  await page.route('**/api/v1/centers/*/activity-sources/class-sessions',r=>r.fulfill({
    status:200,contentType:'application/json',body:JSON.stringify({
      sourceKind:'CLASS_SESSION',records:[{sourceId:session,
        title:'جلسه نمونه',classId:id(99),classTitle:'کلاس',scheduledAt:null}],
      nextBeforeSourceId:null})
  }));
  await page.goto('/');
  await page.getByRole('textbox',{name:'توکن دسترسی'}).fill('synthetic');
  await page.getByRole('button',{name:'دریافت عضویت‌ها'}).click();
  await page.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c1');
  await page.getByRole('button',{name:'دریافت جلسات کلاس'}).click();
  await page.getByRole('button',{name:'گزارش وقوع و حضور جلسه'}).click();
}
test('history paginates 20+2 and attendance remains an independent read',async({page})=>{
  const urls:string[]=[];
  await page.route('**/api/v1/centers/*/class-sessions/*/occurrence-claims**',r=>{
    const url=r.request().url();urls.push(url);
    if(url.includes('/attendance-claims')){
      return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
        sessionId:session,occurrenceReportId:id(2),
        attendanceClaims:[{claimId:id(75),participantRef:'opaque-fixture-only',
          reportedPresence:'REPORTED_PRESENT',recordedAt:at,verificationStatus:'UNVERIFIED'}],
        nextBeforeAt:null,nextBeforeId:null})});
    }
    const older=url.includes('beforeId='+id(3));
    const values=older?[2,1]:Array.from({length:20},(_,i)=>22-i);
    return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
      sessionId:session,occurrenceReports:values.map(n=>({
        occurrenceReportId:id(n),reportedNote:'ادعای جلسه '+n,evidenceRef:null,
        claimedStartedAt:null,claimedEndedAt:null,recordedAt:at,
        verificationStatus:'UNVERIFIED'
      })),nextBeforeAt:older?null:at,nextBeforeId:older?null:id(3)
    })});
  });
  await open(page);
  await page.getByRole('button',{name:'دریافت تاریخچه ادعاهای جلسه'}).click();
  await expect(page.getByText('ادعای جلسه 22',{exact:true})).toBeVisible();
  await expect(page.getByText('زمان ادعایی شروع: نامعلوم').first()).toBeVisible();
  await page.getByRole('button',{name:'گزارش‌های قدیمی‌تر'}).click();
  await expect(page.getByText('ادعای جلسه 1',{exact:true})).toBeVisible();
  await page.getByText('ادعای جلسه 2',{exact:true}).locator('..').getByRole('button',
    {name:'مشاهده ادعاهای حضور این گزارش'}).click();
  await expect(page.getByText(/opaque-fixture-only/)).toBeVisible();
  await expect(page.getByText('حاضر گزارش‌شده',{exact:false})).toBeVisible();
  expect(urls).toHaveLength(3);
  expect(urls[1]).toContain('beforeId='+id(3));
  await page.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c2');
  await expect(page.getByText('opaque-fixture-only')).toHaveCount(0);
  await expect(page.getByText('ادعای جلسه 22',{exact:true})).toHaveCount(0);
});
test('independent attendance denial reveals no participant references',async({page})=>{
  await page.route('**/api/v1/centers/*/class-sessions/*/occurrence-claims**',r=>{
    const url=r.request().url();
    if(url.includes('/attendance-claims'))
      return r.fulfill({status:403,contentType:'application/problem+json',body:'{}'});
    return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
      sessionId:session,occurrenceReports:[{occurrenceReportId:id(2),
        reportedNote:'گزارش قابل مشاهده',evidenceRef:null,
        claimedStartedAt:null,claimedEndedAt:null,recordedAt:at,
        verificationStatus:'UNVERIFIED'}],nextBeforeAt:null,nextBeforeId:null
    })});
  });
  await open(page);
  await page.getByRole('button',{name:'دریافت تاریخچه ادعاهای جلسه'}).click();
  await expect(page.getByText('گزارش قابل مشاهده')).toBeVisible();
  await page.getByRole('button',{name:'مشاهده ادعاهای حضور این گزارش'}).click();
  await expect(page.getByRole('alert')).toContainText('مجوز مستقل تاریخچه حضور و غیاب');
  await expect(page.getByText('opaque-fixture-only')).toHaveCount(0);
});
test('forged VERIFIED history fails closed',async({page})=>{
  await page.route('**/api/v1/centers/*/class-sessions/*/occurrence-claims**',r=>
    r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
      sessionId:session,occurrenceReports:[{occurrenceReportId:id(2),
        reportedNote:'Forged delivery',evidenceRef:null,claimedStartedAt:null,
        claimedEndedAt:null,recordedAt:at,verificationStatus:'VERIFIED'}],
      nextBeforeAt:null,nextBeforeId:null
    })}));
  await open(page);
  await page.getByRole('button',{name:'دریافت تاریخچه ادعاهای جلسه'}).click();
  await expect(page.getByRole('alert')).toContainText('دریافت تاریخچه وقوع جلسه ممکن نیست');
  await expect(page.getByText('Forged delivery')).toHaveCount(0);
});

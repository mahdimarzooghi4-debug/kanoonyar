import { test, expect } from '@playwright/test';
test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE !== 'center','Center reporting only');
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
      sourceKind:'CLASS_SESSION',records:[{sourceId:session,title:'جلسه',
        classId:id(99),classTitle:'کلاس آزمایشی',scheduledAt:null}],nextBeforeSourceId:null
    })
  }));
  await page.goto('/');
  await page.getByRole('textbox',{name:'توکن دسترسی'}).fill('synthetic');
  await page.getByRole('button',{name:'دریافت عضویت‌ها'}).click();
  await page.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c1');
  await page.getByRole('button',{name:'دریافت جلسات کلاس'}).click();
  await page.getByRole('button',{name:'گزارش وقوع و حضور جلسه'}).click();
}
const report=(n:number)=>({
  reportSequence:n,reportId:id(n),draftId:id(91),linkId:id(92),
  issuanceId:id(93),programId:'national-1',nationalVersion:1,pinnedRevision:1,
  reportedSummary:'گزارش آزمایشی '+n,evidenceRef:'synthetic-evidence-'+n,
  claimedOccurredAt:null,recordedAt:at,verificationStatus:'UNVERIFIED'
});
test('linked session reports paginate and stay unverified; center switch clears them',async({page})=>{
  const urls:string[]=[];
  await page.route('**/api/v1/centers/*/class-sessions/*/report-lineage**',r=>{
    const url=r.request().url();urls.push(url);
    const older=url.includes('beforeSequence=3');
    const rows=older?[2,1]:Array.from({length:20},(_,i)=>22-i);
    return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
      sessionId:session,classId:id(99),sessionTitle:'جلسه',classTitle:'کلاس آزمایشی',
      scheduledAt:null,activityReports:rows.map(report),nextBeforeSequence:older?null:3
    })});
  });
  await open(page);
  await page.getByRole('button',{name:'دریافت پرونده گزارش جلسه'}).click();
  await expect(page.getByText('گزارش آزمایشی 22 — راستی‌آزمایی‌نشده')).toBeVisible();
  await expect(page.getByText('برنامه ملی: national-1 / نسخه 1').first()).toBeVisible();
  await page.getByRole('button',{name:'گزارش‌های مرتبط قدیمی‌تر'}).click();
  await expect(page.getByText('گزارش آزمایشی 1 — راستی‌آزمایی‌نشده')).toBeVisible();
  expect(urls).toHaveLength(2);
  expect(urls[1]).toContain('beforeSequence=3');
  await page.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c2');
  await expect(page.getByText('گزارش آزمایشی 22 — راستی‌آزمایی‌نشده')).toHaveCount(0);
});
test('independent denial never reveals evidence or reports',async({page})=>{
  await page.route('**/api/v1/centers/*/class-sessions/*/report-lineage**',r=>
    r.fulfill({status:403,contentType:'application/problem+json',body:'{}'}));
  await open(page);
  await page.getByRole('button',{name:'دریافت پرونده گزارش جلسه'}).click();
  await expect(page.getByRole('alert')).toContainText('مجوز مستقل پرونده گزارش جلسه');
  await expect(page.getByText(/synthetic-evidence-/)).toHaveCount(0);
});
test('forged verified activity or wrong session fails closed',async({page})=>{
  await page.route('**/api/v1/centers/*/class-sessions/*/report-lineage**',r=>
    r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
      sessionId:session,classId:id(99),sessionTitle:'جلسه',classTitle:'کلاس آزمایشی',
      scheduledAt:null,activityReports:[{...report(1),verificationStatus:'VERIFIED'}],
      nextBeforeSequence:null
    })}));
  await open(page);
  await page.getByRole('button',{name:'دریافت پرونده گزارش جلسه'}).click();
  await expect(page.getByRole('alert')).toContainText('پرونده گزارش جلسه قابل دریافت نیست');
  await expect(page.getByText('گزارش آزمایشی 1 — راستی‌آزمایی‌نشده')).toHaveCount(0);
});

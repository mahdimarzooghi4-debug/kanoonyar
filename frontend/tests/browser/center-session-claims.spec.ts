import { test, expect } from '@playwright/test';
test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE!=='center','Center-only reporting');
const session='10000000-0000-4000-8000-000000000019';
async function open(page:import('@playwright/test').Page){
  await page.route('**/api/v1/identity/center-memberships',route=>route.fulfill({
    status:200,contentType:'application/json',
    body:JSON.stringify({memberships:[
      {centerId:'c1',displayName:'کانون اول',roleCode:'member'},
      {centerId:'c2',displayName:'کانون دوم',roleCode:'member'}]})
  }));
  await page.route('**/api/v1/centers/*/activity-sources/class-sessions',route=>
    route.fulfill({status:200,contentType:'application/json',
      body:JSON.stringify({sourceKind:'CLASS_SESSION',records:[{
        sourceId:session,title:'جلسه نمونه',
        classId:'20000000-0000-4000-8000-000000000022',
        classTitle:'کلاس نمونه',scheduledAt:null
      }],nextBeforeSourceId:null})}));
  await page.goto('/');
  await page.getByRole('textbox',{name:'توکن دسترسی'}).fill('synthetic-token');
  await page.getByRole('button',{name:'دریافت عضویت‌ها'}).click();
  await page.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c1');
  await page.getByRole('button',{name:'دریافت جلسات کلاس'}).click();
  await page.getByRole('button',{name:'گزارش وقوع و حضور جلسه'}).click();
}
test('claim source chain is unverified and zero unknown attendance is never inferred',async({page})=>{
  const commands:{url:string,body:Record<string,unknown>}[]=[];
  await page.route('**/api/v1/centers/*/class-sessions/*/*-claims',route=>{
    const body=route.request().postDataJSON() as Record<string,unknown>;
    commands.push({url:route.request().url(),body});
    expect(route.request().method()).toBe('POST');
    if(route.request().url().endsWith('/occurrence-claims'))
      return route.fulfill({status:200,contentType:'application/json',
        body:JSON.stringify({occurrenceReportId:body.occurrenceReportId,sessionId:session,
        verificationStatus:'UNVERIFIED'})});
    return route.fulfill({status:200,contentType:'application/json',
      body:JSON.stringify({claimId:body.claimId,sessionId:session,
      occurrenceReportId:body.occurrenceReportId,verificationStatus:'UNVERIFIED'})});
  });
  await open(page);
  await page.getByRole('textbox',{name:'شرح ادعای برگزاری'}).fill('گزارش آزمایشی');
  await page.getByRole('button',{name:'ثبت گزارش ادعای جلسه'}).click();
  await expect(page.getByText('گزارش ثبت شد؛ راستی‌آزمایی‌نشده است.')).toBeVisible();
  await page.getByRole('textbox',{name:'شناسه مستعار مخاطب'}).fill('opaque-test-only');
  await expect(page.getByRole('button',{name:'ثبت ادعای حضور/غیاب'})).toBeDisabled();
  await page.getByRole('combobox',{name:'وضعیت ادعاشده حضور'}).selectOption('REPORTED_PRESENT');
  await page.getByRole('button',{name:'ثبت ادعای حضور/غیاب'}).click();
  await expect(page.getByText('ادعای حضور/غیاب ثبت شد؛ تأیید نشده است.')).toBeVisible();
  expect(commands).toHaveLength(2);
  expect(commands[0].body.claimedStartedAt).toBeNull();
  expect(commands[1].body.occurrenceReportId).toBe(commands[0].body.occurrenceReportId);
  expect(commands[1].body.reportedPresence).toBe('REPORTED_PRESENT');
  await page.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c2');
  await expect(page.getByRole('region',{name:'گزارش وقوع و حضور جلسه'})).toHaveCount(0);
});
test('denied occurrence never creates attendance UI',async({page})=>{
  await page.route('**/api/v1/centers/*/class-sessions/*/occurrence-claims',route=>
    route.fulfill({status:403,contentType:'application/problem+json',body:'{}'}));
  await open(page);
  await page.getByRole('textbox',{name:'شرح ادعای برگزاری'}).fill('مورد محدود');
  await page.getByRole('button',{name:'ثبت گزارش ادعای جلسه'}).click();
  await expect(page.getByRole('alert')).toContainText('مجوز مستقل گزارش وقوع جلسه');
  await expect(page.getByRole('textbox',{name:'شناسه مستعار مخاطب'})).toHaveCount(0);
});
test('server VERIFIED response cannot be mistaken for legitimate claim confirmation',async({page})=>{
  await page.route('**/api/v1/centers/*/class-sessions/*/occurrence-claims',route=>
    route.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
      occurrenceReportId:'00000000-0000-4000-8000-000000000000',sessionId:session,
      verificationStatus:'VERIFIED'
    })}));
  await open(page);
  await page.getByRole('textbox',{name:'شرح ادعای برگزاری'}).fill('ادعای مشکوک');
  await page.getByRole('button',{name:'ثبت گزارش ادعای جلسه'}).click();
  await expect(page.getByRole('alert')).toContainText('ثبت گزارش وقوع جلسه تأیید نشد');
  await expect(page.getByRole('textbox',{name:'شناسه مستعار مخاطب'})).toHaveCount(0);
});

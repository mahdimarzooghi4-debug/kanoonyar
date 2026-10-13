import {test,expect} from '@playwright/test';
test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE!=='center','S45 original class session operations');
const cls='aaaaaaaa-aaaa-4aaa-8aaa-000000000045';
const first='bbbbbbbb-bbbb-4bbb-8bbb-000000000045';
const second='cccccccc-cccc-4ccc-8ccc-000000000045';
const src=(sourceId:string,title:string)=>({sourceId,classId:cls,title,
 classTitle:'کلاس قدیمی اصلی',scheduledAt:null});
async function enter(p:import('@playwright/test').Page){
 await p.route('**/api/v1/identity/center-memberships',r=>r.fulfill({status:200,
  contentType:'application/json',body:JSON.stringify({memberships:[
   {centerId:'c1',displayName:'کانون یک',roleCode:'member'},
   {centerId:'c2',displayName:'کانون دو',roleCode:'member'}]})}));
 await p.goto('/');
 await p.getByRole('textbox',{name:'توکن دسترسی'}).fill('synthetic-s45');
 await p.getByRole('button',{name:'دریافت عضویت‌ها'}).click();
 await p.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c1');
}
function sourceRoute(p:import('@playwright/test').Page){
 return p.route(/\/activity-sources\/class-sessions(?:\?.*)?$/,r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({sourceKind:'CLASS_SESSION',
   records:[src(second,'جلسه دوم'),src(first,'جلسه اول')],nextBeforeSourceId:null})}));
}
async function workspace(p:import('@playwright/test').Page){
 const panel=p.locator('section[aria-label="مدیریت درخواست کلاس‌های موجود"]');
 await panel.getByRole('button',{name:'دریافت کلاس‌ها از جلسات واقعی'}).click();
 await panel.getByRole('button',{name:'عملیات جلسات واقعی همین کلاس'}).click();
 return panel.locator('section[aria-label="میزکار جلسات کلاس '+cls+'"]');
}
test('existing class -> exact S12 session -> private S35 preparation -> original unverified S14 claim',async({page:p})=>{
 const prepWrites:any[]=[],occurWrites:any[]=[];
 await sourceRoute(p);
 await p.route('**/api/v1/centers/*/learning/session-preparations/revisions',r=>{
  const body=r.request().postDataJSON();prepWrites.push(body);
  return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
   sessionId:body.sessionId,classId:body.classId,revision:body.expectedRevision+1,
   visibility:'PRIVATE_PREPARATION',isPublished:false,deliveryVerified:false,
   isAttendanceVerified:false,isAssessed:false})});
 });
 await p.route('**/api/v1/centers/*/class-sessions/*/occurrence-claims',r=>{
  const body=r.request().postDataJSON();occurWrites.push({
   url:r.request().url(),body,auth:r.request().headers().authorization});
  return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
   occurrenceReportId:body.occurrenceReportId,sessionId:second,verificationStatus:'UNVERIFIED'})});
 });
 await enter(p);
 const desk=await workspace(p);
 await expect(desk.getByText('جلسه اول',{exact:true})).toBeVisible();
 await expect(desk.getByText('جلسه دوم',{exact:true})).toBeVisible();
 await desk.getByRole('button',{name:'آماده‌سازی خصوصی همین جلسه'}).first().click();
 const preparation=desk.locator('section[aria-label="دفتر آماده‌سازی خصوصی جلسات"]');
 await expect(preparation.getByText('جلسه اصلی انتخاب‌شده:',{exact:false})).toBeVisible();
 await expect(preparation.getByRole('combobox',{name:'انتخاب جلسه آموزشی'})).toHaveCount(0);
 await preparation.getByRole('textbox',{name:'عنوان آماده‌سازی جلسه'}).fill('برنامه خصوصی این جلسه');
 await preparation.getByRole('textbox',{name:'متن طرح تدریس'}).fill('صرفاً برنامه داخلی');
 await preparation.getByRole('button',{name:'ذخیره نسخه خصوصی آماده‌سازی'}).click();
 await expect(preparation.getByRole('status')).toContainText('درس منتشر یا اجرا نشده');
 expect(prepWrites).toEqual([{sessionId:second,classId:cls,expectedRevision:0,
  title:'برنامه خصوصی این جلسه',teachingOutline:'صرفاً برنامه داخلی',materialsNote:''}]);
 await desk.getByRole('button',{name:'ادعای وقوع و حضور همین جلسه'}).first().click();
 const report=desk.locator('section[aria-label="گزارش وقوع و حضور جلسه"]');
 await expect(report.getByText('شناسه جلسه: '+second)).toBeVisible();
 await report.getByRole('textbox',{name:'شرح ادعای برگزاری'}).fill('ادعای مربی');
 await report.getByRole('button',{name:'ثبت گزارش ادعای جلسه'}).click();
 await expect(report.getByRole('status')).toContainText('راستی‌آزمایی‌نشده');
 expect(occurWrites).toHaveLength(1);
 expect(occurWrites[0].body).toMatchObject({reportedNote:'ادعای مربی',evidenceRef:null,
  claimedStartedAt:null,claimedEndedAt:null});
 expect(occurWrites[0].url).toContain('/class-sessions/'+second+'/occurrence-claims');
 expect(occurWrites[0].auth).toBe('Bearer synthetic-s45');
 await p.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c2');
 await expect(p.locator('section[aria-label="میزکار جلسات کلاس '+cls+'"]')).toHaveCount(0);
});
test('source catalogue read never grants independent preparation or occurrence writes',async({page:p})=>{
 await sourceRoute(p);
 await p.route('**/api/v1/centers/*/learning/session-preparations/revisions',r=>r.fulfill({
  status:403,contentType:'application/problem+json',body:'{}'}));
 await p.route('**/api/v1/centers/*/class-sessions/*/occurrence-claims',r=>r.fulfill({
  status:403,contentType:'application/problem+json',body:'{}'}));
 await enter(p);
 const desk=await workspace(p);
 await desk.getByRole('button',{name:'آماده‌سازی خصوصی همین جلسه'}).first().click();
 const preparation=desk.locator('section[aria-label="دفتر آماده‌سازی خصوصی جلسات"]');
 await preparation.getByRole('textbox',{name:'عنوان آماده‌سازی جلسه'}).fill('بدون مجوز');
 await preparation.getByRole('button',{name:'ذخیره نسخه خصوصی آماده‌سازی'}).click();
 await expect(preparation.getByRole('alert')).toContainText('مجوز مستقل ثبت آماده‌سازی وجود ندارد.');
 await desk.getByRole('button',{name:'ادعای وقوع و حضور همین جلسه'}).first().click();
 const report=desk.locator('section[aria-label="گزارش وقوع و حضور جلسه"]');
 await report.getByRole('textbox',{name:'شرح ادعای برگزاری'}).fill('بدون مجوز');
 await report.getByRole('button',{name:'ثبت گزارش ادعای جلسه'}).click();
 await expect(report.getByRole('alert')).toContainText('مجوز مستقل گزارش وقوع جلسه تأیید نشده است.');
 await expect(report.getByRole('textbox',{name:'شناسه مستعار مخاطب'})).toHaveCount(0);
});
test('denied original source cannot expose session actions; unexpected source not selectable',async({page:p})=>{
 let claims=0;
 await p.route(/\/activity-sources\/class-sessions(?:\?.*)?$/,r=>r.fulfill({
  status:403,contentType:'application/problem+json',body:'{}'}));
 await p.route('**/api/v1/centers/*/class-sessions/*/occurrence-claims',r=>{
  claims++;return r.fulfill({status:500,body:'{}'});
 });
 await enter(p);
 const panel=p.locator('section[aria-label="مدیریت درخواست کلاس‌های موجود"]');
 await panel.getByRole('button',{name:'دریافت کلاس‌ها از جلسات واقعی'}).click();
 await expect(panel.getByRole('alert')).toContainText('مجوز مستقل کاتالوگ کلاس و جلسه وجود ندارد.');
 await expect(panel.getByRole('button',{name:'عملیات جلسات واقعی همین کلاس'})).toHaveCount(0);
 expect(claims).toBe(0);
});

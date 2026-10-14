import {test,expect} from '@playwright/test';
test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE!=='center',
 'S48 calendar handoff to canonical independent evidence and claims');

const eventId='22222222-2222-4222-8222-222222222248';
const sessionId='33333333-3333-4333-8333-333333333348';
const draftId='11111111-1111-4111-8111-111111111148';
const eventTime='2026-11-12T11:00:00Z';
const sessionTime='2026-11-09T10:00:00Z';
const base={visibility:'PRIVATE_PLAN' as const,isPublished:false as const,
 isExecuted:false as const,isAttendanceVerified:false as const};
const calendarSession={...base,sourceKind:'CLASS_SESSION' as const,sourceId:sessionId,
 scheduledAt:sessionTime,title:'جلسه منبع اصلی',draftId:null,
 pinnedPlanRevision:null,planRevision:null};
const calendarEvent={...base,sourceKind:'CENTER_EVENT' as const,sourceId:eventId,
 scheduledAt:eventTime,title:'رویداد برنامه‌ریزی‌شده اصلی',
 draftId,pinnedPlanRevision:1,planRevision:2};
function packet(items:unknown[]){
 return {fromUtc:'2026-11-01T00:00:00Z',untilUtc:'2026-12-01T00:00:00Z',
  items,nextCursor:null,...base};
}
const report={
 reportSequence:2,reportId:'44444444-4444-4444-8444-444444444448',
 linkId:'55555555-5555-4555-8555-555555555548',
 issuanceId:'66666666-6666-4666-8666-666666666648',
 programId:'national-1',nationalVersion:1,pinnedRevision:1,
 reportedSummary:'گزارش واقعی اما تأییدنشده رویداد',
 evidenceRef:'synthetic-evidence-48',claimedOccurredAt:null,
 recordedAt:'2026-10-13T08:00:00Z',
 sourceKind:'CENTER_EVENT',sourceRecordRef:eventId,
 verificationStatus:'UNVERIFIED',isApproved:false,isExecuted:false};
function dossier(items:unknown[]= [report], overrides:Record<string,unknown>={}){
 return {eventId,draftId,pinnedPlanRevision:1,revision:2,
  title:calendarEvent.title,purpose:'برنامه‌ریزی آزمون',
  locationDescription:'سالن آزمایشی',scheduledAt:eventTime,
  visibility:'PRIVATE_DRAFT',isPublished:false,isExecuted:false,
  isRegistrationOpen:false,verificationStatus:'UNVERIFIED',
  isApproved:false,isOutcome:false,activityReports:items,nextBeforeSequence:null,
  ...overrides};
}
async function enter(p:import('@playwright/test').Page){
 await p.route('**/api/v1/identity/center-memberships',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({memberships:[
   {centerId:'c1',displayName:'کانون ۱',roleCode:'member'},
   {centerId:'c2',displayName:'کانون ۲',roleCode:'member'}]})}));
 await p.goto('/');
 await p.getByRole('textbox',{name:'توکن دسترسی'}).fill('synthetic-s48');
 await p.getByRole('button',{name:'دریافت عضویت‌ها'}).click();
 await p.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c1');
 await p.locator('input[aria-label="شروع بازه تقویم"]').fill('2026-11-01');
 await p.locator('input[aria-label="پایان انحصاری بازه تقویم"]').fill('2026-12-01');
}
function permitCalendar(p:import('@playwright/test').Page,rows:unknown[]=[calendarSession,calendarEvent]){
 return p.route('**/api/v1/centers/*/schedule/private?*',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify(packet(rows))}));
}
const calendar=(p:import('@playwright/test').Page)=>p.locator('section[aria-label="تقویم خصوصی کانون"]');

test('actual S31 event -> exact S30 dossier and S14 class claim via separate original APIs',async({page:p})=>{
 let dossierUrl='',calendarCalls=0;const claims:Array<{url:string;body:any;auth:string|undefined}>=[];
 await p.route('**/api/v1/centers/*/schedule/private?*',r=>{
  calendarCalls++;return r.fulfill({status:200,contentType:'application/json',
   body:JSON.stringify(packet([calendarSession,calendarEvent]))});
 });
 await p.route('**/events/plans/*/source-dossier**',r=>{
  dossierUrl=r.request().url();return r.fulfill({
   status:200,contentType:'application/json',body:JSON.stringify(dossier())});
 });
 await p.route('**/api/v1/centers/*/class-sessions/*/occurrence-claims',r=>{
  const body=r.request().postDataJSON();
  claims.push({url:r.request().url(),body,auth:r.request().headers().authorization});
  return r.fulfill({status:200,contentType:'application/json',
   body:JSON.stringify({occurrenceReportId:body.occurrenceReportId,
    sessionId,verificationStatus:'UNVERIFIED'})});
 });
 await enter(p);
 const c=calendar(p);
 await c.getByRole('button',{name:'خواندن تقویم خصوصی'}).click();
 await expect(c.getByText(calendarEvent.title,{exact:true})).toBeVisible();
 await c.getByRole('button',{name:'پرونده شواهد همین رویداد از تقویم'}).click();
 await c.getByRole('button',{name:'بازکردن پرونده گزارش‌های منبع‌دار رویداد'}).click();
 await c.getByRole('button',{name:'خواندن گزارش‌های واقعی این رویداد'}).click();
 await expect(c.getByText('گزارش واقعی اما تأییدنشده رویداد — UNVERIFIED')).toBeVisible();
 expect(dossierUrl).toContain('/events/plans/'+eventId+'/source-dossier');
 await expect(c.getByText('بدون تأیید گزارش، برگزاری یا مشارکت')).toBeVisible();
 await c.getByRole('button',{name:'گزارش ادعای همین جلسه از تقویم'}).click();
 await expect(c.getByText('گزارش واقعی اما تأییدنشده رویداد — UNVERIFIED')).toHaveCount(0);
 const session=c.locator('section[aria-label="گزارش وقوع و حضور جلسه"]');
 await expect(session.getByText('شناسه جلسه: '+sessionId)).toBeVisible();
 await session.getByRole('textbox',{name:'شرح ادعای برگزاری'}).fill('ادعای انسانی آزمایشی');
 await session.getByRole('button',{name:'ثبت گزارش ادعای جلسه'}).click();
 await expect(session.getByRole('status')).toContainText('راستی‌آزمایی‌نشده است');
 expect(claims).toHaveLength(1);
 expect(claims[0].url).toContain('/class-sessions/'+sessionId+'/occurrence-claims');
 expect(claims[0].body).toMatchObject({reportedNote:'ادعای انسانی آزمایشی',
  evidenceRef:null,claimedStartedAt:null,claimedEndedAt:null});
 expect(claims[0].auth).toBe('Bearer synthetic-s48');
 expect(calendarCalls).toBe(1);
 await p.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c2');
 await expect(calendar(p).getByText(calendarEvent.title,{exact:true})).toHaveCount(0);
 await expect(calendar(p).locator('section[aria-label="گزارش وقوع و حضور جلسه"]')).toHaveCount(0);
});

test('calendar grant does not provide independent event dossier or S14 write permissions',async({page:p})=>{
 await permitCalendar(p);
 await p.route('**/events/plans/*/source-dossier**',r=>r.fulfill({
  status:403,contentType:'application/problem+json',body:'{}'}));
 await p.route('**/api/v1/centers/*/class-sessions/*/occurrence-claims',r=>r.fulfill({
  status:403,contentType:'application/problem+json',body:'{}'}));
 await enter(p);
 const c=calendar(p);
 await c.getByRole('button',{name:'خواندن تقویم خصوصی'}).click();
 await c.getByRole('button',{name:'پرونده شواهد همین رویداد از تقویم'}).click();
 await c.getByRole('button',{name:'بازکردن پرونده گزارش‌های منبع‌دار رویداد'}).click();
 await c.getByRole('button',{name:'خواندن گزارش‌های واقعی این رویداد'}).click();
 await expect(c.getByRole('alert')).toContainText('مجوز مستقل مشاهده پرونده و گزارش اصلی وجود ندارد.');
 await c.getByRole('button',{name:'گزارش ادعای همین جلسه از تقویم'}).click();
 const session=c.locator('section[aria-label="گزارش وقوع و حضور جلسه"]');
 await session.getByRole('textbox',{name:'شرح ادعای برگزاری'}).fill('عدم مجوز');
 await session.getByRole('button',{name:'ثبت گزارش ادعای جلسه'}).click();
 await expect(session.getByRole('alert')).toContainText('مجوز مستقل گزارش وقوع جلسه تأیید نشده است.');
});

test('forged approved dossier and stale event plan revision are denied, not treated as delivered',async({page:p})=>{
 await permitCalendar(p,[calendarEvent]);
 let stale=false;
 await p.route('**/events/plans/*/source-dossier**',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify(stale?
    dossier([],{revision:3}):dossier([{...report,isApproved:true,verificationStatus:'VERIFIED'}]))}));
 await enter(p);
 const c=calendar(p);
 await c.getByRole('button',{name:'خواندن تقویم خصوصی'}).click();
 await c.getByRole('button',{name:'پرونده شواهد همین رویداد از تقویم'}).click();
 await c.getByRole('button',{name:'بازکردن پرونده گزارش‌های منبع‌دار رویداد'}).click();
 await c.getByRole('button',{name:'خواندن گزارش‌های واقعی این رویداد'}).click();
 await expect(c.getByRole('alert')).toContainText('پرونده منبع معتبر یا قابل مشاهده دریافت نشد.');
 await expect(c.getByText(report.reportedSummary+' — UNVERIFIED')).toHaveCount(0);
 stale=true;
 await c.getByRole('button',{name:'خواندن گزارش‌های واقعی این رویداد'}).click();
 await expect(c.getByRole('alert')).toContainText('پرونده منبع معتبر یا قابل مشاهده دریافت نشد.');
});

test('denied calendar and changed UTC window cannot retain event dossiers or session actions',async({page:p})=>{
 let denied=false;
 await p.route('**/api/v1/centers/*/schedule/private?*',r=>r.fulfill({
  status:denied?403:200,contentType:'application/json',
  body:denied?'{}':JSON.stringify(packet([calendarSession,calendarEvent]))}));
 await enter(p);
 const c=calendar(p);
 await c.getByRole('button',{name:'خواندن تقویم خصوصی'}).click();
 await c.getByRole('button',{name:'پرونده شواهد همین رویداد از تقویم'}).click();
 await expect(c.locator('section[aria-label="پرونده منبع و گزارش رویداد"]')).toBeVisible();
 await p.locator('input[aria-label="شروع بازه تقویم"]').fill('2026-11-02');
 await expect(c.locator('section[aria-label="پرونده منبع و گزارش رویداد"]')).toHaveCount(0);
 // A new window is a new authorization read; never reuse an old event action.
 denied=true;
 await c.getByRole('button',{name:'خواندن تقویم خصوصی'}).click();
 await expect(c.getByRole('alert')).toContainText('مجوز مستقل مشاهده تقویم');
 await expect(c.getByRole('button',{name:'پرونده شواهد همین رویداد از تقویم'})).toHaveCount(0);
});

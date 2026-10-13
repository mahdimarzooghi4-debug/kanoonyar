import {test,expect} from '@playwright/test';
test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE!=='center','Private event dossier');
const eventId='22222222-2222-4222-8222-222222222222',draftId='11111111-1111-4111-8111-111111111111';
async function enter(p:import('@playwright/test').Page){
 await p.route('**/api/v1/identity/center-memberships',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({memberships:[
   {centerId:'c1',displayName:'کانون ۱',roleCode:'member'},
   {centerId:'c2',displayName:'کانون ۲',roleCode:'member'}]})}));
 await p.route('**/api/v1/centers/*/events/plans',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({events:[{
   eventId,draftId,pinnedPlanRevision:1,revision:2,
   title:'برنامه به‌روز رویداد',purpose:'هدف',locationDescription:'سالن',
   scheduledAt:'2026-11-12T11:00:00Z',recordedAt:'2026-10-13T08:00:00Z',
   visibility:'PRIVATE_DRAFT',isPublished:false,isExecuted:false,isRegistrationOpen:false
  }],nextBeforeEventId:null})}));
 await p.goto('/');
 await p.getByRole('textbox',{name:'توکن دسترسی'}).fill('synthetic');
 await p.getByRole('button',{name:'دریافت عضویت‌ها'}).click();
 await p.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c1');
 await p.getByRole('button',{name:'بارگذاری برنامه‌های خصوصی رویداد'}).click();
 await p.getByRole('button',{name:'ویرایش برنامه رویداد'}).click();
 await p.getByRole('button',{name:'بازکردن پرونده گزارش‌های منبع‌دار رویداد'}).click();
}
function report(n:number){
 return {reportSequence:n,reportId:'aaaaaaaa-aaaa-4aaa-8aaa-'+String(n).padStart(12,'0'),
  linkId:'bbbbbbbb-bbbb-4bbb-8bbb-000000000001',
  issuanceId:'cccccccc-cccc-4ccc-8ccc-000000000001',
  programId:'national-1',nationalVersion:1,pinnedRevision:1,
  reportedSummary:'ادعای گزارش '+n,evidenceRef:'opaque-'+n,
  claimedOccurredAt:null,recordedAt:'2026-10-13T08:00:00Z',sourceKind:'CENTER_EVENT',
  sourceRecordRef:eventId,verificationStatus:'UNVERIFIED',isApproved:false,isExecuted:false};
}
function packet(records:ReturnType<typeof report>[],next:number|null){
 return {eventId,draftId,pinnedPlanRevision:1,revision:2,title:'برنامه به‌روز رویداد',
  purpose:'هدف',locationDescription:'سالن',scheduledAt:'2026-11-12T11:00:00.000Z',
  visibility:'PRIVATE_DRAFT',isPublished:false,isExecuted:false,isRegistrationOpen:false,
  activityReports:records,nextBeforeSequence:next,verificationStatus:'UNVERIFIED',
  isApproved:false,isOutcome:false};
}
test('source-first dossier uses true cursor and never claims execution',async({page:p})=>{
 const paths:string[]=[];
 await p.route('**/events/plans/*/source-dossier**',r=>{
  paths.push(r.request().url());
  const before=new URL(r.request().url()).searchParams.get('beforeSequence');
  const nums=before===null?Array.from({length:20},(_,i)=>22-i):[2,1];
  const body=packet(nums.map(report),before===null?3:null);
  return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify(body)});
 });
 await enter(p);
 await p.getByRole('button',{name:'خواندن گزارش‌های واقعی این رویداد'}).click();
 await expect(p.getByText('ادعای گزارش 22 — UNVERIFIED')).toBeVisible();
 await expect(p.getByText('ادعای گزارش 3 — UNVERIFIED')).toBeVisible();
 await p.getByRole('button',{name:'گزارش‌های قدیمی‌تر این رویداد'}).click();
 await expect(p.getByText('ادعای گزارش 1 — UNVERIFIED')).toBeVisible();
 expect(paths).toHaveLength(2);
 expect(paths[1]).toContain('beforeSequence=3');
 await expect(p.getByText('بدون تأیید گزارش، برگزاری یا مشارکت').first()).toBeVisible();
 await p.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c2');
 await expect(p.getByText('ادعای گزارش 22 — UNVERIFIED')).toHaveCount(0);
});
test('forged approved report invalidates whole dossier',async({page:p})=>{
 await p.route('**/events/plans/*/source-dossier**',r=>r.fulfill({
  status:200,contentType:'application/json',
  body:JSON.stringify(packet([{...report(1),verificationStatus:'VERIFIED',isApproved:true} as any],null))}));
 await enter(p);
 await p.getByRole('button',{name:'خواندن گزارش‌های واقعی این رویداد'}).click();
 await expect(p.getByRole('alert')).toContainText('پرونده منبع معتبر یا قابل مشاهده دریافت نشد.');
 await expect(p.getByText('ادعای گزارش 1 — UNVERIFIED')).toHaveCount(0);
});
test('revoked dossier grant clears private records',async({page:p})=>{
 await p.route('**/events/plans/*/source-dossier**',r=>r.fulfill({
  status:403,contentType:'application/problem+json',body:'{}'}));
 await enter(p);
 await p.getByRole('button',{name:'خواندن گزارش‌های واقعی این رویداد'}).click();
 await expect(p.getByRole('alert')).toContainText('مجوز مستقل مشاهده پرونده و گزارش اصلی وجود ندارد.');
});

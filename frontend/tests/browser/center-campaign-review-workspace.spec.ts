import {test,expect} from '@playwright/test';
test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE!=='center','Source-first campaign review workspace');
const campaignId='22222222-2222-4222-8222-222222222222';
const draftId='11111111-1111-4111-8111-111111111111';
const associationId='66666666-6666-4666-8666-666666666666';
const reportId='33333333-3333-4333-8333-333333333333';
const linkId='44444444-4444-4444-8444-444444444444';
const sourceId='55555555-5555-4555-8555-555555555555';
const noteId='77777777-7777-4777-8777-777777777777';
const campaign={campaignId,slug:'culture',draftId,pinnedPlanRevision:1,revision:1,
 title:'پویش خصوصی',goalText:'هدف',audienceDescription:'اهالی',
 plannedActivities:[{activityKey:'workshop',title:'کارگاه پیشنهادی'}],
 recordedAt:'2026-10-12T10:00:00Z',visibility:'PRIVATE_DRAFT',isPublished:false,isExecuted:false};
const link={associationId,campaignRevision:1,activityKey:'workshop',reportId,
 sourceKind:'CLASS_SESSION',sourceRecordRef:sourceId,reportedSummary:'گزارش تأییدنشده',
 evidenceRef:'opaque',linkId,verificationStatus:'UNVERIFIED',isExecuted:false};
const source={reportId,activityKey:'workshop',campaignRevision:1,draftId,pinnedPlanRevision:1,
 sourceKind:'CLASS_SESSION',sourceRecordRef:sourceId,reportedSummary:'گزارش تأییدنشده',
 evidenceRef:'opaque',claimedOccurredAt:null,reportRecordedAt:'2026-10-12T10:00:00Z',
 linkedAt:'2026-10-12T10:00:00Z',linkId,verificationStatus:'UNVERIFIED',
 isApproval:false,isExecuted:false,isResolved:false};
const flags={verificationStatus:'UNVERIFIED',isApproval:false,isExecuted:false,isResolved:false};
const id=(n:number)=>'00000000-0000-4000-8000-'+n.toString(16).padStart(12,'0');
const entry=(n:number)=>({entryKind:'NOTE',entryId:id(n),noteId:id(n),requestId:null,
 content:'مشاهده منبع '+n,actorSubject:'synthetic',recordedAt:'2026-10-12T10:00:00Z',...flags});
const base={campaignId,associationId,source,...flags};
async function open(p:import('@playwright/test').Page){
 await p.route('**/api/v1/identity/center-memberships',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({memberships:[
   {centerId:'c1',displayName:'کانون ۱',roleCode:'member'},
   {centerId:'c2',displayName:'کانون ۲',roleCode:'member'}]})}));
 await p.route('**/api/v1/centers/*/campaigns/drafts',r=>r.fulfill({
  status:200,contentType:'application/json',
  body:JSON.stringify({campaigns:[campaign],nextBeforeCampaignId:null})}));
 await p.route('**/api/v1/centers/*/campaigns/*/report-links',r=>r.fulfill({
  status:200,contentType:'application/json',
  body:JSON.stringify({campaignId,associations:[link],nextBeforeAssociationId:null,
   verificationStatus:'UNVERIFIED',isExecuted:false})}));
 await p.goto('/');
 await p.getByRole('textbox',{name:'توکن دسترسی'}).fill('synthetic');
 await p.getByRole('button',{name:'دریافت عضویت‌ها'}).click();
 await p.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c1');
 await p.getByRole('button',{name:'دریافت پویش‌های خصوصی'}).click();
 await p.getByRole('button',{name:'ویرایش پویش'}).click();
 await p.getByRole('button',{name:'مشاهده پیوندهای گزارش پویش'}).click();
 await p.getByRole('button',{name:'بازکردن پرونده یکپارچه پیگیری'}).click();
}
test('source-first timeline pages notes with no verification or false completeness',async({page:p})=>{
 let calls=0;
 await p.route('**/api/v1/centers/*/campaigns/*/report-links/*/review-workspace*',r=>{
  calls++;
  const url=new URL(r.request().url());
  if(calls===1){
   expect(url.searchParams.has('beforeId')).toBeFalsy();
   const rows=Array.from({length:20},(_,i)=>entry(30-i));
   return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
    ...base,entries:rows,nextBeforeAt:rows[19].recordedAt,
    nextBeforeKind:'NOTE',nextBeforeId:rows[19].entryId,isComplete:false})});
  }
  expect(url.searchParams.get('beforeId')).toBe(id(11));
  return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
   ...base,entries:[entry(10)],nextBeforeAt:null,nextBeforeKind:null,
   nextBeforeId:null,isComplete:true})});
 });
 await open(p);
 const pane=p.getByRole('region',{name:'پرونده یکپارچه پیگیری گزارش'});
 await pane.getByRole('button',{name:'خواندن پرونده از منبع'}).click();
 await expect(pane.getByText('مشاهده منبع 30')).toBeVisible();
 await expect(pane.getByText('UNVERIFIED — نتیجه اجرا، مشارکت یا تأیید محسوب نمی‌شود.')).toBeVisible();
 await pane.getByRole('button',{name:'ادامه تاریخچه پرونده'}).click();
 await expect(pane.getByText('مشاهده منبع 10')).toBeVisible();
 await expect(pane.getByText('پایان موارد قابل مشاهده در این خواندن')).toBeVisible();
 expect(calls).toBe(2);
 await p.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c2');
 await expect(p.getByText('مشاهده منبع 30')).toHaveCount(0);
});
test('forged VERIFIED or resolved evidence fails closed',async({page:p})=>{
 await p.route('**/api/v1/centers/*/campaigns/*/report-links/*/review-workspace*',r=>
  r.fulfill({status:200,contentType:'application/json',
   body:JSON.stringify({...base,verificationStatus:'VERIFIED',isApproval:true,
    isExecuted:true,isResolved:true,entries:[{...entry(1),content:'forged success'}],
    nextBeforeAt:null,nextBeforeKind:null,nextBeforeId:null,isComplete:true})}));
 await open(p);
 const pane=p.getByRole('region',{name:'پرونده یکپارچه پیگیری گزارش'});
 await pane.getByRole('button',{name:'خواندن پرونده از منبع'}).click();
 await expect(pane.getByRole('alert')).toContainText('پرونده معتبر یا مجاز دریافت نشد.');
 await expect(pane.getByText('forged success')).toHaveCount(0);
});
test('independent source/inspection/followup read grant failure reveals no timeline',async({page:p})=>{
 await p.route('**/api/v1/centers/*/campaigns/*/report-links/*/review-workspace*',r=>
  r.fulfill({status:403,contentType:'application/problem+json',body:'{}'}));
 await open(p);
 const pane=p.getByRole('region',{name:'پرونده یکپارچه پیگیری گزارش'});
 await pane.getByRole('button',{name:'خواندن پرونده از منبع'}).click();
 await expect(pane.getByRole('alert')).toContainText('مجوزهای مستقل مشاهده پرونده');
 await expect(pane.getByText('گزارش تأییدنشده')).toHaveCount(0);
});

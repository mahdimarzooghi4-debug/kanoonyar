import{test,expect}from'@playwright/test';
test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE!=='center','Center source-only evidence register');
const cid='22222222-2222-4222-8222-222222222222';
const draftId='11111111-1111-4111-8111-111111111111';
const linkId='44444444-4444-4444-8444-444444444444';
const reportId='33333333-3333-4333-8333-333333333333';
const sourceId='55555555-5555-4555-8555-555555555555';
const id=(n:number)=>'00000000-0000-4000-8000-'+n.toString(16).padStart(12,'0');
const campaign={campaignId:cid,slug:'culture',draftId,pinnedPlanRevision:1,revision:1,
 title:'پویش خصوصی',goalText:'هدف',audienceDescription:'اهالی',
 plannedActivities:[{activityKey:'workshop',title:'کارگاه پیشنهادی'}],
 recordedAt:'2026-10-12T10:00:00Z',visibility:'PRIVATE_DRAFT',isPublished:false,isExecuted:false};
const flags={verificationStatus:'UNVERIFIED',isApproval:false,isExecuted:false,isResolved:false};
function row(n:number){return {associationId:id(n),reportId,campaignRevision:1,
 activityKey:'workshop',draftId,pinnedPlanRevision:1,sourceKind:'CLASS_SESSION',
 sourceRecordRef:sourceId,reportedSummary:'ادعای گزارش',evidenceRef:'opaque',linkId,
 reportRecordedAt:'2026-10-12T10:00:00Z',linkedAt:'2026-10-12T10:00:00Z',
 latestEntryKind:n===30?'NOTE':null,latestEntryId:n===30?id(44):null,
 latestEntryAt:n===30?'2026-10-12T10:00:00Z':null,...flags};}
async function open(page:import('@playwright/test').Page){
 await page.route('**/api/v1/identity/center-memberships',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({memberships:[
   {centerId:'c1',displayName:'کانون ۱',roleCode:'member'},
   {centerId:'c2',displayName:'کانون ۲',roleCode:'member'}]})}));
 await page.route('**/api/v1/centers/*/campaigns/drafts',r=>r.fulfill({
  status:200,contentType:'application/json',
  body:JSON.stringify({campaigns:[campaign],nextBeforeCampaignId:null})}));
 await page.goto('/');
 await page.getByRole('textbox',{name:'توکن دسترسی'}).fill('synthetic');
 await page.getByRole('button',{name:'دریافت عضویت‌ها'}).click();
 await page.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c1');
 await page.getByRole('button',{name:'دریافت پویش‌های خصوصی'}).click();
 await page.getByRole('button',{name:'ویرایش پویش'}).click();
 await page.getByRole('button',{name:'بازکردن دفتر گزارش‌های منبع‌دار'}).click();
}
test('lists 20 source references, filters and opens the original S27 review workspace',async({page})=>{
 let calls=0;
 await page.route('**/api/v1/centers/*/campaigns/*/evidence-register*',r=>{
  calls++;
  const u=new URL(r.request().url());
  const before=u.searchParams.get('beforeAssociationId');
  const rows=before?[row(10)]:Array.from({length:20},(_,i)=>row(30-i));
  if(before)expect(before).toBe(id(11));
  return r.fulfill({status:200,contentType:'application/json',
   body:JSON.stringify({campaignId:cid,sourceKind:null,activityKey:null,
    records:rows,nextBeforeAssociationId:before?null:id(11),
    isPageEnd:Boolean(before),...flags})});
 });
 await page.route('**/api/v1/centers/*/campaigns/*/report-links/*/review-workspace*',r=>r.fulfill({
  status:200,contentType:'application/json',
  body:JSON.stringify({campaignId:cid,associationId:id(30),source:{
   reportId,activityKey:'workshop',campaignRevision:1,draftId,pinnedPlanRevision:1,
   sourceKind:'CLASS_SESSION',sourceRecordRef:sourceId,reportedSummary:'ادعای گزارش',
   evidenceRef:'opaque',claimedOccurredAt:null,reportRecordedAt:'2026-10-12T10:00:00Z',
   linkedAt:'2026-10-12T10:00:00Z',linkId,...flags},
   entries:[],nextBeforeAt:null,nextBeforeKind:null,nextBeforeId:null,isComplete:true,...flags})}));
 await open(page);
 const pane=page.getByRole('region',{name:'دفتر گزارش‌های منبع‌دار پویش'});
 await pane.getByRole('button',{name:'دریافت دفتر از منابع اصلی'}).click();
 await expect(pane.getByText('آخرین مکاتبه: NOTE — 2026-10-12T10:00:00Z')).toBeVisible();
 await expect(pane.getByRole('listitem')).toHaveCount(20);
 await pane.getByRole('button',{name:'گزارش‌های منبع‌دار قدیمی‌تر'}).click();
 await expect(pane.getByRole('listitem')).toHaveCount(21);
 const first=pane.getByRole('listitem').first();
 await first.getByRole('button',{name:'بازکردن پرونده یکپارچه پیگیری'}).click();
 await first.getByRole('button',{name:'خواندن پرونده از منبع'}).click();
 await expect(first.getByText('UNVERIFIED — نتیجه اجرا، مشارکت یا تأیید محسوب نمی‌شود.')).toBeVisible();
 expect(calls).toBe(2);
 await page.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c2');
 await expect(page.getByText('آخرین مکاتبه: NOTE — 2026-10-12T10:00:00Z')).toHaveCount(0);
});
test('forged APPROVED/RESOLVED register data fail closed',async({page})=>{
 await page.route('**/api/v1/centers/*/campaigns/*/evidence-register*',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
   campaignId:cid,sourceKind:null,activityKey:null,records:[
    {...row(30),reportedSummary:'forged success',verificationStatus:'VERIFIED',
     isApproval:true,isExecuted:true,isResolved:true}],
   nextBeforeAssociationId:null,isPageEnd:true,
   verificationStatus:'VERIFIED',isApproval:true,isExecuted:true,isResolved:true})}));
 await open(page);
 const pane=page.getByRole('region',{name:'دفتر گزارش‌های منبع‌دار پویش'});
 await pane.getByRole('button',{name:'دریافت دفتر از منابع اصلی'}).click();
 await expect(pane.getByRole('alert')).toContainText('دفتر گزارش مجاز و معتبر دریافت نشد.');
 await expect(pane.getByText('forged success')).toHaveCount(0);
});
test('independent grant denial reveals no private register data',async({page})=>{
 await page.route('**/api/v1/centers/*/campaigns/*/evidence-register*',r=>r.fulfill({
  status:403,contentType:'application/problem+json',body:'{}'}));
 await open(page);
 const pane=page.getByRole('region',{name:'دفتر گزارش‌های منبع‌دار پویش'});
 await pane.getByRole('button',{name:'دریافت دفتر از منابع اصلی'}).click();
 await expect(pane.getByRole('alert')).toContainText('مجوز مستقل مشاهده گزارش‌ها و مکاتبات تأیید نشد.');
 await expect(pane.getByRole('listitem')).toHaveCount(0);
});

import{test,expect}from'@playwright/test';
test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE!=='center','Private human inspection notes');
const campaignId='22222222-2222-4222-8222-222222222222';
const draft='11111111-1111-4111-8111-111111111111';
const associationId='66666666-6666-4666-8666-666666666666';
const report='33333333-3333-4333-8333-333333333333';
const session='55555555-5555-4555-8555-555555555555';
const campaign={campaignId,slug:'culture',draftId:draft,pinnedPlanRevision:1,
 revision:1,title:'پویش خصوصی',goalText:'هدف',audienceDescription:'اهالی',
 plannedActivities:[{activityKey:'workshop',title:'کارگاه پیشنهادی'}],
 recordedAt:'2026-10-12T10:00:00Z',visibility:'PRIVATE_DRAFT',isPublished:false,isExecuted:false};
const link={associationId,campaignRevision:1,activityKey:'workshop',reportId:report,
 sourceKind:'CLASS_SESSION',sourceRecordRef:session,reportedSummary:'ادعای گزارش',evidenceRef:'opaque',
 linkId:'44444444-4444-4444-8444-444444444444',
 verificationStatus:'UNVERIFIED',isExecuted:false};
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
  body:JSON.stringify({campaignId,associations:[link],
   nextBeforeAssociationId:null,verificationStatus:'UNVERIFIED',isExecuted:false})}));
 await p.goto('/');
 await p.getByRole('textbox',{name:'توکن دسترسی'}).fill('synthetic');
 await p.getByRole('button',{name:'دریافت عضویت‌ها'}).click();
 await p.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c1');
 await p.getByRole('button',{name:'دریافت پویش‌های خصوصی'}).click();
 await p.getByRole('button',{name:'ویرایش پویش'}).click();
 await p.getByRole('button',{name:'مشاهده پیوندهای گزارش پویش'}).click();
}
test('inspection note records a human observation without changing verification status',async({page:p})=>{
 let received:any=null;
 await p.route('**/api/v1/centers/*/campaigns/*/report-links/*/inspection-notes',async r=>{
  if(r.request().method()==='POST'){
   received=r.request().postDataJSON();
   await r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
    noteId:received.noteId,campaignId,associationId,verificationStatus:'UNVERIFIED',
    isApproval:false,isExecuted:false})});return;
  }
  await r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
   campaignId,associationId,notes:[{noteId:received.noteId,
    noteText:'نیازمند بررسی مستندات منبع',notedBySubject:'synthetic',
    notedAt:'2026-10-12T10:00:00Z',verificationStatus:'UNVERIFIED',
    isApproval:false,isExecuted:false}],nextBeforeNoteId:null,
   verificationStatus:'UNVERIFIED',isApproval:false,isExecuted:false})});
 });
 await open(p);
 const pane=p.getByRole('region',{name:'یادداشت‌های بازبینی گزارش'});
 await pane.getByRole('textbox',{name:'متن یادداشت بازبینی'}).fill('نیازمند بررسی مستندات منبع');
 await pane.getByRole('button',{name:'ثبت یادداشت بدون تأیید'}).click();
 await expect(pane.getByText('یادداشت ثبت شد؛ گزارش همچنان UNVERIFIED است.')).toBeVisible();
 expect(received.noteText).toBe('نیازمند بررسی مستندات منبع');
 await pane.getByRole('button',{name:'خواندن یادداشت‌های بازبینی'}).click();
 await expect(pane.getByText('UNVERIFIED — بدون تأیید')).toBeVisible();
 await p.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c2');
 await expect(p.getByText('UNVERIFIED — بدون تأیید')).toHaveCount(0);
});
test('forged approved notes never show as reviewed execution',async({page:p})=>{
 await p.route('**/api/v1/centers/*/campaigns/*/report-links/*/inspection-notes',r=>
  r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
   campaignId,associationId,notes:[{noteId:associationId,noteText:'fake',
   notedBySubject:'synthetic',notedAt:'2026-10-12T10:00:00Z',
   verificationStatus:'VERIFIED',isApproval:true,isExecuted:true}],
   nextBeforeNoteId:null,verificationStatus:'VERIFIED',isApproval:true,isExecuted:true})}));
 await open(p);
 const pane=p.getByRole('region',{name:'یادداشت‌های بازبینی گزارش'});
 await pane.getByRole('button',{name:'خواندن یادداشت‌های بازبینی'}).click();
 await expect(pane.getByRole('alert')).toContainText('یادداشت‌ها معتبر دریافت نشدند.');
 await expect(pane.getByText('fake')).toHaveCount(0);
});
test('separate inspection grant denial is visible and no data is shown',async({page:p})=>{
 await p.route('**/api/v1/centers/*/campaigns/*/report-links/*/inspection-notes',r=>
   r.fulfill({status:403,contentType:'application/problem+json',body:'{}'}));
 await open(p);
 const pane=p.getByRole('region',{name:'یادداشت‌های بازبینی گزارش'});
 await pane.getByRole('button',{name:'خواندن یادداشت‌های بازبینی'}).click();
 await expect(pane.getByRole('alert')).toContainText('مجوز مستقل خواندن یادداشت');
});

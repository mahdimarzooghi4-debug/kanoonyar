import{test,expect}from'@playwright/test';
test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE!=='center','Campaign clarification lineage');
const cid='22222222-2222-4222-8222-222222222222';
const draft='11111111-1111-4111-8111-111111111111';
const association='66666666-6666-4666-8666-666666666666';
const noteId='77777777-7777-4777-8777-777777777777';
const sourceId='33333333-3333-4333-8333-333333333333';
const session='55555555-5555-4555-8555-555555555555';
const campaign={campaignId:cid,slug:'culture',draftId:draft,pinnedPlanRevision:1,revision:1,
 title:'پویش خصوصی',goalText:'هدف',audienceDescription:'اهالی',
 plannedActivities:[{activityKey:'workshop',title:'کارگاه پیشنهادی'}],
 recordedAt:'2026-10-12T10:00:00Z',visibility:'PRIVATE_DRAFT',isPublished:false,isExecuted:false};
const link={associationId:association,campaignRevision:1,activityKey:'workshop',reportId:sourceId,
 sourceKind:'CLASS_SESSION',sourceRecordRef:session,reportedSummary:'گزارش تأییدنشده',
 evidenceRef:'opaque',linkId:'44444444-4444-4444-8444-444444444444',
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
  body:JSON.stringify({campaignId:cid,associations:[link],nextBeforeAssociationId:null,
   verificationStatus:'UNVERIFIED',isExecuted:false})}));
 await p.route('**/api/v1/centers/*/campaigns/*/report-links/*/inspection-notes',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
    campaignId:cid,associationId:association,notes:[{noteId,noteText:'ابهام در زمان گزارش',
      notedBySubject:'synthetic',notedAt:'2026-10-12T10:00:00Z',
      verificationStatus:'UNVERIFIED',isApproval:false,isExecuted:false}],
    nextBeforeNoteId:null,verificationStatus:'UNVERIFIED',isApproval:false,isExecuted:false})}));
 await p.goto('/');
 await p.getByRole('textbox',{name:'توکن دسترسی'}).fill('synthetic');
 await p.getByRole('button',{name:'دریافت عضویت‌ها'}).click();
 await p.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c1');
 await p.getByRole('button',{name:'دریافت پویش‌های خصوصی'}).click();
 await p.getByRole('button',{name:'ویرایش پویش'}).click();
 await p.getByRole('button',{name:'مشاهده پیوندهای گزارش پویش'}).click();
 await p.getByRole('button',{name:'خواندن یادداشت‌های بازبینی'}).click();
}
test('records clarification request and response against same note with no approval',async({page:p})=>{
 const sent:any[]=[];
 await p.route('**/api/v1/centers/*/campaigns/*/report-links/*/inspection-notes/*/followups*',async r=>{
  const body=r.request().method()==='POST'?r.request().postDataJSON():null;
  if(body){
   sent.push(body);
   await r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
    campaignId:cid,associationId:association,noteId,entryId:body.entryId,
    entryKind:body.entryKind,requestId:body.requestId,
    verificationStatus:'UNVERIFIED',isApproval:false,isExecuted:false,isResolved:false})});
  }else{
   const url=new URL(r.request().url()),kind=url.searchParams.get('entryKind');
   const requestId=url.searchParams.get('requestId');
   const entries=kind==='REQUEST'&&sent[0]?[
    {entryId:sent[0].entryId,entryKind:'REQUEST',requestId:null,
     content:sent[0].content,actorSubject:'synthetic',recordedAt:'2026-10-12T10:00:00Z',
     verificationStatus:'UNVERIFIED',isApproval:false,isExecuted:false,isResolved:false}]
     :kind==='RESPONSE'&&sent[1]?[
      {entryId:sent[1].entryId,entryKind:'RESPONSE',requestId:sent[0].entryId,
       content:sent[1].content,actorSubject:'synthetic',recordedAt:'2026-10-12T10:00:01Z',
       verificationStatus:'UNVERIFIED',isApproval:false,isExecuted:false,isResolved:false}]:[];
   await r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
    campaignId:cid,associationId:association,noteId,entryKind:kind,
    requestId:kind==='REQUEST'?null:requestId,entries,nextBeforeEntryId:null,
    verificationStatus:'UNVERIFIED',isApproval:false,isExecuted:false,isResolved:false})});
  }
 });
 await open(p);
 const pane=p.getByRole('region',{name:'پیگیری توضیح گزارش'});
 await pane.getByRole('textbox',{name:'متن پیگیری'}).fill('لطفاً زمان را روشن کنید');
 await pane.getByRole('button',{name:'ثبت درخواست توضیح'}).click();
 await expect(pane.getByText('پیگیری ثبت شد؛ گزارش همچنان UNVERIFIED و بدون تأیید است.')).toBeVisible();
 await pane.getByRole('button',{name:'دریافت درخواست‌های توضیح'}).click();
 await pane.getByRole('button',{name:'پاسخ به این درخواست'}).click();
 await pane.getByRole('textbox',{name:'متن پیگیری'}).fill('مدرک هنوز اجرای واقعی را تأیید نمی‌کند');
 await pane.getByRole('button',{name:'ثبت پاسخ بدون تأیید'}).click();
 await pane.getByRole('button',{name:'مشاهده پاسخ‌های درخواست انتخاب‌شده'}).click();
 await expect(pane.getByText('UNVERIFIED — بدون تصمیم')).toBeVisible();
 expect(sent).toHaveLength(2);
 expect(sent[0]).toMatchObject({entryKind:'REQUEST',requestId:null});
 expect(sent[1]).toMatchObject({entryKind:'RESPONSE',requestId:sent[0].entryId});
 await p.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c2');
 await expect(p.getByText('UNVERIFIED — بدون تصمیم')).toHaveCount(0);
});
test('rejects forged approval and resolution in source followups',async({page:p})=>{
 await p.route('**/api/v1/centers/*/campaigns/*/report-links/*/inspection-notes/*/followups*',r=>
  r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
   campaignId:cid,associationId:association,noteId,entryKind:'REQUEST',requestId:null,
   entries:[{entryId:association,entryKind:'REQUEST',requestId:null,
    content:'fake-approval',actorSubject:'synthetic',recordedAt:'2026-10-12T10:00:00Z',
    verificationStatus:'VERIFIED',isApproval:true,isExecuted:true,isResolved:true}],
   nextBeforeEntryId:null,verificationStatus:'VERIFIED',
   isApproval:true,isExecuted:true,isResolved:true})}));
 await open(p);
 const pane=p.getByRole('region',{name:'پیگیری توضیح گزارش'});
 await pane.getByRole('button',{name:'دریافت درخواست‌های توضیح'}).click();
 await expect(pane.getByRole('alert')).toContainText('اطلاعات پیگیری معتبر دریافت نشد.');
 await expect(pane.getByText('fake-approval')).toHaveCount(0);
});
test('independent followup read grant denial leaks no questions',async({page:p})=>{
 await p.route('**/api/v1/centers/*/campaigns/*/report-links/*/inspection-notes/*/followups*',r=>
  r.fulfill({status:403,contentType:'application/problem+json',body:'{}'}));
 await open(p);
 const pane=p.getByRole('region',{name:'پیگیری توضیح گزارش'});
 await pane.getByRole('button',{name:'دریافت درخواست‌های توضیح'}).click();
 await expect(pane.getByRole('alert')).toContainText('مجوز مستقل پیگیری یا خواندن گزارش وجود ندارد.');
});

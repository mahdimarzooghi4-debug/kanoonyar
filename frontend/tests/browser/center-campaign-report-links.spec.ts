import{test,expect}from'@playwright/test';
test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE!=='center','Private campaign source lineage');
const cid='22222222-2222-4222-8222-222222222222';
const draft='11111111-1111-4111-8111-111111111111';
const report='33333333-3333-4333-8333-333333333333';
const link='44444444-4444-4444-8444-444444444444';
const session='55555555-5555-4555-8555-555555555555';
const association='66666666-6666-4666-8666-666666666666';
const source={reportId:report,reportSequence:9,linkId:link,
 sourceKind:'CLASS_SESSION',sourceRecordRef:session,reportedSummary:'گزارش منبع آزمون',
 verificationStatus:'UNVERIFIED'};
const campaign={campaignId:cid,slug:'private-campaign',draftId:draft,
 pinnedPlanRevision:1,revision:1,title:'پویش خصوصی',goalText:'افزایش آگاهی',
 audienceDescription:'اهالی محله',
 plannedActivities:[{activityKey:'workshop',title:'کارگاه پیشنهادی'}],
 recordedAt:'2026-10-12T10:00:00Z',visibility:'PRIVATE_DRAFT',
 isPublished:false,isExecuted:false};
async function enter(p:import('@playwright/test').Page){
 await p.route('**/api/v1/identity/center-memberships',r=>r.fulfill({
  status:200,contentType:'application/json',
  body:JSON.stringify({memberships:[
   {centerId:'c1',displayName:'کانون ۱',roleCode:'member'},
   {centerId:'c2',displayName:'کانون ۲',roleCode:'member'}]})}));
 await p.goto('/');
 await p.getByRole('textbox',{name:'توکن دسترسی'}).fill('synthetic');
 await p.getByRole('button',{name:'دریافت عضویت‌ها'}).click();
 await p.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c1');
}
async function openCampaign(p:import('@playwright/test').Page){
 await p.route('**/api/v1/centers/*/campaigns/drafts',r=>r.fulfill({
  status:200,contentType:'application/json',
  body:JSON.stringify({campaigns:[campaign],nextBeforeCampaignId:null})
 }));
 await enter(p);
 await p.getByRole('button',{name:'دریافت پویش‌های خصوصی'}).click();
 await p.getByRole('button',{name:'ویرایش پویش'}).click();
}
test('reuses an existing unverified owner report without recording execution',async({page:p})=>{
 const posts:any[]=[];
 await p.route('**/api/v1/centers/*/local-plan-drafts/*/activity-reports',r=>r.fulfill({
  status:200,contentType:'application/json',
  body:JSON.stringify({draftId:draft,reports:[source],nextBeforeSequence:null})
 }));
 await p.route('**/api/v1/centers/*/campaigns/*/report-links',async r=>{
  if(r.request().method()==='POST'){
   const x=r.request().postDataJSON();posts.push(x);
   await r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
    ...x,campaignId:cid,verificationStatus:'UNVERIFIED',isExecuted:false,sourceReused:true
   })});return;
  }
  await r.fulfill({status:200,contentType:'application/json',
   body:JSON.stringify({campaignId:cid,verificationStatus:'UNVERIFIED',isExecuted:false,
    associations:[{associationId:posts[0].associationId,campaignRevision:1,
     activityKey:'workshop',reportId:report,linkId:link,sourceKind:'CLASS_SESSION',
     sourceRecordRef:session,reportedSummary:'گزارش منبع آزمون',
     evidenceRef:'opaque-ref',verificationStatus:'UNVERIFIED',isExecuted:false}],
    nextBeforeAssociationId:null})});
 });
 await openCampaign(p);
 const region=p.getByRole('region',{name:'گزارش‌های منبع‌دار پویش'});
 await region.getByRole('button',{name:'دریافت گزارش‌های مجاز طرح محلی'}).click();
 await region.getByRole('combobox',{name:'انتخاب گزارش منبع'}).selectOption(report);
 await region.getByRole('button',{name:'اتصال گزارش موجود به پویش'}).click();
 await expect(region.getByText('پیوند منبع ثبت شد؛ گزارش همچنان تأییدنشده است.')).toBeVisible();
 expect(posts).toHaveLength(1);
 expect(posts[0]).toMatchObject({campaignRevision:1,activityKey:'workshop',reportId:report});
 await region.getByRole('button',{name:'مشاهده پیوندهای گزارش پویش'}).click();
 await expect(region.getByText('UNVERIFIED — اجرای قطعی نیست')).toBeVisible();
 await p.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c2');
 await expect(p.getByText('UNVERIFIED — اجرای قطعی نیست')).toHaveCount(0);
});
test('forged VERIFIED response must not render a successful campaign activity',async({page:p})=>{
 await p.route('**/api/v1/centers/*/campaigns/*/report-links',r=>r.fulfill({
  status:200,contentType:'application/json',
  body:JSON.stringify({campaignId:cid,verificationStatus:'VERIFIED',isExecuted:true,
   associations:[{associationId:association,campaignRevision:1,activityKey:'workshop',
    reportId:report,linkId:link,sourceKind:'CLASS_SESSION',sourceRecordRef:session,
    reportedSummary:'fake',evidenceRef:'ref',verificationStatus:'VERIFIED',isExecuted:true}],
   nextBeforeAssociationId:null})
 }));
 await openCampaign(p);
 const region=p.getByRole('region',{name:'گزارش‌های منبع‌دار پویش'});
 await region.getByRole('button',{name:'مشاهده پیوندهای گزارش پویش'}).click();
 await expect(region.getByRole('alert')).toContainText('پیوندهای گزارش معتبر دریافت نشد.');
 await expect(region.getByText('fake')).toHaveCount(0);
});
test('report permissions are independent of campaign authoring',async({page:p})=>{
 await p.route('**/api/v1/centers/*/local-plan-drafts/*/activity-reports',r=>r.fulfill({
  status:403,contentType:'application/problem+json',body:'{}'
 }));
 await openCampaign(p);
 const region=p.getByRole('region',{name:'گزارش‌های منبع‌دار پویش'});
 await region.getByRole('button',{name:'دریافت گزارش‌های مجاز طرح محلی'}).click();
 await expect(region.getByRole('alert')).toContainText('مجوز مستقل مشاهده گزارش‌های منبع وجود ندارد.');
 await expect(region.getByRole('button',{name:'اتصال گزارش موجود به پویش'})).toBeDisabled();
});

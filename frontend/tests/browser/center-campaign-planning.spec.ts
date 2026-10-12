import {test,expect} from '@playwright/test';
test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE!=='center','Private center campaign planning');
const draft='11111111-1111-4111-8111-111111111111';
async function enter(p:import('@playwright/test').Page){
 await p.route('**/api/v1/identity/center-memberships',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({memberships:[
   {centerId:'c1',displayName:'کانون ۱',roleCode:'member'},
   {centerId:'c2',displayName:'کانون ۲',roleCode:'member'}]})}));
 await p.goto('/');
 await p.getByRole('textbox',{name:'توکن دسترسی'}).fill('synthetic');
 await p.getByRole('button',{name:'دریافت عضویت‌ها'}).click();
 await p.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c1');
}
test('creates and revises private campaign tied to a real local plan',async({page:p})=>{
 let n=0;const requests:any[]=[];
 await p.route('**/api/v1/centers/*/local-plan-drafts',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({drafts:[
    {draftId:draft,revision:2,localTitle:'طرح محلی معتبر',programId:'national-1',nationalVersion:1}]})
 }));
 await p.route('**/api/v1/centers/*/campaigns/drafts/revisions',async r=>{
  const x=r.request().postDataJSON();requests.push(x);n++;
  await r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
   campaignId:x.campaignId,draftId:x.draftId,pinnedPlanRevision:x.pinnedPlanRevision,
   slug:x.slug,revision:n,visibility:'PRIVATE_DRAFT',isPublished:false,isExecuted:false
  })});
 });
 await enter(p);
 await p.getByRole('button',{name:'دریافت طرح‌های محلی مجاز'}).click();
 await p.getByRole('combobox',{name:'طرح محلی پویش'}).selectOption(draft+':2');
 await p.getByRole('textbox',{name:'نشانی پویش'}).fill('culture-week');
 await p.getByRole('textbox',{name:'عنوان پویش'}).fill('پویش فرهنگی');
 await p.getByRole('textbox',{name:'هدف پویش'}).fill('آگاهی عمومی');
 await p.getByRole('textbox',{name:'توصیف کلی مخاطب'}).fill('ساکنان محله');
 await p.getByRole('textbox',{name:'عنوان فعالیت 1'}).fill('کارگاه');
 await p.getByRole('button',{name:'افزودن فعالیت پویش'}).click();
 await p.getByRole('textbox',{name:'کلید فعالیت 2'}).fill('local_visit');
 await p.getByRole('textbox',{name:'عنوان فعالیت 2'}).fill('دیدار محلی');
 await p.getByRole('button',{name:'ذخیره نسخه برنامه‌ریزی پویش'}).click();
 await expect(p.getByText('نسخه پویش ذخیره شد؛ اجرا یا انتشار تأیید نشده است.')).toBeVisible();
 expect(requests[0]).toMatchObject({expectedRevision:0,draftId:draft,pinnedPlanRevision:2});
 expect(requests[0].plannedActivities).toHaveLength(2);
 await p.getByRole('textbox',{name:'عنوان پویش'}).fill('نسخه دوم پویش');
 await p.getByRole('button',{name:'ذخیره نسخه برنامه‌ریزی پویش'}).click();
 expect(requests[1]).toMatchObject({expectedRevision:1,draftId:draft,pinnedPlanRevision:2});
 expect(requests[1].campaignId).toBe(requests[0].campaignId);
 await p.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c2');
 await expect(p.getByText('نسخه پویش ذخیره شد؛ اجرا یا انتشار تأیید نشده است.')).toHaveCount(0);
});
test('no plan is never replaced with fabricated plan',async({page:p})=>{
 await p.route('**/api/v1/centers/*/local-plan-drafts',r=>r.fulfill({
  status:200,contentType:'application/json',body:'{"drafts":[]}'}));
 await enter(p);
 await p.getByRole('button',{name:'دریافت طرح‌های محلی مجاز'}).click();
 await expect(p.getByText('طرح محلی موجود نیست؛ پویش بدون طرح ساخته نمی‌شود.')).toBeVisible();
 await expect(p.getByRole('button',{name:'ذخیره نسخه برنامه‌ریزی پویش'})).toBeDisabled();
});
test('denied read and forged published campaign never expose details',async({page:p})=>{
 await p.route('**/api/v1/centers/*/campaigns/drafts',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
   campaigns:[{campaignId:'22222222-2222-4222-8222-222222222222',
    slug:'bad',draftId:draft,pinnedPlanRevision:2,revision:1,
    title:'leaked campaign',goalText:'private',audienceDescription:'secret',
    plannedActivities:[{activityKey:'step_one',title:'a'}],
    recordedAt:'2026-10-12T10:00:00Z',visibility:'PUBLIC',isPublished:true,isExecuted:true}],
   nextBeforeCampaignId:null})}));
 await enter(p);
 await p.getByRole('button',{name:'دریافت پویش‌های خصوصی'}).click();
 await expect(p.getByRole('alert')).toContainText('فهرست پویش‌ها معتبر دریافت نشد.');
 await expect(p.getByText('leaked campaign')).toHaveCount(0);
});

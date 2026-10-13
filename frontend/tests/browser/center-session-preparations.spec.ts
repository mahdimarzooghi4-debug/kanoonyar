import {test,expect} from '@playwright/test';
test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE!=='center','Private session preparation');
const sessionId='aaaaaaaa-aaaa-4aaa-8aaa-000000000003';
const classId='bbbbbbbb-bbbb-4bbb-8bbb-000000000002';
const source={sourceId:sessionId,classId,title:'جلسه منبع واقعی',
 classTitle:'کلاس آموزشی',scheduledAt:null};
const version={revision:1,title:'طرح درس اول',teachingOutline:'شرح تدریس محرمانه',
 materialsNote:'یادداشت ابزار',recordedAt:'2026-10-13T10:00:00Z',
 visibility:'PRIVATE_PREPARATION',isPublished:false,deliveryVerified:false,
 isAttendanceVerified:false,isAssessed:false};
const prep={sessionId,classId,sessionTitle:source.title,classTitle:source.classTitle,...version};
async function enter(p:import('@playwright/test').Page){
 await p.route('**/api/v1/identity/center-memberships',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({memberships:[
   {centerId:'c1',displayName:'کانون یک',roleCode:'member'},
   {centerId:'c2',displayName:'کانون دو',roleCode:'member'}]})}));
 await p.goto('/');
 await p.getByRole('textbox',{name:'توکن دسترسی'}).fill('synthetic');
 await p.getByRole('button',{name:'دریافت عضویت‌ها'}).click();
 await p.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c1');
}
test('genuine class-session source -> saved private outline -> immutable history; center resets data',
 async({page:p})=>{
 const posts:any[]=[];
 await p.route('**/api/v1/centers/*/activity-sources/class-sessions*',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
   sourceKind:'CLASS_SESSION',records:[source],nextBeforeSourceId:null})}));
 await p.route('**/api/v1/centers/*/learning/session-preparations/revisions',r=>{
  const body=r.request().postDataJSON();posts.push(body);
  return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
   sessionId:body.sessionId,classId:body.classId,revision:body.expectedRevision+1,
   visibility:'PRIVATE_PREPARATION',isPublished:false,deliveryVerified:false,
   isAttendanceVerified:false,isAssessed:false})});
 });
 await p.route('**/api/v1/centers/*/learning/session-preparations',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
   preparations:[prep],nextBeforeSessionId:null,visibility:'PRIVATE_PREPARATION',
   isPublished:false,deliveryVerified:false,isAttendanceVerified:false,isAssessed:false})}));
 await p.route('**/api/v1/centers/*/learning/session-preparations/*/revisions',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
   sessionId,classId,sessionTitle:source.title,revisions:[version],nextBeforeRevision:null,
   visibility:'PRIVATE_PREPARATION',isPublished:false,deliveryVerified:false,
   isAttendanceVerified:false,isAssessed:false})}));
 await enter(p);
 let panel=p.locator('section[aria-label="دفتر آماده‌سازی خصوصی جلسات"]');
 await panel.getByRole('button',{name:'جلسه‌های واقعی کانون'}).click();
 await panel.getByRole('combobox',{name:'انتخاب جلسه آموزشی'}).selectOption(sessionId+':'+classId);
 await panel.getByRole('textbox',{name:'عنوان آماده‌سازی جلسه'}).fill('طرح درس اول');
 await panel.getByRole('textbox',{name:'متن طرح تدریس'}).fill('شرح تدریس محرمانه');
 await panel.getByRole('textbox',{name:'یادداشت منابع تدریس'}).fill('یادداشت ابزار');
 await panel.getByRole('button',{name:'ذخیره نسخه خصوصی آماده‌سازی'}).click();
 await expect(panel.getByRole('status')).toContainText('درس منتشر یا اجرا نشده');
 expect(posts).toHaveLength(1);
 expect(posts[0]).toEqual({sessionId,classId,expectedRevision:0,title:'طرح درس اول',
  teachingOutline:'شرح تدریس محرمانه',materialsNote:'یادداشت ابزار'});
 expect(posts[0].isPublished).toBeUndefined();
 await panel.getByRole('button',{name:'دریافت دفترهای آماده‌سازی'}).click();
 await expect(panel.getByText('طرح درس اول',{exact:true})).toBeVisible();
 await panel.getByRole('button',{name:'ویرایش طرح خصوصی جلسه'}).click();
 await panel.getByRole('button',{name:'تاریخچه آماده‌سازی جلسه'}).click();
 await expect(panel.getByText('نسخه 1 — طرح درس اول — PRIVATE_PREPARATION')).toBeVisible();
 await p.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c2');
 panel=p.locator('section[aria-label="دفتر آماده‌سازی خصوصی جلسات"]');
 await expect(panel.getByText('نسخه 1 — طرح درس اول — PRIVATE_PREPARATION')).toHaveCount(0);
});
test('forged published or assessed preparation cannot be displayed',async({page:p})=>{
 await p.route('**/api/v1/centers/*/learning/session-preparations',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
   preparations:[{...prep,isPublished:true,isAssessed:true}],
   nextBeforeSessionId:null,visibility:'PRIVATE_PREPARATION',
   isPublished:false,deliveryVerified:false,isAttendanceVerified:false,isAssessed:false})}));
 await enter(p);
 const panel=p.locator('section[aria-label="دفتر آماده‌سازی خصوصی جلسات"]');
 await panel.getByRole('button',{name:'دریافت دفترهای آماده‌سازی'}).click();
 await expect(panel.getByRole('alert')).toContainText('دفتر آماده‌سازی معتبر دریافت نشد.');
 await expect(panel.getByText('طرح درس اول',{exact:true})).toHaveCount(0);
});
test('independent source and preparation rights are visibly denied',async({page:p})=>{
 await p.route('**/api/v1/centers/*/activity-sources/class-sessions*',r=>r.fulfill({
  status:403,contentType:'application/problem+json',body:'{}'}));
 await p.route('**/api/v1/centers/*/learning/session-preparations',r=>r.fulfill({
  status:403,contentType:'application/problem+json',body:'{}'}));
 await enter(p);
 const panel=p.locator('section[aria-label="دفتر آماده‌سازی خصوصی جلسات"]');
 await panel.getByRole('button',{name:'جلسه‌های واقعی کانون'}).click();
 await expect(panel.getByText('مجوز مستقل مشاهده جلسات وجود ندارد.')).toBeVisible();
 await panel.getByRole('button',{name:'دریافت دفترهای آماده‌سازی'}).click();
 await expect(panel.getByText('مجوز مستقل مشاهده دفتر آماده‌سازی وجود ندارد.')).toBeVisible();
});

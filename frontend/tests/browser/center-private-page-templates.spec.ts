import {test,expect} from '@playwright/test';
test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE!=='center','Private reusable page templates');
const pageId='aaaaaaaa-aaaa-4aaa-8aaa-000000000051';
const templateId='bbbbbbbb-bbbb-4bbb-8bbb-000000000052';
const source={pageId,slug:'original',revision:2,title:'نسخه اصلی',
 bodyText:'متن دقیق نسخه دو',recordedAt:'2026-10-13T10:00:00Z',
 visibility:'PRIVATE_DRAFT',isPublished:false};
const template={templateId,sourcePageId:pageId,revision:1,pinnedPageRevision:2,
 label:'قالب آموزشی خصوصی',sourceSlug:'original',title:'نسخه اصلی',
 bodyText:'متن دقیق نسخه دو',recordedAt:'2026-10-13T10:00:00Z',
 visibility:'PRIVATE_TEMPLATE',isPublished:false,isShared:false};
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
test('actual page pin to template and explicit new private draft without publishing',async({page:p})=>{
 const templates:any[]=[],clones:any[]=[];
 await p.route('**/api/v1/centers/*/site/drafts',r=>r.fulfill({
  status:200,contentType:'application/json',
  body:JSON.stringify({pages:[source],nextBeforePageId:null})}));
 await p.route('**/api/v1/centers/*/site/templates/revisions',r=>{
  const body=r.request().postDataJSON();templates.push(body);
  return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
   templateId:body.templateId,sourcePageId:body.sourcePageId,
   revision:body.expectedRevision+1,pinnedPageRevision:body.pinnedPageRevision,
   visibility:'PRIVATE_TEMPLATE',isPublished:false,isShared:false})});
 });
 await p.route('**/api/v1/centers/*/site/templates',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
   templates:[template],nextBeforeTemplateId:null,visibility:'PRIVATE_TEMPLATE',
   isPublished:false,isShared:false})}));
 await p.route('**/api/v1/centers/*/site/templates/*/revisions',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
   templateId,sourcePageId:pageId,revisions:[{revision:1,pinnedPageRevision:2,
    label:'قالب آموزشی خصوصی',sourceSlug:'original',title:'نسخه اصلی',bodyText:'متن دقیق نسخه دو',
    recordedAt:'2026-10-13T10:00:00Z',visibility:'PRIVATE_TEMPLATE',isPublished:false,isShared:false}],
   nextBeforeRevision:null,visibility:'PRIVATE_TEMPLATE',isPublished:false,isShared:false})}));
 await p.route('**/api/v1/centers/*/site/drafts/revisions',r=>{
  const body=r.request().postDataJSON();clones.push(body);
  return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
   pageId:body.pageId,slug:body.slug,revision:1,visibility:'PRIVATE_DRAFT',isPublished:false})});
 });
 await enter(p);
 let panel=p.locator('section[aria-label="کتابخانه قالب خصوصی صفحات"]');
 await panel.getByRole('button',{name:'دریافت صفحات مبنای قالب'}).click();
 await panel.getByRole('combobox',{name:'انتخاب صفحه مبنای قالب'}).selectOption(pageId+':2');
 await panel.getByRole('textbox',{name:'نام قالب خصوصی'}).fill('قالب آموزشی خصوصی');
 await panel.getByRole('button',{name:'ثبت نسخه قالب خصوصی'}).click();
 await expect(panel.getByRole('status')).toContainText('هیچ صفحه‌ای منتشر نشده');
 expect(templates).toHaveLength(1);
 expect(templates[0].sourcePageId).toBe(pageId);
 expect(templates[0].pinnedPageRevision).toBe(2);
 await panel.getByRole('button',{name:'دریافت قالب‌های خصوصی'}).click();
 await expect(panel.getByText('قالب آموزشی خصوصی',{exact:true})).toBeVisible();
 await panel.getByRole('button',{name:'ویرایش قالب و تاریخچه'}).click();
 await panel.getByRole('button',{name:'تاریخچه نسخه‌های قالب'}).click();
 await expect(panel.getByText('نسخه قالب 1 — قالب آموزشی خصوصی / نسخه صفحه 2')).toBeVisible();
 await panel.getByRole('button',{name:'ساخت صفحه خصوصی از این قالب'}).click();
 await panel.getByRole('textbox',{name:'اسلاگ صفحه جدید'}).fill('new-private-page');
 await panel.getByRole('button',{name:'ایجاد پیش‌نویس صفحه از قالب'}).click();
 await expect(panel.getByRole('status')).toContainText('انتشار یا اشتراک انجام نشده');
 expect(clones).toHaveLength(1);
 expect(clones[0].slug).toBe('new-private-page');
 expect(clones[0].expectedRevision).toBe(0);
 expect(clones[0].bodyText).toBe('متن دقیق نسخه دو');
 expect(clones[0].pageId).not.toBe(pageId);
 await p.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c2');
 panel=p.locator('section[aria-label="کتابخانه قالب خصوصی صفحات"]');
 await expect(panel.getByText('نسخه قالب 1 — قالب آموزشی خصوصی / نسخه صفحه 2')).toHaveCount(0);
});
test('forged sharing or publication in template response cannot be shown',async({page:p})=>{
 await p.route('**/api/v1/centers/*/site/templates',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
   templates:[{...template,isShared:true,isPublished:true}],nextBeforeTemplateId:null,
   visibility:'PRIVATE_TEMPLATE',isPublished:false,isShared:false})}));
 await enter(p);
 const panel=p.locator('section[aria-label="کتابخانه قالب خصوصی صفحات"]');
 await panel.getByRole('button',{name:'دریافت قالب‌های خصوصی'}).click();
 await expect(panel.getByRole('alert')).toContainText('قالب‌های معتبر دریافت نشدند.');
 await expect(panel.getByRole('button',{name:'ساخت صفحه خصوصی از این قالب'})).toHaveCount(0);
});
test('independent template read and new-page create grants are enforced',async({page:p})=>{
 await p.route('**/api/v1/centers/*/site/templates',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
   templates:[template],nextBeforeTemplateId:null,visibility:'PRIVATE_TEMPLATE',
   isPublished:false,isShared:false})}));
 await p.route('**/api/v1/centers/*/site/drafts/revisions',r=>r.fulfill({
  status:403,contentType:'application/problem+json',body:'{}'}));
 await enter(p);
 const panel=p.locator('section[aria-label="کتابخانه قالب خصوصی صفحات"]');
 await panel.getByRole('button',{name:'دریافت قالب‌های خصوصی'}).click();
 await panel.getByRole('button',{name:'ساخت صفحه خصوصی از این قالب'}).click();
 await panel.getByRole('textbox',{name:'اسلاگ صفحه جدید'}).fill('new-private-page');
 await panel.getByRole('button',{name:'ایجاد پیش‌نویس صفحه از قالب'}).click();
 await expect(panel.getByText('مجوز مستقل ایجاد صفحه جدید تأیید نشد.')).toBeVisible();
});

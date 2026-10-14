import {test,expect} from '@playwright/test';
test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE!=='center','Assembled preview');
const first='aaaaaaaa-aaaa-4aaa-8aaa-000000000101';
const second='aaaaaaaa-aaaa-4aaa-8aaa-000000000102';
const theme={revision:3,layoutKey:'HERO',paletteKey:'FOREST',typeKey:'SYSTEM',
 headerText:'سربرگ کانون واقعی',footerText:'پابرگ خصوصی',showIntro:true,showFooter:true,
 recordedAt:'2026-10-13T10:00:00Z',visibility:'PRIVATE_DRAFT',isPublished:false};
const page=(id:string,position:number,title:string,body:string)=>({
 pageId:id,position,pinnedPageRevision:1,slug:id===first?'home':'about',
 title,bodyText:body,visibility:'PRIVATE_DRAFT',isPublished:false});
const response=(items:unknown[],nextAfterPosition:number|null=null)=>({
 ready:true,missingNavigation:false,missingDesign:false,navigationRevision:2,designRevision:3,
 design:theme,items,nextAfterPosition,visibility:'PRIVATE_DRAFT',isPublished:false,deliveryVerified:false});
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
test('one private site preview renders exact pinned pages and design with safe center reset',async({page:p})=>{
 await p.route('**/api/v1/centers/*/site/assembled-preview*',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify(
   response([page(first,1,'صفحه نخست','متن نسخه یک نه نسخه جدید'),
    page(second,2,'درباره ما','محتوای خصوصی دیگر')]))}));
 await enter(p);
 let section=p.locator('section[aria-label="پیش‌نمایش یکپارچه خصوصی سایت"]');
 await section.getByRole('button',{name:'نمایش نسخه‌های واقعی سایت'}).click();
 await expect(section.getByRole('article',{name:'سایت منتشرنشده کانون'})).toBeVisible();
 await expect(section.getByText('متن نسخه یک نه نسخه جدید')).toBeVisible();
 await expect(section.getByText('سربرگ کانون واقعی')).toBeVisible();
 await expect(section.getByText('پابرگ خصوصی')).toBeVisible();
 await section.getByRole('button',{name:'درباره ما — /about'}).click();
 await expect(section.getByText('محتوای خصوصی دیگر')).toBeVisible();
 await expect(section.getByText('متن نسخه یک نه نسخه جدید')).toHaveCount(0);
 await p.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c2');
 section=p.locator('section[aria-label="پیش‌نمایش یکپارچه خصوصی سایت"]');
 await expect(section.getByText('محتوای خصوصی دیگر')).toHaveCount(0);
});
test('incomplete missing real site design is explicit and never replaced by invented defaults',
 async({page:p})=>{
 await p.route('**/api/v1/centers/*/site/assembled-preview*',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
   ready:false,missingNavigation:true,missingDesign:true,navigationRevision:null,
   designRevision:null,design:null,items:[],nextAfterPosition:null,
   visibility:'PRIVATE_DRAFT',isPublished:false,deliveryVerified:false})}));
 await enter(p);
 const section=p.locator('section[aria-label="پیش‌نمایش یکپارچه خصوصی سایت"]');
 await section.getByRole('button',{name:'نمایش نسخه‌های واقعی سایت'}).click();
 await expect(section.getByRole('status')).toContainText('منوی خصوصی موجود نیست');
 await expect(section.getByRole('article',{name:'سایت منتشرنشده کانون'})).toHaveCount(0);
});
test('forged published or delivered aggregate is rejected',async({page:p})=>{
 await p.route('**/api/v1/centers/*/site/assembled-preview*',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
   ...response([page(first,1,'حمله','متن حساس')]),isPublished:true,deliveryVerified:true})}));
 await enter(p);
 const section=p.locator('section[aria-label="پیش‌نمایش یکپارچه خصوصی سایت"]');
 await section.getByRole('button',{name:'نمایش نسخه‌های واقعی سایت'}).click();
 await expect(section.getByRole('alert')).toContainText('پیش‌نمایش معتبر سایت دریافت نشد.');
 await expect(section.getByText('متن حساس')).toHaveCount(0);
});
test('independent authorization denial is shown without content leak',async({page:p})=>{
 await p.route('**/api/v1/centers/*/site/assembled-preview*',r=>r.fulfill({
  status:403,contentType:'application/problem+json',body:'{}'}));
 await enter(p);
 const section=p.locator('section[aria-label="پیش‌نمایش یکپارچه خصوصی سایت"]');
 await section.getByRole('button',{name:'نمایش نسخه‌های واقعی سایت'}).click();
 await expect(section.getByRole('alert')).toContainText('مجوز مستقل منو، صفحه و طراحی موجود نیست.');
});

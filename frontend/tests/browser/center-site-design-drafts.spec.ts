import {test,expect} from '@playwright/test';
test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE!=='center','Private site design only');
const page='10000000-0000-4000-8000-000000000321';
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
const design=(revision:number,layoutKey='HERO',paletteKey='FOREST')=>({
 revision,layoutKey,paletteKey,typeKey:'SYSTEM',headerText:'کانون الف',
 footerText:'اطلاع‌رسانی',showIntro:true,showFooter:true,
 visibility:'PRIVATE_DRAFT',isPublished:false
});
test('versioned design and source-owned private page render without publication',async({page:p})=>{
 let revision=0;let outgoing:any=null;
 await p.route('**/api/v1/centers/*/site/design',r=>r.fulfill({
  status:200,contentType:'application/json',
  body:JSON.stringify({revision:null,design:null,visibility:'PRIVATE_DRAFT',isPublished:false})
 }));
 await p.route('**/api/v1/centers/*/site/design/revisions',async r=>{
  outgoing=r.request().postDataJSON();revision++;
  await r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
   ...outgoing,revision,visibility:'PRIVATE_DRAFT',isPublished:false
  })});
 });
 await p.route('**/api/v1/centers/*/site/drafts',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({pages:[{
   pageId:page,slug:'center-home',revision:1,title:'عنوان صفحه واقعی',
   bodyText:'متن خصوصی صفحه\\nخط دوم',recordedAt:'2026-10-12T10:00:00Z',
   visibility:'PRIVATE_DRAFT',isPublished:false
  }],nextBeforePageId:null})
 }));
 await enter(p);
 await p.getByRole('button',{name:'دریافت تنظیمات خصوصی سایت'}).click();
 await expect(p.getByText('نسخه فعلی: ثبت‌نشده — فقط PRIVATE_DRAFT')).toBeVisible();
 await p.getByRole('combobox',{name:'چیدمان سایت'}).selectOption('HERO');
 await p.getByRole('combobox',{name:'رنگ‌بندی سایت'}).selectOption('FOREST');
 await p.getByRole('textbox',{name:'عنوان سربرگ'}).fill('کانون الف');
 await p.getByRole('textbox',{name:'متن پابرگ'}).fill('اطلاع‌رسانی');
 await p.getByRole('checkbox',{name:'نمایش پابرگ'}).check();
 await p.getByRole('button',{name:'ذخیره نسخه جدید طراحی خصوصی'}).click();
 await expect(p.getByText('نسخه طراحی ذخیره شد؛ سایت منتشر نشده است.')).toBeVisible();
 expect(outgoing).toMatchObject({expectedRevision:0,layoutKey:'HERO',paletteKey:'FOREST'});
 await p.getByRole('button',{name:'دریافت صفحات خصوصی برای پیش‌نمایش'}).click();
 const preview=p.getByRole('article',{name:'پیش‌نمایش خصوصی صفحه'});
 await expect(preview).toContainText('عنوان صفحه واقعی');
 await expect(preview).toContainText('متن خصوصی صفحه');
 await expect(preview).toContainText('پیش‌نمایش خصوصی — منتشر نشده');
 await p.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c2');
 await expect(preview).toHaveCount(0);
});
test('independent design read denied',async({page:p})=>{
 await p.route('**/api/v1/centers/*/site/design',r=>r.fulfill({
  status:403,contentType:'application/problem+json',body:'{}'
 }));
 await enter(p);
 await p.getByRole('button',{name:'دریافت تنظیمات خصوصی سایت'}).click();
 await expect(p.getByRole('alert')).toContainText('مجوز مستقل مشاهده تنظیمات سایت');
 await expect(p.getByRole('button',{name:'ذخیره نسخه جدید طراحی خصوصی'})).toHaveCount(0);
});
test('forged published design cannot show preview or controls',async({page:p})=>{
 await p.route('**/api/v1/centers/*/site/design',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
   revision:1,design:{...design(1),visibility:'PUBLIC',isPublished:true},
   visibility:'PUBLIC',isPublished:true
  })
 }));
 await enter(p);
 await p.getByRole('button',{name:'دریافت تنظیمات خصوصی سایت'}).click();
 await expect(p.getByRole('alert')).toContainText('تنظیمات معتبر دریافت نشد');
 await expect(p.getByRole('article',{name:'پیش‌نمایش خصوصی صفحه'})).toHaveCount(0);
});
test('forged public page is rejected even if design is private',async({page:p})=>{
 await p.route('**/api/v1/centers/*/site/design',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
   revision:1,design:design(1),visibility:'PRIVATE_DRAFT',isPublished:false
  })
 }));
 await p.route('**/api/v1/centers/*/site/drafts',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({pages:[{
   pageId:page,slug:'home',revision:1,title:'leak',bodyText:'secret',
   visibility:'PUBLIC',isPublished:true
  }],nextBeforePageId:null})
 }));
 await enter(p);
 await p.getByRole('button',{name:'دریافت تنظیمات خصوصی سایت'}).click();
 await p.getByRole('button',{name:'دریافت صفحات خصوصی برای پیش‌نمایش'}).click();
 await expect(p.getByRole('alert')).toContainText('صفحات معتبر برای پیش‌نمایش دریافت نشد');
 await expect(p.getByText('leak')).toHaveCount(0);
});

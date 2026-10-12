import{test,expect}from'@playwright/test';
test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE!=='center','Center website drafts only');
const page='10000000-0000-4000-8000-000000000777';
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
test('builds a private site page, edits by expected version and isolates center switch',async({page:p})=>{
 let version=0;const posts:any[]=[];
 await p.route('**/api/v1/centers/*/site/drafts/revisions',async r=>{
  const x=r.request().postDataJSON();posts.push(x);version++;
  await r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
   pageId:x.pageId,slug:x.slug,revision:version,visibility:'PRIVATE_DRAFT',isPublished:false
  })});
 });
 await p.route('**/api/v1/centers/*/site/drafts',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
   pages:[],nextBeforePageId:null
  })}));
 await enter(p);
 await p.getByRole('textbox',{name:'شناسه نشانی صفحه'}).fill('about-center');
 await p.getByRole('textbox',{name:'عنوان صفحه سایت'}).fill('درباره کانون');
 await p.getByRole('textbox',{name:'متن صفحه سایت'}).fill('متن آزمایشی خصوصی');
 await p.getByRole('button',{name:'ذخیره نسخه خصوصی صفحه'}).click();
 await expect(p.getByText('نسخه پیش‌نویس ذخیره شد؛ صفحه منتشر نشده است.')).toBeVisible();
 expect(posts[0]).toMatchObject({expectedRevision:0,slug:'about-center',title:'درباره کانون',
   bodyText:'متن آزمایشی خصوصی'});
 await p.getByRole('textbox',{name:'عنوان صفحه سایت'}).fill('درباره کانون - جدید');
 await p.getByRole('button',{name:'ذخیره نسخه خصوصی صفحه'}).click();
 await expect(p.getByText('نسخه پیش‌نویس ذخیره شد؛ صفحه منتشر نشده است.')).toBeVisible();
 expect(posts[1].expectedRevision).toBe(1);
 expect(posts[1].pageId).toBe(posts[0].pageId);
 await p.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c2');
 await expect(p.getByText('نسخه پیش‌نویس ذخیره شد؛ صفحه منتشر نشده است.')).toHaveCount(0);
});
test('denies reading other private site draft',async({page:p})=>{
 await p.route('**/api/v1/centers/*/site/drafts',r=>r.fulfill({
  status:403,contentType:'application/problem+json',body:'{}'
 }));
 await enter(p);
 await p.getByRole('button',{name:'دریافت صفحات پیش‌نویس'}).click();
 await expect(p.getByRole('alert')).toContainText('مجوز مستقل مشاهده سایت‌ساز');
});
test('a forged published site-page response is rejected',async({page:p})=>{
 await p.route('**/api/v1/centers/*/site/drafts',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
   pages:[{pageId:page,slug:'home',revision:1,title:'secret',bodyText:'private',
    recordedAt:'2026-10-12T10:00:00Z',visibility:'PUBLIC',isPublished:true}],
   nextBeforePageId:null
  })
 }));
 await enter(p);
 await p.getByRole('button',{name:'دریافت صفحات پیش‌نویس'}).click();
 await expect(p.getByRole('alert')).toContainText('دریافت صفحات ممکن نشد');
 await expect(p.getByText('secret')).toHaveCount(0);
});

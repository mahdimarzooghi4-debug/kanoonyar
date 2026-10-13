import {test,expect} from '@playwright/test';
test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE!=='center','Private site navigation');
const first='aaaaaaaa-aaaa-4aaa-8aaa-000000000001';
const second='bbbbbbbb-bbbb-4bbb-8bbb-000000000002';
const pages=[
 {pageId:second,slug:'resources',revision:1,title:'منابع',bodyText:'متن صفحه دوم',
  recordedAt:'2026-10-13T07:00:00Z',visibility:'PRIVATE_DRAFT',isPublished:false},
 {pageId:first,slug:'about',revision:2,title:'درباره کانون',bodyText:'متن صفحه اول',
  recordedAt:'2026-10-13T07:00:00Z',visibility:'PRIVATE_DRAFT',isPublished:false}
];
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
test('pin real page versions, reorder, save private menu and read true history',async({page:p})=>{
 const posted:Record<string,any>[]=[];
 await p.route('**/api/v1/centers/*/site/drafts',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({pages,nextBeforePageId:null})}));
 await p.route('**/api/v1/centers/*/site/navigation',r=>r.fulfill({
  status:200,contentType:'application/json',
  body:JSON.stringify({revision:null,items:[],visibility:'PRIVATE_DRAFT',isPublished:false})}));
 await p.route('**/api/v1/centers/*/site/navigation/revisions',r=>{
  if(r.request().method()==='POST'){
   const body=r.request().postDataJSON();posted.push(body);
   return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
    revision:body.expectedRevision+1,linkedPages:body.items.length,
    visibility:'PRIVATE_DRAFT',isPublished:false})});
  }
  return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
   revisions:[{revision:1,recordedAt:'2026-10-13T08:00:00Z',items:[
    {position:1,pageId:second,pinnedPageRevision:1,slug:'resources',title:'منابع'},
    {position:2,pageId:first,pinnedPageRevision:2,slug:'about',title:'درباره کانون'}],
    visibility:'PRIVATE_DRAFT',isPublished:false}],nextBeforeRevision:null,
   visibility:'PRIVATE_DRAFT',isPublished:false})});
 });
 await enter(p);
 await p.getByRole('button',{name:'خواندن صفحات واقعی کانون'}).click();
 await p.getByRole('button',{name:'دریافت نسخه منوی خصوصی'}).click();
 await expect(p.getByText('نسخه منو: ثبت‌نشده')).toBeVisible();
 await p.getByRole('combobox',{name:'انتخاب صفحه ناوبری'}).selectOption(first+':2');
 await p.getByRole('button',{name:'افزودن صفحه به منو'}).click();
 await p.getByRole('combobox',{name:'انتخاب صفحه ناوبری'}).selectOption(second+':1');
 await p.getByRole('button',{name:'افزودن صفحه به منو'}).click();
 await p.getByRole('button',{name:'انتقال صفحه resources به بالا'}).click();
 const preview=p.getByRole('navigation',{name:'پیش‌نمایش متنی منوی خصوصی'});
 await expect(preview.getByRole('listitem').first()).toContainText('منابع');
 await p.getByRole('button',{name:'ذخیره نسخه جدید منو'}).click();
 await expect(p.getByText('نسخه خصوصی منو ذخیره شد؛ سایت منتشر نشده است.')).toBeVisible();
 expect(posted).toHaveLength(1);
 expect(posted[0]).toEqual({expectedRevision:0,items:[
  {pageId:second,pinnedPageRevision:1},{pageId:first,pinnedPageRevision:2}]});
 await p.getByRole('button',{name:'تاریخچه نسخه‌های منو'}).click();
 await expect(p.getByText(/نسخه 1 — منابع ← درباره کانون — PRIVATE_DRAFT/)).toBeVisible();
 await p.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c2');
 await expect(p.getByText('منابع — /resources — نسخه منبع 1')).toHaveCount(0);
});
test('forged PUBLIC or published menu is refused rather than previewed',async({page:p})=>{
 await p.route('**/api/v1/centers/*/site/navigation',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({revision:1,
   items:[{position:1,pageId:first,pinnedPageRevision:2,slug:'about',title:'Wrong public menu'}],
   visibility:'PUBLIC',isPublished:true})}));
 await enter(p);
 await p.getByRole('button',{name:'دریافت نسخه منوی خصوصی'}).click();
 await expect(p.getByRole('alert')).toContainText('دریافت منوی معتبر ناموفق بود.');
 await expect(p.getByText('Wrong public menu')).toHaveCount(0);
});
test('independent navigation and source grants can deny access',async({page:p})=>{
 await p.route('**/api/v1/centers/*/site/navigation',r=>r.fulfill({
  status:403,contentType:'application/problem+json',body:'{}'}));
 await p.route('**/api/v1/centers/*/site/drafts',r=>r.fulfill({
  status:403,contentType:'application/problem+json',body:'{}'}));
 await enter(p);
 await p.getByRole('button',{name:'دریافت نسخه منوی خصوصی'}).click();
 await expect(p.getByRole('alert')).toContainText('مجوز مستقل مشاهده منوی خصوصی وجود ندارد.');
 await p.getByRole('button',{name:'خواندن صفحات واقعی کانون'}).click();
 await expect(p.getByRole('alert').last()).toContainText('مجوز مستقل مشاهده صفحات وجود ندارد.');
});

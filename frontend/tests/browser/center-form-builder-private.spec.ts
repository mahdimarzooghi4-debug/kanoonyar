import{test,expect}from'@playwright/test';
test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE!=='center','Private form authoring only');
const fid='10000000-0000-4000-8000-000000000444';
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
test('creates and updates private form with a safe read-only field preview',async({page:p})=>{
 let rev=0;const posts:any[]=[];
 await p.route('**/api/v1/centers/*/site/forms/drafts/revisions',async r=>{
   const x=r.request().postDataJSON();posts.push(x);rev++;
   await r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
    formId:x.formId,slug:x.slug,revision:rev,
    visibility:'PRIVATE_DRAFT',isPublished:false,acceptsSubmissions:false})});
 });
 await enter(p);
 await p.getByRole('textbox',{name:'نشانی فرم خصوصی'}).fill('culture-signup');
 await p.getByRole('textbox',{name:'عنوان فرم خصوصی'}).fill('فرم آزمایشی');
 await p.getByRole('textbox',{name:'عنوان فیلد 1'}).fill('موضوع');
 await p.getByRole('button',{name:'افزودن فیلد'}).click();
 await p.getByRole('textbox',{name:'کلید فیلد 2'}).fill('selection');
 await p.getByRole('textbox',{name:'عنوان فیلد 2'}).fill('موضوع انتخابی');
 await p.getByRole('combobox',{name:'نوع فیلد 2'}).selectOption('SINGLE_SELECT');
 await expect(p.getByRole('button',{name:/ارسال پاسخ/})).toHaveCount(0);
 await expect(p.getByRole('section',{name:'پیش‌نمایش خصوصی و غیرقابل ارسال فرم'})
    .getByRole('combobox',{name:'پیش‌نمایش موضوع انتخابی'})).toBeDisabled();
 await p.getByRole('button',{name:'ذخیره نسخه خصوصی فرم'}).click();
 await expect(p.getByText('نسخه فرم ثبت شد؛ دریافت پاسخ غیرفعال است.')).toBeVisible();
 expect(posts[0].fields).toHaveLength(2);
 expect(posts[0].fields[1].kind).toBe('SINGLE_SELECT');
 expect(posts[0].expectedRevision).toBe(0);
 await p.getByRole('textbox',{name:'عنوان فرم خصوصی'}).fill('نسخه دوم فرم');
 await p.getByRole('button',{name:'ذخیره نسخه خصوصی فرم'}).click();
 await expect(p.getByText('نسخه فرم ثبت شد؛ دریافت پاسخ غیرفعال است.')).toBeVisible();
 expect(posts[1].expectedRevision).toBe(1);
 expect(posts[1].formId).toBe(posts[0].formId);
 await p.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c2');
 await expect(p.getByText('نسخه فرم ثبت شد؛ دریافت پاسخ غیرفعال است.')).toHaveCount(0);
});
test('read grant denial reveals no center form list',async({page:p})=>{
 await p.route('**/api/v1/centers/*/site/forms/drafts',r=>
   r.fulfill({status:403,contentType:'application/problem+json',body:'{}'}));
 await enter(p);
 await p.getByRole('button',{name:'دریافت فرم‌های خصوصی'}).click();
 await expect(p.getByRole('alert')).toContainText('مجوز مستقل مشاهده فرم‌های خصوصی');
});
test('forged PUBLIC or submittable forms cannot be displayed',async({page:p})=>{
 await p.route('**/api/v1/centers/*/site/forms/drafts',r=>
   r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
    forms:[{formId:fid,slug:'secret',revision:1,title:'leaked',
     fields:[{key:'topic',label:'Topic',kind:'SHORT_TEXT',required:false,options:[]}],
     recordedAt:'2026-10-12T10:00:00Z',visibility:'PUBLIC',
     isPublished:true,acceptsSubmissions:true}],nextBeforeFormId:null
   })}));
 await enter(p);
 await p.getByRole('button',{name:'دریافت فرم‌های خصوصی'}).click();
 await expect(p.getByRole('alert')).toContainText('فهرست فرم‌ها معتبر دریافت نشد.');
 await expect(p.getByText('leaked')).toHaveCount(0);
});

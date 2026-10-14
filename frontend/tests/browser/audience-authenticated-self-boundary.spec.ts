import { test, expect } from '@playwright/test';

test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE !== 'audience',
  'S54 OIDC self-only audience access boundary');

const published = {programs:[{programId:'public-s54',version:1,title:'برنامه ملی عمومی',
 nationalObjectives:['هدف معتبر عمومی'],mandatoryRequirements:['الزام عمومی']}]};
async function publicCatalog(page: import('@playwright/test').Page){
 await page.route('**/api/v1/programs/published',route=>route.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify(published)}));
}
const area = (page:import('@playwright/test').Page)=>
 page.getByRole('region',{name:'بررسی هویت خود مخاطب'});
const subjectArea = (page:import('@playwright/test').Page)=>
 page.getByRole('region',{name:'هویت بررسی‌شده خودم'});

test('public S53 discovery stays readable without a bearer or login call',async({page})=>{
 const requests:string[]=[];
 await page.route('**/api/v1/identity/**',route=>{
  requests.push(route.request().url());
  return route.fulfill({status:401,body:'{}'});
 });
 await publicCatalog(page);
 await page.goto('/');
 await expect(page.getByText('برنامه ملی عمومی')).toBeVisible();
 await expect(area(page).getByRole('heading',{name:'بررسی هویت خودم'})).toBeVisible();
 await expect(subjectArea(page)).toHaveCount(0);
 expect(requests).toHaveLength(0);
 await expect(page.getByRole('button',{name:/ثبت‌نام|پرداخت|پذیرش|ارسال رضایت/})).toHaveCount(0);
});

test('authenticated subject comes only from server and cannot imply personal records or permission',async({page})=>{
 const identityCalls:Array<{method:string;url:string;authorization:string|undefined;credentials:string|undefined}>=[];
 await publicCatalog(page);
 await page.route('**/api/v1/identity/me',route=>{
  const req=route.request();
  identityCalls.push({method:req.method(),url:req.url(),
   authorization:req.headers()['authorization'],credentials:req.headers()['cookie']});
  return route.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
   subjectId:'signed-oidc-subject-54',role:'GLOBAL_ADMIN',
   guardianVerified:true,consentAccepted:true,centerMemberships:['center-X']})});
 });
 await page.goto('/');
 const a=area(page);
 await a.getByRole('textbox',{name:'توکن هویت آزمایشی'}).fill('signed-test-credential-54');
 await a.getByRole('button',{name:'بررسی هویت احرازشده'}).click();
 await expect(subjectArea(page)).toContainText('signed-oidc-subject-54');
 await expect(subjectArea(page)).toContainText('هیچ مجوز عملیاتی اعطا نشده است');
 expect(identityCalls).toHaveLength(1);
 expect(identityCalls[0].authorization).toBe('Bearer signed-test-credential-54');
 expect(identityCalls[0].method).toBe('GET');
 expect(identityCalls[0].credentials).toBeUndefined();
 expect(identityCalls[0].url).toContain('/api/v1/identity/me');
 await expect(page.getByText('GLOBAL_ADMIN')).toHaveCount(0);
 await expect(page.getByText('guardianVerified')).toHaveCount(0);
 await expect(page.getByText('center-X')).toHaveCount(0);
 await expect(page.getByText('برنامه ملی عمومی')).toBeVisible();
 expect(await page.evaluate(()=>[...Object.keys(localStorage),...Object.keys(sessionStorage)]
   .filter(k=>/token|identity|subject|audience/i.test(k)))).toHaveLength(0);
 await a.getByRole('button',{name:'پاک‌کردن اطلاعات ورود'}).click();
 await expect(subjectArea(page)).toHaveCount(0);
 await expect(a.getByRole('textbox',{name:'توکن هویت آزمایشی'})).toHaveValue('');
 await expect(page.getByText('برنامه ملی عمومی')).toBeVisible();
});

test('401 expired or invalid OIDC and 403 missing-sub never retain a verified subject',async({page})=>{
 await publicCatalog(page);
 let status=200;
 await page.route('**/api/v1/identity/me',route=>route.fulfill({
  status,contentType:'application/json',
  body:status===200?'{"subjectId":"previously-signed-sub"}':'{}'}));
 await page.goto('/');
 const a=area(page);
 await a.getByRole('textbox',{name:'توکن هویت آزمایشی'}).fill('token-old');
 await a.getByRole('button',{name:'بررسی هویت احرازشده'}).click();
 await expect(subjectArea(page)).toContainText('previously-signed-sub');
 status=401;
 await a.getByRole('button',{name:'بررسی هویت احرازشده'}).click();
 await expect(subjectArea(page)).toHaveCount(0);
 await expect(a.getByRole('alert')).toContainText('اعتبار امضا، صادرکننده، مخاطب یا زمان اعتبار');
 status=403;
 await a.getByRole('button',{name:'بررسی هویت احرازشده'}).click();
 await expect(a.getByRole('alert')).toContainText('شناسه احرازشده معتبر');
 await expect(subjectArea(page)).toHaveCount(0);
});

test('malformed untrusted self response or server outage never authorizes audience actions',async({page})=>{
 await publicCatalog(page);
 let variant:'malformed'|'outage'='malformed';
 await page.route('**/api/v1/identity/me',route=>route.fulfill(
  variant==='malformed'?
   {status:200,contentType:'application/json',body:'{"subjectId":"","role":"admin"}'}:
   {status:503,contentType:'application/problem+json',body:'{}'}));
 await page.goto('/');
 const a=area(page);
 await a.getByRole('textbox',{name:'توکن هویت آزمایشی'}).fill('opaque-token');
 await a.getByRole('button',{name:'بررسی هویت احرازشده'}).click();
 await expect(a.getByRole('alert')).toContainText('پاسخ معتبر هویت دریافت نشد');
 await expect(subjectArea(page)).toHaveCount(0);
 variant='outage';
 await a.getByRole('button',{name:'بررسی هویت احرازشده'}).click();
 await expect(a.getByRole('alert')).toContainText('سرویس هویت در دسترس نیست');
 await expect(subjectArea(page)).toHaveCount(0);
 await expect(page.getByText('هدف معتبر عمومی')).toHaveCount(0);
 await expect(page.getByText('برنامه ملی عمومی')).toBeVisible();
});

test('changing credentials while original request is in-flight cannot reinstate old subject',async({page})=>{
 await publicCatalog(page);
 let call=0;
 await page.route('**/api/v1/identity/me',async route=>{
  call++;
  if(call===1){
   await new Promise(resolve=>setTimeout(resolve,400));
   try { await route.fulfill({status:200,contentType:'application/json',
    body:'{"subjectId":"leaked-old-subject"}'}); } catch { /* expected abort */ }
  }else await route.fulfill({status:200,contentType:'application/json',
   body:'{"subjectId":"verified-new-subject"}'});
 });
 await page.goto('/');
 const a=area(page);
 await a.getByRole('textbox',{name:'توکن هویت آزمایشی'}).fill('first-token');
 await a.getByRole('button',{name:'بررسی هویت احرازشده'}).click();
 await a.getByRole('textbox',{name:'توکن هویت آزمایشی'}).fill('second-token');
 await expect(subjectArea(page)).toHaveCount(0);
 await a.getByRole('button',{name:'بررسی هویت احرازشده'}).click();
 await expect(subjectArea(page)).toContainText('verified-new-subject');
 await page.waitForTimeout(500);
 await expect(page.getByText('leaked-old-subject')).toHaveCount(0);
 await expect(subjectArea(page)).toContainText('verified-new-subject');
});

test('deleting credentials during in-flight verification clears private content without blocking public read',async({page})=>{
 await publicCatalog(page);
 await page.route('**/api/v1/identity/me',async route=>{
  await new Promise(resolve=>setTimeout(resolve,250));
  try { await route.fulfill({status:200,contentType:'application/json',
   body:'{"subjectId":"late-old-identity"}'}); } catch { /* aborted */ }
 });
 await page.goto('/');
 const a=area(page);
 await a.getByRole('textbox',{name:'توکن هویت آزمایشی'}).fill('old-secret');
 await a.getByRole('button',{name:'بررسی هویت احرازشده'}).click();
 await a.getByRole('button',{name:'پاک‌کردن اطلاعات ورود'}).click();
 await page.waitForTimeout(300);
 await expect(subjectArea(page)).toHaveCount(0);
 await expect(a.getByRole('textbox',{name:'توکن هویت آزمایشی'})).toHaveValue('');
 await expect(page.getByText('برنامه ملی عمومی')).toBeVisible();
});

import {test,expect} from '@playwright/test';
test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE!=='center','S47 genuine class-specific session lookup');

const cls='aaaaaaaa-aaaa-4aaa-8aaa-000000000047';
const unrelated='bbbbbbbb-bbbb-4bbb-8bbb-000000000047';
const classTitle='کلاس با جلسات پراکنده';
const classEntry={classId:cls,title:classTitle};
const ids=Array.from({length:22},(_,i)=>'00000000-0000-4000-8000-'+
 String(22-i).padStart(12,'0'));
const records=ids.map((sourceId,i)=>({sourceId,classId:cls,classTitle,
 title:'جلسه '+String(22-i),scheduledAt:null}));
async function enter(p:import('@playwright/test').Page){
 await p.route('**/api/v1/identity/center-memberships',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({memberships:[
   {centerId:'c1',displayName:'کانون یک',roleCode:'member'},
   {centerId:'c2',displayName:'کانون دوم',roleCode:'member'}]})}));
 await p.goto('/');
 await p.getByRole('textbox',{name:'توکن دسترسی'}).fill('synthetic-s47');
 await p.getByRole('button',{name:'دریافت عضویت‌ها'}).click();
 await p.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c1');
}
async function open(p:import('@playwright/test').Page){
 const container=p.locator('section[aria-label="مدیریت درخواست کلاس‌های موجود"]');
 await container.getByRole('button',{name:'دریافت کاتالوگ اصلی همه کلاس‌ها'}).click();
 await container.getByRole('button',{name:'مشاهده جلسات همین کلاس'}).click();
 return container.locator('section[aria-label="میزکار جلسات کلاس '+cls+'"]');
}
function catalogue(p:import('@playwright/test').Page){
 return p.route(/\/classes\/catalogue(?:\?.*)?$/,r=>r.fulfill({
  status:200,contentType:'application/json',
  body:JSON.stringify({classes:[classEntry],nextBeforeClassId:null})}));
}
test('class-owner catalogue -> original per-class S12 20+2 sessions without global source scan',async({page:p})=>{
 const urls:string[]=[];
 await catalogue(p);
 await p.route(/\/classes\/[^/]+\/source-sessions(?:\?.*)?$/,r=>{
  urls.push(r.request().url());
  const page2=r.request().url().includes('beforeSourceId=');
  return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
   sourceKind:'CLASS_SESSION',classId:cls,classTitle,
   records:page2?records.slice(20):records.slice(0,20),
   nextBeforeSourceId:page2?null:ids[19]})});
 });
 let globalCalls=0;
 await p.route(/\/activity-sources\/class-sessions(?:\?.*)?$/,r=>{
  globalCalls++;return r.fulfill({status:403,body:'{}'});
 });
 await enter(p);
 const panel=await open(p);
 await panel.getByRole('button',{name:'دریافت همه جلسات این کلاس از منبع اصلی'}).click();
 await expect(panel.getByRole('button',{name:'آماده‌سازی خصوصی همین جلسه'})).toHaveCount(20);
 await panel.getByRole('button',{name:'جلسات قدیمی‌تر همین کلاس'}).click();
 await expect(panel.getByRole('button',{name:'آماده‌سازی خصوصی همین جلسه'})).toHaveCount(22);
 expect(urls).toHaveLength(2);
 expect(urls.every(x=>x.includes('/classes/'+cls+'/source-sessions'))).toBe(true);
 expect(urls[1]).toContain('beforeSourceId='+ids[19]);
 expect(globalCalls).toBe(0);
 await panel.getByRole('button',{name:'آماده‌سازی خصوصی همین جلسه'}).last().click();
 const prep=panel.locator('section[aria-label="دفتر آماده‌سازی خصوصی جلسات"]');
 await expect(prep.getByText('جلسه اصلی انتخاب‌شده:',{exact:false})).toBeVisible();
 await expect(prep.getByRole('combobox',{name:'انتخاب جلسه آموزشی'})).toHaveCount(0);
 await p.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c2');
 await expect(p.locator('section[aria-label="میزکار جلسات کلاس '+cls+'"]')).toHaveCount(0);
});
test('class with no sessions has valid zero-session response without fabricated evidence',async({page:p})=>{
 await catalogue(p);
 await p.route(/\/classes\/[^/]+\/source-sessions(?:\?.*)?$/,r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
   sourceKind:'CLASS_SESSION',classId:cls,classTitle,records:[],nextBeforeSourceId:null})}));
 await enter(p);
 const panel=await open(p);
 await panel.getByRole('button',{name:'دریافت همه جلسات این کلاس از منبع اصلی'}).click();
 await expect(panel.getByRole('status')).toContainText('برای این کلاس جلسه‌ای ثبت نشده است');
 await expect(panel.getByRole('button',{name:'ادعای وقوع و حضور همین جلسه'})).toHaveCount(0);
});
test('independent S12 denial removes session actions despite existing class catalogue access',async({page:p})=>{
 await catalogue(p);
 await p.route(/\/classes\/[^/]+\/source-sessions(?:\?.*)?$/,r=>r.fulfill({
  status:403,contentType:'application/problem+json',body:'{}'}));
 await enter(p);
 const panel=await open(p);
 await panel.getByRole('button',{name:'دریافت همه جلسات این کلاس از منبع اصلی'}).click();
 await expect(panel.getByRole('alert')).toContainText('مجوز مستقل مشاهده جلسات اصلی این کلاس وجود ندارد.');
 await expect(panel.getByRole('button',{name:'آماده‌سازی خصوصی همین جلسه'})).toHaveCount(0);
});
test('cross-class row, wrong owner title and forged cursor fail closed',async({page:p})=>{
 await catalogue(p);
 let mode:'wrongClass'|'wrongTitle'|'cursor'='wrongClass';
 await p.route(/\/classes\/[^/]+\/source-sessions(?:\?.*)?$/,r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
   sourceKind:'CLASS_SESSION',classId:cls,classTitle,
   records:[{...records[0],classId:mode==='wrongClass'?unrelated:cls,
    classTitle:mode==='wrongTitle'?'تحریف‌شده':classTitle}],
   nextBeforeSourceId:mode==='cursor'?ids[0]:null})}));
 await enter(p);
 const panel=await open(p);
 for(const x of ['wrongClass','wrongTitle','cursor'] as const){
  mode=x;
  await panel.getByRole('button',{name:'دریافت همه جلسات این کلاس از منبع اصلی'}).click();
  await expect(panel.getByRole('alert')).toContainText('فهرست معتبر جلسات اصلی این کلاس دریافت نشد.');
  await expect(panel.getByRole('button',{name:'ادعای وقوع و حضور همین جلسه'})).toHaveCount(0);
 }
});

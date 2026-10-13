import {test,expect} from '@playwright/test';
test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE!=='center','Private calendar workspace');
const eventId='22222222-2222-4222-8222-222222222222';
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
 await p.locator('input[aria-label="شروع بازه تقویم"]').fill('2026-11-01');
 await p.locator('input[aria-label="پایان انحصاری بازه تقویم"]').fill('2026-12-01');
}
function item(n:number){
 return {scheduledAt:'2026-11-'+String(n).padStart(2,'0')+'T10:00:00Z',
  sourceKind:'CLASS_SESSION',sourceId:'aaaaaaaa-aaaa-4aaa-8aaa-'+String(n).padStart(12,'0'),
  title:'جلسه کلاس '+n,draftId:null,pinnedPlanRevision:null,planRevision:null,
  visibility:'PRIVATE_PLAN',isPublished:false,isExecuted:false,isAttendanceVerified:false};
}
function packet(items:ReturnType<typeof item>[],next:unknown){
 return {fromUtc:'2026-11-01T00:00:00Z',untilUtc:'2026-12-01T00:00:00Z',
  items,nextCursor:next,visibility:'PRIVATE_PLAN',
  isPublished:false,isExecuted:false,isAttendanceVerified:false};
}
test('loads source owner rows, honest statuses and ordered keyset page',async({page:p})=>{
 const urls:string[]=[];
 await p.route('**/api/v1/centers/*/schedule/private?*',r=>{
  urls.push(r.request().url());const before=new URL(r.request().url()).searchParams.get('beforeAt');
  const rows=before?[item(21),item(22)]:Array.from({length:20},(_,i)=>item(i+1));
  return r.fulfill({status:200,contentType:'application/json',
   body:JSON.stringify(packet(rows,before?null:{
    scheduledAt:rows[19].scheduledAt,sourceKind:rows[19].sourceKind,sourceId:rows[19].sourceId
   }))});
 });
 await enter(p);
 await p.getByRole('button',{name:'خواندن تقویم خصوصی'}).click();
 await expect(p.getByText('جلسه کلاس 1',{exact:true})).toBeVisible();
 await expect(p.getByText('جلسه کلاس 20')).toBeVisible();
 await p.getByRole('button',{name:'ادامه تقویم خصوصی'}).click();
 await expect(p.getByText('جلسه کلاس 22')).toBeVisible();
 expect(urls).toHaveLength(2);
 expect(urls[1]).toContain('beforeKind=CLASS_SESSION');
 await p.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c2');
 await expect(p.getByText('جلسه کلاس 1',{exact:true})).toHaveCount(0);
});
test('forged published or executed source cannot enter calendar',async({page:p})=>{
 await p.route('**/api/v1/centers/*/schedule/private?*',r=>r.fulfill({
  status:200,contentType:'application/json',
  body:JSON.stringify(packet([{...item(1),isPublished:true} as any],null))}));
 await enter(p);
 await p.getByRole('button',{name:'خواندن تقویم خصوصی'}).click();
 await expect(p.getByRole('alert')).toContainText('دریافت یا اعتبارسنجی تقویم خصوصی ممکن نشد.');
 await expect(p.getByText('جلسه کلاس 1',{exact:true})).toHaveCount(0);
});
test('revoked independent grant exposes no calendar data',async({page:p})=>{
 await p.route('**/api/v1/centers/*/schedule/private?*',r=>r.fulfill({
  status:403,contentType:'application/problem+json',body:'{}'}));
 await enter(p);
 await p.getByRole('button',{name:'خواندن تقویم خصوصی'}).click();
 await expect(p.getByRole('alert')).toContainText('مجوز مستقل مشاهده تقویم');
});

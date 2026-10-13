import {test,expect} from '@playwright/test';
test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE!=='center','Recipient-private proposal notes');
const id='aaaaaaaa-aaaa-4aaa-8aaa-000000000021';
const note='bbbbbbbb-bbbb-4bbb-8bbb-000000000019';
const proposal={proposalId:id,sourceCenterId:'c1',targetCenterId:'c2',
 title:'پیشنهاد مشترک واقعی',summary:'شرح پیشنهادی مجاز',
 recordedAt:'2026-10-13T10:00:00Z',visibility:'EXPLICIT_TARGET_ONLY',
 reviewStatus:'PROPOSED_UNREVIEWED',isAccepted:false,isExecuted:false};
const entry={noteId:note,noteText:'یادداشت محرمانه فقط برای کانون گیرنده',
 recordedAt:'2026-10-13T11:00:00Z',visibility:'RECIPIENT_INTERNAL',
 reviewStatus:'PROPOSED_UNREVIEWED',isAccepted:false,isExecuted:false};
async function enter(p:import('@playwright/test').Page){
 await p.route('**/api/v1/identity/center-memberships',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({memberships:[
   {centerId:'c1',displayName:'کانون مبدأ',roleCode:'member'},
   {centerId:'c2',displayName:'کانون گیرنده',roleCode:'member'}]})}));
 await p.goto('/');
 await p.getByRole('textbox',{name:'توکن دسترسی'}).fill('synthetic');
 await p.getByRole('button',{name:'دریافت عضویت‌ها'}).click();
 await p.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c1');
}
test('only the incoming center can see and author internal notes, and switching centers clears them',
 async({page:p})=>{
 const submissions:any[]=[];
 await p.route('**/api/v1/centers/*/collaboration/outgoing*',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
   direction:'OUTGOING',proposals:[proposal],nextBeforeProposalId:null,
   visibility:'EXPLICIT_TARGET_ONLY',reviewStatus:'PROPOSED_UNREVIEWED',
   isAccepted:false,isExecuted:false})}));
 await p.route('**/api/v1/centers/*/collaboration/incoming*',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
   direction:'INCOMING',proposals:[proposal],nextBeforeProposalId:null,
   visibility:'EXPLICIT_TARGET_ONLY',reviewStatus:'PROPOSED_UNREVIEWED',
   isAccepted:false,isExecuted:false})}));
 await p.route('**/api/v1/centers/*/collaboration/proposals/*/recipient-notes*',r=>{
  if(r.request().method()==='POST'){
   const body=r.request().postDataJSON();submissions.push(body);
   return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
    noteId:body.noteId,proposalId:id,targetCenterId:'c2',
    visibility:'RECIPIENT_INTERNAL',reviewStatus:'PROPOSED_UNREVIEWED',
    isAccepted:false,isExecuted:false})});
  }
  return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
   proposalId:id,targetCenterId:'c2',notes:[entry],nextBeforeNoteId:null,
   visibility:'RECIPIENT_INTERNAL',reviewStatus:'PROPOSED_UNREVIEWED',
   isAccepted:false,isExecuted:false})});
 });
 await enter(p);
 const container=p.locator('section[aria-label="همکاری خصوصی میان کانون‌ها"]');
 await container.getByRole('button',{name:'دریافت پیشنهادهای مبدأ'}).click();
 await expect(container.getByText('پیشنهاد مشترک واقعی',{exact:true})).toBeVisible();
 await expect(container.locator('section[aria-label^="یادداشت خصوصی پیشنهاد"]')).toHaveCount(0);
 await p.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c2');
 const dest=p.locator('section[aria-label="همکاری خصوصی میان کانون‌ها"]');
 await dest.getByRole('button',{name:'دریافت پیشنهادهای مقصد'}).click();
 const notes=dest.locator('section[aria-label="یادداشت خصوصی پیشنهاد '+id+'"]');
 await expect(notes).toBeVisible();
 await notes.getByRole('button',{name:'دریافت یادداشت‌های داخلی'}).click();
 await expect(notes.getByText('یادداشت محرمانه فقط برای کانون گیرنده — RECIPIENT_INTERNAL')).toBeVisible();
 await notes.getByRole('textbox',{name:'متن یادداشت داخلی همکاری'}).fill('فقط بررسی داخلی؛ بدون تصمیم');
 await notes.getByRole('button',{name:'ثبت یادداشت داخلی بدون تصمیم'}).click();
 await expect(notes.getByRole('status')).toContainText('همچنان پذیرفته نشده است');
 expect(submissions).toHaveLength(1);
 expect(submissions[0].noteText).toBe('فقط بررسی داخلی؛ بدون تصمیم');
 expect(submissions[0].isAccepted).toBeUndefined();
 await p.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c1');
 await expect(p.getByText('یادداشت محرمانه فقط برای کانون گیرنده — RECIPIENT_INTERNAL')).toHaveCount(0);
});
test('forged accepted review note is rejected',async({page:p})=>{
 await p.route('**/api/v1/centers/*/collaboration/incoming*',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
   direction:'INCOMING',proposals:[proposal],nextBeforeProposalId:null,
   visibility:'EXPLICIT_TARGET_ONLY',reviewStatus:'PROPOSED_UNREVIEWED',
   isAccepted:false,isExecuted:false})}));
 await p.route('**/api/v1/centers/*/collaboration/proposals/*/recipient-notes*',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
   proposalId:id,targetCenterId:'c2',notes:[{...entry,isAccepted:true}],
   nextBeforeNoteId:null,visibility:'RECIPIENT_INTERNAL',
   reviewStatus:'PROPOSED_UNREVIEWED',isAccepted:false,isExecuted:false})}));
 await enter(p);
 await p.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c2');
 const dest=p.locator('section[aria-label="همکاری خصوصی میان کانون‌ها"]');
 await dest.getByRole('button',{name:'دریافت پیشنهادهای مقصد'}).click();
 const notes=dest.locator('section[aria-label="یادداشت خصوصی پیشنهاد '+id+'"]');
 await notes.getByRole('button',{name:'دریافت یادداشت‌های داخلی'}).click();
 await expect(notes.getByRole('alert')).toContainText('یادداشت معتبر دریافت نشد.');
 await expect(notes.getByText('یادداشت محرمانه فقط برای کانون گیرنده — RECIPIENT_INTERNAL')).toHaveCount(0);
});
test('separate recipient note read and write permissions are denied',async({page:p})=>{
 await p.route('**/api/v1/centers/*/collaboration/incoming*',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
   direction:'INCOMING',proposals:[proposal],nextBeforeProposalId:null,
   visibility:'EXPLICIT_TARGET_ONLY',reviewStatus:'PROPOSED_UNREVIEWED',
   isAccepted:false,isExecuted:false})}));
 await p.route('**/api/v1/centers/*/collaboration/proposals/*/recipient-notes*',r=>r.fulfill({
  status:403,contentType:'application/problem+json',body:'{}'}));
 await enter(p);
 await p.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c2');
 const dest=p.locator('section[aria-label="همکاری خصوصی میان کانون‌ها"]');
 await dest.getByRole('button',{name:'دریافت پیشنهادهای مقصد'}).click();
 const notes=dest.locator('section[aria-label="یادداشت خصوصی پیشنهاد '+id+'"]');
 await notes.getByRole('button',{name:'دریافت یادداشت‌های داخلی'}).click();
 await expect(notes.getByRole('alert')).toContainText('مجوز مستقل مشاهده یادداشت مقصد موجود نیست.');
 await notes.getByRole('textbox',{name:'متن یادداشت داخلی همکاری'}).fill('محتوای داخلی');
 await notes.getByRole('button',{name:'ثبت یادداشت داخلی بدون تصمیم'}).click();
 await expect(notes.getByText('مجوز مستقل ثبت یادداشت مقصد وجود ندارد.',{exact:true})).toBeVisible();
});

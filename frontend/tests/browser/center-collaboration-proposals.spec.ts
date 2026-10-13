import {test,expect} from '@playwright/test';
test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE!=='center','Collaboration center workspace');
const fixed='aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const entry=(id:string,source='c1',target='c2')=>({
 proposalId:id,sourceCenterId:source,targetCenterId:target,
 title:'پیشنهاد همکاری آموزشی',summary:'شرح مشترک مجاز',
 recordedAt:'2026-10-13T10:00:00Z',visibility:'EXPLICIT_TARGET_ONLY',
 reviewStatus:'PROPOSED_UNREVIEWED',isAccepted:false,isExecuted:false
});
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
test('directed A to B submission, explicit recipient read, safe scope reset',async({page:p})=>{
 p.on('console',m=>{if(m.text().includes('S34_DIAG'))console.log('S34_BROWSER_CONSOLE',m.text());});
 p.on('response',async r=>{if(r.url().includes('/collaboration/outgoing'))console.log('S34_OUTGOING_RESPONSE',r.status(),(await r.text()).slice(0,700));});
 let recordedId=fixed;const writes:any[]=[];
 await p.route('**/api/v1/centers/*/collaboration/proposals',r=>{
  const request=r.request().postDataJSON();
  writes.push(request);recordedId=request.proposalId;
  return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
   proposalId:recordedId,sourceCenterId:'c1',targetCenterId:request.targetCenterId,
   visibility:'EXPLICIT_TARGET_ONLY',reviewStatus:'PROPOSED_UNREVIEWED',
   isAccepted:false,isExecuted:false})});
 });
 await p.route('**/api/v1/centers/*/collaboration/outgoing*',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
   direction:'OUTGOING',proposals:[entry(recordedId)],
   nextBeforeProposalId:null,visibility:'EXPLICIT_TARGET_ONLY',
   reviewStatus:'PROPOSED_UNREVIEWED',isAccepted:false,isExecuted:false})}));
 await p.route('**/api/v1/centers/*/collaboration/incoming*',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
   direction:'INCOMING',proposals:[entry(recordedId)],
   nextBeforeProposalId:null,visibility:'EXPLICIT_TARGET_ONLY',
   reviewStatus:'PROPOSED_UNREVIEWED',isAccepted:false,isExecuted:false})}));
 await enter(p);
 let panel=p.locator('section[aria-label="همکاری خصوصی میان کانون‌ها"]');
 await panel.getByRole('textbox',{name:'شناسه کانون دریافت‌کننده'}).fill('c2');
 await panel.getByRole('textbox',{name:'عنوان پیشنهاد همکاری'}).fill('پیشنهاد همکاری آموزشی');
 await panel.getByRole('textbox',{name:'شرح پیشنهاد همکاری'}).fill('شرح مشترک مجاز');
 await panel.getByRole('button',{name:'ثبت پیشنهاد برای کانون مشخص'}).click();
 await expect(panel.getByRole('status')).toContainText('هنوز پذیرفته نشده');
 expect(writes).toHaveLength(1);
 expect(writes[0]).toMatchObject({targetCenterId:'c2',title:'پیشنهاد همکاری آموزشی'});
 expect(writes[0].isAccepted).toBeUndefined();
 await panel.getByRole('button',{name:'دریافت پیشنهادهای مبدأ'}).click();
 await expect.poll(async()=>await panel.innerText(),{timeout:5000}).toContain('پیشنهاد همکاری آموزشی');
 await p.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c2');
 panel=p.locator('section[aria-label="همکاری خصوصی میان کانون‌ها"]');
 await expect(panel.getByText('پیشنهاد همکاری آموزشی',{exact:true})).toHaveCount(0);
 await panel.getByRole('button',{name:'دریافت پیشنهادهای مقصد'}).click();
 await expect(panel.getByText('پیشنهاد همکاری آموزشی',{exact:true})).toBeVisible();
 await expect(panel.getByText(/پذیرش یا اجرا تأیید نشده است/)).toBeVisible();
});
test('forged acceptance and wrong recipient are rejected',async({page:p})=>{
 await p.route('**/api/v1/centers/*/collaboration/incoming*',r=>r.fulfill({
  status:200,contentType:'application/json',body:JSON.stringify({
   direction:'INCOMING',proposals:[{...entry(fixed,'c1','c9'),isAccepted:true}],
   nextBeforeProposalId:null,visibility:'EXPLICIT_TARGET_ONLY',
   reviewStatus:'PROPOSED_UNREVIEWED',isAccepted:false,isExecuted:false})}));
 await enter(p);
 await p.getByRole('combobox',{name:'انتخاب کانون برای پیش‌نویس‌ها'}).selectOption('c2');
 const panel=p.locator('section[aria-label="همکاری خصوصی میان کانون‌ها"]');
 await panel.getByRole('button',{name:'دریافت پیشنهادهای مقصد'}).click();
 await expect(panel.getByRole('alert')).toContainText('دریافت معتبر پیشنهادهای همکاری ناموفق بود.');
 await expect(panel.getByText('پیشنهاد همکاری آموزشی',{exact:true})).toHaveCount(0);
});
test('independent grants deny incoming and outgoing without leaking proposal',async({page:p})=>{
 await p.route('**/api/v1/centers/*/collaboration/incoming*',r=>r.fulfill({
  status:403,contentType:'application/problem+json',body:'{}'}));
 await p.route('**/api/v1/centers/*/collaboration/outgoing*',r=>r.fulfill({
  status:403,contentType:'application/problem+json',body:'{}'}));
 await enter(p);
 const panel=p.locator('section[aria-label="همکاری خصوصی میان کانون‌ها"]');
 await panel.getByRole('button',{name:'دریافت پیشنهادهای مبدأ'}).click();
 await expect(panel.locator('section[aria-label="پیشنهادهای ارسالی کانون"]')
 .getByRole('alert')).toContainText('مجوز مستقل مشاهده پیشنهادهای همکاری وجود ندارد.');
 await panel.getByRole('button',{name:'دریافت پیشنهادهای مقصد'}).click();
 await expect(panel.locator('section[aria-label="پیشنهادهای دریافتی کانون"]')
 .getByRole('alert')).toContainText('مجوز مستقل مشاهده پیشنهادهای همکاری وجود ندارد.');
});

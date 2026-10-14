import { test, expect } from '@playwright/test';
const experience=process.env.KANOONYAR_BROWSER_EXPERIENCE;
test.skip(experience==='center','Only headquarters and audience public national catalogs');

const programId='zz-s56-public';
const item=(n:number)=>({programId,version:n,title:'برنامه نسخه‌دار سراسری',
 nationalObjectives:['هدف منتشرشده '+n],
 mandatoryRequirements:['الزام منتشرشده '+n]});
const first=Array.from({length:500},(_,i)=>item(i+1));
const second=[item(501),item(502)];
const cursor={afterProgramId:programId,afterVersion:500};

test('over 500 genuinely published versions load completely across a real source cursor',async({page})=>{
 const calls:string[]=[];
 await page.route('**/api/v1/programs/published**',async route=>{
  const url=new URL(route.request().url());
  calls.push(url.search);
  if(!url.search){
    return route.fulfill({status:200,contentType:'application/json',
      body:JSON.stringify({programs:first,nextCursor:cursor})});
  }
  expect(url.searchParams.get('afterProgramId')).toBe(programId);
  expect(url.searchParams.get('afterVersion')).toBe('500');
  await new Promise(resolve=>setTimeout(resolve,130));
  return route.fulfill({status:200,contentType:'application/json',
    body:JSON.stringify({programs:second,nextCursor:null})});
 });
 await page.goto('/');
 await expect(page.getByText('هدف منتشرشده 502')).toHaveCount(0);
 if(experience==='headquarters'){
  await expect(page.getByRole('heading',{name:'برنامه نسخه‌دار سراسری — نسخه 502'})).toBeVisible();
  await expect(page.locator('article')).toHaveCount(502);
 }else{
  await expect(page.getByText('1 برنامه از 1 برنامه منتشرشده')).toBeVisible();
  await expect(page.getByText('آخرین نسخه منتشرشده: 502؛ تعداد نسخه‌های منتشرشده: 502')).toBeVisible();
  await page.getByRole('button',{name:'مشاهده نسخه‌ها و الزامات برنامه'}).click();
  const compare=page.getByRole('combobox',{name:'نسخه منتشرشده پیشین برای مقایسه'});
  await compare.selectOption('1');
  await expect(page.getByText('هدف منتشرشده 502')).toBeVisible();
  await expect(page.getByText('هدف منتشرشده 1')).toBeVisible();
 }
 expect(calls).toHaveLength(2);
 expect(calls[0]).toBe('');
 expect(calls[1]).toContain('afterVersion=500');
});

test('broken continuation cannot present a partial public catalog as complete',async({page})=>{
 await page.route('**/api/v1/programs/published**',async route=>{
  const next=new URL(route.request().url()).search;
  return route.fulfill({status:next?503:200,
    contentType:'application/json',
    body:next?'{}':JSON.stringify({programs:first,nextCursor:cursor})});
 });
 await page.goto('/');
 await expect(page.getByRole('alert')).toBeVisible();
 await expect(page.getByText('برنامه نسخه‌دار سراسری',{exact:true})).toHaveCount(0);
 await expect(page.getByRole('heading',{name:'برنامه نسخه‌دار سراسری — نسخه 500'})).toHaveCount(0);
});

test('forged or repeated published cursor fails closed, not a loop or hidden truncation',async({page})=>{
 await page.route('**/api/v1/programs/published**',route=>route.fulfill({
  status:200,contentType:'application/json',
  body:JSON.stringify({programs:first,nextCursor:{
    afterProgramId:programId,afterVersion:499}}),
 }));
 await page.goto('/');
 await expect(page.getByRole('alert')).toBeVisible();
 await expect(page.getByText('برنامه نسخه‌دار سراسری',{exact:true})).toHaveCount(0);
});

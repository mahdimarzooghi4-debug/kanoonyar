import { test, expect } from '@playwright/test';
test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE !== 'headquarters', 'Headquarters-only inspection workbench');
const row=(programId:string,version=1)=>({rollupContractVersion:'S83_NATIONAL_INSPECTION_ROLLUP_V1',programId,nationalVersion:version,reviewRequestCount:2,requestsWithoutInspectionCount:1,requestsWithInspectionCount:1,inspectionNoteCount:1,integrityConsistentNoteCount:1,integrityInconsistentNoteCount:0,sourceComparisonUnavailableNoteCount:0,currentSourceMatchNoteCount:1,currentSourceDriftNoteCount:0,sourceFreshnessUnclassifiedNoteCount:0,latestTechnicalObservationAt:'2026-10-16T12:00:00Z',humanReviewRecorded:false,issuanceAuthorized:false,issuanceExecuted:false});
test('forbidden inspection read shows no rollup data',async({page})=>{
 await page.route('**/api/v1/headquarters/national-issuance/review-rollup',r=>r.fulfill({status:403,contentType:'application/problem+json',body:'{}'}));
 await page.goto('/');await expect(page.getByRole('heading',{name:'پوشش فنی بازرسی درخواست‌های ابلاغ ملی'})).toBeVisible();
 await expect(page.getByRole('alert')).toContainText('دسترسی خواندن پوشش بازرسی');await expect(page.getByRole('table')).toHaveCount(0);
});
test('workbench follows exact cursor and shows aggregate evidence',async({page})=>{
 let second=false;
 await page.route('**/api/v1/headquarters/national-issuance/review-rollup**',r=>{
  const u=new URL(r.request().url());if(u.searchParams.has('beforeProgramId')){second=true;expect(u.searchParams.get('beforeProgramId')).toBe('national-1');expect(u.searchParams.get('beforeNationalVersion')).toBe('1');return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({rollups:[row('national-2')],nextCursor:null})});}
  return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({rollups:Array.from({length:20},(_,i)=>row(i===19?'national-1':'national-'+String(20-i).padStart(2,'0'))),nextCursor:{beforeProgramId:'national-1',beforeNationalVersion:1}})});
 });
 await page.goto('/');await expect(page.getByRole('rowheader',{name:'national-20 / ۱'})).toBeVisible();
 await expect(page.getByText('تأیید انسانی، مجوز یا صدور ابلاغ را نشان نمی‌دهند')).toBeVisible();
 await page.getByRole('button',{name:'بعدی'}).click();await expect(page.getByRole('rowheader',{name:'national-2 / ۱'})).toBeVisible();expect(second).toBe(true);
});
test('workbench rejects a response that claims issuance authority',async({page})=>{
 await page.route('**/api/v1/headquarters/national-issuance/review-rollup',r=>r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({rollups:[{...row('national-1'),issuanceAuthorized:true}],nextCursor:null})}));
 await page.goto('/');await expect(page.getByRole('alert')).toContainText('با قرارداد فنی سازگار نیست');await expect(page.getByRole('table')).toHaveCount(0);
});

import { test, expect } from '@playwright/test';
import { readFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
test.skip(process.env.KANOONYAR_BROWSER_EXPERIENCE !== 'headquarters', 'Headquarters-only inspection workbench');
const exportHeaders=['شناسه برنامه','نسخه ملی','درخواست','بدون بازرسی','با بازرسی','یادداشت بازرسی','یکپارچگی سازگار','یکپارچگی ناسازگار','مقایسه ناموجود','منبع منطبق','منبع متغیر','تازگی نامشخص','Digest نامعتبر درخواست','ساختار نامعتبر درخواست','عدم تطبیق انتخاب کانون','پرچم اختیار نامشخص','شواهد Audit نامعتبر درخواست','شواهد Outbox نامعتبر درخواست','یکپارچگی درخواست اصلی نامعتبر','یکپارچگی مشاهده نامعتبر','شواهد Audit بازرسی نامعتبر','مشاهده غیرقابل‌مقایسه','آخرین مشاهده فنی'];
const csvCell=(value:string)=>'"'+value.replaceAll('"','""')+'"';
const exportCsv=(ids:string[])=>'\uFEFF'+exportHeaders.map(csvCell).join(',')+'\r\n'+ids.map((id,index)=>{const values=Array(23).fill('');values[0]=id;values[1]='1';values[2]='1';if(index===1)values[22]='first\r\nsecond';return values.map(csvCell).join(',');}).join('\r\n')+'\r\n';
const dossierCsvHeaders=['program_id','national_version','request_id','note_id','inspected_at','source_changed_since_request','original_request_integrity_valid','observation_integrity_valid','audit_and_outbox_integrity_valid','current_source_matches_observation','publication_state_changed','center_registry_change_count','exact_issuance_change_count','source_observation_comparable','human_review_recorded','issuance_authorized','issuance_executed','finding_codes'];
const dossierCsv=(programId:string)=>'\uFEFF'+dossierCsvHeaders.map(csvCell).join(',')+'\r\n'+[
 programId,'4','aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa','ffffffff-ffff-ffff-ffff-ffffffffffff','2026-10-17T12:00:00.0000000Z','true','true','true','true','true','false','1','0','true','false','false','false',''
].map(csvCell).join(',')+'\r\n';
const dossierRow=(noteId:string)=>({requestId:'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',noteId,inspectedAt:'2026-10-17T12:00:00.0000000Z',sourceChangedSinceRequest:true,originalRequestIntegrityValid:true,observationIntegrityValid:true,auditAndOutboxIntegrityValid:true,currentSourceMatchesObservation:true,publicationStateChanged:false,centerRegistryChangeCount:1,exactIssuanceChangeCount:0,sourceObservationComparable:true,humanReviewRecorded:false,issuanceAuthorized:false,issuanceExecuted:false,findingCodes:[]});
const row=(programId:string,version=1)=>({rollupContractVersion:'S90_NATIONAL_INSPECTION_ROLLUP_V2',programId,nationalVersion:version,reviewRequestCount:2,requestsWithoutInspectionCount:1,requestsWithInspectionCount:1,inspectionNoteCount:1,integrityConsistentNoteCount:1,integrityInconsistentNoteCount:0,sourceComparisonUnavailableNoteCount:0,currentSourceMatchNoteCount:0,currentSourceDriftNoteCount:1,sourceFreshnessUnclassifiedNoteCount:0,sourceSnapshotDigestInvalidRequestCount:0,sourceEnvelopeInvalidRequestCount:0,centerSelectionMismatchRequestCount:0,authorityFlagsNotClearRequestCount:0,auditEvidenceInvalidRequestCount:0,outboxEvidenceInvalidRequestCount:0,originalRequestIntegrityInvalidNoteCount:0,observationIntegrityInvalidNoteCount:0,inspectionAuditEvidenceInvalidNoteCount:0,sourceObservationNotComparableNoteCount:0,latestTechnicalObservationAt:'2026-10-16T12:00:00Z',humanReviewRecorded:false,issuanceAuthorized:false,issuanceExecuted:false,findingCodes:[]});
test('forbidden inspection read shows no rollup data',async({page})=>{
 await page.route('**/api/v1/headquarters/national-issuance/review-rollup',r=>r.fulfill({status:403,contentType:'application/problem+json',body:'{}'}));
 await page.goto('/');await expect(page.getByRole('heading',{name:'پوشش فنی بازرسی درخواست‌های ابلاغ ملی'})).toBeVisible();await page.getByRole('button',{name:'نمایش خلاصه پوشش فنی'}).click();
 await expect(page.locator('section[aria-labelledby="inspection-rollup-title"] [role="alert"]')).toContainText('دسترسی خواندن پوشش بازرسی');await expect(page.getByRole('table')).toHaveCount(0);
});
test('workbench follows exact cursor and shows aggregate evidence',async({page})=>{
 let second=false;
 await page.route('**/api/v1/headquarters/national-issuance/review-rollup**',r=>{
  const u=new URL(r.request().url());if(u.searchParams.has('beforeProgramId')){second=true;expect(u.searchParams.get('beforeProgramId')).toBe('national-1');expect(u.searchParams.get('beforeNationalVersion')).toBe('1');return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({rollups:[row('national-2')],nextCursor:null})});}
  return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({rollups:Array.from({length:20},(_,i)=>row(i===19?'national-1':'national-'+String(20-i).padStart(2,'0'))),nextCursor:{beforeProgramId:'national-1',beforeNationalVersion:1}})});
 });
 await page.goto('/');await page.getByRole('button',{name:'نمایش خلاصه پوشش فنی'}).click();await expect(page.getByRole('rowheader',{name:'national-20 / ۱'})).toBeVisible();
 const firstRow=page.getByRole('row').filter({has:page.getByRole('rowheader',{name:'national-20 / ۱'})});
 await expect(firstRow.locator('td').nth(6)).toHaveText('۰ / ۱');
 await expect(page.getByText('کدهای خطا فقط شمارش تشخیصی‌اند')).toBeVisible();
 await page.getByRole('button',{name:'بعدی'}).click();await expect(page.getByRole('rowheader',{name:'national-2 / ۱'})).toBeVisible();expect(second).toBe(true);
});
test('workbench rejects duplicates anywhere in a later page',async({page})=>{
 await page.route('**/api/v1/headquarters/national-issuance/review-rollup**',r=>{
  const u=new URL(r.request().url());if(u.searchParams.has('beforeProgramId'))return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({rollups:[row('national-0'),row('national-19')],nextCursor:null})});
  return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({rollups:Array.from({length:20},(_,i)=>row(i===19?'national-1':'national-'+String(20-i).padStart(2,'0'))),nextCursor:{beforeProgramId:'national-1',beforeNationalVersion:1}})});
 });
 await page.goto('/');await page.getByRole('button',{name:'نمایش خلاصه پوشش فنی'}).click();await expect(page.getByRole('rowheader',{name:'national-20 / ۱'})).toBeVisible();
 await page.getByRole('button',{name:'بعدی'}).click();await expect(page.locator('section[aria-labelledby="inspection-rollup-title"] [role="alert"]')).toContainText('صفحه بعدی پوشش بازرسی تکراری یا نامعتبر است.');await expect(page.getByRole('table')).toHaveCount(0);
});
test('workbench filters national groups by stable technical finding codes',async({page})=>{
 let filtered=false;
 await page.route('**/api/v1/headquarters/national-issuance/review-rollup**',r=>{
  const u=new URL(r.request().url());
  if(u.searchParams.get('findingsOnly')==='true'){
   filtered=true;
   return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({rollups:[{...row('finding-program'),inspectionAuditEvidenceInvalidNoteCount:1}],nextCursor:null})});
  }
  return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({rollups:[row('clean-program')],nextCursor:null})});
 });
 await page.goto('/');await page.getByRole('button',{name:'نمایش خلاصه پوشش فنی'}).click();
 await expect(page.getByRole('rowheader',{name:'clean-program / ۱'})).toBeVisible();
 await page.getByRole('button',{name:'فقط گروه‌های دارای کد فنی'}).click();
 await expect(page.getByRole('rowheader',{name:'finding-program / ۱'})).toBeVisible();
 await expect(page.getByRole('rowheader',{name:'clean-program / ۱'})).toHaveCount(0);
 await expect(page.getByRole('button',{name:'فقط گروه‌های دارای کد فنی'})).toHaveAttribute('aria-pressed','true');
 expect(filtered).toBe(true);
});
test('workbench filters by one selected finding code across pages',async({page})=>{
 let selectedCode='';
 await page.route('**/api/v1/headquarters/national-issuance/review-rollup**',r=>{
  const u=new URL(r.request().url());
  selectedCode=u.searchParams.get('findingCode')||'';
  if(selectedCode==='INSPECTION_AUDIT_EVIDENCE_INVALID'){
   expect(u.searchParams.get('findingsOnly')).toBe('true');
   return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({rollups:[{...row('audit-finding'),inspectionAuditEvidenceInvalidNoteCount:1}],nextCursor:null})});
  }
  if(u.searchParams.get('findingsOnly')==='true')
   return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({rollups:[{...row('audit-finding'),inspectionAuditEvidenceInvalidNoteCount:1},{...row('source-finding'),sourceEnvelopeInvalidRequestCount:1}],nextCursor:null})});
  return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({rollups:[row('clean-program')],nextCursor:null})});
 });
 await page.goto('/');await page.getByRole('button',{name:'نمایش خلاصه پوشش فنی'}).click();
 await page.getByRole('button',{name:'فقط گروه‌های دارای کد فنی'}).click();
 await expect(page.getByRole('rowheader',{name:'source-finding / ۱'})).toBeVisible();
 await page.getByLabel('کد فنی مشخص').selectOption('INSPECTION_AUDIT_EVIDENCE_INVALID');
 await expect(page.getByRole('rowheader',{name:'audit-finding / ۱'})).toBeVisible();
 await expect(page.getByRole('rowheader',{name:'source-finding / ۱'})).toHaveCount(0);
 expect(selectedCode).toBe('INSPECTION_AUDIT_EVIDENCE_INVALID');
});
test('workbench looks up one exact national program version',async({page})=>{
 let exact=false;
 await page.route('**/api/v1/headquarters/national-issuance/review-rollup**',r=>{
  const u=new URL(r.request().url());
  if(u.searchParams.has('targetProgramId')){
   exact=true;
   expect(u.searchParams.get('targetProgramId')).toBe('national-target');
   expect(u.searchParams.get('targetNationalVersion')).toBe('3');
   return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({rollups:[row('national-target',3)],nextCursor:null})});
  }
  return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({rollups:[row('national-other')],nextCursor:null})});
 });
 await page.goto('/');await page.getByRole('button',{name:'نمایش خلاصه پوشش فنی'}).click();
 await page.getByLabel('شناسه برنامه ملی').fill('national-target');
 await page.getByLabel('نسخه ملی').fill('3');
 await page.getByRole('button',{name:'یافتن نسخه دقیق'}).click();
 await expect(page.getByRole('rowheader',{name:'national-target / ۳'})).toBeVisible();
 await expect(page.getByRole('rowheader',{name:'national-other / ۱'})).toHaveCount(0);
 expect(exact).toBe(true);
});
test('technical dossier drilldown is version-scoped, keyset-paged and excludes private evidence',async({page})=>{
 let dossierCalls=0;
 await page.route('**/api/v1/headquarters/national-issuance/review-dossier**',r=>{
  const u=new URL(r.request().url());dossierCalls++;expect(u.searchParams.get('programId')).toBe('dossier-program');expect(u.searchParams.get('nationalVersion')).toBe('4');
  if(dossierCalls===1){expect(u.searchParams.has('beforeNoteId')).toBe(false);const rows=Array.from({length:50},(_,i)=>dossierRow('00000000-0000-0000-0000-'+String(i+1).padStart(12,'0')));const last=rows[49];return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({contractVersion:'S108_NATIONAL_INSPECTION_DOSSIER_V5',findingsOnly:false,findingCode:null,programId:'dossier-program',nationalVersion:4,matchingNoteSetSha256:'a'.repeat(64),matchingNoteCount:51,inspections:rows,nextCursor:{beforeInspectedAt:last.inspectedAt,beforeNoteId:last.noteId}})});}
  expect(u.searchParams.get('beforeNoteId')).toBe('00000000-0000-0000-0000-000000000050');expect(u.searchParams.get('beforeInspectedAt')).toBe('2026-10-17T12:00:00.0000000Z');
  return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({contractVersion:'S108_NATIONAL_INSPECTION_DOSSIER_V5',findingsOnly:false,findingCode:null,programId:'dossier-program',nationalVersion:4,matchingNoteSetSha256:'a'.repeat(64),matchingNoteCount:51,inspections:[dossierRow('ffffffff-ffff-ffff-ffff-ffffffffffff')],nextCursor:null})});
 });
 await page.route('**/api/v1/headquarters/national-issuance/review-rollup**',r=>r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({rollups:[row('dossier-program',4)],nextCursor:null})}));
 await page.goto('/');await page.getByRole('button',{name:'نمایش خلاصه پوشش فنی'}).click();await expect(page.getByRole('rowheader',{name:'dossier-program / ۴'})).toBeVisible();
 await page.getByRole('button',{name:'نمایش پرونده فنی'}).click();
 const dossier=page.locator('section[aria-labelledby="inspection-dossier-title"]');await expect(dossier.getByRole('heading')).toContainText('dossier-program / ۴');
 await expect(dossier.getByRole('row')).toHaveCount(51);await expect(dossier.getByText('مجوز و صدور false')).toHaveCount(50);await expect(dossier.getByText(/۵۱ یادداشت با فیلتر فعلی/)).toBeVisible();
 await expect(dossier).not.toContainText('centerId');await expect(dossier).not.toContainText('technicalNote');
 await dossier.getByRole('button',{name:'بعدی'}).click();await expect(dossier.getByRole('row')).toHaveCount(2);await expect(dossier.getByText('شناسه یادداشت')).toBeVisible();expect(dossierCalls).toBe(2);
});
test('technical dossier rejects any asserted human decision or issuance state',async({page})=>{
 await page.route('**/api/v1/headquarters/national-issuance/review-dossier**',r=>r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({contractVersion:'S108_NATIONAL_INSPECTION_DOSSIER_V5',findingsOnly:false,findingCode:null,programId:'dossier-program',nationalVersion:4,matchingNoteSetSha256:'a'.repeat(64),matchingNoteCount:1,inspections:[{...dossierRow('ffffffff-ffff-ffff-ffff-ffffffffffff'),humanReviewRecorded:true}],nextCursor:null})}));
 await page.route('**/api/v1/headquarters/national-issuance/review-rollup**',r=>r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({rollups:[row('dossier-program',4)],nextCursor:null})}));
 await page.goto('/');await page.getByRole('button',{name:'نمایش خلاصه پوشش فنی'}).click();await expect(page.getByRole('rowheader',{name:'dossier-program / ۴'})).toBeVisible();await page.getByRole('button',{name:'نمایش پرونده فنی'}).click();
 const dossier=page.locator('section[aria-labelledby="inspection-dossier-title"]');await expect(dossier.getByRole('alert')).toContainText('با قرارداد بازرسی سازگار نیست');await expect(dossier.getByRole('table')).toHaveCount(0);
});
test('technical dossier finding filter is bound to exact target and response contract',async({page})=>{
 const requested:URL[]=[];
 await page.route('**/api/v1/headquarters/national-issuance/review-rollup**',r=>r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({rollups:[row('dossier-program',4)],nextCursor:null})}));
 await page.route('**/api/v1/headquarters/national-issuance/review-dossier**',r=>{
  const url=new URL(r.request().url()),findingCode=url.searchParams.get('findingCode');requested.push(url);
  return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
   contractVersion:'S108_NATIONAL_INSPECTION_DOSSIER_V5',findingsOnly:findingCode!==null||url.searchParams.get('findingsOnly')==='true',
   findingCode,programId:'dossier-program',nationalVersion:4,matchingNoteSetSha256:'a'.repeat(64),matchingNoteCount:1,
   inspections:findingCode?[{...dossierRow('ffffffff-ffff-ffff-ffff-ffffffffffff'),findingCodes:[findingCode]}]:[dossierRow('eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee')],nextCursor:null
  })});
 });
 await page.goto('/');await page.getByRole('button',{name:'نمایش خلاصه پوشش فنی'}).click();await page.getByRole('button',{name:'نمایش پرونده فنی'}).click();
 const dossier=page.locator('section[aria-labelledby="inspection-dossier-title"]');
 await dossier.getByLabel('فیلتر پرونده فنی').selectOption('INSPECTION_AUDIT_EVIDENCE_INVALID');
 await expect(dossier.getByRole('rowheader',{name:/ffffffff-ffff/})).toHaveCount(0);
 await expect(dossier.getByRole('cell',{name:'ffffffff-ffff-ffff-ffff-ffffffffffff'})).toBeVisible();
 await expect(dossier.getByRole('cell',{name:/INSPECTION_AUDIT_EVIDENCE_INVALID/})).toBeVisible();
 expect(requested.at(-1)?.searchParams.get('programId')).toBe('dossier-program');
 expect(requested.at(-1)?.searchParams.get('nationalVersion')).toBe('4');
 expect(requested.at(-1)?.searchParams.get('findingsOnly')).toBe('true');
 expect(requested.at(-1)?.searchParams.get('findingCode')).toBe('INSPECTION_AUDIT_EVIDENCE_INVALID');
});
test('technical dossier rejects a match count smaller than its page',async({page})=>{
 await page.route('**/api/v1/headquarters/national-issuance/review-dossier**',r=>r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
  contractVersion:'S108_NATIONAL_INSPECTION_DOSSIER_V5',findingsOnly:false,findingCode:null,programId:'dossier-program',nationalVersion:4,
  matchingNoteSetSha256:'a'.repeat(64),matchingNoteCount:0,inspections:[dossierRow('ffffffff-ffff-ffff-ffff-ffffffffffff')],nextCursor:null
 })}));
 await page.route('**/api/v1/headquarters/national-issuance/review-rollup**',r=>r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({rollups:[row('dossier-program',4)],nextCursor:null})}));
 await page.goto('/');await page.getByRole('button',{name:'نمایش خلاصه پوشش فنی'}).click();await page.getByRole('button',{name:'نمایش پرونده فنی'}).click();
 const dossier=page.locator('section[aria-labelledby="inspection-dossier-title"]');
 await expect(dossier.getByRole('alert')).toContainText('با قرارداد بازرسی سازگار نیست');
 await expect(dossier.getByRole('table')).toHaveCount(0);
});
test('technical dossier stops pagination when the matching set changes with an equal count',async({page})=>{
 let calls=0;
 await page.route('**/api/v1/headquarters/national-issuance/review-dossier**',r=>{
  calls++;
  const rows=calls===1?Array.from({length:50},(_,i)=>dossierRow('00000000-0000-0000-0000-'+String(i+1).padStart(12,'0'))):[dossierRow('ffffffff-ffff-ffff-ffff-ffffffffffff')];
  const last=rows[rows.length-1];
  return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({
   contractVersion:'S108_NATIONAL_INSPECTION_DOSSIER_V5',findingsOnly:false,findingCode:null,programId:'dossier-program',nationalVersion:4,
   matchingNoteSetSha256:calls===1?'a'.repeat(64):'b'.repeat(64),matchingNoteCount:51,inspections:rows,
   nextCursor:calls===1?{beforeInspectedAt:last.inspectedAt,beforeNoteId:last.noteId}:null
  })});
 });
 await page.route('**/api/v1/headquarters/national-issuance/review-rollup**',r=>r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({rollups:[row('dossier-program',4)],nextCursor:null})}));
 await page.goto('/');await page.getByRole('button',{name:'نمایش خلاصه پوشش فنی'}).click();await page.getByRole('button',{name:'نمایش پرونده فنی'}).click();
 const dossier=page.locator('section[aria-labelledby="inspection-dossier-title"]');
 await expect(dossier.getByRole('row')).toHaveCount(51);
 await dossier.getByRole('button',{name:'بعدی'}).click();
 await expect(dossier.getByRole('alert')).toContainText('مجموعه یادداشت‌های منطبق بین صفحه‌ها تغییر کرده است');
 await expect(dossier.getByRole('table')).toHaveCount(0);
 expect(calls).toBe(2);
});
test('exact-version dossier CSV verifies schema, row count and SHA-256 before download',async({page})=>{
 let requested:URL|null=null;
 await page.route('**/api/v1/headquarters/national-issuance/review-rollup**',r=>r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({rollups:[row('dossier-program',4)],nextCursor:null})}));
 await page.route('**/api/v1/headquarters/national-issuance/review-dossier**',r=>r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({contractVersion:'S108_NATIONAL_INSPECTION_DOSSIER_V5',findingsOnly:false,findingCode:null,programId:'dossier-program',nationalVersion:4,matchingNoteSetSha256:'a'.repeat(64),matchingNoteCount:1,inspections:[dossierRow('ffffffff-ffff-ffff-ffff-ffffffffffff')],nextCursor:null})}));
 await page.route('**/api/v1/headquarters/national-issuance/review-dossier/export.csv**',r=>{
  requested=new URL(r.request().url());expect(requested.searchParams.get('programId')).toBe('dossier-program');expect(requested.searchParams.get('nationalVersion')).toBe('4');
  const csv=dossierCsv('dossier-program'),digest=createHash('sha256').update(csv,'utf8').digest('hex');
  return r.fulfill({status:200,contentType:'text/csv; charset=utf-8',body:csv,headers:{
   'X-National-Inspection-Dossier-Export-Contract':'S105_NATIONAL_INSPECTION_DOSSIER_CSV_RECEIPT_V2',
   'X-National-Inspection-Dossier-Export-Schema':'S105_NATIONAL_INSPECTION_DOSSIER_CSV_V2',
   'X-National-Inspection-Dossier-Export-Row-Count':'1','X-National-Inspection-Dossier-Export-SHA256':digest
  }});
 });
 await page.goto('/');await page.getByRole('button',{name:'نمایش خلاصه پوشش فنی'}).click();await expect(page.getByRole('rowheader',{name:'dossier-program / ۴'})).toBeVisible();
 await page.getByRole('button',{name:'نمایش پرونده فنی'}).click();const dossier=page.locator('section[aria-labelledby="inspection-dossier-title"]');
 const downloadPromise=page.waitForEvent('download');await dossier.getByRole('button',{name:'دانلود CSV پروندهٔ فنی'}).click();
 const download=await downloadPromise;expect(download.suggestedFilename()).toBe('national-inspection-dossier.csv');expect(requested).not.toBeNull();
 await expect(dossier.getByText(/پروندهٔ دقیق شامل ۱ یادداشت فنی است/)).toBeVisible();
});
test('dossier CSV refuses corrupt digest and over-limit partial exports',async({page})=>{
 let mode='digest';let downloads=0;page.on('download',()=>downloads++);
 await page.route('**/api/v1/headquarters/national-issuance/review-rollup**',r=>r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({rollups:[row('dossier-program',4)],nextCursor:null})}));
 await page.route('**/api/v1/headquarters/national-issuance/review-dossier**',r=>r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({contractVersion:'S108_NATIONAL_INSPECTION_DOSSIER_V5',findingsOnly:false,findingCode:null,programId:'dossier-program',nationalVersion:4,matchingNoteSetSha256:'a'.repeat(64),matchingNoteCount:1,inspections:[dossierRow('ffffffff-ffff-ffff-ffff-ffffffffffff')],nextCursor:null})}));
 await page.route('**/api/v1/headquarters/national-issuance/review-dossier/export.csv**',r=>{
  if(mode==='limit')return r.fulfill({status:413,contentType:'application/problem+json',body:'{}'});
  return r.fulfill({status:200,contentType:'text/csv; charset=utf-8',body:dossierCsv('dossier-program'),headers:{
   'X-National-Inspection-Dossier-Export-Contract':'S105_NATIONAL_INSPECTION_DOSSIER_CSV_RECEIPT_V2',
   'X-National-Inspection-Dossier-Export-Schema':'S105_NATIONAL_INSPECTION_DOSSIER_CSV_V2',
   'X-National-Inspection-Dossier-Export-Row-Count':'1','X-National-Inspection-Dossier-Export-SHA256':'0'.repeat(64)
  }});
 });
 await page.goto('/');await page.getByRole('button',{name:'نمایش خلاصه پوشش فنی'}).click();await page.getByRole('button',{name:'نمایش پرونده فنی'}).click();
 const dossier=page.locator('section[aria-labelledby="inspection-dossier-title"]');await dossier.getByRole('button',{name:'دانلود CSV پروندهٔ فنی'}).click();
 await expect(dossier.getByRole('alert')).toContainText('اثر انگشت');expect(downloads).toBe(0);
 mode='limit';await dossier.getByRole('button',{name:'دانلود CSV پروندهٔ فنی'}).click();
 await expect(dossier.getByRole('alert')).toContainText('خروجی ناقص ساخته نشد');expect(downloads).toBe(0);
});
test('CSV export uses one atomic filtered endpoint response and excludes private fields',async({page})=>{
 const requested:URL[]=[];
 await page.route('**/api/v1/headquarters/national-issuance/review-rollup**',r=>{
  const u=new URL(r.request().url());
  if(u.pathname.endsWith('/export.csv')){
   requested.push(u);
   const ids=Array.from({length:22},(_,i)=>i===0?"'=1+1":i===1?'export-02':'export-'+String(i+1).padStart(2,'0'));
   const csv=exportCsv(ids);
   const digest=createHash('sha256').update(csv,'utf8').digest('hex');
   return r.fulfill({status:200,contentType:'text/csv; charset=utf-8',body:csv,headers:{
    'X-National-Inspection-Export-Contract':'S98_NATIONAL_INSPECTION_CSV_RECEIPT_V1',
    'X-National-Inspection-Export-Schema':'S100_NATIONAL_INSPECTION_CSV_SCHEMA_V1',
    'X-National-Inspection-Export-Row-Count':'22',
    'X-National-Inspection-Export-SHA256':digest
   }});
  }
  if(u.searchParams.get('findingsOnly')==='true'&&u.searchParams.get('findingCode')==='INSPECTION_AUDIT_EVIDENCE_INVALID')
   return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({rollups:[{...row('filtered-program'),inspectionAuditEvidenceInvalidNoteCount:1}],nextCursor:null})});
  return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({rollups:[row('initial')],nextCursor:null})});
 });
 await page.goto('/');
 await page.getByRole('button',{name:'نمایش خلاصه پوشش فنی'}).click();
 await page.getByRole('button',{name:'فقط گروه‌های دارای کد فنی'}).click();
 await page.getByLabel('کد فنی مشخص').selectOption('INSPECTION_AUDIT_EVIDENCE_INVALID');
 await expect(page.getByRole('rowheader',{name:'filtered-program / ۱'})).toBeVisible();
 const downloadPromise=page.waitForEvent('download');
 await page.getByRole('button',{name:'دانلود CSV تجمیعی'}).click();
 const download=await downloadPromise;
 expect(download.suggestedFilename()).toBe('national-inspection-rollup.csv');
 expect(requested).toHaveLength(1);
 expect(requested[0].searchParams.get('findingsOnly')).toBe('true');
 expect(requested[0].searchParams.get('findingCode')).toBe('INSPECTION_AUDIT_EVIDENCE_INVALID');
 expect(requested[0].searchParams.has('beforeProgramId')).toBe(false);
 const filePath=await download.path();expect(filePath).not.toBeNull();
 const csv=await readFile(filePath!,'utf8');
 expect(csv.charCodeAt(0)).toBe(0xFEFF);
 expect(csv.split('\r\n')).toHaveLength(25);
 expect(csv.split('\r\n')[1]).toContain('"\'=1+1"');
 expect(csv).not.toContain('centerId');
 expect(csv).not.toContain('technicalNote');
 await expect(page.getByText(/SHA-256:/)).toBeVisible();
});
test('CSV export refuses a validly receipted file with the wrong schema version',async({page})=>{
 let downloads=0;page.on('download',()=>{downloads++;});
 await page.route('**/api/v1/headquarters/national-issuance/review-rollup**',r=>{
  const u=new URL(r.request().url());if(u.pathname.endsWith('/export.csv')){const csv=exportCsv(['synthetic']);return r.fulfill({status:200,contentType:'text/csv; charset=utf-8',body:csv,headers:{
   'X-National-Inspection-Export-Contract':'S98_NATIONAL_INSPECTION_CSV_RECEIPT_V1','X-National-Inspection-Export-Schema':'S99_UNKNOWN_SCHEMA',
   'X-National-Inspection-Export-Row-Count':'1','X-National-Inspection-Export-SHA256':createHash('sha256').update(csv,'utf8').digest('hex')}});}
  return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({rollups:[row('ready')],nextCursor:null})});
 });
 await page.goto('/');await page.getByRole('button',{name:'نمایش خلاصه پوشش فنی'}).click();await expect(page.getByRole('rowheader',{name:'ready / ۱'})).toBeVisible();
 await page.getByRole('button',{name:'دانلود CSV تجمیعی'}).click();
 await expect(page.locator('section[aria-labelledby="inspection-rollup-title"] [role="alert"]')).toContainText('رسید یکپارچگی');expect(downloads).toBe(0);
});
test('CSV export refuses rows whose column count differs from the declared schema',async({page})=>{
 let downloads=0;page.on('download',()=>{downloads++;});
 await page.route('**/api/v1/headquarters/national-issuance/review-rollup**',r=>{
  const u=new URL(r.request().url());if(u.pathname.endsWith('/export.csv')){const csv='\uFEFF'+exportHeaders.map(csvCell).join(',')+'\r\n'+Array(22).fill('"value"').join(',')+'\r\n';return r.fulfill({status:200,contentType:'text/csv; charset=utf-8',body:csv,headers:{
   'X-National-Inspection-Export-Contract':'S98_NATIONAL_INSPECTION_CSV_RECEIPT_V1','X-National-Inspection-Export-Schema':'S100_NATIONAL_INSPECTION_CSV_SCHEMA_V1',
   'X-National-Inspection-Export-Row-Count':'1','X-National-Inspection-Export-SHA256':createHash('sha256').update(csv,'utf8').digest('hex')}});}
  return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({rollups:[row('ready')],nextCursor:null})});
 });
 await page.goto('/');await page.getByRole('button',{name:'نمایش خلاصه پوشش فنی'}).click();await expect(page.getByRole('rowheader',{name:'ready / ۱'})).toBeVisible();
 await page.getByRole('button',{name:'دانلود CSV تجمیعی'}).click();
 await expect(page.locator('section[aria-labelledby="inspection-rollup-title"] [role="alert"]')).toContainText('تعداد ستون');expect(downloads).toBe(0);
});
test('CSV export refuses a reordered header even when the schema version matches',async({page})=>{
 let downloads=0;page.on('download',()=>{downloads++;});
 await page.route('**/api/v1/headquarters/national-issuance/review-rollup**',r=>{
  const u=new URL(r.request().url());if(u.pathname.endsWith('/export.csv')){const headers=[...exportHeaders];[headers[0],headers[1]]=[headers[1],headers[0]];const csv='\uFEFF'+headers.map(csvCell).join(',')+'\r\n'+Array(23).fill('"value"').join(',')+'\r\n';return r.fulfill({status:200,contentType:'text/csv; charset=utf-8',body:csv,headers:{
   'X-National-Inspection-Export-Contract':'S98_NATIONAL_INSPECTION_CSV_RECEIPT_V1','X-National-Inspection-Export-Schema':'S100_NATIONAL_INSPECTION_CSV_SCHEMA_V1',
   'X-National-Inspection-Export-Row-Count':'1','X-National-Inspection-Export-SHA256':createHash('sha256').update(csv,'utf8').digest('hex')}});}
  return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({rollups:[row('ready')],nextCursor:null})});
 });
 await page.goto('/');await page.getByRole('button',{name:'نمایش خلاصه پوشش فنی'}).click();await expect(page.getByRole('rowheader',{name:'ready / ۱'})).toBeVisible();
 await page.getByRole('button',{name:'دانلود CSV تجمیعی'}).click();
 await expect(page.locator('section[aria-labelledby="inspection-rollup-title"] [role="alert"]')).toContainText('سرستون CSV');expect(downloads).toBe(0);
});
test('CSV export refuses a file whose receipt digest does not match',async({page})=>{
 let downloads=0;
 page.on('download',()=>{downloads++;});
 await page.route('**/api/v1/headquarters/national-issuance/review-rollup**',r=>{
  const u=new URL(r.request().url());
  if(u.pathname.endsWith('/export.csv')){
   const csv=exportCsv(['synthetic']);
   return r.fulfill({status:200,contentType:'text/csv; charset=utf-8',body:csv,headers:{
    'X-National-Inspection-Export-Contract':'S98_NATIONAL_INSPECTION_CSV_RECEIPT_V1',
    'X-National-Inspection-Export-Schema':'S100_NATIONAL_INSPECTION_CSV_SCHEMA_V1',
    'X-National-Inspection-Export-Row-Count':'1',
    'X-National-Inspection-Export-SHA256':'0'.repeat(64)
   }});
  }
  return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({rollups:[row('ready')],nextCursor:null})});
 });
 await page.goto('/');await page.getByRole('button',{name:'نمایش خلاصه پوشش فنی'}).click();
 await expect(page.getByRole('rowheader',{name:'ready / ۱'})).toBeVisible();
 await page.getByRole('button',{name:'دانلود CSV تجمیعی'}).click();
 await expect(page.locator('section[aria-labelledby="inspection-rollup-title"] [role="alert"]')).toContainText('اثر انگشت');
 expect(downloads).toBe(0);
});
test('CSV export refuses a receipt whose row count disagrees with quoted records',async({page})=>{
 let downloads=0;
 page.on('download',()=>{downloads++;});
 await page.route('**/api/v1/headquarters/national-issuance/review-rollup**',r=>{
  const u=new URL(r.request().url());
  if(u.pathname.endsWith('/export.csv')){
   const csv=exportCsv(['synthetic']);
   return r.fulfill({status:200,contentType:'text/csv; charset=utf-8',body:csv,headers:{
    'X-National-Inspection-Export-Contract':'S98_NATIONAL_INSPECTION_CSV_RECEIPT_V1',
    'X-National-Inspection-Export-Schema':'S100_NATIONAL_INSPECTION_CSV_SCHEMA_V1',
    'X-National-Inspection-Export-Row-Count':'2',
    'X-National-Inspection-Export-SHA256':createHash('sha256').update(csv,'utf8').digest('hex')
   }});
  }
  return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({rollups:[row('ready')],nextCursor:null})});
 });
 await page.goto('/');await page.getByRole('button',{name:'نمایش خلاصه پوشش فنی'}).click();
 await expect(page.getByRole('rowheader',{name:'ready / ۱'})).toBeVisible();
 await page.getByRole('button',{name:'دانلود CSV تجمیعی'}).click();
 await expect(page.locator('section[aria-labelledby="inspection-rollup-title"] [role="alert"]')).toContainText('تعداد ردیف');
 expect(downloads).toBe(0);
});
test('CSV export does not start a download when the backend rejects the row limit',async({page})=>{
 let exports=0,downloads=0;
 page.on('download',()=>{downloads++;});
 await page.route('**/api/v1/headquarters/national-issuance/review-rollup**',r=>{
  const u=new URL(r.request().url());
  if(u.pathname.endsWith('/export.csv')){
   exports++;
   return r.fulfill({status:413,contentType:'application/problem+json',body:'{}'});
  }
  return r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({rollups:[row('ready')],nextCursor:null})});
 });
 await page.goto('/');await page.getByRole('button',{name:'نمایش خلاصه پوشش فنی'}).click();
 await expect(page.getByRole('rowheader',{name:'ready / ۱'})).toBeVisible();
 await page.getByRole('button',{name:'دانلود CSV تجمیعی'}).click();
 await expect(page.locator('section[aria-labelledby="inspection-rollup-title"] [role="alert"]')).toContainText('۱۰٬۰۰۰');
 expect(exports).toBe(1);
 expect(downloads).toBe(0);
});
test('workbench rejects a response that claims issuance authority',async({page})=>{
 await page.route('**/api/v1/headquarters/national-issuance/review-rollup',r=>r.fulfill({status:200,contentType:'application/json',body:JSON.stringify({rollups:[{...row('national-1'),issuanceAuthorized:true}],nextCursor:null})}));
 await page.goto('/');await page.getByRole('button',{name:'نمایش خلاصه پوشش فنی'}).click();await expect(page.locator('section[aria-labelledby="inspection-rollup-title"] [role="alert"]')).toContainText('با قرارداد فنی سازگار نیست');await expect(page.getByRole('table')).toHaveCount(0);
});

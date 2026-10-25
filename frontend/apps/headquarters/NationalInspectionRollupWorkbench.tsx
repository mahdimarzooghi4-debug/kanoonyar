import React, { useEffect, useState } from 'react';
type Cursor={beforeProgramId:string;beforeNationalVersion:number};
type Target={programId:string;nationalVersion:number};
type Row={rollupContractVersion:'S90_NATIONAL_INSPECTION_ROLLUP_V2';programId:string;nationalVersion:number;reviewRequestCount:number;requestsWithoutInspectionCount:number;requestsWithInspectionCount:number;inspectionNoteCount:number;integrityConsistentNoteCount:number;integrityInconsistentNoteCount:number;sourceComparisonUnavailableNoteCount:number;currentSourceMatchNoteCount:number;currentSourceDriftNoteCount:number;sourceFreshnessUnclassifiedNoteCount:number;sourceSnapshotDigestInvalidRequestCount:number;sourceEnvelopeInvalidRequestCount:number;centerSelectionMismatchRequestCount:number;authorityFlagsNotClearRequestCount:number;auditEvidenceInvalidRequestCount:number;outboxEvidenceInvalidRequestCount:number;originalRequestIntegrityInvalidNoteCount:number;observationIntegrityInvalidNoteCount:number;inspectionAuditEvidenceInvalidNoteCount:number;sourceObservationNotComparableNoteCount:number;latestTechnicalObservationAt:string|null;humanReviewRecorded:false;issuanceAuthorized:false;issuanceExecuted:false};
type Page={rollups:Row[];nextCursor:Cursor|null};
type State={status:'loading'|'ready'|'error';error:string;pages:Page[];index:number};
const initial:State={status:'loading',error:'',pages:[],index:0};
const fields=['nationalVersion','reviewRequestCount','requestsWithoutInspectionCount','requestsWithInspectionCount','inspectionNoteCount','integrityConsistentNoteCount','integrityInconsistentNoteCount','sourceComparisonUnavailableNoteCount','currentSourceMatchNoteCount','currentSourceDriftNoteCount','sourceFreshnessUnclassifiedNoteCount','sourceSnapshotDigestInvalidRequestCount','sourceEnvelopeInvalidRequestCount','centerSelectionMismatchRequestCount','authorityFlagsNotClearRequestCount','auditEvidenceInvalidRequestCount','outboxEvidenceInvalidRequestCount','originalRequestIntegrityInvalidNoteCount','observationIntegrityInvalidNoteCount','inspectionAuditEvidenceInvalidNoteCount','sourceObservationNotComparableNoteCount'] as const;
function parsePage(value:unknown):Page|null {
 if(!value||typeof value!=='object')return null;
 const root=value as Record<string,unknown>;
 if(!Array.isArray(root.rollups)||root.rollups.length>20)return null;
 const rows:Row[]=[],seen=new Set<string>();
 for(const raw of root.rollups){
  if(!raw||typeof raw!=='object')return null;
  const r=raw as Record<string,unknown>;
  if(r.rollupContractVersion!=='S90_NATIONAL_INSPECTION_ROLLUP_V2'||typeof r.programId!=='string'||!r.programId.trim()||r.programId.length>256||
   fields.some(k=>typeof r[k]!=='number'||!Number.isSafeInteger(r[k])||(r[k] as number)<0)||(r.nationalVersion as number)<1||
   ((r.sourceSnapshotDigestInvalidRequestCount as number)>(r.reviewRequestCount as number)||(r.sourceEnvelopeInvalidRequestCount as number)>(r.reviewRequestCount as number)||(r.centerSelectionMismatchRequestCount as number)>(r.reviewRequestCount as number)||(r.authorityFlagsNotClearRequestCount as number)>(r.reviewRequestCount as number)||(r.auditEvidenceInvalidRequestCount as number)>(r.reviewRequestCount as number)||(r.outboxEvidenceInvalidRequestCount as number)>(r.reviewRequestCount as number)||(r.originalRequestIntegrityInvalidNoteCount as number)>(r.inspectionNoteCount as number)||(r.observationIntegrityInvalidNoteCount as number)>(r.inspectionNoteCount as number)||(r.inspectionAuditEvidenceInvalidNoteCount as number)>(r.inspectionNoteCount as number)||(r.sourceObservationNotComparableNoteCount as number)>(r.inspectionNoteCount as number))||r.humanReviewRecorded!==false||r.issuanceAuthorized!==false||r.issuanceExecuted!==false||
   !(r.latestTechnicalObservationAt===null||(typeof r.latestTechnicalObservationAt==='string'&&Number.isFinite(Date.parse(r.latestTechnicalObservationAt)))))return null;
  const key=JSON.stringify([r.programId,r.nationalVersion]);if(seen.has(key))return null;seen.add(key);rows.push(r as unknown as Row);
 }
 let next:Cursor|null=null;
 if(root.nextCursor!==null){
  if(!root.nextCursor||typeof root.nextCursor!=='object'||rows.length!==20)return null;
  const c=root.nextCursor as Record<string,unknown>,last=rows[rows.length-1];
  if(typeof c.beforeProgramId!=='string'||!Number.isSafeInteger(c.beforeNationalVersion)||c.beforeProgramId!==last.programId||c.beforeNationalVersion!==last.nationalVersion)return null;
  next={beforeProgramId:c.beforeProgramId,beforeNationalVersion:c.beforeNationalVersion as number};
 }
 return {rollups:rows,nextCursor:next};
}
async function read(cursor:Cursor|null,signal:AbortSignal,findingsOnly:boolean,findingCode:string,target:Target|null):Promise<Page>{
 const params=new URLSearchParams();
 if(cursor){params.set('beforeProgramId',cursor.beforeProgramId);params.set('beforeNationalVersion',String(cursor.beforeNationalVersion));}
 if(findingsOnly)params.set('findingsOnly','true');
 if(findingCode)params.set('findingCode',findingCode);
 if(target){params.set('targetProgramId',target.programId);params.set('targetNationalVersion',String(target.nationalVersion));}
 const encoded=params.toString(),query=encoded?'?'+encoded:'';
 const response=await fetch('/api/v1/headquarters/national-issuance/review-rollup'+query,{signal,cache:'no-store',credentials:'same-origin'});
 if(response.status===401)throw Error('برای مشاهده این بخش، ورود سازمانی لازم است.');
 if(response.status===403)throw Error('دسترسی خواندن پوشش بازرسی در این محیط فعال یا مجاز نیست.');
 if(!response.ok)throw Error('خلاصه پوشش بازرسی فعلاً در دسترس نیست.');
 const parsed=parsePage(await response.json());if(!parsed)throw Error('پاسخ پوشش بازرسی با قرارداد فنی سازگار نیست.');return parsed;
}
const n=(v:number)=>new Intl.NumberFormat('fa-IR').format(v);
const findingCodes=[
 ['SOURCE_SNAPSHOT_DIGEST_INVALID','Digest نسخه درخواست'],
 ['SOURCE_ENVELOPE_INVALID','ساختار منبع درخواست'],
 ['CENTER_SELECTION_MISMATCH','تطبیق انتخاب کانون'],
 ['AUTHORITY_FLAGS_NOT_CLEAR','پرچم‌های اختیار'],
 ['AUDIT_EVIDENCE_INVALID','شواهد Audit درخواست'],
 ['OUTBOX_EVIDENCE_INVALID','شواهد Outbox درخواست'],
 ['ORIGINAL_REQUEST_INTEGRITY_INVALID','یکپارچگی درخواست اصلی'],
 ['OBSERVATION_INTEGRITY_INVALID','یکپارچگی مشاهده'],
 ['INSPECTION_AUDIT_EVIDENCE_INVALID','شواهد Audit بازرسی'],
 ['SOURCE_OBSERVATION_NOT_COMPARABLE','عدم‌مقایسه‌پذیری مشاهده']
] as const;
function countCsvDataRows(text:string):number{
 if(!text.startsWith('\uFEFF'))throw Error('فایل CSV فاقد نشان UTF-8 معتبر است؛ فایلی ساخته نشد.');
 let records=0,inQuotes=false,afterQuote=false,fieldStart=true,recordStarted=false;
 for(let i=1;i<text.length;i++){
  const ch=text[i];
  if(inQuotes){
   if(ch==='"'){
    if(text[i+1]==='"')i++;
    else{inQuotes=false;afterQuote=true;}
   }
   continue;
  }
  if(afterQuote){
   if(ch===','){afterQuote=false;fieldStart=true;recordStarted=true;continue;}
   if(ch==='\r'&&text[i+1]==='\n'){
    records++;afterQuote=false;fieldStart=true;recordStarted=false;i++;continue;
   }
   throw Error('ساختار رکوردهای CSV معتبر نیست؛ فایلی ساخته نشد.');
  }
  if(ch==='"'){
   if(!fieldStart)throw Error('ساختار رکوردهای CSV معتبر نیست؛ فایلی ساخته نشد.');
   inQuotes=true;fieldStart=false;recordStarted=true;continue;
  }
  if(ch===','){fieldStart=true;recordStarted=true;continue;}
  if(ch==='\r'&&text[i+1]==='\n'){
   records++;fieldStart=true;recordStarted=false;i++;continue;
  }
  if(ch==='\r'||ch==='\n')throw Error('پایان رکوردهای CSV معتبر نیست؛ فایلی ساخته نشد.');
  fieldStart=false;recordStarted=true;
 }
 if(inQuotes)throw Error('ساختار رکوردهای CSV معتبر نیست؛ فایلی ساخته نشد.');
 if(recordStarted)records++;
 if(records===0)throw Error('فایل CSV سرستون معتبری ندارد؛ فایلی ساخته نشد.');
 return records-1;
}
export function NationalInspectionRollupWorkbench(){
 const [s,setS]=useState<State>(initial),[reload,setReload]=useState(0),[opened,setOpened]=useState(false),[findingsOnly,setFindingsOnly]=useState(false),[findingCode,setFindingCode]=useState('');
 const [targetProgramInput,setTargetProgramInput]=useState(''),[targetVersionInput,setTargetVersionInput]=useState(''),[target,setTarget]=useState<Target|null>(null),[targetError,setTargetError]=useState('');
 const [exporting,setExporting]=useState(false),[exportError,setExportError]=useState(''),[exportStatus,setExportStatus]=useState('');
 useEffect(()=>{if(!opened)return;const c=new AbortController();setS(initial);void read(null,c.signal,findingsOnly,findingCode,target).then(p=>{if(!c.signal.aborted)setS({status:'ready',error:'',pages:[p],index:0});}).catch(e=>{if(!c.signal.aborted)setS({status:'error',error:e instanceof Error?e.message:'خلاصه پوشش بازرسی فعلاً در دسترس نیست.',pages:[],index:0});});return()=>c.abort();},[reload,opened,findingsOnly,findingCode,target]);
 async function exportAll(){
  if(!opened||s.status!=='ready'||exporting)return;
  setExporting(true);setExportError('');setExportStatus('');
  try{
   const params=new URLSearchParams();
   if(findingsOnly)params.set('findingsOnly','true');
   if(findingCode)params.set('findingCode',findingCode);
   if(target){params.set('targetProgramId',target.programId);params.set('targetNationalVersion',String(target.nationalVersion));}
   const encoded=params.toString(),query=encoded?'?'+encoded:'';
   const response=await fetch('/api/v1/headquarters/national-issuance/review-rollup/export.csv'+query,
    {cache:'no-store',credentials:'same-origin',headers:{Accept:'text/csv'}});
   if(response.status===401)throw Error('برای دریافت این فایل، ورود سازمانی لازم است.');
   if(response.status===403)throw Error('دسترسی دریافت فایل پوشش بازرسی در این محیط فعال یا مجاز نیست.');
   if(response.status===413)throw Error('حجم خروجی از سقف ۱۰٬۰۰۰ گروه بیشتر است؛ فیلتر دقیق‌تری انتخاب کنید.');
   if(!response.ok)throw Error('فایل کامل پوشش بازرسی فعلاً در دسترس نیست.');
   const contract=response.headers.get('X-National-Inspection-Export-Contract');
   const rawCount=response.headers.get('X-National-Inspection-Export-Row-Count')||'';
   const expectedDigest=response.headers.get('X-National-Inspection-Export-SHA256')||'';
   const rowCount=Number(rawCount);
   if(contract!=='S98_NATIONAL_INSPECTION_CSV_RECEIPT_V1'||
    !/^(0|[1-9]\d{0,4})$/.test(rawCount)||!Number.isSafeInteger(rowCount)||rowCount>10000||
    !/^[a-f0-9]{64}$/.test(expectedDigest))
    throw Error('رسید یکپارچگی خروجی CSV معتبر نیست؛ فایلی ساخته نشد.');
   const blob=await response.blob();
   if(!blob.type.toLowerCase().startsWith('text/csv')||blob.size<3)
    throw Error('پاسخ خروجی CSV معتبر نیست؛ فایلی ساخته نشد.');
   const bytes=new Uint8Array(await blob.arrayBuffer());
   if(bytes[0]!==0xef||bytes[1]!==0xbb||bytes[2]!==0xbf)
    throw Error('فایل CSV فاقد نشان UTF-8 معتبر است؛ فایلی ساخته نشد.');
   let csvText:string;
   try{csvText=new TextDecoder('utf-8',{fatal:true,ignoreBOM:true}).decode(bytes);}
   catch{throw Error('کدگذاری UTF-8 فایل CSV معتبر نیست؛ فایلی ساخته نشد.');}
   if(countCsvDataRows(csvText)!==rowCount)
    throw Error('تعداد ردیف‌های CSV با رسید سرور یکسان نیست؛ فایلی ساخته نشد.');
   if(!globalThis.crypto?.subtle)
    throw Error('اعتبارسنجی اثرانگشت CSV در این مرورگر در دسترس نیست؛ فایلی ساخته نشد.');
   const digestBytes=new Uint8Array(await globalThis.crypto.subtle.digest('SHA-256',bytes));
   const actualDigest=Array.from(digestBytes,value=>value.toString(16).padStart(2,'0')).join('');
   if(actualDigest!==expectedDigest)
    throw Error('اثر انگشت فایل CSV با رسید سرور یکسان نیست؛ فایلی ساخته نشد.');
   const url=URL.createObjectURL(blob),anchor=document.createElement('a');
   anchor.href=url;anchor.download='national-inspection-rollup.csv';anchor.style.display='none';
   document.body.appendChild(anchor);anchor.click();anchor.remove();window.setTimeout(()=>URL.revokeObjectURL(url),1000);
   setExportStatus('فایل CSV شامل '+n(rowCount)+' ردیف است؛ SHA-256: '+actualDigest);
  }catch(e){
   setExportError(e instanceof Error?e.message:'خروجی کامل ساخته نشد؛ دوباره تلاش کنید.');
  }finally{setExporting(false);}
 }
 async function next(){const current=s.pages[s.index];if(!current?.nextCursor||s.status!=='ready')return;if(s.pages[s.index+1]){setS({...s,index:s.index+1});return;}
  setS({...s,status:'loading'});try{const p=await read(current.nextCursor,new AbortController().signal,findingsOnly,findingCode,target);
   const priorKeys=new Set(s.pages.flatMap(pg=>pg.rollups.map(r=>JSON.stringify([r.programId,r.nationalVersion]))));
   const pageKeys=p.rollups.map(r=>JSON.stringify([r.programId,r.nationalVersion]));
   if(pageKeys.some(key=>priorKeys.has(key))||new Set(pageKeys).size!==pageKeys.length)throw Error('صفحه بعدی پوشش بازرسی تکراری یا نامعتبر است.');
   setS({status:'ready',error:'',pages:[...s.pages,p],index:s.index+1});
  }catch(e){setS({...s,status:'error',error:e instanceof Error?e.message:'صفحه بعدی پوشش بازرسی در دسترس نیست.'});}}
 const page=s.pages[s.index];
 return <section aria-labelledby="inspection-rollup-title" style={{marginTop:'2.5rem',paddingTop:'1.5rem',borderTop:'1px solid #ddd'}}>
  <h2 id="inspection-rollup-title">پوشش فنی بازرسی درخواست‌های ابلاغ ملی</h2>
  <p>این داده‌ها خلاصه فنیِ درخواست‌ها و یادداشت‌های ثبت‌شده‌اند؛ کدهای خطا فقط شمارش تشخیصی‌اند و تأیید انسانی، مجوز یا صدور ابلاغ را نشان نمی‌دهند.</p>
  {!opened&&<button type="button" onClick={()=>setOpened(true)}>نمایش خلاصه پوشش فنی</button>}
  {opened&&<form onSubmit={event=>{event.preventDefault();const programId=targetProgramInput.trim(),nationalVersion=Number(targetVersionInput);if(!programId||programId.length>256||!Number.isSafeInteger(nationalVersion)||nationalVersion<1){setTargetError('شناسه برنامه و نسخه معتبر را وارد کنید.');return;}setTargetError('');setTarget({programId,nationalVersion});}}>
   <label>شناسه برنامه ملی
    <input aria-label="شناسه برنامه ملی" required maxLength={256} disabled={exporting} value={targetProgramInput} onChange={event=>setTargetProgramInput(event.target.value)}/>
   </label>
   <label>نسخه ملی
    <input aria-label="نسخه ملی" required type="number" min={1} step={1} disabled={exporting} value={targetVersionInput} onChange={event=>setTargetVersionInput(event.target.value)}/>
   </label>
   <button type="submit" disabled={exporting}>یافتن نسخه دقیق</button>
   {targetError&&<p role="alert">{targetError}</p>}
   {target&&<button type="button" disabled={exporting} onClick={()=>{setTarget(null);setTargetProgramInput('');setTargetVersionInput('');setTargetError('');}}>نمایش همه نسخه‌ها</button>}
  </form>}
  {opened&&<button type="button" aria-pressed={findingsOnly} disabled={exporting} onClick={()=>{if(findingsOnly)setFindingCode('');setFindingsOnly(value=>!value);}}>فقط گروه‌های دارای کد فنی</button>}
  {opened&&findingsOnly&&<><p>این فیلتر فقط وجود کد تشخیصی فنی را جست‌وجو می‌کند؛ یافته‌ها تصمیم یا مجوز نیستند.</p>
   <label>کد فنی مشخص
    <select aria-label="کد فنی مشخص" disabled={exporting} value={findingCode} onChange={e=>setFindingCode(e.target.value)}>
     <option value="">همه کدهای فنی</option>
     {findingCodes.map(([code,label])=><option key={code} value={code}>{code} — {label}</option>)}
    </select>
   </label></>}
  {s.status==='ready'&&<div><button type="button" disabled={exporting} onClick={()=>void exportAll()}>{exporting?'در حال آماده‌سازی CSV…':'دانلود CSV تجمیعی'}</button>
   {exporting&&<p role="status">فایل در یک خواندن سازگار از پایگاه داده ساخته می‌شود؛ تا تکمیل اعتبارسنجی دانلود آغاز نمی‌شود.</p>}
   {exportError&&<p role="alert">{exportError}</p>}{exportStatus&&<p role="status">{exportStatus}</p>}
  </div>}
  {s.status==='loading'&&<p role="status">در حال دریافت خلاصه پوشش…</p>}
  {s.status==='error'&&<div role="alert"><p>{s.error}</p><button onClick={()=>setReload(x=>x+1)}>تلاش دوباره</button></div>}
  {s.status==='ready'&&page&&(page.rollups.length===0?<p role="status">{findingCode?'گروهی با این کد فنی ثبت نشده.':findingsOnly?'گروهی با کد فنی ثبت‌شده وجود ندارد.':'گروه برنامه/نسخه‌ای برای نمایش وجود ندارد.'}</p>:
   <div style={{overflowX:'auto'}}><table><caption>خلاصه پوشش فنی به تفکیک نسخه برنامه ملی</caption><thead><tr>
    <th>برنامه / نسخه</th><th>درخواست</th><th>بدون بازرسی</th><th>با بازرسی</th><th>یادداشت</th><th>سازگار / ناسازگار</th><th>مقایسه ناموجود</th><th>منطبق / متغیر</th><th>تازگی نامشخص</th><th>خطاهای فنی درخواست، به تفکیک کد</th><th>خطاهای فنی یادداشت، به تفکیک کد</th><th>آخرین مشاهده فنی</th>
   </tr></thead><tbody>{page.rollups.map(r=><tr key={r.programId+':'+r.nationalVersion}>
    <th scope="row">{r.programId} / {n(r.nationalVersion)}</th><td>{n(r.reviewRequestCount)}</td><td>{n(r.requestsWithoutInspectionCount)}</td><td>{n(r.requestsWithInspectionCount)}</td><td>{n(r.inspectionNoteCount)}</td><td>{n(r.integrityConsistentNoteCount)} / {n(r.integrityInconsistentNoteCount)}</td><td>{n(r.sourceComparisonUnavailableNoteCount)}</td><td>{n(r.currentSourceMatchNoteCount)} / {n(r.currentSourceDriftNoteCount)}</td><td>{n(r.sourceFreshnessUnclassifiedNoteCount)}</td><td><span>Digest: {n(r.sourceSnapshotDigestInvalidRequestCount)}؛ Envelope: {n(r.sourceEnvelopeInvalidRequestCount)}؛ انتخاب کانون: {n(r.centerSelectionMismatchRequestCount)}؛ پرچم اختیار: {n(r.authorityFlagsNotClearRequestCount)}؛ Audit: {n(r.auditEvidenceInvalidRequestCount)}؛ Outbox: {n(r.outboxEvidenceInvalidRequestCount)}</span></td><td><span>درخواست اصلی: {n(r.originalRequestIntegrityInvalidNoteCount)}؛ مشاهده: {n(r.observationIntegrityInvalidNoteCount)}؛ Audit: {n(r.inspectionAuditEvidenceInvalidNoteCount)}؛ عدم‌مقایسه‌پذیری: {n(r.sourceObservationNotComparableNoteCount)}</span></td><td>{r.latestTechnicalObservationAt?new Date(r.latestTechnicalObservationAt).toLocaleString('fa-IR'):'ثبت نشده'}</td>
   </tr>)}</tbody></table></div>)}
  {s.status==='ready'&&<nav aria-label="صفحه‌های خلاصه پوشش بازرسی"><button disabled={exporting||s.index===0} onClick={()=>setS({...s,index:s.index-1})}>قبلی</button><span aria-live="polite"> صفحه {n(s.index+1)} </span><button disabled={exporting||!page?.nextCursor} onClick={()=>void next()}>بعدی</button></nav>}
 </section>;
}

import React, { useEffect, useState } from 'react';
type Cursor={beforeProgramId:string;beforeNationalVersion:number};
type Row={rollupContractVersion:'S83_NATIONAL_INSPECTION_ROLLUP_V1';programId:string;nationalVersion:number;reviewRequestCount:number;requestsWithoutInspectionCount:number;requestsWithInspectionCount:number;inspectionNoteCount:number;integrityConsistentNoteCount:number;integrityInconsistentNoteCount:number;sourceComparisonUnavailableNoteCount:number;currentSourceMatchNoteCount:number;currentSourceDriftNoteCount:number;sourceFreshnessUnclassifiedNoteCount:number;latestTechnicalObservationAt:string|null;humanReviewRecorded:false;issuanceAuthorized:false;issuanceExecuted:false};
type Page={rollups:Row[];nextCursor:Cursor|null};
type State={status:'loading'|'ready'|'error';error:string;pages:Page[];index:number};
const initial:State={status:'loading',error:'',pages:[],index:0};
const fields=['nationalVersion','reviewRequestCount','requestsWithoutInspectionCount','requestsWithInspectionCount','inspectionNoteCount','integrityConsistentNoteCount','integrityInconsistentNoteCount','sourceComparisonUnavailableNoteCount','currentSourceMatchNoteCount','currentSourceDriftNoteCount','sourceFreshnessUnclassifiedNoteCount'] as const;
function parsePage(value:unknown):Page|null {
 if(!value||typeof value!=='object')return null;
 const root=value as Record<string,unknown>;
 if(!Array.isArray(root.rollups)||root.rollups.length>20)return null;
 const rows:Row[]=[],seen=new Set<string>();
 for(const raw of root.rollups){
  if(!raw||typeof raw!=='object')return null;
  const r=raw as Record<string,unknown>;
  if(r.rollupContractVersion!=='S83_NATIONAL_INSPECTION_ROLLUP_V1'||typeof r.programId!=='string'||!r.programId.trim()||r.programId.length>256||
   fields.some(k=>typeof r[k]!=='number'||!Number.isSafeInteger(r[k])||(r[k] as number)<0)||(r.nationalVersion as number)<1||
   r.humanReviewRecorded!==false||r.issuanceAuthorized!==false||r.issuanceExecuted!==false||
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
async function read(cursor:Cursor|null,signal:AbortSignal):Promise<Page>{
 const query=cursor?'?'+new URLSearchParams({beforeProgramId:cursor.beforeProgramId,beforeNationalVersion:String(cursor.beforeNationalVersion)}):'';
 const response=await fetch('/api/v1/headquarters/national-issuance/review-rollup'+query,{signal,cache:'no-store',credentials:'same-origin'});
 if(response.status===401)throw Error('برای مشاهده این بخش، ورود سازمانی لازم است.');
 if(response.status===403)throw Error('دسترسی خواندن پوشش بازرسی در این محیط فعال یا مجاز نیست.');
 if(!response.ok)throw Error('خلاصه پوشش بازرسی فعلاً در دسترس نیست.');
 const parsed=parsePage(await response.json());if(!parsed)throw Error('پاسخ پوشش بازرسی با قرارداد فنی سازگار نیست.');return parsed;
}
const n=(v:number)=>new Intl.NumberFormat('fa-IR').format(v);
export function NationalInspectionRollupWorkbench(){
 const [s,setS]=useState<State>(initial),[reload,setReload]=useState(0);
 useEffect(()=>{const c=new AbortController();setS(initial);void read(null,c.signal).then(p=>{if(!c.signal.aborted)setS({status:'ready',error:'',pages:[p],index:0});}).catch(e=>{if(!c.signal.aborted)setS({status:'error',error:e instanceof Error?e.message:'خلاصه پوشش بازرسی فعلاً در دسترس نیست.',pages:[],index:0});});return()=>c.abort();},[reload]);
 async function next(){const current=s.pages[s.index];if(!current?.nextCursor||s.status!=='ready')return;if(s.pages[s.index+1]){setS({...s,index:s.index+1});return;}
  setS({...s,status:'loading'});try{const p=await read(current.nextCursor,new AbortController().signal),first=p.rollups[0];
   if(!first||s.pages.some(pg=>pg.rollups.some(r=>r.programId===first.programId&&r.nationalVersion===first.nationalVersion)))throw Error('صفحه بعدی پوشش بازرسی تکراری یا نامعتبر است.');
   setS({status:'ready',error:'',pages:[...s.pages,p],index:s.index+1});
  }catch(e){setS({...s,status:'error',error:e instanceof Error?e.message:'صفحه بعدی پوشش بازرسی در دسترس نیست.'});}}
 const page=s.pages[s.index];
 return <section aria-labelledby="inspection-rollup-title" style={{marginTop:'2.5rem',paddingTop:'1.5rem',borderTop:'1px solid #ddd'}}>
  <h2 id="inspection-rollup-title">پوشش فنی بازرسی درخواست‌های ابلاغ ملی</h2>
  <p>این داده‌ها خلاصه فنیِ درخواست‌ها و یادداشت‌های ثبت‌شده‌اند؛ تأیید انسانی، مجوز یا صدور ابلاغ را نشان نمی‌دهند.</p>
  {s.status==='loading'&&<p role="status">در حال دریافت خلاصه پوشش…</p>}
  {s.status==='error'&&<div role="alert"><p>{s.error}</p><button onClick={()=>setReload(x=>x+1)}>تلاش دوباره</button></div>}
  {s.status==='ready'&&page&&(page.rollups.length===0?<p role="status">گروه برنامه/نسخه‌ای برای نمایش وجود ندارد.</p>:
   <div style={{overflowX:'auto'}}><table><caption>خلاصه پوشش فنی به تفکیک نسخه برنامه ملی</caption><thead><tr>
    <th>برنامه / نسخه</th><th>درخواست</th><th>بدون بازرسی</th><th>با بازرسی</th><th>یادداشت</th><th>سازگار / ناسازگار</th><th>مقایسه ناموجود</th><th>منطبق / متغیر</th><th>تازگی نامشخص</th><th>آخرین مشاهده فنی</th>
   </tr></thead><tbody>{page.rollups.map(r=><tr key={r.programId+':'+r.nationalVersion}>
    <th scope="row">{r.programId} / {n(r.nationalVersion)}</th><td>{n(r.reviewRequestCount)}</td><td>{n(r.requestsWithoutInspectionCount)}</td><td>{n(r.requestsWithInspectionCount)}</td><td>{n(r.inspectionNoteCount)}</td><td>{n(r.integrityConsistentNoteCount)} / {n(r.integrityInconsistentNoteCount)}</td><td>{n(r.sourceComparisonUnavailableNoteCount)}</td><td>{n(r.currentSourceMatchNoteCount)} / {n(r.currentSourceDriftNoteCount)}</td><td>{n(r.sourceFreshnessUnclassifiedNoteCount)}</td><td>{r.latestTechnicalObservationAt?new Date(r.latestTechnicalObservationAt).toLocaleString('fa-IR'):'ثبت نشده'}</td>
   </tr>)}</tbody></table></div>)}
  {s.status==='ready'&&<nav aria-label="صفحه‌های خلاصه پوشش بازرسی"><button disabled={s.index===0} onClick={()=>setS({...s,index:s.index-1})}>قبلی</button><span aria-live="polite"> صفحه {n(s.index+1)} </span><button disabled={!page?.nextCursor} onClick={()=>void next()}>بعدی</button></nav>}
 </section>;
}

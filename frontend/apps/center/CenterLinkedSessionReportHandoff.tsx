import React,{useEffect,useRef,useState} from 'react';
import { SessionReportLineage } from './SessionReportLineage';

type Scope={centerId:string;token:string;draftId:string;workCardId:string;sessionId:string;
 pinnedRevision:number;programId:string;nationalVersion:number};
type Trace={linkId:string;issuanceId:string;pinnedRevision:number;latestRevision:number;
 programId:string;nationalVersion:number;localTitle:string;plannedActivities:unknown[]};
type State='idle'|'loading'|'ready'|'denied'|'unlinked'|'mismatch'|'error';
type Save='idle'|'saving'|'saved'|'denied'|'conflict'|'error';
const uuid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
function validTrace(v:unknown,p:Scope):v is Trace{
 if(!v||typeof v!=='object')return false;const t=v as Record<string,unknown>;
 return typeof t.linkId==='string'&&uuid.test(t.linkId)&&
 typeof t.issuanceId==='string'&&uuid.test(t.issuanceId)&&
 typeof t.programId==='string'&&t.programId===p.programId&&
 t.nationalVersion===p.nationalVersion&&t.pinnedRevision===p.pinnedRevision&&
 Number.isSafeInteger(t.latestRevision)&&Number(t.latestRevision)>=p.pinnedRevision&&
 typeof t.localTitle==='string'&&t.localTitle.trim().length>0&&
 Array.isArray(t.plannedActivities);
}
/** Handoff to existing original S17 report owner and S16 independently gated dossier.
 * Work-card/S41 linking is private planning, not authenticated evidence of delivery. */
export function CenterLinkedSessionReportHandoff(props:Scope){
 const {centerId,token,draftId,sessionId,workCardId,pinnedRevision}=props;
 const epoch=useRef(0),reportId=useRef(crypto.randomUUID());
 const [state,setState]=useState<State>('idle'),[trace,setTrace]=useState<Trace|null>(null);
 const [summary,setSummary]=useState(''),[evidence,setEvidence]=useState('');
 const [save,setSave]=useState<Save>('idle'),[dossier,setDossier]=useState(false);
 useEffect(()=>()=>{epoch.current++;},[]);
 function editSummary(s:string){setSummary(s);setSave('idle');reportId.current=crypto.randomUUID();}
 function editEvidence(s:string){setEvidence(s);setSave('idle');reportId.current=crypto.randomUUID();}
 async function load(){
  if(!token.trim()||state==='loading')return;
  const run=++epoch.current;setTrace(null);setState('loading');
  setSave('idle');setDossier(false);
  try{
   const r=await fetch('/api/v1/centers/'+encodeURIComponent(centerId)+
    '/local-plan-drafts/'+encodeURIComponent(draftId)+'/execution-trace',
    {headers:{Authorization:'Bearer '+token.trim()},cache:'no-store'});
   if(run!==epoch.current)return;
   if(r.status===401||r.status===403){setState('denied');return;}
   if(!r.ok)throw Error('Trace denied');
   const raw:unknown=await r.json();
   if(run!==epoch.current||!raw||typeof raw!=='object')throw Error('Invalid trace');
   const value=raw as Record<string,unknown>;
   if(value.draftId!==draftId)throw Error('Foreign trace');
   if(value.trace===null){setState('unlinked');return;}
   if(!validTrace(value.trace,props)){setState('mismatch');return;}
   setTrace(value.trace);setState('ready');
  }catch{if(run===epoch.current){setTrace(null);setState('error');}}
 }
 async function submit(){
  if(state!=='ready'||trace===null||save==='saving'||save==='saved'||
   !token.trim()||!uuid.test(sessionId)||!uuid.test(draftId)||
   !summary.trim()||!evidence.trim()||summary.length>2048||evidence.length>512)return;
  const run=++epoch.current;setSave('saving');
  const sentId=reportId.current,linkId=trace.linkId;
  try{
   const r=await fetch('/api/v1/centers/'+encodeURIComponent(centerId)+'/activity-reports/sourced',{
    method:'POST',headers:{Authorization:'Bearer '+token.trim(),'Content-Type':'application/json'},
    body:JSON.stringify({reportId:sentId,draftId,linkId,sourceKind:'CLASS_SESSION',
     sourceId:sessionId,reportedSummary:summary.trim(),evidenceRef:evidence.trim(),
     claimedOccurredAt:null})
   });
   if(run!==epoch.current)return;
   if(r.status===401||r.status===403){setSave('denied');return;}
   if(r.status===409){setSave('conflict');return;}
   if(!r.ok)throw Error('Write blocked');
   const raw:unknown=await r.json();
   if(run!==epoch.current||!raw||typeof raw!=='object')throw Error('Invalid acknowledgment');
   const a=raw as Record<string,unknown>;
   if(a.reportId!==sentId||a.draftId!==draftId||a.linkId!==linkId||
    a.sourceKind!=='CLASS_SESSION'||a.sourceId!==sessionId||
    a.verificationStatus!=='UNVERIFIED')throw Error('False verified report');
   setSave('saved');
  }catch{if(run===epoch.current)setSave('error');}
 }
 return <section aria-label={'گزارش خصوصی جلسه پیوندخورده '+sessionId}
  style={{border:'1px solid #aaa',padding:8,marginTop:8}}>
  <h6>گزارش فعالیت جلسه و پیوند نسخه ابلاغ</h6>
  <p>کارت کار {workCardId} — طرح نسخه {pinnedRevision}.
   این پیوند اثبات اجرای جلسه یا صحت شواهد نیست.</p>
  <button type="button" onClick={()=>void load()} disabled={state==='loading'}>
   بررسی پیوند ابلاغ معتبر همین نسخه طرح</button>{' '}
  <button type="button" onClick={()=>setDossier(x=>!x)}>
   {dossier?'بستن پرونده گزارش جلسه':'مشاهده پرونده اصلی گزارش جلسه'}</button>
  {dossier&&<div>
   <p>پرونده اصلی ممکن است گزارش‌های سایر طرح‌ها را نیز نشان دهد؛
    هر گزارش با نسخه بومی و ابلاغ اصلی خود نمایش داده می‌شود.</p>
   <SessionReportLineage key={centerId+':'+token+':'+sessionId}
    centerId={centerId} token={token} sessionId={sessionId}/>
  </div>}
  {state==='denied'&&<p role="alert">مجوز مستقل مشاهده پیوند ابلاغ وجود ندارد.</p>}
  {state==='unlinked'&&<p role="alert">برای این طرح پیوند ابلاغ تثبیت‌شده موجود نیست؛ گزارش از این مسیر ثبت نمی‌شود.</p>}
  {state==='mismatch'&&<p role="alert">نسخه یا برنامه ملی پیوند ابلاغ با نسخه اصلی کارت کار تطبیق ندارد؛ ثبت مسدود است.</p>}
  {state==='error'&&<p role="alert">پیوند معتبر طرح از مالک اصلی دریافت نشد.</p>}
  {state==='ready'&&trace&&<div>
   <p>ابلاغ {trace.issuanceId} — پیوند اصلی {trace.linkId} — نسخه بومی {trace.pinnedRevision}.</p>
   <p>ثبت فقط یک ادعای انسانی و راستی‌آزمایی‌نشده بر مبنای جلسه پیوندخورده است.
    اجازه ثبت گزارش مستقل از اجازه مشاهده کارت کار است.</p>
   <label>شرح گزارش ادعایی
    <input aria-label={'شرح گزارش جلسه '+sessionId} maxLength={2048}
     disabled={save==='saved'||save==='saving'} value={summary}
     onChange={e=>editSummary(e.target.value)}/></label>
   <label>ارجاع شواهد تأییدنشده
    <input aria-label={'ارجاع شواهد جلسه '+sessionId} maxLength={512}
     disabled={save==='saved'||save==='saving'} value={evidence}
     onChange={e=>editEvidence(e.target.value)}/></label>
   <button type="button" disabled={!summary.trim()||!evidence.trim()||
    save==='saving'||save==='saved'} onClick={()=>void submit()}>
    ثبت گزارش جلسه به صورت راستی‌آزمایی‌نشده</button>
  </div>}
  {save==='saved'&&<p role="status">گزارش اصلی ثبت شد؛ همچنان UNVERIFIED و بدون تأیید اجرا یا مدرک است.</p>}
  {save==='denied'&&<p role="alert">مجوز مستقل ثبت گزارش ادعایی وجود ندارد.</p>}
  {save==='conflict'&&<p role="alert">گزارش تکراری یا پیوند منبع/نسخه نامعتبر است؛ سوابق اصلی بررسی شود.</p>}
  {save==='error'&&<p role="alert">تأیید معتبر ذخیره گزارش دریافت نشد؛ درخواست مشابه را بدون بررسی تکرار نکنید.</p>}
 </section>;
}

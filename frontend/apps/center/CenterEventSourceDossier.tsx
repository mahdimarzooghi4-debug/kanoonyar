import React,{useEffect,useRef,useState} from 'react';

type Report={reportSequence:number;reportId:string;linkId:string;issuanceId:string;
 programId:string;nationalVersion:number;pinnedRevision:number;
 reportedSummary:string;evidenceRef:string;claimedOccurredAt:string|null;
 recordedAt:string;sourceKind:'CENTER_EVENT';sourceRecordRef:string;
 verificationStatus:'UNVERIFIED';isApproved:false;isExecuted:false};
type Props={centerId:string;token:string;eventId:string;draftId:string;
 pinnedPlanRevision:number;revision:number;title:string;scheduledAt:string};
type State='idle'|'loading'|'ready'|'denied'|'error';
const uuid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
function time(x:unknown):x is string{return typeof x==='string'&&!Number.isNaN(Date.parse(x));}
function validReport(x:unknown,p:Props):x is Report{
 if(!x||typeof x!=='object')return false;
 const r=x as Record<string,unknown>;
 return Number.isSafeInteger(r.reportSequence)&&Number(r.reportSequence)>0&&
  typeof r.reportId==='string'&&uuid.test(r.reportId)&&
  typeof r.linkId==='string'&&uuid.test(r.linkId)&&
  typeof r.issuanceId==='string'&&uuid.test(r.issuanceId)&&
  typeof r.programId==='string'&&r.programId.length>0&&
  Number.isSafeInteger(r.nationalVersion)&&Number(r.nationalVersion)>0&&
  r.pinnedRevision===p.pinnedPlanRevision&&
  typeof r.reportedSummary==='string'&&typeof r.evidenceRef==='string'&&
  (r.claimedOccurredAt===null||time(r.claimedOccurredAt))&&time(r.recordedAt)&&
  r.sourceKind==='CENTER_EVENT'&&r.sourceRecordRef===p.eventId&&
  r.verificationStatus==='UNVERIFIED'&&r.isApproved===false&&r.isExecuted===false;
}
export function CenterEventSourceDossier(p:Props){
 const epoch=useRef(0),[expanded,setExpanded]=useState(false);
 const [status,setStatus]=useState<State>('idle'),[rows,setRows]=useState<Report[]>([]);
 const [cursor,setCursor]=useState<number|null>(null);
 useEffect(()=>{
  epoch.current++;setExpanded(false);setStatus('idle');setRows([]);setCursor(null);
  return()=>{epoch.current++};
 },[p.centerId,p.token,p.eventId,p.draftId,p.pinnedPlanRevision,p.revision]);
 async function load(before:number|null){
  if(status==='loading'||!expanded||!p.token.trim()||!p.centerId)return;
  const current=++epoch.current;setStatus('loading');
  if(before===null){setRows([]);setCursor(null);}
  try{
   const url='/api/v1/centers/'+encodeURIComponent(p.centerId)+'/events/plans/'+
    encodeURIComponent(p.eventId)+'/source-dossier'+
    (before===null?'':'?beforeSequence='+before);
   const response=await fetch(url,{headers:{Authorization:'Bearer '+p.token.trim()},
    cache:'no-store'});
   if(current!==epoch.current)return;
   if(response.status===401||response.status===403){setRows([]);setCursor(null);setStatus('denied');return;}
   if(!response.ok)throw Error('Source unavailable');
   const raw:unknown=await response.json();
   if(current!==epoch.current||!raw||typeof raw!=='object')throw Error('Invalid');
   const data=raw as Record<string,unknown>;
   if(data.eventId!==p.eventId||data.draftId!==p.draftId||
    data.pinnedPlanRevision!==p.pinnedPlanRevision||data.revision!==p.revision||
    data.title!==p.title||!time(data.scheduledAt)||
    Date.parse(data.scheduledAt)!==Date.parse(p.scheduledAt)||
    data.visibility!=='PRIVATE_DRAFT'||data.isPublished!==false||
    data.isExecuted!==false||data.isRegistrationOpen!==false||
    data.verificationStatus!=='UNVERIFIED'||data.isApproved!==false||data.isOutcome!==false||
    !Array.isArray(data.activityReports)||data.activityReports.length>20||
    !data.activityReports.every(x=>validReport(x,p))||
    !(data.nextBeforeSequence===null||(Number.isSafeInteger(data.nextBeforeSequence)&&
      Number(data.nextBeforeSequence)>0)))throw Error('Forged event dossier');
   const reports=data.activityReports as Report[];
   if(reports.some((r,i)=>(before!==null&&r.reportSequence>=before)||
    (i>0&&reports[i-1].reportSequence<=r.reportSequence))||
    data.nextBeforeSequence!==null&&(reports.length!==20||
     reports[19].reportSequence!==data.nextBeforeSequence))throw Error('Bad source cursor');
   setRows(old=>before?[...old,...reports]:reports);
   setCursor(data.nextBeforeSequence as number|null);setStatus('ready');
  }catch{if(current===epoch.current){setRows([]);setCursor(null);setStatus('error');}}
 }
 return <section aria-label="پرونده منبع و گزارش رویداد" style={{marginTop:10}}>
  <button type="button" onClick={()=>{epoch.current++;setExpanded(v=>!v);
   setRows([]);setCursor(null);setStatus('idle');}}>
   {expanded?'بستن پرونده گزارش رویداد':'بازکردن پرونده گزارش‌های منبع‌دار رویداد'}</button>
  {expanded&&<>
   <p>برنامه ثبت‌شده، نه اجرای قطعی. گزارش‌ها ادعاهای منبع‌دار و راستی‌آزمایی‌نشده‌اند.</p>
   <p>نسخه برنامه {p.revision} — {p.title} — زمان برنامه‌ریزی: {p.scheduledAt}</p>
   <button type="button" disabled={status==='loading'} onClick={()=>void load(null)}>
    خواندن گزارش‌های واقعی این رویداد</button>
   {status==='denied'&&<p role="alert">مجوز مستقل مشاهده پرونده و گزارش اصلی وجود ندارد.</p>}
   {status==='error'&&<p role="alert">پرونده منبع معتبر یا قابل مشاهده دریافت نشد.</p>}
   {status==='ready'&&rows.length===0&&<p>گزارش منطبق یافت نشد؛ این به معنای اجرای صفر نیست.</p>}
   {status==='ready'&&<ul>{rows.map(r=><li key={r.reportId}>
    <strong>{r.reportedSummary} — UNVERIFIED</strong>
    <p>گزارش اصلی: {r.reportId} / سند منبع: {r.evidenceRef}</p>
    <p>نسخه طرح محلی: {r.pinnedRevision} / نسخه برنامه ملی: {r.nationalVersion}</p>
    <p>زمان ادعاشده: {r.claimedOccurredAt??'نامعلوم'}؛ زمان ثبت: {r.recordedAt}</p>
    <p>بدون تأیید گزارش، برگزاری یا مشارکت</p>
   </li>)}</ul>}
   {cursor!==null&&<button type="button" disabled={status==='loading'}
    onClick={()=>void load(cursor)}>گزارش‌های قدیمی‌تر این رویداد</button>}
  </>}
 </section>;
}

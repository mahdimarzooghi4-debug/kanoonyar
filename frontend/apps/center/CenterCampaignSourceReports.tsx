import React,{useEffect,useRef,useState} from 'react';
import { CenterCampaignInspectionNotes } from './CenterCampaignInspectionNotes';

type Activity={activityKey:string;title:string};
type Report={reportId:string;reportSequence:number;linkId:string;sourceKind:'CLASS_SESSION'|'CENTER_EVENT';
 sourceRecordRef:string;reportedSummary:string;verificationStatus:'UNVERIFIED'};
type Association={associationId:string;campaignRevision:number;activityKey:string;reportId:string;
 sourceKind:'CLASS_SESSION'|'CENTER_EVENT';sourceRecordRef:string;reportedSummary:string;
 evidenceRef:string;linkId:string;verificationStatus:'UNVERIFIED';isExecuted:false};
type Mode='idle'|'loading'|'ready'|'denied'|'error';
type Save='idle'|'saving'|'saved'|'denied'|'conflict'|'error';
const uuid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const kind=(x:unknown)=>x==='CLASS_SESSION'||x==='CENTER_EVENT';
function validReport(raw:unknown):raw is Report {
 if(!raw||typeof raw!=='object')return false;
 const x=raw as Record<string,unknown>;
 return typeof x.reportId==='string'&&uuid.test(x.reportId)&&
   Number.isSafeInteger(x.reportSequence)&&Number(x.reportSequence)>0&&
   typeof x.linkId==='string'&&uuid.test(x.linkId)&&kind(x.sourceKind)&&
   typeof x.sourceRecordRef==='string'&&uuid.test(x.sourceRecordRef)&&
   typeof x.reportedSummary==='string'&&x.verificationStatus==='UNVERIFIED';
}
function validAssociation(raw:unknown):raw is Association {
 if(!raw||typeof raw!=='object')return false;
 const x=raw as Record<string,unknown>;
 return typeof x.associationId==='string'&&uuid.test(x.associationId)&&
  typeof x.reportId==='string'&&uuid.test(x.reportId)&&
  typeof x.linkId==='string'&&uuid.test(x.linkId)&&
  Number.isSafeInteger(x.campaignRevision)&&Number(x.campaignRevision)>0&&
  typeof x.activityKey==='string'&&/^[a-z][a-z0-9_]{0,31}$/.test(x.activityKey)&&
  kind(x.sourceKind)&&typeof x.sourceRecordRef==='string'&&uuid.test(x.sourceRecordRef)&&
  typeof x.reportedSummary==='string'&&typeof x.evidenceRef==='string'&&
  x.verificationStatus==='UNVERIFIED'&&x.isExecuted===false;
}

export function CenterCampaignSourceReports({centerId,token,campaignId,draftId,
 campaignRevision,activities}:{centerId:string;token:string;campaignId:string;draftId:string;
 campaignRevision:number;activities:Activity[]}){
 const epoch=useRef(0);
 const [candidates,setCandidates]=useState<Report[]>([]);
 const [candidateStatus,setCandidateStatus]=useState<Mode>('idle');
 const [candidateCursor,setCandidateCursor]=useState<number|null>(null);
 const [chosenReport,setChosenReport]=useState('');
 const [chosenActivity,setChosenActivity]=useState(activities[0]?.activityKey??'');
 const [associations,setAssociations]=useState<Association[]>([]);
 const [associationStatus,setAssociationStatus]=useState<Mode>('idle');
 const [associationCursor,setAssociationCursor]=useState<string|null>(null);
 const [save,setSave]=useState<Save>('idle');
 const prefix='/api/v1/centers/'+encodeURIComponent(centerId);
 const reportsUrl=prefix+'/local-plan-drafts/'+encodeURIComponent(draftId)+'/activity-reports';
 const linksUrl=prefix+'/campaigns/'+encodeURIComponent(campaignId)+'/report-links';
 const headers={Authorization:'Bearer '+token.trim()};
 useEffect(()=>{
  epoch.current++;setCandidates([]);setCandidateStatus('idle');setCandidateCursor(null);
  setChosenReport('');setChosenActivity(activities[0]?.activityKey??'');
  setAssociations([]);setAssociationStatus('idle');setAssociationCursor(null);setSave('idle');
  return()=>{epoch.current++};
 },[centerId,token,campaignId,draftId,campaignRevision]);
 async function loadReports(before:number|null){
  if(!centerId||!token.trim()||candidateStatus==='loading')return;
  const id=++epoch.current;setCandidateStatus('loading');setSave('idle');
  if(before===null){setCandidates([]);setCandidateCursor(null);setChosenReport('');}
  try{
   const response=await fetch(reportsUrl+(before===null?'':'?beforeSequence='+before),
    {headers,cache:'no-store'});
   if(id!==epoch.current)return;
   if(response.status===401||response.status===403){setCandidates([]);setCandidateStatus('denied');return;}
   if(!response.ok)throw Error('Reports unavailable');
   const raw:unknown=await response.json();
   if(id!==epoch.current||!raw||typeof raw!=='object')throw Error('Invalid');
   const x=raw as Record<string,unknown>;
   if(x.draftId!==draftId||!Array.isArray(x.reports)||x.reports.length>20||
     !x.reports.every(validReport)||!(x.nextBeforeSequence===null||
      (Number.isSafeInteger(x.nextBeforeSequence)&&Number(x.nextBeforeSequence)>0)))
    throw Error('Invalid source report collection');
   const rows=x.reports as Report[];
   if(rows.some((v,i)=>(before!==null&&v.reportSequence>=before)||
      (i>0&&rows[i-1].reportSequence<=v.reportSequence)))throw Error('Invalid report order');
   if(x.nextBeforeSequence!==null&&(rows.length!==20||
      rows[19].reportSequence!==x.nextBeforeSequence))throw Error('Invalid report cursor');
   setCandidates(old=>before===null?rows:[...old,...rows]);
   setCandidateCursor(x.nextBeforeSequence as number|null);
   setCandidateStatus('ready');
  }catch{if(id===epoch.current){setCandidates([]);setCandidateCursor(null);setCandidateStatus('error');}}
 }
 async function loadLinks(before:string|null){
  if(!centerId||!token.trim()||associationStatus==='loading')return;
  const id=++epoch.current;setAssociationStatus('loading');
  if(before===null){setAssociations([]);setAssociationCursor(null);}
  try{
   const response=await fetch(linksUrl+(before===null?'':'?beforeAssociationId='+encodeURIComponent(before)),
    {headers,cache:'no-store'});
   if(id!==epoch.current)return;
   if(response.status===401||response.status===403){setAssociations([]);setAssociationStatus('denied');return;}
   if(!response.ok)throw Error('Unavailable');
   const raw:unknown=await response.json();
   if(id!==epoch.current||!raw||typeof raw!=='object')throw Error('Invalid');
   const x=raw as Record<string,unknown>;
   if(x.campaignId!==campaignId||x.verificationStatus!=='UNVERIFIED'||
     x.isExecuted!==false||!Array.isArray(x.associations)||x.associations.length>20||
     !x.associations.every(validAssociation)||
     !(x.nextBeforeAssociationId===null||
       (typeof x.nextBeforeAssociationId==='string'&&uuid.test(x.nextBeforeAssociationId))))
    throw Error('Untrusted association response');
   const rows=x.associations as Association[];
   if(rows.some((v,i)=>(before!==null&&v.associationId.toLowerCase()>=before.toLowerCase())||
      (i>0&&rows[i-1].associationId.toLowerCase()<=v.associationId.toLowerCase())))
    throw Error('Invalid association order');
   if(x.nextBeforeAssociationId!==null&&(rows.length!==20||
    rows[19].associationId.toLowerCase()!==String(x.nextBeforeAssociationId).toLowerCase()))
    throw Error('Invalid association cursor');
   setAssociations(old=>before===null?rows:[...old,...rows]);
   setAssociationCursor(x.nextBeforeAssociationId as string|null);
   setAssociationStatus('ready');
  }catch{if(id===epoch.current){setAssociations([]);setAssociationCursor(null);setAssociationStatus('error');}}
 }
 async function recordLink(){
  if(!chosenReport||!candidates.some(r=>r.reportId===chosenReport)||
   !activities.some(a=>a.activityKey===chosenActivity)||save==='saving'||save==='saved')return;
  const id=++epoch.current,associationId=crypto.randomUUID();
  setSave('saving');
  try{
   const response=await fetch(linksUrl,{
    method:'POST',headers:{...headers,'Content-Type':'application/json'},
    body:JSON.stringify({associationId,campaignRevision,activityKey:chosenActivity,reportId:chosenReport})
   });
   if(id!==epoch.current)return;
   if(response.status===401||response.status===403){setSave('denied');return;}
   if(response.status===409){setSave('conflict');return;}
   if(!response.ok)throw Error('Unavailable');
   const raw:unknown=await response.json();
   if(id!==epoch.current||!raw||typeof raw!=='object')throw Error('Invalid');
   const x=raw as Record<string,unknown>;
   if(x.associationId!==associationId||x.campaignId!==campaignId||
     x.campaignRevision!==campaignRevision||x.activityKey!==chosenActivity||
     x.reportId!==chosenReport||x.verificationStatus!=='UNVERIFIED'||
     x.isExecuted!==false||x.sourceReused!==true)throw Error('Forged report status');
   setSave('saved');setAssociationStatus('idle');setAssociations([]);setAssociationCursor(null);
  }catch{if(id===epoch.current)setSave('error');}
 }
 return <section aria-label="گزارش‌های منبع‌دار پویش" style={{marginTop:'1rem'}}>
  <h3>پیوند گزارش منبع‌دار به فعالیت پیشنهادی</h3>
  <p>گزارش‌ها از ماژول مالک خوانده می‌شوند؛ وجودشان به معنی اجرای تأییدشده یا مشارکت نیست.</p>
  <button type="button" disabled={!centerId||candidateStatus==='loading'} onClick={()=>void loadReports(null)}>
   دریافت گزارش‌های مجاز طرح محلی</button>{' '}
  <button type="button" disabled={!centerId||associationStatus==='loading'} onClick={()=>void loadLinks(null)}>
   مشاهده پیوندهای گزارش پویش</button>
  {candidateStatus==='denied'&&<p role="alert">مجوز مستقل مشاهده گزارش‌های منبع وجود ندارد.</p>}
  {candidateStatus==='error'&&<p role="alert">گزارش منبع معتبر دریافت نشد.</p>}
  {candidateStatus==='ready'&&candidates.length===0&&<p>گزارشی برای انتخاب وجود ندارد؛ هیچ اجرای فرضی ساخته نمی‌شود.</p>}
  {candidateStatus==='ready'&&candidates.length>0&&<label>گزارش موجود
   <select aria-label="انتخاب گزارش منبع" value={chosenReport}
    onChange={e=>{setChosenReport(e.target.value);setSave('idle');}}>
    <option value="">انتخاب کنید</option>
    {candidates.map(r=><option key={r.reportId} value={r.reportId}>
      {r.sourceKind} — {r.reportedSummary} (UNVERIFIED)</option>)}
   </select>
  </label>}
  {candidateCursor!==null&&<button type="button" onClick={()=>void loadReports(candidateCursor)}>
    گزارش‌های قدیمی‌تر</button>}
  <label>فعالیت برنامه‌ریزی‌شده
   <select aria-label="انتخاب فعالیت پویش" value={chosenActivity}
    onChange={e=>{setChosenActivity(e.target.value);setSave('idle');}}>
    {activities.map(a=><option key={a.activityKey} value={a.activityKey}>{a.title||a.activityKey}</option>)}
   </select>
  </label>
  <button type="button" disabled={!chosenReport||!chosenActivity||save==='saving'||save==='saved'}
   onClick={()=>void recordLink()}>اتصال گزارش موجود به پویش</button>
  {save==='saved'&&<p role="status">پیوند منبع ثبت شد؛ گزارش همچنان تأییدنشده است.</p>}
  {save==='denied'&&<p role="alert">مجوز مستقل اتصال گزارش و دسترسی به منبع وجود ندارد.</p>}
  {save==='conflict'&&<p role="alert">گزارش، فعالیت یا نسخه پویش سازگار نیست یا پیوند تکراری است.</p>}
  {save==='error'&&<p role="alert">پاسخ معتبر اتصال گزارش دریافت نشد.</p>}
  {associationStatus==='denied'&&<p role="alert">مجوز مشاهده پیوندهای گزارش وجود ندارد.</p>}
  {associationStatus==='error'&&<p role="alert">پیوندهای گزارش معتبر دریافت نشد.</p>}
  {associationStatus==='ready'&&associations.length===0&&<p>پیوند گزارشی ثبت نشده است.</p>}
  {associationStatus==='ready'&&<ul>{associations.map(a=><li key={a.associationId}>
    {a.activityKey} — {a.sourceKind} — {a.reportedSummary}
    {' '}<strong>UNVERIFIED — اجرای قطعی نیست</strong>
    {' '}(نسخه {a.campaignRevision})
    <CenterCampaignInspectionNotes key={a.associationId}
     centerId={centerId} token={token} campaignId={campaignId} associationId={a.associationId}/>
   </li>)}</ul>}
  {associationCursor!==null&&<button type="button" onClick={()=>void loadLinks(associationCursor)}>
   پیوندهای قدیمی‌تر</button>}
 </section>;
}

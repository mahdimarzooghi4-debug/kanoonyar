import React,{useEffect,useRef,useState} from 'react';
import {CenterCampaignReviewWorkspace} from './CenterCampaignReviewWorkspace';

type SourceKind='CLASS_SESSION'|'CENTER_EVENT';
type EntryKind='NOTE'|'REQUEST'|'RESPONSE';
type RecordRow={associationId:string;reportId:string;campaignRevision:number;
 activityKey:string;draftId:string;pinnedPlanRevision:number;sourceKind:SourceKind;
 sourceRecordRef:string;reportedSummary:string;evidenceRef:string;linkId:string;
 reportRecordedAt:string;linkedAt:string;latestEntryKind:EntryKind|null;
 latestEntryId:string|null;latestEntryAt:string|null;verificationStatus:'UNVERIFIED';
 isApproval:false;isExecuted:false;isResolved:false};
type Mode='idle'|'loading'|'ready'|'denied'|'error';
const uuid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
function dateValid(x:unknown){return typeof x==='string'&&!Number.isNaN(Date.parse(x));}
function rowValid(v:unknown,draftId:string,pinned:number):v is RecordRow{
 if(!v||typeof v!=='object')return false;
 const x=v as Record<string,unknown>;
 return typeof x.associationId==='string'&&uuid.test(x.associationId)&&
 typeof x.reportId==='string'&&uuid.test(x.reportId)&&
 typeof x.linkId==='string'&&uuid.test(x.linkId)&&
 x.draftId===draftId&&x.pinnedPlanRevision===pinned&&
 Number.isSafeInteger(x.campaignRevision)&&Number(x.campaignRevision)>0&&
 typeof x.activityKey==='string'&&/^[a-z][a-z0-9_]{0,31}$/.test(x.activityKey)&&
 (x.sourceKind==='CLASS_SESSION'||x.sourceKind==='CENTER_EVENT')&&
 typeof x.sourceRecordRef==='string'&&uuid.test(x.sourceRecordRef)&&
 typeof x.reportedSummary==='string'&&typeof x.evidenceRef==='string'&&
 dateValid(x.reportRecordedAt)&&dateValid(x.linkedAt)&&
 (x.latestEntryKind===null&&x.latestEntryId===null&&x.latestEntryAt===null||
  (x.latestEntryKind==='NOTE'||x.latestEntryKind==='REQUEST'||x.latestEntryKind==='RESPONSE')&&
  typeof x.latestEntryId==='string'&&uuid.test(x.latestEntryId)&&dateValid(x.latestEntryAt))&&
 x.verificationStatus==='UNVERIFIED'&&x.isApproval===false&&
 x.isExecuted===false&&x.isResolved===false;
}

export function CenterCampaignEvidenceRegister({centerId,token,campaignId,draftId,
 pinnedPlanRevision,activities}:{centerId:string;token:string;campaignId:string;
 draftId:string;pinnedPlanRevision:number;activities:{activityKey:string;title:string}[]}){
 const epoch=useRef(0);
 const [expanded,setExpanded]=useState(false),[status,setStatus]=useState<Mode>('idle');
 const [rows,setRows]=useState<RecordRow[]>([]),[cursor,setCursor]=useState<string|null>(null);
 const [kind,setKind]=useState<''|SourceKind>(''),[activity,setActivity]=useState('');
 useEffect(()=>{
  epoch.current++;setExpanded(false);setStatus('idle');setRows([]);setCursor(null);
  setKind('');setActivity('');
  return()=>{epoch.current++};
 },[centerId,token,campaignId,draftId,pinnedPlanRevision]);
 async function load(before:string|null){
  if(status==='loading'||!token.trim()||!centerId||!expanded)return;
  const id=++epoch.current;setStatus('loading');
  if(before===null){setRows([]);setCursor(null);}
  const q=new URLSearchParams();
  if(before)q.set('beforeAssociationId',before);
  if(kind)q.set('sourceKind',kind);
  if(activity)q.set('activityKey',activity);
  try{
   const response=await fetch('/api/v1/centers/'+encodeURIComponent(centerId)+
    '/campaigns/'+encodeURIComponent(campaignId)+'/evidence-register'+
    (q.size?'?'+q.toString():''),
    {headers:{Authorization:'Bearer '+token.trim()},cache:'no-store'});
   if(id!==epoch.current)return;
   if(response.status===401||response.status===403){
    setRows([]);setCursor(null);setStatus('denied');return;
   }
   if(!response.ok)throw Error('Unknown source');
   const raw:unknown=await response.json();
   if(id!==epoch.current||!raw||typeof raw!=='object')throw Error('Invalid');
   const x=raw as Record<string,unknown>;
   if(x.campaignId!==campaignId||x.sourceKind!==(kind||null)||
    x.activityKey!==(activity||null)||x.verificationStatus!=='UNVERIFIED'||
    x.isApproval!==false||x.isExecuted!==false||x.isResolved!==false||
    !Array.isArray(x.records)||x.records.length>20||
    !x.records.every(v=>rowValid(v,draftId,pinnedPlanRevision))||
    !(x.nextBeforeAssociationId===null||
      typeof x.nextBeforeAssociationId==='string'&&uuid.test(x.nextBeforeAssociationId)))
    throw Error('Forged register');
   const items=x.records as RecordRow[];
   if(items.some((v,i)=>(kind&&v.sourceKind!==kind)||(activity&&v.activityKey!==activity)||
    (before!==null&&v.associationId.toLowerCase()>=before.toLowerCase())||
    (i>0&&items[i-1].associationId.toLowerCase()<=v.associationId.toLowerCase())))
    throw Error('Incompatible lineage');
   if(x.nextBeforeAssociationId!==null&&(items.length!==20||
      items[19].associationId.toLowerCase()!==String(x.nextBeforeAssociationId).toLowerCase()||
      x.isPageEnd!==false)||x.nextBeforeAssociationId===null&&x.isPageEnd!==true)
    throw Error('Invalid pagination');
   setRows(old=>before?[...old,...items]:items);
   setCursor(x.nextBeforeAssociationId as string|null);setStatus('ready');
  }catch{if(id===epoch.current){setRows([]);setCursor(null);setStatus('error');}}
 }
 function adjustKind(v:''|SourceKind){epoch.current++;setKind(v);setStatus('idle');setRows([]);setCursor(null);}
 function adjustActivity(v:string){epoch.current++;setActivity(v);setStatus('idle');setRows([]);setCursor(null);}
 return <section aria-label="دفتر گزارش‌های منبع‌دار پویش" style={{marginTop:12}}>
  <button type="button" onClick={()=>{epoch.current++;setExpanded(v=>!v);setRows([]);
    setCursor(null);setStatus('idle');}}>
    {expanded?'بستن دفتر گزارش‌ها':'بازکردن دفتر گزارش‌های منبع‌دار'}</button>
  {expanded&&<>
   <p>فهرست ارجاعات منبع و آخرین مکاتبه انسانی، بدون امتیاز، تأیید، تعداد مشارکت یا نتیجه اجرای پویش.</p>
   <label>نوع منبع
    <select aria-label="فیلتر نوع گزارش" value={kind}
     onChange={e=>adjustKind(e.target.value as ''|SourceKind)}>
     <option value="">همه انواع منبع</option>
     <option value="CLASS_SESSION">جلسه کلاس</option>
     <option value="CENTER_EVENT">رویداد کانون</option>
    </select>
   </label>
   <label>فعالیت برنامه‌ریزی‌شده
    <select aria-label="فیلتر فعالیت پویش" value={activity}
     onChange={e=>adjustActivity(e.target.value)}>
     <option value="">تمام فعالیت‌ها</option>
     {activities.map(a=><option value={a.activityKey} key={a.activityKey}>
       {a.title||a.activityKey}</option>)}
    </select>
   </label>
   <button type="button" disabled={status==='loading'}
    onClick={()=>void load(null)}>دریافت دفتر از منابع اصلی</button>
   {status==='denied'&&<p role="alert">مجوز مستقل مشاهده گزارش‌ها و مکاتبات تأیید نشد.</p>}
   {status==='error'&&<p role="alert">دفتر گزارش مجاز و معتبر دریافت نشد.</p>}
   {status==='ready'&&rows.length===0&&<p>رکوردی مطابق فیلتر موجود نیست؛ نبود گزارش، اجرای صفر نیست.</p>}
   {status==='ready'&&<ul>{rows.map(r=><li key={r.associationId}>
    <strong>{r.sourceKind} — {r.activityKey}</strong>
    {' — '}{r.reportedSummary} (نسخه پویش {r.campaignRevision})
    <p>آخرین مکاتبه: {r.latestEntryKind?
      r.latestEntryKind+' — '+r.latestEntryAt:
      'مکاتبه‌ای ثبت نشده؛ نتیجه بازبینی نامعلوم است'}</p>
    <p><strong>UNVERIFIED — اجرا و تأیید نشده محسوب نمی‌شود.</strong></p>
    <CenterCampaignReviewWorkspace centerId={centerId} token={token}
     campaignId={campaignId} associationId={r.associationId}
     reportId={r.reportId} activityKey={r.activityKey}
     campaignRevision={r.campaignRevision} linkId={r.linkId}/>
   </li>)}</ul>}
   {cursor&&<button type="button" disabled={status==='loading'}
    onClick={()=>void load(cursor)}>گزارش‌های منبع‌دار قدیمی‌تر</button>}
  </>}
 </section>;
}

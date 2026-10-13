import React,{useEffect,useRef,useState} from 'react';

type Item={entryKind:'NOTE'|'REQUEST'|'RESPONSE';entryId:string;noteId:string;
 requestId:string|null;content:string;actorSubject:string;recordedAt:string;
 verificationStatus:'UNVERIFIED';isApproval:false;isExecuted:false;isResolved:false};
type Source={reportId:string;activityKey:string;campaignRevision:number;draftId:string;
 pinnedPlanRevision:number;sourceKind:'CLASS_SESSION'|'CENTER_EVENT';
 sourceRecordRef:string;reportedSummary:string;evidenceRef:string;
 claimedOccurredAt:string|null;reportRecordedAt:string;linkedAt:string;linkId:string;
 verificationStatus:'UNVERIFIED';isApproval:false;isExecuted:false;isResolved:false};
type Cursor={at:string;kind:'NOTE'|'REQUEST'|'RESPONSE';id:string};
type Status='idle'|'loading'|'ready'|'denied'|'error';
const uuid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const kinds=['NOTE','REQUEST','RESPONSE'] as const;
function safeFlags(x:Record<string,unknown>){
 return x.verificationStatus==='UNVERIFIED'&&x.isApproval===false&&
  x.isExecuted===false&&x.isResolved===false;
}
function validDate(x:unknown){return typeof x==='string'&&!Number.isNaN(Date.parse(x));}
function validItem(raw:unknown):raw is Item{
 if(!raw||typeof raw!=='object')return false;
 const x=raw as Record<string,unknown>;
 return kinds.some(k=>k===x.entryKind)&&typeof x.entryId==='string'&&uuid.test(x.entryId)&&
  typeof x.noteId==='string'&&uuid.test(x.noteId)&&
  (x.requestId===null||(typeof x.requestId==='string'&&uuid.test(x.requestId)))&&
  (x.entryKind==='NOTE'&&x.entryId===x.noteId&&x.requestId===null||
   x.entryKind==='REQUEST'&&x.requestId===x.entryId||
   x.entryKind==='RESPONSE'&&x.requestId!==null)&&
  typeof x.content==='string'&&x.content.trim().length>0&&x.content.length<=1000&&
  typeof x.actorSubject==='string'&&x.actorSubject.trim().length>0&&
  validDate(x.recordedAt)&&safeFlags(x);
}
function validSource(raw:unknown,report:string,activity:string,revision:number,link:string):raw is Source{
 if(!raw||typeof raw!=='object')return false;
 const x=raw as Record<string,unknown>;
 return x.reportId===report&&x.activityKey===activity&&x.campaignRevision===revision&&x.linkId===link&&
  typeof x.draftId==='string'&&uuid.test(x.draftId)&&
  Number.isSafeInteger(x.pinnedPlanRevision)&&Number(x.pinnedPlanRevision)>0&&
  (x.sourceKind==='CLASS_SESSION'||x.sourceKind==='CENTER_EVENT')&&
  typeof x.sourceRecordRef==='string'&&uuid.test(x.sourceRecordRef)&&
  typeof x.reportedSummary==='string'&&typeof x.evidenceRef==='string'&&
  (x.claimedOccurredAt===null||validDate(x.claimedOccurredAt))&&
  validDate(x.reportRecordedAt)&&validDate(x.linkedAt)&&safeFlags(x);
}
function key(x:{recordedAt:string;entryKind:string;entryId:string}):[number,string,string]{
 return [Date.parse(x.recordedAt),x.entryKind,x.entryId.toLowerCase()];
}
function older(a:[number,string,string],b:[number,string,string]){
 return a[0]<b[0]||a[0]===b[0]&&(a[1]<b[1]||a[1]===b[1]&&a[2]<b[2]);
}
export function CenterCampaignReviewWorkspace({centerId,token,campaignId,associationId,
 reportId,activityKey,campaignRevision,linkId}:{centerId:string;token:string;
 campaignId:string;associationId:string;reportId:string;activityKey:string;
 campaignRevision:number;linkId:string}){
 const epoch=useRef(0);
 const [open,setOpen]=useState(false),[status,setStatus]=useState<Status>('idle');
 const [source,setSource]=useState<Source|null>(null),[items,setItems]=useState<Item[]>([]);
 const [cursor,setCursor]=useState<Cursor|null>(null);
 useEffect(()=>{
  epoch.current++;setOpen(false);setStatus('idle');setSource(null);setItems([]);setCursor(null);
  return()=>{epoch.current++};
 },[centerId,token,campaignId,associationId,reportId,activityKey,campaignRevision,linkId]);
 const url='/api/v1/centers/'+encodeURIComponent(centerId)+
 '/campaigns/'+encodeURIComponent(campaignId)+'/report-links/'+encodeURIComponent(associationId)+
 '/review-workspace';
 async function load(before:Cursor|null){
  if(status==='loading'||!centerId||!token.trim())return;
  const id=++epoch.current;setStatus('loading');
  if(!before){setItems([]);setSource(null);setCursor(null);}
  try{
   const q=new URLSearchParams();
   if(before){q.set('beforeAt',before.at);q.set('beforeKind',before.kind);q.set('beforeId',before.id);}
   const r=await fetch(url+(before?'?'+q.toString():''),{
    headers:{Authorization:'Bearer '+token.trim()},cache:'no-store'});
   if(id!==epoch.current)return;
   if(r.status===401||r.status===403){setSource(null);setItems([]);setCursor(null);setStatus('denied');return;}
   if(!r.ok)throw Error('No authorized original source');
   const raw:unknown=await r.json();
   if(id!==epoch.current||!raw||typeof raw!=='object')throw Error('Invalid');
   const x=raw as Record<string,unknown>;
   if(x.campaignId!==campaignId||x.associationId!==associationId||!safeFlags(x)||
    !validSource(x.source,reportId,activityKey,campaignRevision,linkId)||
    !Array.isArray(x.entries)||x.entries.length>20||!x.entries.every(validItem))
    throw Error('Forged lineage');
   const rows=x.entries as Item[];
   let previous=before?[Date.parse(before.at),before.kind,before.id.toLowerCase()] as [number,string,string]:null;
   for(const row of rows){
    const current=key(row);
    if(previous&&!older(current,previous))throw Error('Unordered timeline');
    previous=current;
   }
   const hasNext=x.nextBeforeAt!==null||x.nextBeforeKind!==null||x.nextBeforeId!==null;
   if(hasNext&&(!validDate(x.nextBeforeAt)||!kinds.some(k=>k===x.nextBeforeKind)||
     typeof x.nextBeforeId!=='string'||!uuid.test(x.nextBeforeId)||
     rows.length!==20||!rows.length||x.nextBeforeAt!==rows[19].recordedAt||
     x.nextBeforeKind!==rows[19].entryKind||x.nextBeforeId!==rows[19].entryId||
     x.isComplete!==false))throw Error('Invalid cursor');
   if(!hasNext&&(x.isComplete!==true||x.nextBeforeAt!==null||
    x.nextBeforeKind!==null||x.nextBeforeId!==null))throw Error('Invalid completion');
   if(before&&source&&JSON.stringify(x.source)!==JSON.stringify(source))
    throw Error('Source changed during pagination');
   setSource(x.source);setItems(old=>before?[...old,...rows]:rows);
   setCursor(hasNext?{at:x.nextBeforeAt as string,kind:x.nextBeforeKind as Cursor['kind'],
     id:x.nextBeforeId as string}:null);
   setStatus('ready');
  }catch{if(id===epoch.current){setSource(null);setItems([]);setCursor(null);setStatus('error');}}
 }
 return <section aria-label="پرونده یکپارچه پیگیری گزارش" style={{margin:'0.5rem 0'}}>
  <button type="button" onClick={()=>{
    epoch.current++;setOpen(v=>!v);setStatus('idle');setSource(null);setItems([]);setCursor(null);
  }}>{open?'بستن پرونده پیگیری':'بازکردن پرونده یکپارچه پیگیری'}</button>
  {open&&<>
   <p>نمای منبع و مکاتبات انسانی؛ هیچ گزارشی با این نما تأیید، اجرا یا مختومه نمی‌شود.</p>
   <button type="button" disabled={status==='loading'} onClick={()=>void load(null)}>
     خواندن پرونده از منبع</button>
   {status==='denied'&&<p role="alert">مجوزهای مستقل مشاهده پرونده و گزارش اصلی وجود ندارد.</p>}
   {status==='error'&&<p role="alert">پرونده معتبر یا مجاز دریافت نشد.</p>}
   {status==='ready'&&source&&<>
    <dl>
     <dt>منبع اصلی</dt><dd>{source.sourceKind} — {source.sourceRecordRef}</dd>
     <dt>گزارش ادعایی</dt><dd>{source.reportedSummary}</dd>
     <dt>فعالیت برنامه‌ریزی‌شده و نسخه</dt>
     <dd>{source.activityKey} — نسخه {source.campaignRevision}، نسخه طرح محلی {source.pinnedPlanRevision}</dd>
    </dl>
    <p><strong>UNVERIFIED — نتیجه اجرا، مشارکت یا تأیید محسوب نمی‌شود.</strong></p>
    {items.length===0&&<p>در این پیوند هنوز یادداشت یا مکاتبه‌ای ثبت نشده است.</p>}
    <ol>{items.map(x=><li key={x.entryKind+':'+x.entryId}>
     {x.entryKind==='NOTE'?'یادداشت':x.entryKind==='REQUEST'?'درخواست توضیح':'پاسخ'}
     {' — '}{x.content} — {x.actorSubject}
     {x.entryKind!=='NOTE'&&<small> (یادداشت: {x.noteId}{x.requestId?'، درخواست: '+x.requestId:''})</small>}
     {' '}<strong>UNVERIFIED — بدون تصمیم</strong>
    </li>)}</ol>
    {cursor?<button type="button" disabled={status==='loading'} onClick={()=>void load(cursor)}>
      ادامه تاریخچه پرونده</button>:<p>پایان موارد قابل مشاهده در این خواندن</p>}
   </>}
  </>}
 </section>;
}

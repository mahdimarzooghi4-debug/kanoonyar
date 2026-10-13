import React,{useEffect,useRef,useState} from 'react';
import {CenterSessionPreparations} from './CenterSessionPreparations';
import {SessionClaimReporter} from './SessionClaimReporter';

type Session={sourceId:string;classId:string;title:string;classTitle:string;scheduledAt:string|null};
type Status='idle'|'loading'|'ready'|'denied'|'error';
type Task={sourceId:string;kind:'PREPARATION'|'CLAIM'};
const uuid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const id=(v:unknown):v is string=>typeof v==='string'&&uuid.test(v);
const stamp=(v:unknown):v is string=>typeof v==='string'&&
 !Number.isNaN(Date.parse(v));
function valid(value:unknown,classId:string,classTitle:string):value is Session{
 if(!value||typeof value!=='object')return false;
 const v=value as Record<string,unknown>;
 return id(v.sourceId)&&v.classId===classId&&
  v.classTitle===classTitle&&typeof v.title==='string'&&
  v.title.trim().length>0&&v.title.length<=512&&
  (v.scheduledAt===null||stamp(v.scheduledAt));
}
/** Original S12 class source, not a derived or verified session list. */
export function CenterScopedSessionWorkspace({centerId,token,classId,classTitle,initialSessions}:{
 centerId:string;token:string;classId:string;classTitle:string;initialSessions:Session[];
}){
 const epoch=useRef(0);
 const [items,setItems]=useState<Session[]>(initialSessions);
 const [cursor,setCursor]=useState<string|null>(null);
 const [status,setStatus]=useState<Status>(initialSessions.length>0?'ready':'idle');
 const [task,setTask]=useState<Task|null>(null);
 useEffect(()=>()=>{epoch.current++;},[]);
 async function load(before:string|null){
  if(!centerId||!token.trim()||status==='loading')return;
  const request=++epoch.current;
  setTask(null);setStatus('loading');
  if(before===null){setItems([]);setCursor(null);}
  try{
   const path='/api/v1/centers/'+encodeURIComponent(centerId)+
    '/classes/'+encodeURIComponent(classId)+'/source-sessions'+
    (before===null?'':'?beforeSourceId='+encodeURIComponent(before));
   const response=await fetch(path,{headers:{Authorization:'Bearer '+token.trim()},cache:'no-store'});
   if(request!==epoch.current)return;
   if(response.status===401||response.status===403){
    setItems([]);setCursor(null);setStatus('denied');return;
   }
   if(!response.ok)throw Error('Original source unavailable');
   const raw:unknown=await response.json();
   if(request!==epoch.current||!raw||typeof raw!=='object')throw Error('Invalid class source');
   const x=raw as Record<string,unknown>;
   if(x.sourceKind!=='CLASS_SESSION'||x.classId!==classId||
    x.classTitle!==classTitle||!Array.isArray(x.records)||x.records.length>20||
    !x.records.every(v=>valid(v,classId,classTitle))||
    !(x.nextBeforeSourceId===null||id(x.nextBeforeSourceId))){
     console.warn('[S47 scope]',JSON.stringify({
      responseKind:x.sourceKind,receivedClass:x.classId,expectedClass:classId,
      receivedTitle:x.classTitle,expectedTitle:classTitle,
      rowCount:Array.isArray(x.records)?x.records.length:-1,
      invalidIndices:Array.isArray(x.records)?x.records.map((v,i)=>valid(v,classId,classTitle)?-1:i).filter(i=>i>=0):[],
      next:x.nextBeforeSourceId,nextValid:x.nextBeforeSourceId===null||id(x.nextBeforeSourceId)
     }));
     throw Error('Class scope mismatch');
    }
   const rows=x.records as Session[];
   if(rows.some((v,i)=>(before!==null&&v.sourceId.toLowerCase()>=before.toLowerCase())||
    (i>0&&rows[i-1].sourceId.toLowerCase()<=v.sourceId.toLowerCase()))||
    (x.nextBeforeSourceId!==null&&(rows.length!==20||
     rows[19].sourceId.toLowerCase()!==String(x.nextBeforeSourceId).toLowerCase())))
    throw Error('Forged cursor');
   const combined=before===null?rows:[...items,...rows];
   const seen=new Set<string>();
   for(const item of combined){
    const key=item.sourceId.toLowerCase();
    if(seen.has(key))throw Error('Duplicate original session');
    seen.add(key);
   }
   setItems(combined);setCursor(x.nextBeforeSourceId as string|null);setStatus('ready');
  }catch(error){
   console.warn('[S47 source validation]',error instanceof Error?error.message:'unknown');
   if(request===epoch.current){
    setItems([]);setCursor(null);setTask(null);setStatus('error');
   }
  }
 }
 return <section aria-label={'میزکار جلسات کلاس '+classId}>
  <h4>جلسات اصلی کلاس {classTitle}</h4>
  <p>جلسه برنامه‌ریزی‌شده به معنی برگزاری یا حضور تأییدشده نیست.
   دریافت جلسات نیازمند مجوز مستقل کاتالوگ منابع S12 است؛
   ایجاد گزارش و آماده‌سازی نیز هرکدام مجوز جداگانه دارند.</p>
  <button type="button" disabled={!token.trim()||status==='loading'}
   onClick={()=>void load(null)}>دریافت همه جلسات این کلاس از منبع اصلی</button>
  {status==='denied'&&<p role="alert">مجوز مستقل مشاهده جلسات اصلی این کلاس وجود ندارد.</p>}
  {status==='error'&&<p role="alert">فهرست معتبر جلسات اصلی این کلاس دریافت نشد.</p>}
  {status==='ready'&&items.length===0&&<p role="status">برای این کلاس جلسه‌ای ثبت نشده است؛
   صفر جلسه ثبت‌شده، تأیید اجرای هیچ فعالیتی نیست.</p>}
  {status==='ready'&&<ul>{items.map(item=><li key={item.sourceId}>
   <strong>{item.title}</strong> — زمان برنامه‌ریزی: {item.scheduledAt===null?'نامعلوم':
    new Date(item.scheduledAt).toLocaleString('fa-IR')}
   <p>شناسه جلسه: {item.sourceId}</p>
   <button type="button" onClick={()=>setTask(x=>x?.sourceId===item.sourceId&&
    x.kind==='PREPARATION'?null:{sourceId:item.sourceId,kind:'PREPARATION'})}>
    آماده‌سازی خصوصی همین جلسه</button>
   <button type="button" onClick={()=>setTask(x=>x?.sourceId===item.sourceId&&
    x.kind==='CLAIM'?null:{sourceId:item.sourceId,kind:'CLAIM'})}>
    ادعای وقوع و حضور همین جلسه</button>
   {task?.sourceId===item.sourceId&&task.kind==='PREPARATION'&&
    <CenterSessionPreparations key={'prep:'+centerId+':'+token+':'+item.sourceId}
     centerId={centerId} token={token} initialSession={item}/>}
   {task?.sourceId===item.sourceId&&task.kind==='CLAIM'&&
    <SessionClaimReporter key={'claim:'+centerId+':'+token+':'+item.sourceId}
     centerId={centerId} token={token} sessionId={item.sourceId}
     onClose={()=>setTask(null)}/>}
  </li>)}</ul>}
  {status==='ready'&&cursor!==null&&<button type="button"
   onClick={()=>void load(cursor)}>جلسات قدیمی‌تر همین کلاس</button>}
 </section>;
}

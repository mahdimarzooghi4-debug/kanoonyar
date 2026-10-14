import React,{useEffect,useRef,useState} from 'react';
import {CenterEventSourceDossier} from './CenterEventSourceDossier';
import {SessionClaimReporter} from './SessionClaimReporter';
type SourceKind='CLASS_SESSION'|'CENTER_EVENT';
type Item={scheduledAt:string;sourceKind:SourceKind;sourceId:string;title:string;
 draftId:string|null;pinnedPlanRevision:number|null;planRevision:number|null;
 visibility:'PRIVATE_PLAN';isPublished:false;isExecuted:false;isAttendanceVerified:false};
type Cursor={scheduledAt:string;sourceKind:SourceKind;sourceId:string};
const uuid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const iso=(value:unknown):value is string=>typeof value==='string'&&
 !Number.isNaN(Date.parse(value))&&
 (value.endsWith('Z')||/[+-]\d{2}:\d{2}$/.test(value));
function validItem(x:unknown):x is Item{
 if(!x||typeof x!=='object')return false;
 const a=x as Record<string,unknown>;
 if(!iso(a.scheduledAt)||typeof a.title!=='string'||!a.title.trim()||
  typeof a.sourceId!=='string'||!uuid.test(a.sourceId)||
  a.visibility!=='PRIVATE_PLAN'||a.isPublished!==false||
  a.isExecuted!==false||a.isAttendanceVerified!==false)return false;
 if(a.sourceKind==='CLASS_SESSION')return a.draftId===null&&
  a.pinnedPlanRevision===null&&a.planRevision===null;
 if(a.sourceKind==='CENTER_EVENT')return typeof a.draftId==='string'&&
  uuid.test(a.draftId)&&Number.isSafeInteger(a.pinnedPlanRevision)&&
  Number(a.pinnedPlanRevision)>0&&Number.isSafeInteger(a.planRevision)&&Number(a.planRevision)>0;
 return false;
}
const cmp=(a:Cursor,b:Cursor)=>Date.parse(a.scheduledAt)-Date.parse(b.scheduledAt)||
 a.sourceKind.localeCompare(b.sourceKind)||a.sourceId.toLowerCase().localeCompare(b.sourceId.toLowerCase());
export function CenterPrivateCalendar({token,centerId}:{token:string;centerId:string}){
 const epoch=useRef(0);
 const [from,setFrom]=useState(''),[until,setUntil]=useState('');
 const [items,setItems]=useState<Item[]>([]),[cursor,setCursor]=useState<Cursor|null>(null);
 const [status,setStatus]=useState<'idle'|'loading'|'ready'|'denied'|'error'>('idle');
 const [activeWindow,setActiveWindow]=useState<{from:string;until:string}|null>(null);
 const [opened,setOpened]=useState<string|null>(null);
 useEffect(()=>()=>{epoch.current++;},[]);
 const datesValid=from!==''&&until!==''&&from<until;
 async function load(before:Cursor|null){
  if(!centerId||!token.trim()||status==='loading')return;
  const window=before===null?{from,until}:activeWindow;
  if(!window||!window.from||!window.until||window.from>=window.until)return;
  const current=++epoch.current;
  setOpened(null);
  setStatus('loading');
  if(!before){setItems([]);setCursor(null);setActiveWindow(window);}
  try{
   const params=new URLSearchParams({
    fromUtc:window.from+'T00:00:00Z',untilUtc:window.until+'T00:00:00Z'
   });
   if(before){params.set('beforeAt',before.scheduledAt);
    params.set('beforeKind',before.sourceKind);params.set('beforeSourceId',before.sourceId);}
   const response=await fetch('/api/v1/centers/'+encodeURIComponent(centerId)+
    '/schedule/private?'+params,{headers:{Authorization:'Bearer '+token.trim()},cache:'no-store'});
   if(epoch.current!==current)return;
   if(response.status===401||response.status===403){
    setStatus('denied');setItems([]);setCursor(null);setOpened(null);return;
   }
   if(!response.ok)throw Error('Unavailable');
   const payload:unknown=await response.json();
   if(epoch.current!==current||!payload||typeof payload!=='object')throw Error('Invalid');
   const data=payload as Record<string,unknown>;
   if(!iso(data.fromUtc)||!iso(data.untilUtc)||
    Date.parse(data.fromUtc)!==Date.parse(window.from+'T00:00:00Z')||
    Date.parse(data.untilUtc)!==Date.parse(window.until+'T00:00:00Z')||
    data.visibility!=='PRIVATE_PLAN'||data.isExecuted!==false||
    data.isPublished!==false||data.isAttendanceVerified!==false||
    !Array.isArray(data.items)||data.items.length>20||!data.items.every(validItem))
    throw Error('Forged calendar');
   const list=data.items as Item[];
   const next=data.nextCursor;
   if(next!==null&&(!next||typeof next!=='object'||!iso((next as Cursor).scheduledAt)||
    !uuid.test(String((next as Cursor).sourceId))||
    !['CLASS_SESSION','CENTER_EVENT'].includes(String((next as Cursor).sourceKind))))
    throw Error('Invalid cursor');
   if(list.some((a,i)=>Date.parse(a.scheduledAt)<Date.parse(window.from+'T00:00:00Z')||
    Date.parse(a.scheduledAt)>=Date.parse(window.until+'T00:00:00Z')||
    (before!==null&&cmp(a,before)<=0)||(i>0&&cmp(list[i-1],a)>=0))||
    (next!==null&&(list.length!==20||cmp(list[19],next as Cursor)!==0)))
    throw Error('Invalid order');
   setItems(old=>before===null?list:[...old,...list]);
   setCursor(next as Cursor|null);setStatus('ready');
  }catch{if(epoch.current===current){setItems([]);setCursor(null);setOpened(null);setStatus('error');}}
 }
 return <section aria-label="تقویم خصوصی کانون" style={{border:'1px solid #aaa',padding:12,marginTop:16}}>
  <h3>تقویم برنامه‌ریزی خصوصی کانون</h3>
  <p>فقط زمان‌های برنامه‌ریزی‌شده کلاس و رویداد؛ نه برگزاری، حضور، ثبت‌نام یا انتشار عمومی.
   بازکردن پرونده شواهد رویداد یا ثبت ادعای جلسه، به مجوزهای مستقل اصلی نیاز دارد.
   تغییر بازه یا بازیابی مجدد، عملیات باز را می‌بندد.</p>
  <label>از تاریخ (UTC) <input aria-label="شروع بازه تقویم" type="date" value={from}
   onChange={e=>{epoch.current++;setFrom(e.target.value);setStatus('idle');setItems([]);setCursor(null);setActiveWindow(null);setOpened(null);}}/></label>{' '}
  <label>تا تاریخ، انحصاری (UTC) <input aria-label="پایان انحصاری بازه تقویم" type="date" value={until}
   onChange={e=>{epoch.current++;setUntil(e.target.value);setStatus('idle');setItems([]);setCursor(null);setActiveWindow(null);setOpened(null);}}/></label>{' '}
  <button type="button" disabled={!centerId||!token.trim()||!datesValid||status==='loading'}
   onClick={()=>void load(null)}>خواندن تقویم خصوصی</button>
  {status==='loading'&&<p role="status">در حال دریافت زمان‌بندی…</p>}
  {status==='denied'&&<p role="alert">مجوز مستقل مشاهده تقویم، کلاس و رویداد تأیید نشده است.</p>}
  {status==='error'&&<p role="alert">دریافت یا اعتبارسنجی تقویم خصوصی ممکن نشد.</p>}
  {status==='ready'&&items.length===0&&<p>برنامه زمان‌دار در این بازه پیدا نشد؛ زمان نامعلوم به معنای عدم فعالیت نیست.</p>}
  {status==='ready'&&<ol>{items.map(x=><li key={x.sourceKind+':'+x.sourceId}>
   <strong>{x.title}</strong> — {x.sourceKind==='CLASS_SESSION'?'جلسه کلاس':'رویداد خصوصی'}
   <p>زمان UTC: {x.scheduledAt} — فقط برنامه‌ریزی‌شده</p>
   <p>شناسه منبع: {x.sourceId}</p>
   {x.sourceKind==='CENTER_EVENT'&&<p>نسخه رویداد {x.planRevision}، نسخه طرح محلی {x.pinnedPlanRevision}</p>}
   <button type="button" onClick={()=>setOpened(current=>current===
    x.sourceKind+':'+x.sourceId?null:x.sourceKind+':'+x.sourceId)}>
    {opened===x.sourceKind+':'+x.sourceId?'بستن عملیات منبع تقویم':
      x.sourceKind==='CENTER_EVENT'?'پرونده شواهد همین رویداد از تقویم':
      'گزارش ادعای همین جلسه از تقویم'}
   </button>
   {opened===x.sourceKind+':'+x.sourceId&&x.sourceKind==='CENTER_EVENT'&&
    <CenterEventSourceDossier
     key={'calendar-dossier:'+centerId+':'+token+':'+x.sourceId+':'+x.planRevision}
     centerId={centerId} token={token} eventId={x.sourceId}
     draftId={x.draftId!} pinnedPlanRevision={x.pinnedPlanRevision!}
     revision={x.planRevision!} title={x.title} scheduledAt={x.scheduledAt}/>}
   {opened===x.sourceKind+':'+x.sourceId&&x.sourceKind==='CLASS_SESSION'&&
    <SessionClaimReporter
     key={'calendar-session:'+centerId+':'+token+':'+x.sourceId}
     centerId={centerId} token={token} sessionId={x.sourceId}
     onClose={()=>setOpened(null)}/>}
  </li>)}</ol>}
  {status==='ready'&&cursor!==null&&<button type="button" onClick={()=>void load(cursor)}>
   ادامه تقویم خصوصی</button>}
 </section>;
}

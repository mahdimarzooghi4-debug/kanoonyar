import React,{useEffect,useRef,useState} from 'react';
import { CenterSessionPreparations } from './CenterSessionPreparations';
import { CenterLinkedSessionReportHandoff } from './CenterLinkedSessionReportHandoff';

type Session={sourceId:string;classId:string;title:string;classTitle:string;scheduledAt:string|null};
type Link={sessionId:string;classId:string;sessionTitle:string;classTitle:string;
 scheduledAt:string|null;recordedAt:string;visibility:'PRIVATE_PLANNING';
 isPublished:false;isExecuted:false;isAttendanceVerified:false;isDeliveryVerified:false};
type State='idle'|'loading'|'ready'|'denied'|'error';
type Write='idle'|'saving'|'saved'|'denied'|'conflict'|'error';
const uuid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const date=(v:unknown):v is string=>typeof v==='string'&&!Number.isNaN(Date.parse(v));
const flags=(v:Record<string,unknown>)=>v.visibility==='PRIVATE_PLANNING'&&
 v.isPublished===false&&v.isExecuted===false&&
 v.isAttendanceVerified===false&&v.isDeliveryVerified===false;
function session(v:unknown):v is Session{
 if(!v||typeof v!=='object')return false;const x=v as Record<string,unknown>;
 return typeof x.sourceId==='string'&&uuid.test(x.sourceId)&&
 typeof x.classId==='string'&&uuid.test(x.classId)&&
 typeof x.title==='string'&&x.title.trim().length>0&&
 typeof x.classTitle==='string'&&x.classTitle.trim().length>0&&
 (x.scheduledAt===null||date(x.scheduledAt));
}
function link(v:unknown):v is Link{
 if(!v||typeof v!=='object')return false;const x=v as Record<string,unknown>;
 return typeof x.sessionId==='string'&&uuid.test(x.sessionId)&&
 typeof x.classId==='string'&&uuid.test(x.classId)&&
 typeof x.sessionTitle==='string'&&x.sessionTitle.trim().length>0&&
 typeof x.classTitle==='string'&&x.classTitle.trim().length>0&&
 (x.scheduledAt===null||date(x.scheduledAt))&&
 date(x.recordedAt)&&flags(x);
}
export function CenterWorkSessionLinks({centerId,workCardId,token,draftId,pinnedRevision,programId,nationalVersion}:{
 centerId:string;workCardId:string;token:string;draftId:string;
 pinnedRevision:number;programId:string;nationalVersion:number
}){
 const epoch=useRef(0),root='/api/v1/centers/'+encodeURIComponent(centerId);
 const headers={Authorization:'Bearer '+token.trim()};
 const [catalog,setCatalog]=useState<Session[]>([]),[catalogCursor,setCatalogCursor]=useState<string|null>(null);
 const [sourceState,setSourceState]=useState<State>('idle');
 const [links,setLinks]=useState<Link[]>([]),[cursor,setCursor]=useState<string|null>(null);
 const [state,setState]=useState<State>('idle'),[selected,setSelected]=useState('');
 const [write,setWrite]=useState<Write>('idle'),[openPrep,setOpenPrep]=useState(false);
 useEffect(()=>()=>{epoch.current++;},[]);
 const path=root+'/operations/work-cards/'+encodeURIComponent(workCardId)+'/session-links';
 async function sources(before:string|null){
  if(!token.trim()||!centerId||sourceState==='loading')return;
  const request=++epoch.current;setSourceState('loading');
  if(before===null){setCatalog([]);setCatalogCursor(null);setSelected('');}
  try{
   const r=await fetch(root+'/activity-sources/class-sessions'+
    (before?'?beforeSourceId='+encodeURIComponent(before):''),{headers,cache:'no-store'});
   if(request!==epoch.current)return;
   if(r.status===401||r.status===403){setCatalog([]);setCatalogCursor(null);setSourceState('denied');return;}
   if(!r.ok)throw Error('Source');
   const raw:unknown=await r.json();if(request!==epoch.current||!raw||typeof raw!=='object')throw Error('Invalid');
   const v=raw as Record<string,unknown>;
   if(v.sourceKind!=='CLASS_SESSION'||!Array.isArray(v.records)||v.records.length>20||
    !v.records.every(session)||!(v.nextBeforeSourceId===null||
     (typeof v.nextBeforeSourceId==='string'&&uuid.test(v.nextBeforeSourceId))))
    throw Error('Fake session catalogue');
   const list=v.records as Session[];
   if(list.some((x,i)=>(before!==null&&x.sourceId.toLowerCase()>=before.toLowerCase())||
    (i>0&&list[i-1].sourceId.toLowerCase()<=x.sourceId.toLowerCase()))||
    (v.nextBeforeSourceId!==null&&(list.length!==20||
     list[19].sourceId.toLowerCase()!==String(v.nextBeforeSourceId).toLowerCase())))
    throw Error('Source cursor');
   setCatalog(old=>before?[...old,...list]:list);
   setCatalogCursor(v.nextBeforeSourceId as string|null);setSourceState('ready');
  }catch{if(request===epoch.current){setCatalog([]);setCatalogCursor(null);setSourceState('error');}}
 }
 async function list(before:string|null){
  if(!token.trim()||!centerId||state==='loading')return;
  const request=++epoch.current;setState('loading');
  if(before===null){setLinks([]);setCursor(null);}
  try{
   const r=await fetch(path+(before?'?beforeSessionId='+encodeURIComponent(before):''),
    {headers,cache:'no-store'});
   if(request!==epoch.current)return;
   if(r.status===401||r.status===403){setLinks([]);setCursor(null);setState('denied');return;}
   if(!r.ok)throw Error('Unavailable');
   const raw:unknown=await r.json();if(request!==epoch.current||!raw||typeof raw!=='object')throw Error('Invalid');
   const v=raw as Record<string,unknown>;
   if(v.workCardId!==workCardId||!flags(v)||!Array.isArray(v.links)||v.links.length>20||
    !v.links.every(link)||!(v.nextBeforeSessionId===null||
     (typeof v.nextBeforeSessionId==='string'&&uuid.test(v.nextBeforeSessionId))))
    throw Error('Forged linked session');
   const rows=v.links as Link[];
   if(rows.some((x,i)=>(before!==null&&x.sessionId.toLowerCase()>=before.toLowerCase())||
    (i>0&&rows[i-1].sessionId.toLowerCase()<=x.sessionId.toLowerCase()))||
    (v.nextBeforeSessionId!==null&&(rows.length!==20||
     rows[19].sessionId.toLowerCase()!==String(v.nextBeforeSessionId).toLowerCase())))
    throw Error('Invalid link cursor');
   setLinks(old=>before?[...old,...rows]:rows);setCursor(v.nextBeforeSessionId as string|null);
   setState('ready');
  }catch{if(request===epoch.current){setLinks([]);setCursor(null);setState('error');}}
 }
 async function attach(){
  if(!selected||!catalog.some(x=>x.sourceId===selected)||write==='saving'||!token.trim())return;
  const request=++epoch.current;setWrite('saving');
  try{
   const r=await fetch(path,{method:'POST',headers:{...headers,'Content-Type':'application/json'},
    body:JSON.stringify({sessionId:selected})});
   if(request!==epoch.current)return;
   if(r.status===401||r.status===403){setWrite('denied');return;}
   if(r.status===409){setWrite('conflict');return;}
   if(!r.ok)throw Error('Storage');
   const raw:unknown=await r.json();if(request!==epoch.current||!raw||typeof raw!=='object')throw Error('Invalid');
   const v=raw as Record<string,unknown>;
   if(v.workCardId!==workCardId||v.sessionId!==selected||!flags(v))
    throw Error('Forged delivery');
   setWrite('saved');setSelected('');setLinks([]);setCursor(null);setState('idle');
  }catch{if(request===epoch.current)setWrite('error');}
 }
 return <section aria-label={'جلسات آموزشی مرتبط با کارت '+workCardId}
  style={{border:'1px solid #aaa',padding:8,marginTop:8}}>
  <h5>پیوند برنامه‌ریزی کارت کار به جلسات آموزشی</h5>
  <p>فقط رابطه برنامه‌ریزی خصوصی؛ جلسه برگزار، حضور ثبت یا هدف ملی محقق نشده است.</p>
  <button type="button" onClick={()=>void sources(null)} disabled={!token.trim()||sourceState==='loading'}>
   دریافت جلسه‌های واقعی برای پیوند</button>{' '}
  <button type="button" onClick={()=>void list(null)} disabled={!token.trim()||state==='loading'}>
   مشاهده جلسات پیوندخورده</button>
  {sourceState==='denied'&&<p role="alert">مجوز دیدن جلسات واقعی وجود ندارد.</p>}
  {sourceState==='error'&&<p role="alert">کاتالوگ معتبر جلسات دریافت نشد.</p>}
  {state==='denied'&&<p role="alert">مجوز مستقل خواندن پیوند جلسات وجود ندارد.</p>}
  {state==='error'&&<p role="alert">فهرست معتبر پیوندها دریافت نشد.</p>}
  {sourceState==='ready'&&<label>جلسه واقعی
   <select aria-label={'انتخاب جلسه مرتبط با '+workCardId} value={selected}
    onChange={e=>{setSelected(e.target.value);setWrite('idle');}}>
    <option value="">انتخاب جلسه از کاتالوگ</option>
    {catalog.map(s=><option key={s.sourceId} value={s.sourceId}>
     {s.classTitle} — {s.title}</option>)}
   </select></label>}
  {catalogCursor&&<button type="button" onClick={()=>void sources(catalogCursor)}>
   جلسات واقعی بیشتر</button>}
  <button type="button" disabled={!selected||write==='saving'} onClick={()=>void attach()}>
   ثبت پیوند خصوصی جلسه به کارت کار</button>
  {write==='saved'&&<p role="status">پیوند برنامه‌ریزی ثبت شد؛ برگزاری یا حضور تأیید نشده است.</p>}
  {write==='conflict'&&<p role="alert">این پیوند تکراری است یا منبع معتبر نیست.</p>}
  {write==='denied'&&<p role="alert">مجوز مستقل ثبت پیوند وجود ندارد.</p>}
  {write==='error'&&<p role="alert">تأیید معتبر ذخیره پیوند دریافت نشد.</p>}
  {state==='ready'&&<ul>{links.map(s=><li key={s.sessionId}>
   <strong>{s.classTitle} — {s.sessionTitle}</strong>
   <p>شناسه جلسه: {s.sessionId}؛ زمان اصلی: {s.scheduledAt??'نامشخص'}</p>
   <p>برگزاری و حضور: نامشخص</p>
   <CenterLinkedSessionReportHandoff key={centerId+':'+token+':'+s.sessionId}
    centerId={centerId} token={token} draftId={draftId} workCardId={workCardId}
    sessionId={s.sessionId} pinnedRevision={pinnedRevision}
    programId={programId} nationalVersion={nationalVersion}/>
  </li>)}</ul>}
  {cursor&&<button type="button" onClick={()=>void list(cursor)}>جلسات پیوندخورده بیشتر</button>}
  {state==='ready'&&links.length>0&&<button type="button" onClick={()=>setOpenPrep(v=>!v)}>
   {openPrep?'بستن دفتر آماده‌سازی':'باز کردن دفتر اصلی آماده‌سازی جلسات'}</button>}
  {openPrep&&<CenterSessionPreparations key={centerId+':'+token}
    centerId={centerId} token={token}/>}
 </section>;
}

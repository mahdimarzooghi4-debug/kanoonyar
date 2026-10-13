import React,{useEffect,useRef,useState} from 'react';
type Plan={draftId:string;revision:number;localTitle:string;programId:string;nationalVersion:number};
type Revision={revision:number;title:string;purpose:string;locationDescription:string;
 scheduledAt:string;recordedAt:string;visibility:'PRIVATE_DRAFT';isPublished:false;
 isExecuted:false;isRegistrationOpen:false};
type EventPlan=Revision&{eventId:string;draftId:string;pinnedPlanRevision:number};
type State='idle'|'loading'|'ready'|'denied'|'error';
const uuid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
function revision(x:unknown):x is Revision{
 if(!x||typeof x!=='object')return false;
 const r=x as Record<string,unknown>;
 return Number.isSafeInteger(r.revision)&&Number(r.revision)>0&&
  typeof r.title==='string'&&r.title.trim().length>0&&r.title.length<=200&&
  typeof r.purpose==='string'&&r.purpose.trim().length>0&&r.purpose.length<=500&&
  typeof r.locationDescription==='string'&&r.locationDescription.trim().length>0&&r.locationDescription.length<=200&&
  typeof r.scheduledAt==='string'&&!Number.isNaN(Date.parse(r.scheduledAt))&&
  typeof r.recordedAt==='string'&&!Number.isNaN(Date.parse(r.recordedAt))&&
  r.visibility==='PRIVATE_DRAFT'&&r.isPublished===false&&r.isExecuted===false&&r.isRegistrationOpen===false;
}
function validEvent(x:unknown):x is EventPlan{
 if(!revision(x))return false;
 const r=x as unknown as Record<string,unknown>;
 return typeof r.eventId==='string'&&uuid.test(r.eventId)&&
  typeof r.draftId==='string'&&uuid.test(r.draftId)&&
  Number.isSafeInteger(r.pinnedPlanRevision)&&Number(r.pinnedPlanRevision)>0;
}
function validPlans(x:unknown):x is {drafts:Plan[]}{
 if(!x||typeof x!=='object')return false;
 const r=x as Record<string,unknown>;
 return Array.isArray(r.drafts)&&r.drafts.length<=200&&r.drafts.every(v=>{
  if(!v||typeof v!=='object')return false;
  const p=v as Record<string,unknown>;
  return typeof p.draftId==='string'&&uuid.test(p.draftId)&&
    Number.isSafeInteger(p.revision)&&Number(p.revision)>0&&
    typeof p.localTitle==='string'&&typeof p.programId==='string'&&
    Number.isSafeInteger(p.nationalVersion)&&Number(p.nationalVersion)>0;
 });
}
export function CenterEventPrivatePlanner({centerId,token}:{centerId:string;token:string}){
 const epoch=useRef(0),prefix='/api/v1/centers/'+encodeURIComponent(centerId);
 const headers={Authorization:'Bearer '+token.trim()};
 const [plans,setPlans]=useState<Plan[]>([]),[planState,setPlanState]=useState<State>('idle');
 const [events,setEvents]=useState<EventPlan[]>([]),[listState,setListState]=useState<State>('idle');
 const [cursor,setCursor]=useState<string|null>(null),[selected,setSelected]=useState<EventPlan|null>(null);
 const [planId,setPlanId]=useState(''),[title,setTitle]=useState(''),[purpose,setPurpose]=useState('');
 const [location,setLocation]=useState(''),[scheduled,setScheduled]=useState('');
 const [save,setSave]=useState<'idle'|'saving'|'saved'|'denied'|'conflict'|'error'>('idle');
 const [history,setHistory]=useState<Revision[]>([]),[historyState,setHistoryState]=useState<State>('idle');
 const [historyCursor,setHistoryCursor]=useState<number|null>(null);
 useEffect(()=>{
  epoch.current++;setPlans([]);setPlanState('idle');setEvents([]);setListState('idle');
  setCursor(null);setSelected(null);setPlanId('');setTitle('');setPurpose('');
  setLocation('');setScheduled('');setSave('idle');setHistory([]);setHistoryState('idle');
  setHistoryCursor(null);return()=>{epoch.current++};
 },[centerId,token]);
 function edit(e:EventPlan|null){
  epoch.current++;setSelected(e);setPlanId(e?e.draftId+':'+e.pinnedPlanRevision:'');
  setTitle(e?.title??'');setPurpose(e?.purpose??'');setLocation(e?.locationDescription??'');
  setScheduled(e?.scheduledAt?new Date(e.scheduledAt).toISOString().slice(0,16):'');
  setSave('idle');setHistory([]);setHistoryState('idle');setHistoryCursor(null);
 }
 async function loadPlans(){
  if(!token.trim()||!centerId||planState==='loading')return;
  const n=++epoch.current;setPlans([]);setPlanState('loading');
  try{
   const r=await fetch(prefix+'/local-plan-drafts',{headers,cache:'no-store'});
   if(n!==epoch.current)return;
   if(r.status===401||r.status===403){setPlanState('denied');return;}
   if(!r.ok)throw Error('Plan unavailable');
   const x:unknown=await r.json();
   if(n!==epoch.current||!validPlans(x))throw Error('Invalid plan source');
   setPlans(x.drafts);setPlanState('ready');
  }catch{if(n===epoch.current)setPlanState('error');}
 }
 async function loadList(before:string|null){
  if(!token.trim()||!centerId||listState==='loading')return;
  const n=++epoch.current;setListState('loading');
  if(before===null){setEvents([]);setCursor(null);}
  try{
   const r=await fetch(prefix+'/events/plans'+(before?'?beforeEventId='+encodeURIComponent(before):''),
    {headers,cache:'no-store'});
   if(n!==epoch.current)return;
   if(r.status===401||r.status===403){setEvents([]);setCursor(null);setListState('denied');return;}
   if(!r.ok)throw Error('Unavailable');
   const raw:unknown=await r.json();
   if(n!==epoch.current||!raw||typeof raw!=='object')throw Error('Invalid');
   const x=raw as Record<string,unknown>;
   if(!Array.isArray(x.events)||x.events.length>20||!x.events.every(validEvent)||
     !(x.nextBeforeEventId===null||(typeof x.nextBeforeEventId==='string'&&uuid.test(x.nextBeforeEventId))))
    throw Error('Unsafe event list');
   const rows=x.events as EventPlan[];
   if(rows.some((e,i)=>(before!==null&&e.eventId.toLowerCase()>=before.toLowerCase())||
     (i>0&&rows[i-1].eventId.toLowerCase()<=e.eventId.toLowerCase())))throw Error('Invalid order');
   if(x.nextBeforeEventId!==null&&(rows.length!==20||
      rows[19].eventId.toLowerCase()!==String(x.nextBeforeEventId).toLowerCase()))throw Error('Invalid cursor');
   setEvents(old=>before?[...old,...rows]:rows);setCursor(x.nextBeforeEventId as string|null);
   setListState('ready');
  }catch{if(n===epoch.current){setEvents([]);setCursor(null);setListState('error');}}
 }
 async function append(){
  const chosen=selected?{draftId:selected.draftId,revision:selected.pinnedPlanRevision}:
   plans.find(x=>x.draftId+':'+x.revision===planId);
  if(!chosen||!title.trim()||title.length>200||!purpose.trim()||purpose.length>500||
    !location.trim()||location.length>200||!scheduled||
    Number.isNaN(Date.parse(scheduled+'Z'))||!token.trim())return;
  const n=++epoch.current,eventId=selected?.eventId??crypto.randomUUID(),
   expectedRevision=selected?.revision??0;
  const data={eventId,expectedRevision,draftId:chosen.draftId,
   pinnedPlanRevision:chosen.revision,title:title.trim(),purpose:purpose.trim(),
   locationDescription:location.trim(),scheduledAt:new Date(scheduled+'Z').toISOString()};
  setSave('saving');
  try{
   const r=await fetch(prefix+'/events/plans/revisions',{method:'POST',headers:{...headers,
    'Content-Type':'application/json'},body:JSON.stringify(data)});
   if(n!==epoch.current)return;
   if(r.status===401||r.status===403){setSave('denied');return;}
   if(r.status===409){setSave('conflict');return;}
   if(!r.ok)throw Error('Unavailable');
   const raw:unknown=await r.json();
   if(n!==epoch.current||!raw||typeof raw!=='object')throw Error('Invalid');
   const x=raw as Record<string,unknown>;
   if(x.eventId!==eventId||x.draftId!==chosen.draftId||
     x.pinnedPlanRevision!==chosen.revision||x.revision!==expectedRevision+1||
     x.visibility!=='PRIVATE_DRAFT'||x.isPublished!==false||
     x.isExecuted!==false||x.isRegistrationOpen!==false)throw Error('Forged event status');
   const value:EventPlan={...data,revision:expectedRevision+1,recordedAt:new Date().toISOString(),
    visibility:'PRIVATE_DRAFT',isPublished:false,isExecuted:false,isRegistrationOpen:false};
   setSelected(value);setEvents(old=>[value,...old.filter(e=>e.eventId!==eventId)]);
   setHistory([]);setHistoryCursor(null);setHistoryState('idle');setSave('saved');
  }catch{if(n===epoch.current)setSave('error');}
 }
 async function loadHistory(before:number|null){
  if(!selected||historyState==='loading')return;
  const e=selected,n=++epoch.current;setHistoryState('loading');
  if(before===null){setHistory([]);setHistoryCursor(null);}
  try{
   const r=await fetch(prefix+'/events/plans/'+encodeURIComponent(e.eventId)+'/revisions'+
    (before===null?'':'?beforeRevision='+before),{headers,cache:'no-store'});
   if(n!==epoch.current)return;
   if(r.status===401||r.status===403){setHistory([]);setHistoryState('denied');return;}
   if(!r.ok)throw Error('Missing event');
   const raw:unknown=await r.json();
   if(n!==epoch.current||!raw||typeof raw!=='object')throw Error('Invalid');
   const x=raw as Record<string,unknown>;
   if(x.eventId!==e.eventId||x.draftId!==e.draftId||
    x.pinnedPlanRevision!==e.pinnedPlanRevision||
    !Array.isArray(x.revisions)||x.revisions.length>20||!x.revisions.every(revision)||
    !(x.nextBeforeRevision===null||(Number.isSafeInteger(x.nextBeforeRevision)&&Number(x.nextBeforeRevision)>0)))
    throw Error('Unsafe event history');
   const rows=x.revisions as Revision[];
   if(rows.some((v,i)=>(before!==null&&v.revision>=before)||(i>0&&rows[i-1].revision<=v.revision)))throw Error('Bad history order');
   if(x.nextBeforeRevision!==null&&(rows.length!==20||rows[19].revision!==x.nextBeforeRevision))
    throw Error('Bad history cursor');
   setHistory(old=>before===null?rows:[...old,...rows]);
   setHistoryCursor(x.nextBeforeRevision as number|null);setHistoryState('ready');
  }catch{if(n===epoch.current){setHistory([]);setHistoryState('error');}}
 }
 const canSave=(Boolean(selected)||plans.some(p=>p.draftId+':'+p.revision===planId))&&
  title.trim().length>0&&title.length<=200&&purpose.trim().length>0&&purpose.length<=500&&
  location.trim().length>0&&location.length<=200&&scheduled!==''&&
  !Number.isNaN(Date.parse(scheduled+'Z'))&&save!=='saving';
 return <section aria-label="برنامه‌ریزی خصوصی رویدادهای کانون" style={{marginTop:'2rem'}}>
  <h2>رویدادهای کانون — پیش‌نویس خصوصی</h2>
  <p>زمان و مکان صرفاً برنامه‌ریزی است؛ برگزاری، انتشار، ثبت‌نام یا مشارکت قطعی ثبت نمی‌شود.</p>
  <button type="button" onClick={()=>void loadPlans()} disabled={planState==='loading'||!centerId}>دریافت طرح‌های محلی رویداد</button>{' '}
  <button type="button" onClick={()=>void loadList(null)} disabled={listState==='loading'||!centerId}>دریافت رویدادهای خصوصی</button>{' '}
  <button type="button" onClick={()=>edit(null)}>رویداد جدید</button>
  {planState==='denied'&&<p role="alert">مجوز مشاهده طرح‌های محلی وجود ندارد.</p>}
  {planState==='error'&&<p role="alert">دریافت طرح معتبر ناموفق بود.</p>}
  {planState==='ready'&&plans.length===0&&<p>بدون طرح محلی معتبر نمی‌توان رویداد ایجاد کرد.</p>}
  {listState==='denied'&&<p role="alert">مجوز مستقل خواندن رویداد وجود ندارد.</p>}
  {listState==='error'&&<p role="alert">دریافت رویداد خصوصی ناموفق بود.</p>}
  {listState==='ready'&&<ul>{events.map(e=><li key={e.eventId}>{e.title} — نسخه {e.revision}
   {' '}<button type="button" onClick={()=>edit(e)}>ویرایش برنامه رویداد</button></li>)}</ul>}
  {cursor&&<button type="button" onClick={()=>void loadList(cursor)}>رویدادهای قدیمی‌تر</button>}
  <label>طرح محلی رویداد
   <select aria-label="طرح محلی رویداد" value={planId} disabled={Boolean(selected)}
    onChange={e=>{setPlanId(e.target.value);setSave('idle');}}>
    <option value="">انتخاب طرح واقعی کانون</option>
    {plans.map(p=><option key={p.draftId+':'+p.revision} value={p.draftId+':'+p.revision}>
     {p.localTitle} — نسخه {p.revision}</option>)}
    {selected&&!plans.some(p=>p.draftId+':'+p.revision===planId)&&
     <option value={planId}>طرح مبنای نسخه ثبت‌شده</option>}
   </select>
  </label>
  <label>عنوان رویداد <input aria-label="عنوان رویداد" value={title} maxLength={200}
    onChange={e=>{setTitle(e.target.value);setSave('idle');}}/></label>
  <label>هدف رویداد <textarea aria-label="هدف رویداد" value={purpose} maxLength={500}
    onChange={e=>{setPurpose(e.target.value);setSave('idle');}}/></label>
  <label>توصیف محل برگزاری <input aria-label="توصیف محل برگزاری" value={location} maxLength={200}
    onChange={e=>{setLocation(e.target.value);setSave('idle');}}/></label>
  <label>زمان برنامه‌ریزی‌شده (UTC) <input type="datetime-local" aria-label="زمان رویداد UTC"
   value={scheduled} onChange={e=>{setScheduled(e.target.value);setSave('idle');}}/></label>
  <button type="button" disabled={!canSave} onClick={()=>void append()}>ذخیره نسخه برنامه رویداد</button>
  {save==='saved'&&<p role="status">نسخه برنامه رویداد ذخیره شد؛ انتشار یا برگزاری تأیید نشده است.</p>}
  {save==='denied'&&<p role="alert">مجوز مستقل ایجاد یا اصلاح رویداد وجود ندارد.</p>}
  {save==='conflict'&&<p role="alert">نسخه تغییر کرده یا طرح مبنا معتبر نیست؛ داده را تازه کنید.</p>}
  {save==='error'&&<p role="alert">تأیید معتبر ذخیره رویداد دریافت نشد.</p>}
  {selected&&<><button type="button" onClick={()=>void loadHistory(null)}>تاریخچه نسخه‌های رویداد</button>
   {historyState==='denied'&&<p role="alert">دسترسی تاریخچه مجاز نیست.</p>}
   {historyState==='error'&&<p role="alert">تاریخچه معتبر دریافت نشد.</p>}
   {historyState==='ready'&&<ul>{history.map(h=><li key={h.revision}>
    نسخه {h.revision}: {h.title} — {h.scheduledAt} — PRIVATE_DRAFT</li>)}</ul>}
   {historyCursor&&<button type="button" onClick={()=>void loadHistory(historyCursor)}>نسخه‌های قدیمی‌تر</button>}
  </>}
 </section>;
}

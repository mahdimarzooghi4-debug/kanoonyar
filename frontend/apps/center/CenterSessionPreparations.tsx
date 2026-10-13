import React,{useEffect,useRef,useState} from 'react';

type Session={sourceId:string;classId:string;title:string;classTitle:string;scheduledAt:string|null};
type Prep={sessionId:string;classId:string;sessionTitle:string;classTitle:string;revision:number;
 title:string;teachingOutline:string;materialsNote:string;recordedAt:string;
 visibility:'PRIVATE_PREPARATION';isPublished:false;deliveryVerified:false;
 isAttendanceVerified:false;isAssessed:false};
type Revision={revision:number;title:string;teachingOutline:string;materialsNote:string;recordedAt:string;
 visibility:'PRIVATE_PREPARATION';isPublished:false;deliveryVerified:false;
 isAttendanceVerified:false;isAssessed:false};
type State='idle'|'loading'|'ready'|'denied'|'error';
type Result='idle'|'saving'|'saved'|'denied'|'conflict'|'error';
const uuid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const stamp=(x:unknown):x is string=>typeof x==='string'&&!Number.isNaN(Date.parse(x));
function privateState(v:Record<string,unknown>){
 return v.visibility==='PRIVATE_PREPARATION'&&v.isPublished===false&&
  v.deliveryVerified===false&&v.isAttendanceVerified===false&&v.isAssessed===false;
}
function session(x:unknown):x is Session{
 if(!x||typeof x!=='object')return false;const v=x as Record<string,unknown>;
 return typeof v.sourceId==='string'&&uuid.test(v.sourceId)&&
  typeof v.classId==='string'&&uuid.test(v.classId)&&
  typeof v.title==='string'&&v.title.trim().length>0&&
  typeof v.classTitle==='string'&&v.classTitle.trim().length>0&&
  (v.scheduledAt===null||stamp(v.scheduledAt));
}
function revision(x:unknown):x is Revision{
 if(!x||typeof x!=='object')return false;const v=x as Record<string,unknown>;
 return Number.isSafeInteger(v.revision)&&Number(v.revision)>0&&
  typeof v.title==='string'&&v.title.trim().length>0&&v.title.length<=160&&
  typeof v.teachingOutline==='string'&&v.teachingOutline.length<=4000&&
  typeof v.materialsNote==='string'&&v.materialsNote.length<=2000&&
  stamp(v.recordedAt)&&privateState(v);
}
function preparation(x:unknown):x is Prep{
 if(!revision(x)||!x||typeof x!=='object')return false;const v=x as Record<string,unknown>;
 return typeof v.sessionId==='string'&&uuid.test(v.sessionId)&&
  typeof v.classId==='string'&&uuid.test(v.classId)&&
  typeof v.sessionTitle==='string'&&v.sessionTitle.trim().length>0&&
  typeof v.classTitle==='string'&&v.classTitle.trim().length>0;
}
export function CenterSessionPreparations({centerId,token}:{centerId:string;token:string}){
 const epoch=useRef(0),prefix='/api/v1/centers/'+encodeURIComponent(centerId);
 const headers={Authorization:'Bearer '+token.trim()};
 const [sources,setSources]=useState<Session[]>([]),[sourceCursor,setSourceCursor]=useState<string|null>(null);
 const [sourceState,setSourceState]=useState<State>('idle');
 const [items,setItems]=useState<Prep[]>([]),[cursor,setCursor]=useState<string|null>(null);
 const [listState,setListState]=useState<State>('idle');
 const [selected,setSelected]=useState<Prep|null>(null),[sourceKey,setSourceKey]=useState('');
 const [title,setTitle]=useState(''),[outline,setOutline]=useState(''),[materials,setMaterials]=useState('');
 const [writeState,setWriteState]=useState<Result>('idle'),[history,setHistory]=useState<Revision[]>([]);
 const [historyState,setHistoryState]=useState<State>('idle'),[historyCursor,setHistoryCursor]=useState<number|null>(null);
 useEffect(()=>()=>{epoch.current++;},[]);
 function edit(x:Prep|null){
  epoch.current++;setSelected(x);setSourceKey(x?x.sessionId+':'+x.classId:'');
  setTitle(x?.title??'');setOutline(x?.teachingOutline??'');setMaterials(x?.materialsNote??'');
  setWriteState('idle');setHistory([]);setHistoryState('idle');setHistoryCursor(null);
 }
 async function loadSources(before:string|null){
  if(!centerId||!token.trim()||sourceState==='loading')return;
  const run=++epoch.current;setSourceState('loading');
  if(before===null){setSources([]);setSourceCursor(null);}
  try{
   const url=prefix+'/activity-sources/class-sessions'+
    (before?'?beforeSourceId='+encodeURIComponent(before):'');
   const resp=await fetch(url,{headers,cache:'no-store'});
   if(run!==epoch.current)return;
   if(resp.status===401||resp.status===403){setSources([]);setSourceCursor(null);setSourceState('denied');return;}
   if(!resp.ok)throw Error('Unavailable');
   const raw:unknown=await resp.json();
   if(run!==epoch.current||!raw||typeof raw!=='object')throw Error('Invalid source');
   const x=raw as Record<string,unknown>;
   if(x.sourceKind!=='CLASS_SESSION'||!Array.isArray(x.records)||x.records.length>20||
    !x.records.every(session)||!(x.nextBeforeSourceId===null||
    (typeof x.nextBeforeSourceId==='string'&&uuid.test(x.nextBeforeSourceId))))
    throw Error('Source catalogue invalid');
   const records=x.records as Session[];
   if(records.some((v,i)=>(before!==null&&v.sourceId.toLowerCase()>=before.toLowerCase())||
    (i>0&&records[i-1].sourceId.toLowerCase()<=v.sourceId.toLowerCase()))||
    (x.nextBeforeSourceId!==null&&(records.length!==20||
     records[19].sourceId.toLowerCase()!==String(x.nextBeforeSourceId).toLowerCase())))
    throw Error('Source cursor invalid');
   setSources(old=>before?[...old,...records]:records);
   setSourceCursor(x.nextBeforeSourceId as string|null);setSourceState('ready');
  }catch{if(run===epoch.current){setSources([]);setSourceCursor(null);setSourceState('error');}}
 }
 async function loadList(before:string|null){
  if(!centerId||!token.trim()||listState==='loading')return;
  const run=++epoch.current;setListState('loading');
  if(before===null){setItems([]);setCursor(null);}
  try{
   const resp=await fetch(prefix+'/learning/session-preparations'+
    (before?'?beforeSessionId='+encodeURIComponent(before):''),{headers,cache:'no-store'});
   if(run!==epoch.current)return;
   if(resp.status===401||resp.status===403){setItems([]);setCursor(null);setListState('denied');return;}
   if(!resp.ok)throw Error('Unavailable');
   const raw:unknown=await resp.json();
   if(run!==epoch.current||!raw||typeof raw!=='object')throw Error('Invalid');
   const x=raw as Record<string,unknown>;
   if(!privateState(x)||!Array.isArray(x.preparations)||x.preparations.length>20||
    !x.preparations.every(preparation)||!(x.nextBeforeSessionId===null||
     (typeof x.nextBeforeSessionId==='string'&&uuid.test(x.nextBeforeSessionId))))
     throw Error('Forged preparation list');
   const records=x.preparations as Prep[];
   if(records.some((v,i)=>(before!==null&&v.sessionId.toLowerCase()>=before.toLowerCase())||
    (i>0&&records[i-1].sessionId.toLowerCase()<=v.sessionId.toLowerCase()))||
    (x.nextBeforeSessionId!==null&&(records.length!==20||
     records[19].sessionId.toLowerCase()!==String(x.nextBeforeSessionId).toLowerCase())))
    throw Error('Preparation cursor invalid');
   setItems(old=>before?[...old,...records]:records);
   setCursor(x.nextBeforeSessionId as string|null);setListState('ready');
  }catch{if(run===epoch.current){setItems([]);setCursor(null);setListState('error');}}
 }
 async function save(){
  if(!centerId||!token.trim()||writeState==='saving'||!title.trim()||
   title.length>160||outline.length>4000||materials.length>2000)return;
  const choice=selected?{sourceId:selected.sessionId,classId:selected.classId}:
   sources.find(x=>x.sourceId+':'+x.classId===sourceKey);
  if(!choice)return;
  const run=++epoch.current;setWriteState('saving');
  const expectedRevision=selected?.revision??0;
  try{
   const resp=await fetch(prefix+'/learning/session-preparations/revisions',{
    method:'POST',headers:{...headers,'Content-Type':'application/json'},
    body:JSON.stringify({sessionId:choice.sourceId,classId:choice.classId,expectedRevision,
     title:title.trim(),teachingOutline:outline,materialsNote:materials})});
   if(run!==epoch.current)return;
   if(resp.status===401||resp.status===403){setWriteState('denied');return;}
   if(resp.status===409){setWriteState('conflict');return;}
   if(!resp.ok)throw Error('Unavailable');
   const raw:unknown=await resp.json();
   if(run!==epoch.current||!raw||typeof raw!=='object')throw Error('Invalid');
   const x=raw as Record<string,unknown>;
   if(x.sessionId!==choice.sourceId||x.classId!==choice.classId||
    x.revision!==expectedRevision+1||!privateState(x))throw Error('Forged save');
   edit(null);setWriteState('saved');setItems([]);setCursor(null);setListState('idle');
  }catch{if(run===epoch.current)setWriteState('error');}
 }
 async function loadHistory(before:number|null){
  if(!selected||historyState==='loading')return;
  const entry=selected,run=++epoch.current;setHistoryState('loading');
  if(before===null){setHistory([]);setHistoryCursor(null);}
  try{
   const url=prefix+'/learning/session-preparations/'+encodeURIComponent(entry.sessionId)+
    '/revisions'+(before===null?'':'?beforeRevision='+before);
   const resp=await fetch(url,{headers,cache:'no-store'});
   if(run!==epoch.current)return;
   if(resp.status===401||resp.status===403){setHistory([]);setHistoryState('denied');return;}
   if(!resp.ok)throw Error('Unavailable');
   const raw:unknown=await resp.json();
   if(run!==epoch.current||!raw||typeof raw!=='object')throw Error('Invalid');
   const x=raw as Record<string,unknown>;
   if(!privateState(x)||x.sessionId!==entry.sessionId||x.classId!==entry.classId||
    !Array.isArray(x.revisions)||x.revisions.length>20||!x.revisions.every(revision)||
    !(x.nextBeforeRevision===null||(Number.isSafeInteger(x.nextBeforeRevision)&&
      Number(x.nextBeforeRevision)>0)))throw Error('Forged history');
   const records=x.revisions as Revision[];
   if(records.some((v,i)=>(before!==null&&v.revision>=before)||
    (i>0&&records[i-1].revision<=v.revision))||
    (x.nextBeforeRevision!==null&&(records.length!==20||
     records[19].revision!==x.nextBeforeRevision)))throw Error('History order invalid');
   setHistory(old=>before===null?records:[...old,...records]);
   setHistoryCursor(x.nextBeforeRevision as number|null);setHistoryState('ready');
  }catch{if(run===epoch.current){setHistory([]);setHistoryCursor(null);setHistoryState('error');}}
 }
 return <section aria-label="دفتر آماده‌سازی خصوصی جلسات" style={{border:'1px solid #aaa',padding:12,marginTop:16}}>
  <h3>دفتر آماده‌سازی جلسات کلاس</h3>
  <p>طرح تدریس برای استفاده خصوصی مربیان؛ نه انتشار درس، نمره، حضور یا تأیید برگزاری.</p>
  <button type="button" disabled={!centerId||!token.trim()||sourceState==='loading'}
   onClick={()=>void loadSources(null)}>جلسه‌های واقعی کانون</button>{' '}
  <button type="button" disabled={!centerId||!token.trim()||listState==='loading'}
   onClick={()=>void loadList(null)}>دریافت دفترهای آماده‌سازی</button>
  {sourceState==='denied'&&<p role="alert">مجوز مستقل مشاهده جلسات وجود ندارد.</p>}
  {sourceState==='error'&&<p role="alert">فهرست معتبر جلسات دریافت نشد.</p>}
  {listState==='denied'&&<p role="alert">مجوز مستقل مشاهده دفتر آماده‌سازی وجود ندارد.</p>}
  {listState==='error'&&<p role="alert">دفتر آماده‌سازی معتبر دریافت نشد.</p>}
  {sourceCursor&&<button type="button" onClick={()=>void loadSources(sourceCursor)}>جلسات بیشتر</button>}
  {listState==='ready'&&<ul>{items.map(x=><li key={x.sessionId}>
   <strong>{x.title}</strong> — {x.classTitle} / {x.sessionTitle} — نسخه {x.revision}
   <button type="button" onClick={()=>edit(x)}>ویرایش طرح خصوصی جلسه</button>
  </li>)}</ul>}
  {cursor&&<button type="button" onClick={()=>void loadList(cursor)}>آماده‌سازی‌های بیشتر</button>}
  {selected&&<button type="button" onClick={()=>edit(null)}>شروع آماده‌سازی جدید</button>}
  {!selected&&<label>جلسه مبنا <select aria-label="انتخاب جلسه آموزشی" value={sourceKey}
   onChange={e=>{setSourceKey(e.target.value);setWriteState('idle');}}>
   <option value="">جلسه معتبر را انتخاب کنید</option>
   {sources.map(x=><option key={x.sourceId} value={x.sourceId+':'+x.classId}>
    {x.classTitle} / {x.title}</option>)}
  </select></label>}
  {selected&&<p>جلسه: {selected.sessionTitle}؛ شناسه منبع تغییر نمی‌کند.</p>}
  <label>عنوان طرح تدریس <input aria-label="عنوان آماده‌سازی جلسه" maxLength={160} value={title}
   onChange={e=>setTitle(e.target.value)}/></label>
  <label>طرح تدریس <textarea aria-label="متن طرح تدریس" maxLength={4000} value={outline}
   onChange={e=>setOutline(e.target.value)}/></label>
  <label>یادداشت ابزار و منابع <textarea aria-label="یادداشت منابع تدریس" maxLength={2000}
   value={materials} onChange={e=>setMaterials(e.target.value)}/></label>
  <button type="button" disabled={!title.trim()||writeState==='saving'||(!selected&&!sourceKey)}
   onClick={()=>void save()}>ذخیره نسخه خصوصی آماده‌سازی</button>
  {writeState==='saved'&&<p role="status">آماده‌سازی خصوصی ذخیره شد؛ درس منتشر یا اجرا نشده است.</p>}
  {writeState==='conflict'&&<p role="alert">نسخه یا جلسه منبع معتبر نیست؛ داده را تازه کنید.</p>}
  {writeState==='denied'&&<p role="alert">مجوز مستقل ثبت آماده‌سازی وجود ندارد.</p>}
  {writeState==='error'&&<p role="alert">تأیید معتبر ذخیره آماده‌سازی دریافت نشد.</p>}
  {selected&&<><button type="button" onClick={()=>void loadHistory(null)}>تاریخچه آماده‌سازی جلسه</button>
   {historyState==='denied'&&<p role="alert">مجوز خواندن تاریخچه وجود ندارد.</p>}
   {historyState==='error'&&<p role="alert">تاریخچه معتبر دریافت نشد.</p>}
   {historyState==='ready'&&<ul>{history.map(x=><li key={x.revision}>
    نسخه {x.revision} — {x.title} — PRIVATE_PREPARATION
   </li>)}</ul>}
   {historyCursor&&<button type="button" onClick={()=>void loadHistory(historyCursor)}>
    نسخه‌های قدیمی‌تر</button>}
  </>}
 </section>;
}

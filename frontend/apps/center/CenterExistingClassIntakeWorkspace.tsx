import React,{useEffect,useRef,useState} from 'react';
import { ClassCapacityIntake } from './ClassCapacityIntake';
import { CenterClassSessionPlanner } from './CenterClassSessionPlanner';

type SourceSession={sourceId:string;classId:string;title:string;classTitle:string;scheduledAt:string|null};
type Stage='idle'|'loading'|'ready'|'denied'|'error';
const uuid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const id=(x:unknown):x is string=>typeof x==='string'&&uuid.test(x);
const date=(x:unknown):x is string=>typeof x==='string'&&!Number.isNaN(Date.parse(x));
function valid(x:unknown):x is SourceSession{
 if(!x||typeof x!=='object')return false;const v=x as Record<string,unknown>;
 return id(v.sourceId)&&id(v.classId)&&
  typeof v.title==='string'&&v.title.trim().length>0&&v.title.length<=512&&
  typeof v.classTitle==='string'&&v.classTitle.trim().length>0&&v.classTitle.length<=512&&
  (v.scheduledAt===null||date(v.scheduledAt));
}
/** Existing center-owned classes discoverable from original S12 sessions.
 * No standalone class directory claim: classes without a session are not listed here. */
export function CenterExistingClassIntakeWorkspace({centerId,token}:{centerId:string;token:string}){
 const generation=useRef(0);
 const [sessions,setSessions]=useState<SourceSession[]>([]);
 const [cursor,setCursor]=useState<string|null>(null);
 const [status,setStatus]=useState<Stage>('idle');
 const [opened,setOpened]=useState<string|null>(null);
 const [openedScheduler,setOpenedScheduler]=useState<string|null>(null);
 const prefix='/api/v1/centers/'+encodeURIComponent(centerId);
 useEffect(()=>()=>{generation.current++;},[]);
 async function load(before:string|null){
  if(!centerId||!token.trim()||status==='loading')return;
  const current=++generation.current;
  setStatus('loading');setOpened(null);setOpenedScheduler(null);
  if(before===null){setSessions([]);setCursor(null);}
  try{
   const response=await fetch(prefix+'/activity-sources/class-sessions'+
    (before===null?'':'?beforeSourceId='+encodeURIComponent(before)),
    {headers:{Authorization:'Bearer '+token.trim()},cache:'no-store'});
   if(current!==generation.current)return;
   if(response.status===401||response.status===403){
    setSessions([]);setCursor(null);setStatus('denied');return;
   }
   if(!response.ok)throw Error('Source unavailable');
   const raw:unknown=await response.json();
   if(current!==generation.current||!raw||typeof raw!=='object')throw Error('Invalid');
   const v=raw as Record<string,unknown>;
   if(v.sourceKind!=='CLASS_SESSION'||!Array.isArray(v.records)||v.records.length>20||
    !v.records.every(valid)||
    !(v.nextBeforeSourceId===null||id(v.nextBeforeSourceId)))throw Error('Forged source');
   const rows=v.records as SourceSession[];
   if(rows.some((s,i)=>(before!==null&&s.sourceId.toLowerCase()>=before.toLowerCase())||
    (i>0&&rows[i-1].sourceId.toLowerCase()<=s.sourceId.toLowerCase()))||
    (v.nextBeforeSourceId!==null&&(rows.length!==20||
     rows[19].sourceId.toLowerCase()!==String(v.nextBeforeSourceId).toLowerCase())))
    throw Error('Untrusted source cursor');
   const combined=before===null?rows:[...sessions,...rows];
   const seen=new Set<string>(),titles=new Map<string,string>();
   for(const s of combined){
    const sourceKey=s.sourceId.toLowerCase(),classKey=s.classId.toLowerCase();
    if(seen.has(sourceKey))throw Error('Duplicate session');
    seen.add(sourceKey);
    const prev=titles.get(classKey);
    if(prev!==undefined&&prev!==s.classTitle)throw Error('Conflicting class owner');
    titles.set(classKey,s.classTitle);
   }
   setSessions(combined);setCursor(v.nextBeforeSourceId as string|null);
   setStatus('ready');
  }catch{if(current===generation.current){
   setSessions([]);setCursor(null);setStatus('error');setOpened(null);
  }}
 }
 const classes=Array.from(new Map(sessions.map(s=>[s.classId.toLowerCase(),
  {classId:s.classId,title:s.classTitle}])).values());
 return <section aria-label="مدیریت درخواست کلاس‌های موجود"
  style={{border:'1px solid #aaa',padding:12,marginTop:12}}>
  <h3>مدیریت ظرفیت و درخواست کلاس‌های موجود</h3>
  <p>کلاس‌ها تنها از جلسات واقعی ثبت‌شده همان کانون کشف می‌شوند.
   نبودن کلاس در این فهرست به معنی نبود آن در سامانه نیست.
   درخواست‌ها فقط REQUESTED هستند؛ صندلی رزرو یا رضایت قانونی تأیید نمی‌شود.</p>
  <button type="button" disabled={status==='loading'||!token.trim()} onClick={()=>void load(null)}>
   دریافت کلاس‌ها از جلسات واقعی</button>
  {status==='denied'&&<p role="alert">مجوز مستقل کاتالوگ کلاس و جلسه وجود ندارد.</p>}
  {status==='error'&&<p role="alert">کاتالوگ معتبر کلاس‌های موجود دریافت نشد.</p>}
  {status==='ready'&&classes.length===0&&<p>در این بخش، جلسه‌ای برای شناسایی کلاس ثبت نشده است.</p>}
  {status==='ready'&&<ul>{classes.map(c=><li key={c.classId}>
   <strong>{c.title}</strong> — کلاس ثبت‌شده در منبع اصلی
   <button type="button" onClick={()=>setOpenedScheduler(x=>x===c.classId?null:c.classId)}>
    {openedScheduler===c.classId?'بستن برنامه‌ریزی جلسه':'برنامه‌ریزی جلسه جدید برای همین کلاس'}</button>
   {openedScheduler===c.classId&&<CenterClassSessionPlanner
    key={'existing-session:'+centerId+':'+token+':'+c.classId}
    centerId={centerId} token={token} existingClass={{classId:c.classId,title:c.title}}/>}
   <button type="button" onClick={()=>setOpened(x=>x===c.classId?null:c.classId)}>
    {opened===c.classId?'بستن مدیریت ثبت‌نام اولیه':'مدیریت ظرفیت و درخواست‌های همین کلاس'}</button>
   {opened===c.classId&&<ClassCapacityIntake key={centerId+':'+token+':'+c.classId}
    centerId={centerId} token={token} classId={c.classId}
    onClose={()=>setOpened(null)}/>}
  </li>)}</ul>}
  {status==='ready'&&cursor!==null&&<button type="button" onClick={()=>void load(cursor)}>
   جلسات بعدی برای یافتن کلاس‌های بیشتر</button>}
 </section>;
}

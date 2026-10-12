import React, { useEffect, useRef, useState } from 'react';

type Kind='CLASS_SESSION'|'CENTER_EVENT';
type Source={sourceId:string;title:string;classId:string|null;classTitle:string|null;scheduledAt:string|null};
type Status='idle'|'loading'|'ready'|'denied'|'error';
type Save='idle'|'saving'|'saved'|'denied'|'conflict'|'error';
const uuid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
function validSource(v:unknown,k:Kind):v is Source {
 if(!v||typeof v!=='object')return false;
 const s=v as Record<string,unknown>;
 return typeof s.sourceId==='string'&&uuid.test(s.sourceId)&&
   typeof s.title==='string'&&s.title.trim().length>0&&
   (k==='CLASS_SESSION'?
     (typeof s.classId==='string'&&uuid.test(s.classId)&&typeof s.classTitle==='string'):
     (s.classId===null&&s.classTitle===null))&&
   (s.scheduledAt===null||(typeof s.scheduledAt==='string'&&
      !Number.isNaN(Date.parse(s.scheduledAt))));
}
export function CenterActivityReportComposer({token,centerId,draftId,linkId}:{
 token:string;centerId:string;draftId:string;linkId:string
}){
 const epoch=useRef(0),requestId=useRef(crypto.randomUUID());
 const [kind,setKind]=useState<Kind>('CLASS_SESSION');
 const [sources,setSources]=useState<Source[]>([]);
 const [cursor,setCursor]=useState<string|null>(null);
 const [sourceId,setSourceId]=useState('');
 const [status,setStatus]=useState<Status>('idle');
 const [save,setSave]=useState<Save>('idle');
 const [summary,setSummary]=useState('');
 const [evidence,setEvidence]=useState('');
 useEffect(()=>{
   epoch.current++;requestId.current=crypto.randomUUID();
   setKind('CLASS_SESSION');setSources([]);setCursor(null);setSourceId('');
   setStatus('idle');setSave('idle');setSummary('');setEvidence('');
   return()=>{epoch.current++};
 },[token,centerId,draftId,linkId]);
 function changeKind(next:Kind){
   epoch.current++;setKind(next);setSources([]);setCursor(null);
   setSourceId('');setStatus('idle');setSave('idle');requestId.current=crypto.randomUUID();
 }
 async function load(before:string|null){
   if(!token.trim()||!centerId||status==='loading')return;
   const current=++epoch.current;setStatus('loading');setSave('idle');
   setSourceId('');
   if(before===null){setSources([]);setCursor(null);}
   try{
     const type=kind==='CLASS_SESSION'?'class-sessions':'events';
     const uri='/api/v1/centers/'+encodeURIComponent(centerId)+
       '/activity-sources/'+type+(before===null?'':'?beforeSourceId='+encodeURIComponent(before));
     const response=await fetch(uri,{headers:{Authorization:'Bearer '+token.trim()},cache:'no-store'});
     if(current!==epoch.current)return;
     if(response.status===401||response.status===403){
       setSources([]);setCursor(null);setStatus('denied');return;
     }
     if(!response.ok)throw Error('Unavailable');
     const raw:unknown=await response.json();
     if(current!==epoch.current||!raw||typeof raw!=='object')throw Error('Malformed');
     const data=raw as Record<string,unknown>;
     if(data.sourceKind!==kind||!Array.isArray(data.records)||data.records.length>20||
       !data.records.every(v=>validSource(v,kind))||
       !(data.nextBeforeSourceId===null||
         (typeof data.nextBeforeSourceId==='string'&&uuid.test(data.nextBeforeSourceId))))
       throw Error('Invalid sources');
     const records=data.records as Source[];
     if(records.some((r,i)=>
       (before!==null&&r.sourceId.toLowerCase()>=before.toLowerCase())||
       (i>0&&records[i-1].sourceId.toLowerCase()<=r.sourceId.toLowerCase()))||
       (data.nextBeforeSourceId!==null&&
         (records.length!==20||records[19].sourceId.toLowerCase()!==String(data.nextBeforeSourceId).toLowerCase())))
       throw Error('Invalid pagination');
     setSources(old=>before===null?records:[...old,...records]);
     setCursor(data.nextBeforeSourceId as string|null);setStatus('ready');
   }catch{if(current===epoch.current){setSources([]);setCursor(null);setStatus('error');}}
 }
 async function submit(){
   if(!centerId||!token.trim()||!uuid.test(sourceId)||!summary.trim()||!evidence.trim()||
     summary.length>2048||evidence.length>512||save==='saving'||save==='saved')return;
   const current=++epoch.current;setSave('saving');
   const reportId=requestId.current;
   try{
     const response=await fetch('/api/v1/centers/'+encodeURIComponent(centerId)+
       '/activity-reports/sourced',{method:'POST',headers:{
         Authorization:'Bearer '+token.trim(),'Content-Type':'application/json'},
       body:JSON.stringify({reportId,draftId,linkId,sourceKind:kind,sourceId,
         evidenceRef:evidence.trim(),reportedSummary:summary.trim(),claimedOccurredAt:null})});
     if(current!==epoch.current)return;
     if(response.status===401||response.status===403){setSave('denied');return;}
     if(response.status===409){setSave('conflict');return;}
     if(!response.ok)throw Error('Unavailable');
     const raw:unknown=await response.json();
     if(current!==epoch.current||!raw||typeof raw!=='object')throw Error('Invalid response');
     const d=raw as Record<string,unknown>;
     if(d.reportId!==reportId||d.draftId!==draftId||d.linkId!==linkId||
       d.sourceKind!==kind||d.sourceId!==sourceId||d.verificationStatus!=='UNVERIFIED')
       throw Error('Forged acknowledgment');
     setSave('saved');
   }catch{if(current===epoch.current)setSave('error');}
 }
 return <section aria-label="ثبت گزارش فعالیت متصل به منبع" style={{border:'1px solid #aaa',padding:'0.75rem'}}>
   <h4>ثبت گزارش فعالیتِ متصل به طرح بومی</h4>
   <p>فقط گزارش راستی‌آزمایی‌نشده؛ این ثبت، برگزاری، کیفیت یا تأیید مدرک نیست.</p>
   <label>نوع منبع <select aria-label="نوع منبع گزارش" value={kind}
     onChange={e=>changeKind(e.target.value as Kind)}>
     <option value="CLASS_SESSION">جلسه کلاس</option>
     <option value="CENTER_EVENT">رویداد</option>
   </select></label>{' '}
   <button type="button" disabled={!centerId||status==='loading'}
     onClick={()=>void load(null)}>دریافت منابع برای گزارش فعالیت</button>
   {status==='loading'&&<p role="status">در حال دریافت منابع…</p>}
   {status==='denied'&&<p role="alert">مجوز مستقل مشاهده منابع تأیید نشده است.</p>}
   {status==='error'&&<p role="alert">منابع قابل دریافت نیستند.</p>}
   {status==='ready'&&<>
     <label>انتخاب منبع <select aria-label="منبع گزارش فعالیت" value={sourceId}
       onChange={e=>{epoch.current++;setSourceId(e.target.value);setSave('idle');requestId.current=crypto.randomUUID();}}>
       <option value="">انتخاب کنید</option>
       {sources.map(x=><option key={x.sourceId} value={x.sourceId}>{x.title}</option>)}
     </select></label>
     {sources.length===0&&<p>منبع مجاز در این صفحه وجود ندارد؛ فعالیت فرض نمی‌شود.</p>}
     {cursor&&<button type="button" onClick={()=>void load(cursor)}>منابع قدیمی‌تر گزارش</button>}
     <div>
       <label>شرح گزارش <input aria-label="شرح گزارش فعالیت" value={summary}
         disabled={save==='saved'} onChange={e=>{setSummary(e.target.value);setSave('idle');}}/></label>
       <label>ارجاع شواهد (تأییدنشده) <input aria-label="ارجاع شواهد گزارش" value={evidence}
         disabled={save==='saved'} onChange={e=>{setEvidence(e.target.value);setSave('idle');}}/></label>
       <button type="button" disabled={!uuid.test(sourceId)||!summary.trim()||!evidence.trim()||
         save==='saving'||save==='saved'} onClick={()=>void submit()}>
         ثبت گزارش فعالیت تأییدنشده</button>
     </div>
   </>}
   {save==='saving'&&<p role="status">در حال ثبت ادعا…</p>}
   {save==='saved'&&<p role="status">گزارش فعالیت ثبت شد؛ همچنان راستی‌آزمایی‌نشده است.</p>}
   {save==='denied'&&<p role="alert">مجوز مستقل ثبت گزارش فعالیت تأیید نشده است.</p>}
   {save==='conflict'&&<p role="alert">گزارش تکراری یا منبع/طرح نامعتبر است؛ سوابق سرور را بررسی کنید.</p>}
   {save==='error'&&<p role="alert">ثبت گزارش فعالیت تأیید نشد.</p>}
 </section>;
}

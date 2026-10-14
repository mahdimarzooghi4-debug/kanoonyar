import React,{useEffect,useRef,useState} from 'react';
type Entry={entryId:string;entryKind:'REQUEST'|'RESPONSE';requestId:string|null;
 content:string;actorSubject:string;recordedAt:string;verificationStatus:'UNVERIFIED';
 isApproval:false;isExecuted:false;isResolved:false};
type Mode='idle'|'loading'|'ready'|'denied'|'error';
type Save='idle'|'saving'|'saved'|'denied'|'conflict'|'error';
const uuid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
function validEntry(raw:unknown,kind:'REQUEST'|'RESPONSE',request:string|null):raw is Entry{
 if(!raw||typeof raw!=='object')return false;
 const x=raw as Record<string,unknown>;
 return typeof x.entryId==='string'&&uuid.test(x.entryId)&&x.entryKind===kind&&
  x.requestId===request&&typeof x.content==='string'&&x.content.trim().length>0&&x.content.length<=1000&&
  typeof x.actorSubject==='string'&&x.actorSubject.trim().length>0&&
  typeof x.recordedAt==='string'&&!Number.isNaN(Date.parse(x.recordedAt))&&
  x.verificationStatus==='UNVERIFIED'&&x.isApproval===false&&
  x.isExecuted===false&&x.isResolved===false;
}
export function CenterCampaignFollowup({centerId,token,campaignId,associationId,noteId}:{
 centerId:string;token:string;campaignId:string;associationId:string;noteId:string}){
 const epoch=useRef(0);
 const [status,setStatus]=useState<Mode>('idle'),[save,setSave]=useState<Save>('idle');
 const [entries,setEntries]=useState<Entry[]>([]),[cursor,setCursor]=useState<string|null>(null);
 const [kind,setKind]=useState<'REQUEST'|'RESPONSE'>('REQUEST');
 const [selectedRequest,setSelectedRequest]=useState('');
 const [content,setContent]=useState('');
 const base='/api/v1/centers/'+encodeURIComponent(centerId)+'/campaigns/'+encodeURIComponent(campaignId)+
 '/report-links/'+encodeURIComponent(associationId)+'/inspection-notes/'+encodeURIComponent(noteId)+'/followups';
 const headers={Authorization:'Bearer '+token.trim()};
 useEffect(()=>{
  epoch.current++;setStatus('idle');setSave('idle');setEntries([]);setCursor(null);
  setKind('REQUEST');setSelectedRequest('');setContent('');
  return()=>{epoch.current++};
 },[centerId,token,campaignId,associationId,noteId]);
 function choose(k:'REQUEST'|'RESPONSE',request:string){
  epoch.current++;setKind(k);setSelectedRequest(request);setEntries([]);
  setCursor(null);setStatus('idle');setSave('idle');setContent('');
 }
 async function load(before:string|null,k=kind,req=selectedRequest){
  if(status==='loading'||(k==='RESPONSE'&&!uuid.test(req)))return;
  const id=++epoch.current;setStatus('loading');
  if(before===null){setEntries([]);setCursor(null);}
  const params=new URLSearchParams({entryKind:k});
  if(k==='RESPONSE')params.set('requestId',req);
  if(before!==null)params.set('beforeEntryId',before);
  try{
   const r=await fetch(base+'?'+params.toString(),{headers,cache:'no-store'});
   if(id!==epoch.current)return;
   if(r.status===401||r.status===403){setEntries([]);setStatus('denied');return;}
   if(!r.ok)throw Error('Unavailable');
   const data:unknown=await r.json();
   if(id!==epoch.current||!data||typeof data!=='object')throw Error('Invalid');
   const x=data as Record<string,unknown>,request=k==='RESPONSE'?req:null;
   if(x.campaignId!==campaignId||x.associationId!==associationId||x.noteId!==noteId||
    x.entryKind!==k||x.requestId!==request||x.verificationStatus!=='UNVERIFIED'||
    x.isApproval!==false||x.isExecuted!==false||x.isResolved!==false||
    !Array.isArray(x.entries)||x.entries.length>20||
    !x.entries.every(v=>validEntry(v,k,request))||
    !(x.nextBeforeEntryId===null||
     (typeof x.nextBeforeEntryId==='string'&&uuid.test(x.nextBeforeEntryId))))throw Error('Forged followup');
   const rows=x.entries as Entry[];
   if(rows.some((v,i)=>(before!==null&&v.entryId.toLowerCase()>=before.toLowerCase())||
    (i>0&&rows[i-1].entryId.toLowerCase()<=v.entryId.toLowerCase())))throw Error('Bad order');
   if(x.nextBeforeEntryId!==null&&(rows.length!==20||
    rows[19].entryId.toLowerCase()!==String(x.nextBeforeEntryId).toLowerCase()))
    throw Error('Bad cursor');
   setEntries(old=>before===null?rows:[...old,...rows]);
   setCursor(x.nextBeforeEntryId as string|null);setStatus('ready');
  }catch{if(id===epoch.current){setEntries([]);setCursor(null);setStatus('error');}}
 }
 async function append(){
  if(!content.trim()||content.length>1000||save==='saving'||save==='saved'||
    (kind==='RESPONSE'&&!uuid.test(selectedRequest)))return;
  const id=++epoch.current,entryId=crypto.randomUUID(),req=kind==='RESPONSE'?selectedRequest:null;
  setSave('saving');
  try{
   const r=await fetch(base,{method:'POST',headers:{...headers,'Content-Type':'application/json'},
     body:JSON.stringify({entryId,entryKind:kind,requestId:req,content:content.trim()})});
   if(id!==epoch.current)return;
   if(r.status===401||r.status===403){setSave('denied');return;}
   if(r.status===409){setSave('conflict');return;}
   if(!r.ok)throw Error('Unavailable');
   const data:unknown=await r.json();
   if(id!==epoch.current||!data||typeof data!=='object')throw Error('Invalid');
   const x=data as Record<string,unknown>;
   if(x.campaignId!==campaignId||x.associationId!==associationId||x.noteId!==noteId||
    x.entryId!==entryId||x.entryKind!==kind||x.requestId!==req||
    x.verificationStatus!=='UNVERIFIED'||x.isApproval!==false||
    x.isExecuted!==false||x.isResolved!==false)throw Error('Forged decision');
   setSave('saved');setEntries([]);setCursor(null);setStatus('idle');setContent('');
  }catch{if(id===epoch.current)setSave('error');}
 }
 return <section aria-label="پیگیری توضیح گزارش" style={{padding:8,marginTop:8,border:'1px dashed #bbb'}}>
  <h5>پیگیری توضیح درباره یادداشت</h5>
  <p>درخواست و پاسخ فقط مکاتبه‌اند؛ پاسخ دادن به معنی رفع ابهام، تأیید یا خاتمه پیگیری نیست.</p>
  <button type="button" onClick={()=>choose('REQUEST','')}>درخواست‌های توضیح</button>
  {kind==='REQUEST'&&<button type="button" disabled={status==='loading'} onClick={()=>void load(null)}>
    دریافت درخواست‌های توضیح</button>}
  {kind==='RESPONSE'&&<button type="button" onClick={()=>choose('REQUEST','')}>بازگشت به درخواست‌ها</button>}
  {kind==='RESPONSE'&&<button type="button" disabled={status==='loading'} onClick={()=>void load(null)}>
    مشاهده پاسخ‌های درخواست انتخاب‌شده</button>}
  {status==='denied'&&<p role="alert">مجوز مستقل پیگیری یا خواندن گزارش وجود ندارد.</p>}
  {status==='error'&&<p role="alert">اطلاعات پیگیری معتبر دریافت نشد.</p>}
  {status==='ready'&&entries.length===0&&<p>رکوردی ثبت نشده؛ نتیجه نامعلوم است.</p>}
  {status==='ready'&&<ul>{entries.map(e=><li key={e.entryId}>
   {e.content} — {e.actorSubject} <strong>UNVERIFIED — بدون تصمیم</strong>
   {kind==='REQUEST'&&<button type="button" onClick={()=>choose('RESPONSE',e.entryId)}>
     پاسخ به این درخواست</button>}
  </li>)}</ul>}
  {cursor!==null&&<button type="button" onClick={()=>void load(cursor)}>موارد قدیمی‌تر</button>}
  <label>{kind==='REQUEST'?'متن درخواست توضیح':'متن پاسخ به درخواست'}
   <textarea aria-label="متن پیگیری" value={content} maxLength={1000}
    onChange={e=>{setContent(e.target.value);setSave('idle');}}/>
  </label>
  <button type="button" disabled={!content.trim()||save==='saving'||save==='saved'||
    (kind==='RESPONSE'&&!uuid.test(selectedRequest))} onClick={()=>void append()}>
    {kind==='REQUEST'?'ثبت درخواست توضیح':'ثبت پاسخ بدون تأیید'}
  </button>
  {save==='saved'&&<p role="status">پیگیری ثبت شد؛ گزارش همچنان UNVERIFIED و بدون تأیید است.</p>}
  {save==='denied'&&<p role="alert">مجوز مستقل ثبت پیگیری و مشاهده منبع وجود ندارد.</p>}
  {save==='conflict'&&<p role="alert">ارجاع درخواست/پاسخ معتبر نیست یا شناسه تکراری است.</p>}
  {save==='error'&&<p role="alert">ثبت پیگیری معتبر تأیید نشد.</p>}
 </section>;
}

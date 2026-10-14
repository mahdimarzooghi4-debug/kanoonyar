import React,{useEffect,useRef,useState} from 'react';
import { CenterCampaignFollowup } from './CenterCampaignFollowup';
type Note={noteId:string;noteText:string;notedBySubject:string;notedAt:string;
 verificationStatus:'UNVERIFIED';isApproval:false;isExecuted:false};
type Mode='idle'|'loading'|'ready'|'denied'|'error';
type Save='idle'|'saving'|'saved'|'denied'|'conflict'|'error';
const uuid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
function validNote(raw:unknown):raw is Note{
 if(!raw||typeof raw!=='object')return false;
 const x=raw as Record<string,unknown>;
 return typeof x.noteId==='string'&&uuid.test(x.noteId)&&
 typeof x.noteText==='string'&&x.noteText.trim().length>0&&x.noteText.length<=1000&&
 typeof x.notedBySubject==='string'&&x.notedBySubject.trim().length>0&&
 typeof x.notedAt==='string'&&!Number.isNaN(Date.parse(x.notedAt))&&
 x.verificationStatus==='UNVERIFIED'&&x.isApproval===false&&x.isExecuted===false;
}
export function CenterCampaignInspectionNotes({centerId,token,campaignId,associationId}:{
 centerId:string;token:string;campaignId:string;associationId:string}){
 const epoch=useRef(0);
 const [status,setStatus]=useState<Mode>('idle');
 const [save,setSave]=useState<Save>('idle');
 const [notes,setNotes]=useState<Note[]>([]);
 const [cursor,setCursor]=useState<string|null>(null);
 const [noteText,setNoteText]=useState('');
 const base='/api/v1/centers/'+encodeURIComponent(centerId)+
  '/campaigns/'+encodeURIComponent(campaignId)+
  '/report-links/'+encodeURIComponent(associationId)+'/inspection-notes';
 const headers={Authorization:'Bearer '+token.trim()};
 useEffect(()=>{
  epoch.current++;setStatus('idle');setSave('idle');setNotes([]);setCursor(null);setNoteText('');
  return()=>{epoch.current++};
 },[centerId,token,campaignId,associationId]);
 async function load(before:string|null){
  if(!centerId||!token.trim()||status==='loading')return;
  const id=++epoch.current;setStatus('loading');
  if(before===null){setNotes([]);setCursor(null);}
  try{
   const r=await fetch(base+(before===null?'':'?beforeNoteId='+encodeURIComponent(before)),
    {headers,cache:'no-store'});
   if(id!==epoch.current)return;
   if(r.status===401||r.status===403){setNotes([]);setStatus('denied');return;}
   if(!r.ok)throw Error('Unavailable');
   const value:unknown=await r.json();
   if(id!==epoch.current||!value||typeof value!=='object')throw Error('Invalid');
   const x=value as Record<string,unknown>;
   if(x.campaignId!==campaignId||x.associationId!==associationId||
    x.verificationStatus!=='UNVERIFIED'||x.isApproval!==false||x.isExecuted!==false||
    !Array.isArray(x.notes)||x.notes.length>20||!x.notes.every(validNote)||
    !(x.nextBeforeNoteId===null||
     (typeof x.nextBeforeNoteId==='string'&&uuid.test(x.nextBeforeNoteId))))
    throw Error('Forged inspection state');
   const rows=x.notes as Note[];
   if(rows.some((v,i)=>(before!==null&&v.noteId.toLowerCase()>=before.toLowerCase())||
    (i>0&&rows[i-1].noteId.toLowerCase()<=v.noteId.toLowerCase())))
    throw Error('Invalid paging order');
   if(x.nextBeforeNoteId!==null&&(rows.length!==20||
    rows[19].noteId.toLowerCase()!==String(x.nextBeforeNoteId).toLowerCase()))
    throw Error('Invalid paging cursor');
   setNotes(old=>before===null?rows:[...old,...rows]);
   setCursor(x.nextBeforeNoteId as string|null);setStatus('ready');
  }catch{if(id===epoch.current){setNotes([]);setCursor(null);setStatus('error');}}
 }
 async function append(){
  if(!noteText.trim()||noteText.length>1000||save==='saving'||save==='saved')return;
  const id=++epoch.current,noteId=crypto.randomUUID();
  setSave('saving');
  try{
   const r=await fetch(base,{method:'POST',
    headers:{...headers,'Content-Type':'application/json'},
    body:JSON.stringify({noteId,noteText:noteText.trim()})});
   if(id!==epoch.current)return;
   if(r.status===401||r.status===403){setSave('denied');return;}
   if(r.status===409){setSave('conflict');return;}
   if(!r.ok)throw Error('Unavailable');
   const value:unknown=await r.json();
   if(id!==epoch.current||!value||typeof value!=='object')throw Error('Invalid');
   const x=value as Record<string,unknown>;
   if(x.noteId!==noteId||x.campaignId!==campaignId||x.associationId!==associationId||
    x.verificationStatus!=='UNVERIFIED'||x.isApproval!==false||x.isExecuted!==false)
    throw Error('Forged review decision');
   setSave('saved');setStatus('idle');setNotes([]);setCursor(null);setNoteText('');
  }catch{if(id===epoch.current)setSave('error');}
 }
 return <section aria-label="یادداشت‌های بازبینی گزارش" style={{padding:8,border:'1px solid #aaa',marginTop:8}}>
  <h4>یادداشت انسانی درباره گزارش تأییدنشده</h4>
  <p>یادداشت، تأیید اجرا یا صحت گزارش نیست. اطلاعات شخصی مخاطبان را وارد نکنید.</p>
  <button type="button" disabled={status==='loading'} onClick={()=>void load(null)}>خواندن یادداشت‌های بازبینی</button>
  {status==='denied'&&<p role="alert">مجوز مستقل خواندن یادداشت و گزارش وجود ندارد.</p>}
  {status==='error'&&<p role="alert">یادداشت‌ها معتبر دریافت نشدند.</p>}
  {status==='ready'&&notes.length===0&&<p>یادداشتی ثبت نشده است؛ نتیجه بازبینی نامعلوم است.</p>}
  {status==='ready'&&<ul>{notes.map(n=><li key={n.noteId}>
   {n.noteText} — ثبت‌کننده: {n.notedBySubject}
   {' '}<strong>UNVERIFIED — بدون تأیید</strong>
   <CenterCampaignFollowup key={n.noteId} centerId={centerId} token={token}
    campaignId={campaignId} associationId={associationId} noteId={n.noteId}/>
  </li>)}</ul>}
  {cursor!==null&&<button type="button" onClick={()=>void load(cursor)}>یادداشت‌های قدیمی‌تر</button>}
  <label>متن یادداشت بدون اطلاعات شخصی
   <textarea aria-label="متن یادداشت بازبینی" maxLength={1000} value={noteText}
    onChange={e=>{setNoteText(e.target.value);setSave('idle');}}/>
  </label>
  <button type="button" disabled={!noteText.trim()||save==='saving'||save==='saved'}
   onClick={()=>void append()}>ثبت یادداشت بدون تأیید</button>
  {save==='saved'&&<p role="status">یادداشت ثبت شد؛ گزارش همچنان UNVERIFIED است.</p>}
  {save==='denied'&&<p role="alert">مجوز مستقل ثبت یادداشت و خواندن گزارش وجود ندارد.</p>}
  {save==='conflict'&&<p role="alert">شناسه یادداشت تکراری یا پیوند گزارش نامعتبر است.</p>}
  {save==='error'&&<p role="alert">ثبت یادداشت معتبر تأیید نشد.</p>}
 </section>;
}

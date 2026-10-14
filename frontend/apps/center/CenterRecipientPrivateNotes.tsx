import React,{useEffect,useRef,useState} from 'react';

type Note={noteId:string;noteText:string;recordedAt:string;
 visibility:'RECIPIENT_INTERNAL';reviewStatus:'PROPOSED_UNREVIEWED';
 isAccepted:false;isExecuted:false};
type State='idle'|'loading'|'ready'|'denied'|'error';
const uuid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
function validNote(x:unknown):x is Note{
 if(!x||typeof x!=='object')return false;
 const v=x as Record<string,unknown>;
 return typeof v.noteId==='string'&&uuid.test(v.noteId)&&
  typeof v.noteText==='string'&&v.noteText.trim().length>0&&v.noteText.length<=1200&&
  typeof v.recordedAt==='string'&&!Number.isNaN(Date.parse(v.recordedAt))&&
  v.visibility==='RECIPIENT_INTERNAL'&&v.reviewStatus==='PROPOSED_UNREVIEWED'&&
  v.isAccepted===false&&v.isExecuted===false;
}
export function CenterRecipientPrivateNotes({centerId,proposalId,token}:{
 centerId:string;proposalId:string;token:string
}){
 const epoch=useRef(0);
 const [draft,setDraft]=useState('');
 const [state,setState]=useState<State>('idle'),[notes,setNotes]=useState<Note[]>([]);
 const [cursor,setCursor]=useState<string|null>(null);
 const [write,setWrite]=useState<'idle'|'saving'|'saved'|'denied'|'conflict'|'error'>('idle');
 const url='/api/v1/centers/'+encodeURIComponent(centerId)+
  '/collaboration/proposals/'+encodeURIComponent(proposalId)+'/recipient-notes';
 const headers={Authorization:'Bearer '+token.trim()};
 useEffect(()=>()=>{epoch.current++;},[]);
 async function load(before:string|null){
  if(!centerId||!token.trim()||!uuid.test(proposalId)||state==='loading')return;
  const run=++epoch.current;setState('loading');
  if(before===null){setNotes([]);setCursor(null);}
  try{
   const response=await fetch(url+(before?'?beforeNoteId='+encodeURIComponent(before):''),
    {headers,cache:'no-store'});
   if(epoch.current!==run)return;
   if(response.status===401||response.status===403){setNotes([]);setCursor(null);setState('denied');return;}
   if(!response.ok)throw Error('Unavailable');
   const raw:unknown=await response.json();
   if(epoch.current!==run||!raw||typeof raw!=='object')throw Error('Invalid');
   const v=raw as Record<string,unknown>;
   if(v.proposalId!==proposalId||v.targetCenterId!==centerId||
    v.visibility!=='RECIPIENT_INTERNAL'||v.reviewStatus!=='PROPOSED_UNREVIEWED'||
    v.isAccepted!==false||v.isExecuted!==false||
    !Array.isArray(v.notes)||v.notes.length>20||!v.notes.every(validNote)||
    !(v.nextBeforeNoteId===null||(typeof v.nextBeforeNoteId==='string'&&uuid.test(v.nextBeforeNoteId))))
    throw Error('Forged private note response');
   const rows=v.notes as Note[];
   if(rows.some((x,i)=>(before!==null&&x.noteId.toLowerCase()>=before.toLowerCase())||
    (i>0&&rows[i-1].noteId.toLowerCase()<=x.noteId.toLowerCase()))||
    (v.nextBeforeNoteId!==null&&(rows.length!==20||
     rows[19].noteId.toLowerCase()!==String(v.nextBeforeNoteId).toLowerCase())))
    throw Error('Invalid note page');
   setNotes(old=>before?[...old,...rows]:rows);
   setCursor(v.nextBeforeNoteId as string|null);setState('ready');
  }catch{if(epoch.current===run){setNotes([]);setCursor(null);setState('error');}}
 }
 async function save(){
  if(!centerId||!token.trim()||!uuid.test(proposalId)||!draft.trim()||
   draft.length>1200||write==='saving')return;
  const run=++epoch.current;setWrite('saving');const noteId=crypto.randomUUID();
  try{
   const response=await fetch(url,{
    method:'POST',headers:{...headers,'Content-Type':'application/json'},
    body:JSON.stringify({noteId,noteText:draft.trim()})});
   if(epoch.current!==run)return;
   if(response.status===401||response.status===403){setWrite('denied');return;}
   if(response.status===409){setWrite('conflict');return;}
   if(!response.ok)throw Error('Unavailable');
   const raw:unknown=await response.json();
   if(epoch.current!==run||!raw||typeof raw!=='object')throw Error('Invalid');
   const v=raw as Record<string,unknown>;
   if(v.noteId!==noteId||v.proposalId!==proposalId||v.targetCenterId!==centerId||
    v.visibility!=='RECIPIENT_INTERNAL'||v.reviewStatus!=='PROPOSED_UNREVIEWED'||
    v.isAccepted!==false||v.isExecuted!==false)
    throw Error('Forged acknowledgement');
   setDraft('');setNotes([]);setCursor(null);setState('idle');setWrite('saved');
  }catch{if(epoch.current===run)setWrite('error');}
 }
 return <section aria-label={'یادداشت خصوصی پیشنهاد '+proposalId} style={{border:'1px solid #bbb',padding:8,marginTop:8}}>
  <h5>یادداشت‌های داخلی دریافت‌کننده</h5>
  <p>فقط اعضای مجاز کانون مقصد؛ این یادداشت به فرستنده نمایش داده نمی‌شود
   و تأیید یا رد همکاری نیست.</p>
  <button type="button" disabled={state==='loading'} onClick={()=>void load(null)}>
   دریافت یادداشت‌های داخلی</button>
  {state==='denied'&&<p role="alert">مجوز مستقل مشاهده یادداشت مقصد موجود نیست.</p>}
  {state==='error'&&<p role="alert">یادداشت معتبر دریافت نشد.</p>}
  {state==='ready'&&<ul>{notes.map(x=><li key={x.noteId}>{x.noteText} — RECIPIENT_INTERNAL</li>)}</ul>}
  {cursor&&<button type="button" onClick={()=>void load(cursor)}>یادداشت‌های قدیمی‌تر</button>}
  <label>یادداشت خصوصی جدید
   <textarea aria-label="متن یادداشت داخلی همکاری" maxLength={1200} value={draft}
    onChange={e=>{setDraft(e.target.value);setWrite('idle');}}/>
  </label>
  <button type="button" disabled={!draft.trim()||write==='saving'} onClick={()=>void save()}>
   ثبت یادداشت داخلی بدون تصمیم</button>
  {write==='saved'&&<p role="status">یادداشت خصوصی ثبت شد؛ همکاری همچنان پذیرفته نشده است.</p>}
  {write==='denied'&&<p role="alert">مجوز مستقل ثبت یادداشت مقصد وجود ندارد.</p>}
  {write==='conflict'&&<p role="alert">یادداشت تکراری است یا پیشنهاد متعلق به این کانون نیست.</p>}
  {write==='error'&&<p role="alert">تأیید معتبر ثبت یادداشت دریافت نشد.</p>}
 </section>;
}

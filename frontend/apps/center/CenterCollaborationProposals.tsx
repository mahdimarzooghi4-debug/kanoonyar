import React,{useEffect,useRef,useState} from 'react';
import { CenterRecipientPrivateNotes } from './CenterRecipientPrivateNotes';

type Proposal={proposalId:string;sourceCenterId:string;targetCenterId:string;
 title:string;summary:string;recordedAt:string;visibility:'EXPLICIT_TARGET_ONLY';
 reviewStatus:'PROPOSED_UNREVIEWED';isAccepted:false;isExecuted:false};
type Direction='INCOMING'|'OUTGOING';
type State='idle'|'loading'|'ready'|'denied'|'error';
const uuid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const proposal=(x:unknown,center:string,d:Direction):x is Proposal=>{
 if(!x||typeof x!=='object')return false;
 const v=x as Record<string,unknown>;
 return typeof v.proposalId==='string'&&uuid.test(v.proposalId)&&
  typeof v.sourceCenterId==='string'&&v.sourceCenterId.length>0&&
  typeof v.targetCenterId==='string'&&v.targetCenterId.length>0&&
  v.sourceCenterId!==v.targetCenterId&&
  (d==='INCOMING'?v.targetCenterId===center:v.sourceCenterId===center)&&
  typeof v.title==='string'&&v.title.trim().length>0&&v.title.length<=160&&
  typeof v.summary==='string'&&v.summary.trim().length>0&&v.summary.length<=1200&&
  typeof v.recordedAt==='string'&&!Number.isNaN(Date.parse(v.recordedAt))&&
  v.visibility==='EXPLICIT_TARGET_ONLY'&&v.reviewStatus==='PROPOSED_UNREVIEWED'&&
  v.isAccepted===false&&v.isExecuted===false;
};
type Feed={entries:Proposal[];cursor:string|null;state:State};
const empty=():Feed=>({entries:[],cursor:null,state:'idle'});
export function CenterCollaborationProposals({centerId,token}:{
 centerId:string;token:string
}){
 const seq=useRef({incoming:0,outgoing:0,write:0});
 const [destination,setDestination]=useState(''),[title,setTitle]=useState(''),
  [summary,setSummary]=useState('');
 const [write,setWrite]=useState<'idle'|'saving'|'saved'|'denied'|'conflict'|'error'>('idle');
 const [incoming,setIncoming]=useState<Feed>(empty);
 const [outgoing,setOutgoing]=useState<Feed>(empty);
 const prefix='/api/v1/centers/'+encodeURIComponent(centerId)+'/collaboration';
 const headers={Authorization:'Bearer '+token.trim()};
 useEffect(()=>()=>{seq.current.incoming++;seq.current.outgoing++;seq.current.write++;},[]);
 async function submit(){
  if(!centerId||!token.trim()||write==='saving'||!destination.trim()||
   destination.trim()===centerId||!title.trim()||title.length>160||
   !summary.trim()||summary.length>1200)return;
  const run=++seq.current.write;
  setWrite('saving');
  const proposalId=crypto.randomUUID();
  try{
   const resp=await fetch(prefix+'/proposals',{method:'POST',
    headers:{...headers,'Content-Type':'application/json'},
    body:JSON.stringify({proposalId,targetCenterId:destination.trim(),
     title:title.trim(),summary:summary.trim()})});
   if(seq.current.write!==run)return;
   if(resp.status===401||resp.status===403){setWrite('denied');return;}
   if(resp.status===409){setWrite('conflict');return;}
   if(!resp.ok)throw Error('Unavailable');
   const raw:unknown=await resp.json();
   if(seq.current.write!==run||!raw||typeof raw!=='object')throw Error('Invalid');
   const r=raw as Record<string,unknown>;
   if(r.proposalId!==proposalId||r.sourceCenterId!==centerId||
    r.targetCenterId!==destination.trim()||r.visibility!=='EXPLICIT_TARGET_ONLY'||
    r.reviewStatus!=='PROPOSED_UNREVIEWED'||r.isAccepted!==false||r.isExecuted!==false)
    throw Error('Forged response');
   setWrite('saved');setDestination('');setTitle('');setSummary('');
   seq.current.outgoing++;setOutgoing(empty());
  }catch{if(seq.current.write===run)setWrite('error');}
 }
 async function read(direction:Direction,before:string|null){
  const inc=direction==='INCOMING';
  const current=inc?incoming:outgoing;
  if(!centerId||!token.trim()||current.state==='loading')return;
  const run=++seq.current[inc?'incoming':'outgoing'];
  const set=inc?setIncoming:setOutgoing;
  set(x=>({...x,state:'loading',...(before===null?{entries:[],cursor:null}:{})}));
  try{
   const path=inc?'incoming':'outgoing';
   const resp=await fetch(prefix+'/'+path+
     (before===null?'':'?beforeProposalId='+encodeURIComponent(before)),{headers,cache:'no-store'});
   if(seq.current[inc?'incoming':'outgoing']!==run)return;
   if(resp.status===401||resp.status===403){set({...empty(),state:'denied'});return;}
   if(!resp.ok)throw Error('Unavailable');
   const raw:unknown=await resp.json();
   if(seq.current[inc?'incoming':'outgoing']!==run||!raw||typeof raw!=='object')throw Error('Invalid');
   const x=raw as Record<string,unknown>;
   if(x.direction!==direction||x.visibility!=='EXPLICIT_TARGET_ONLY'||
    x.reviewStatus!=='PROPOSED_UNREVIEWED'||x.isAccepted!==false||
    x.isExecuted!==false||!Array.isArray(x.proposals)||x.proposals.length>20||
    !x.proposals.every(p=>proposal(p,centerId,direction))||
    !(x.nextBeforeProposalId===null||(typeof x.nextBeforeProposalId==='string'&&
    uuid.test(x.nextBeforeProposalId))))throw Error('Invalid collaboration response');
   const records=x.proposals as Proposal[];
   if(records.some((r,i)=>(before!==null&&r.proposalId.toLowerCase()>=before.toLowerCase())||
    (i>0&&records[i-1].proposalId.toLowerCase()<=r.proposalId.toLowerCase()))||
    (x.nextBeforeProposalId!==null&&(records.length!==20||
    records[19].proposalId.toLowerCase()!==String(x.nextBeforeProposalId).toLowerCase())))
    throw Error('Invalid cursor');
   set(old=>({entries:before===null?records:[...old.entries,...records],
    cursor:x.nextBeforeProposalId as string|null,state:'ready'}));
  }catch{if(seq.current[inc?'incoming':'outgoing']===run)set({...empty(),state:'error'});}
 }
 function panel(direction:Direction,data:Feed){
  const inc=direction==='INCOMING';
  return <section aria-label={inc?'پیشنهادهای دریافتی کانون':'پیشنهادهای ارسالی کانون'}>
   <h4>{inc?'پیشنهادهای دریافتی':'پیشنهادهای ارسالی'}</h4>
   <button type="button" disabled={!centerId||!token.trim()||data.state==='loading'}
    onClick={()=>void read(direction,null)}>{inc?'دریافت پیشنهادهای مقصد':'دریافت پیشنهادهای مبدأ'}</button>
   {data.state==='denied'&&<p role="alert">مجوز مستقل مشاهده پیشنهادهای همکاری وجود ندارد.</p>}
   {data.state==='error'&&<p role="alert">دریافت معتبر پیشنهادهای همکاری ناموفق بود.</p>}
   {data.state==='ready'&&data.entries.length===0&&<p>پیشنهاد دارای مجوز مشاهده یافت نشد.</p>}
   {data.state==='ready'&&<ul>{data.entries.map(x=><li key={x.proposalId}>
    <strong>{x.title}</strong> — {x.summary}
    <p>مبدأ: {x.sourceCenterId}؛ مقصد: {x.targetCenterId}</p>
    <p>فقط پیشنهاد بررسی‌نشده؛ پذیرش یا اجرا تأیید نشده است.</p>
    {inc&&<CenterRecipientPrivateNotes key={centerId+':'+token+':'+x.proposalId}
      centerId={centerId} token={token} proposalId={x.proposalId}/>}
   </li>)}</ul>}
   {data.state==='ready'&&data.cursor!==null&&<button type="button"
    onClick={()=>void read(direction,data.cursor)}>پیشنهادهای بیشتر</button>}
  </section>;
 }
 return <section aria-label="همکاری خصوصی میان کانون‌ها" style={{marginTop:16,padding:12,border:'1px solid #aaa'}}>
  <h3>پیشنهاد همکاری بین کانون‌ها</h3>
  <p>فقط عنوان و شرحی را وارد کنید که مجازید با کانون مقصد به اشتراک بگذارید.
   اطلاعات شخصی، پرونده مخاطب یا تعهد مالی درج نکنید. ثبت پیشنهاد، پذیرش یا اجرای همکاری نیست.</p>
  <label>شناسه دقیق کانون مقصد
   <input aria-label="شناسه کانون دریافت‌کننده" value={destination}
    onChange={e=>{setDestination(e.target.value);setWrite('idle');}}/>
  </label>
  <label>عنوان پیشنهادی
   <input aria-label="عنوان پیشنهاد همکاری" value={title} maxLength={160}
    onChange={e=>{setTitle(e.target.value);setWrite('idle');}}/>
  </label>
  <label>شرح قابل‌اشتراک با مقصد
   <textarea aria-label="شرح پیشنهاد همکاری" value={summary} maxLength={1200}
    onChange={e=>{setSummary(e.target.value);setWrite('idle');}}/>
  </label>
  <button type="button" disabled={!centerId||!token.trim()||write==='saving'||
   !destination.trim()||destination.trim()===centerId||!title.trim()||!summary.trim()}
   onClick={()=>void submit()}>ثبت پیشنهاد برای کانون مشخص</button>
  {write==='saved'&&<p role="status">پیشنهاد خصوصی برای کانون مقصد ثبت شد؛ هنوز پذیرفته نشده است.</p>}
  {write==='denied'&&<p role="alert">مجوز مستقل ایجاد پیشنهاد همکاری وجود ندارد.</p>}
  {write==='conflict'&&<p role="alert">شناسه تکراری یا کانون مقصد نامعتبر است.</p>}
  {write==='error'&&<p role="alert">تأیید معتبر ثبت پیشنهاد دریافت نشد.</p>}
  {panel('OUTGOING',outgoing)}
  {panel('INCOMING',incoming)}
 </section>;
}

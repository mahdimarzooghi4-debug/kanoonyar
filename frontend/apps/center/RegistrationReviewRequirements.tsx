import React,{useEffect,useRef,useState} from 'react';

type Topic='IDENTITY_AUTHORITY'|'ELIGIBILITY_CRITERIA'|'CONSENT_AUTHORITY'|'ADMISSION_POLICY';
type Requirement={reviewRequestId:string;reviewTopic:Topic;reviewStatus:'REVIEW_REQUIRED';recordedAt:string};
type RecordSet={classId:string;requestId:string;registrationState:'REQUESTED';
 enrollmentDecision:null;seatReserved:false;reviewRequests:Requirement[]};
type State='idle'|'loading'|'ready'|'denied'|'error';
type Save='idle'|'saving'|'raised'|'denied'|'conflict'|'error';
const topics:Topic[]=['IDENTITY_AUTHORITY','ELIGIBILITY_CRITERIA','CONSENT_AUTHORITY','ADMISSION_POLICY'];
const titles:Record<Topic,string>={
 IDENTITY_AUTHORITY:'احراز هویت و اختیار نمایندگی',
 ELIGIBILITY_CRITERIA:'شرایط صلاحیت مخاطب',
 CONSENT_AUTHORITY:'رضایت و اختیار قانونی',
 ADMISSION_POLICY:'قواعد پذیرش'
};
const uuid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
function valid(data:unknown,cls:string,request:string):data is RecordSet {
 if(!data||typeof data!=='object')return false;
 const x=data as Record<string,unknown>;
 if(x.classId!==cls||x.requestId!==request||x.registrationState!=='REQUESTED'||
    x.enrollmentDecision!==null||x.seatReserved!==false||
    !Array.isArray(x.reviewRequests)||x.reviewRequests.length>4)return false;
 const seen=new Set<string>();
 for(const item of x.reviewRequests){
   if(!item||typeof item!=='object')return false;
   const y=item as Record<string,unknown>;
   if(typeof y.reviewRequestId!=='string'||!uuid.test(y.reviewRequestId)||
      typeof y.reviewTopic!=='string'||!topics.includes(y.reviewTopic as Topic)||
      y.reviewStatus!=='REVIEW_REQUIRED'||typeof y.recordedAt!=='string'||
      !y.recordedAt.endsWith('Z')||Number.isNaN(Date.parse(y.recordedAt))||
      seen.has(y.reviewTopic))return false;
   seen.add(y.reviewTopic);
 }
 return true;
}
export function RegistrationReviewRequirements({token,centerId,classId,requestId,onClose}:{
 token:string;centerId:string;classId:string;requestId:string;onClose:()=>void;
}){
 const epoch=useRef(0),reviewId=useRef(crypto.randomUUID());
 const [status,setStatus]=useState<State>('idle');
 const [save,setSave]=useState<Save>('idle');
 const [record,setRecord]=useState<RecordSet|null>(null);
 const [topic,setTopic]=useState<Topic>('IDENTITY_AUTHORITY');
 useEffect(()=>{
   epoch.current++;reviewId.current=crypto.randomUUID();
   setStatus('idle');setSave('idle');setRecord(null);setTopic('IDENTITY_AUTHORITY');
   return()=>{epoch.current++};
 },[token,centerId,classId,requestId]);
 const url='/api/v1/centers/'+encodeURIComponent(centerId)+'/classes/'+
   encodeURIComponent(classId)+'/registration-intents/'+encodeURIComponent(requestId)+'/review-requests';
 async function load(){
  if(!centerId||!token.trim()||status==='loading')return;
  const current=++epoch.current;setStatus('loading');setRecord(null);setSave('idle');
  try{
   const response=await fetch(url,{headers:{Authorization:'Bearer '+token.trim()},cache:'no-store'});
   if(current!==epoch.current)return;
   if(response.status===401||response.status===403){setStatus('denied');return;}
   if(!response.ok)throw Error('Unavailable');
   const raw:unknown=await response.json();
   if(current!==epoch.current||!valid(raw,classId,requestId))throw Error('Forged review response');
   setRecord(raw);setStatus('ready');
  }catch{if(current===epoch.current){setStatus('error');setRecord(null);}}
 }
 async function raise(){
  if(!record||save==='saving'||save==='raised'||record.reviewRequests.some(r=>r.reviewTopic===topic))return;
  const current=++epoch.current;setSave('saving');
  const id=reviewId.current;
  try{
   const response=await fetch(url,{method:'POST',headers:{
    Authorization:'Bearer '+token.trim(),'Content-Type':'application/json'
   },body:JSON.stringify({reviewRequestId:id,reviewTopic:topic})});
   if(current!==epoch.current)return;
   if(response.status===401||response.status===403){setSave('denied');return;}
   if(response.status===409){setSave('conflict');return;}
   if(!response.ok)throw Error('Unavailable');
   const raw:unknown=await response.json();
   if(current!==epoch.current||!raw||typeof raw!=='object')throw Error('Invalid ack');
   const x=raw as Record<string,unknown>;
   if(x.classId!==classId||x.requestId!==requestId||x.reviewRequestId!==id||
      x.reviewTopic!==topic||x.reviewStatus!=='REVIEW_REQUIRED'||
      x.enrollmentDecision!==null||x.seatReserved!==false)
       throw Error('Verified or wrong-source acknowledgement');
   setSave('raised');
  }catch{if(current===epoch.current)setSave('error');}
 }
 return <section aria-label="نیازهای بررسی انسانی ثبت‌نام" style={{border:'1px solid #aaa',padding:'0.75rem'}}>
   <h5>بررسی انسانی درخواست اولیه</h5>
   <p>صرفاً ثبت موضوع نیازمند بررسی؛ نه احراز رضایت، صلاحیت یا تأیید ثبت‌نام.</p>
   <button type="button" onClick={onClose}>بستن بررسی</button>{' '}
   <button type="button" disabled={status==='loading'} onClick={()=>void load()}>دریافت نیازهای بررسی</button>
   {status==='loading'&&<p role="status">در حال دریافت بررسی‌ها…</p>}
   {status==='denied'&&<p role="alert">مجوز مستقل مشاهده بررسی انسانی تأیید نشده است.</p>}
   {status==='error'&&<p role="alert">نمای بررسی معتبر دریافت نشد.</p>}
   {status==='ready'&&record&&<>
     <p>درخواست همچنان REQUESTED است؛ هیچ صندلی رزرو نشده است.</p>
     {record.reviewRequests.length===0?
       <p>هیچ موضوعی ثبت نشده؛ این به معنی تأیید هیچ شرطی نیست.</p>:
       <ul>{record.reviewRequests.map(r=><li key={r.reviewRequestId}>
         {titles[r.reviewTopic]} — نیازمند بررسی (REVIEW_REQUIRED)
       </li>)}</ul>}
     <label>موضوع نیازمند بررسی <select aria-label="موضوع بررسی انسانی" value={topic}
       onChange={e=>{setTopic(e.target.value as Topic);setSave('idle');reviewId.current=crypto.randomUUID();}}>
       {topics.map(value=><option key={value} value={value}>{titles[value]}</option>)}
     </select></label>
     <button type="button" disabled={save==='saving'||save==='raised'||
        record.reviewRequests.some(r=>r.reviewTopic===topic)}
       onClick={()=>void raise()}>ثبت نیاز به بررسی انسانی</button>
     {save==='raised'&&<p role="status">نیاز به بررسی ثبت شد؛ هیچ تأیید یا پذیرشی انجام نشد.</p>}
     {save==='denied'&&<p role="alert">مجوز مستقل ثبت بررسی انسانی تأیید نشده است.</p>}
     {save==='conflict'&&<p role="alert">این بررسی پیش‌تر ثبت شده یا درخواست نامعتبر است.</p>}
     {save==='error'&&<p role="alert">ثبت نیاز به بررسی تأیید نشد.</p>}
   </>}
 </section>;
}

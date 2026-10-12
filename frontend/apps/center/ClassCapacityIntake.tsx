import React,{useEffect,useRef,useState} from 'react';

type Item={requestId:string;applicantRef:string;capacityRevision:number;
  recordedAt:string;state:'REQUESTED';seatReserved:false};
type Snapshot={classId:string;currentRevision:number|null;
  configuredCapacity:number|null;confirmedEnrollmentCount:null;
  registrationIntents:Item[];nextBeforeRequestId:string|null};
type Status='idle'|'loading'|'ready'|'denied'|'error';
type Save='idle'|'saving'|'saved'|'denied'|'conflict'|'error';
const uuid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const ident=(v:unknown):v is string=>typeof v==='string'&&uuid.test(v);
const positive=(v:unknown)=>Number.isSafeInteger(v)&&Number(v)>0;
function validItem(v:unknown):v is Item {
 if(!v||typeof v!=='object')return false;
 const x=v as Record<string,unknown>;
 return ident(x.requestId)&&ident(x.applicantRef)&&positive(x.capacityRevision)&&
   typeof x.recordedAt==='string'&&!Number.isNaN(Date.parse(x.recordedAt))&&
   x.state==='REQUESTED'&&x.seatReserved===false;
}
function validPayload(v:unknown,cls:string,before:string|null):v is Snapshot{
 if(!v||typeof v!=='object')return false;
 const x=v as Record<string,unknown>;
 if(x.classId!==cls||
   !(x.currentRevision===null||positive(x.currentRevision))||
   !(x.configuredCapacity===null||positive(x.configuredCapacity))||
   ((x.currentRevision===null)!==(x.configuredCapacity===null))||
   x.confirmedEnrollmentCount!==null||!Array.isArray(x.registrationIntents)||
   x.registrationIntents.length>20||!x.registrationIntents.every(validItem)||
   !(x.nextBeforeRequestId===null||ident(x.nextBeforeRequestId)))return false;
 const rows=x.registrationIntents as Item[];
 if(rows.some((v,i)=>(before!==null&&v.requestId.toLowerCase()>=before.toLowerCase())||
   (i>0&&rows[i-1].requestId.toLowerCase()<=v.requestId.toLowerCase())))return false;
 return x.nextBeforeRequestId===null||(rows.length===20&&
   rows[19].requestId.toLowerCase()===String(x.nextBeforeRequestId).toLowerCase());
}

/** Intake is non-confirming. Does not validate consent or consume class seats. */
export function ClassCapacityIntake({centerId,classId,token,onClose}:{
 centerId:string;classId:string;token:string;onClose:()=>void
}){
 const epoch=useRef(0),requestId=useRef(crypto.randomUUID());
 const [data,setData]=useState<Snapshot|null>(null);
 const [status,setStatus]=useState<Status>('idle');
 const [capacityInput,setCapacityInput]=useState('');
 const [applicantRef,setApplicantRef]=useState('');
 const [capacityState,setCapacityState]=useState<Save>('idle');
 const [requestState,setRequestState]=useState<Save>('idle');
 useEffect(()=>{epoch.current++;requestId.current=crypto.randomUUID();
   setData(null);setStatus('idle');setCapacityInput('');setApplicantRef('');
   setCapacityState('idle');setRequestState('idle');
   return()=>{epoch.current++;};
 },[centerId,classId,token]);
 const prefix='/api/v1/centers/'+encodeURIComponent(centerId)+'/classes/'+encodeURIComponent(classId);
 async function load(before:string|null){
   if(status==='loading'||!centerId||!token.trim())return;
   const current=++epoch.current;setStatus('loading');
   if(before===null)setData(null);
   try {
     const response=await fetch(prefix+'/intake'+
       (before===null?'':'?beforeRequestId='+encodeURIComponent(before)),
       {headers:{Authorization:'Bearer '+token.trim()},cache:'no-store'});
     if(current!==epoch.current)return;
     if(response.status===401||response.status===403){setData(null);setStatus('denied');return;}
     if(!response.ok)throw Error('Unavailable');
     const raw:unknown=await response.json();
     if(current!==epoch.current||!validPayload(raw,classId,before))throw Error('Malformed intake');
     setData(old=>before===null||old===null?raw:{
       ...raw,registrationIntents:[...old.registrationIntents,...raw.registrationIntents]
     });
     setStatus('ready');
   }catch{if(current===epoch.current){setData(null);setStatus('error');}}
 }
 async function setCapacity(){
   const parsed=Number(capacityInput);
   if(!data||!Number.isSafeInteger(parsed)||parsed<=0||data.currentRevision===null&&
     data.configuredCapacity!==null||capacityState==='saving')return;
   const current=++epoch.current;setCapacityState('saving');
   try{
     const resp=await fetch(prefix+'/capacity-revisions',{
       method:'POST',headers:{Authorization:'Bearer '+token.trim(),'Content-Type':'application/json'},
       body:JSON.stringify({expectedRevision:data.currentRevision??0,capacity:parsed})
     });
     if(current!==epoch.current)return;
     if(resp.status===401||resp.status===403){setCapacityState('denied');return;}
     if(resp.status===409){setCapacityState('conflict');return;}
     if(!resp.ok)throw Error('Unavailable');
     const raw:unknown=await resp.json();
     if(current!==epoch.current||!raw||typeof raw!=='object')throw Error('Invalid acknowledgement');
     const d=raw as Record<string,unknown>;
     if(d.classId!==classId||d.revision!==(data.currentRevision??0)+1||
        d.capacity!==parsed||d.confirmedEnrollmentCount!==null)
       throw Error('Invalid capacity acknowledgement');
     setData({...data,currentRevision:d.revision as number,configuredCapacity:parsed});
     setCapacityState('saved');setCapacityInput('');
   }catch{if(current===epoch.current)setCapacityState('error');}
 }
 async function submit(){
   if(!data?.currentRevision||!ident(applicantRef)||requestState==='saving'||
      requestState==='saved')return;
   const current=++epoch.current;setRequestState('saving');
   const requested=requestId.current;
   try{
     const resp=await fetch(prefix+'/registration-intents',{
       method:'POST',headers:{Authorization:'Bearer '+token.trim(),'Content-Type':'application/json'},
       body:JSON.stringify({requestId:requested,applicantRef})
     });
     if(current!==epoch.current)return;
     if(resp.status===401||resp.status===403){setRequestState('denied');return;}
     if(resp.status===409){setRequestState('conflict');return;}
     if(!resp.ok)throw Error('Unavailable');
     const raw:unknown=await resp.json();
     if(current!==epoch.current||!raw||typeof raw!=='object')throw Error('Invalid response');
     const d=raw as Record<string,unknown>;
     if(d.classId!==classId||d.requestId!==requested||
       !positive(d.capacityRevision)||d.state!=='REQUESTED'||d.seatReserved!==false)
       throw Error('Forged confirmation');
     setRequestState('saved');
   }catch{if(current===epoch.current)setRequestState('error');}
 }
 return <section aria-label="ظرفیت و درخواست اولیه کلاس" style={{border:'1px solid #aaa',padding:'1rem'}}>
   <h4>ظرفیت و درخواست اولیه کلاس</h4>
   <p>ثبت درخواست، ثبت‌نام قطعی یا رزرو ظرفیت نیست. صلاحیت و رضایت مخاطب هنوز تأیید نشده‌اند.</p>
   <button type="button" onClick={onClose}>بستن ظرفیت کلاس</button>{' '}
   <button type="button" disabled={status==='loading'} onClick={()=>void load(null)}>دریافت وضعیت ظرفیت</button>
   {status==='loading'&&<p role="status">در حال دریافت اطلاعات ظرفیت…</p>}
   {status==='denied'&&<p role="alert">مجوز مستقل مشاهده ظرفیت و درخواست‌ها تأیید نشده است.</p>}
   {status==='error'&&<p role="alert">اطلاعات ظرفیت در دسترس نیست.</p>}
   {status==='ready'&&data&&<>
     <p>ظرفیت تعریف‌شده: {data.configuredCapacity??'نامشخص'} — نسخه: {data.currentRevision??'ثبت‌نشده'}</p>
     <p>آمار ثبت‌نام قطعی: نامعلوم؛ از درخواست‌های اولیه استنتاج نمی‌شود.</p>
     <label>ظرفیت صریح جدید <input aria-label="ظرفیت جدید کلاس" type="number" min="1" step="1"
       value={capacityInput} onChange={e=>{setCapacityInput(e.target.value);setCapacityState('idle');}} /></label>
     <button type="button" disabled={!Number.isSafeInteger(Number(capacityInput))||
       Number(capacityInput)<=0||capacityInput.trim()===''||capacityState==='saving'}
       onClick={()=>void setCapacity()}>ثبت نسخه ظرفیت کلاس</button>
     {capacityState==='saved'&&<p role="status">نسخه جدید ظرفیت ثبت شد؛ هیچ ثبت‌نامی تأیید نشد.</p>}
     {capacityState==='denied'&&<p role="alert">مجوز مستقل تغییر ظرفیت تأیید نشده است.</p>}
     {capacityState==='conflict'&&<p role="alert">نسخه ظرفیت تغییر کرده است؛ وضعیت را تازه دریافت کنید.</p>}
     {capacityState==='error'&&<p role="alert">ثبت ظرفیت تأیید نشد.</p>}
     <h5>درخواست‌های ثبت‌نام غیرقطعی</h5>
     {data.registrationIntents.length===0?<p>درخواستی در این صفحه ثبت نشده است.</p>:
       <ul>{data.registrationIntents.map(r=><li key={r.requestId}>
         شناسه درخواست: {r.requestId} — وضعیت: REQUESTED (بدون رزرو) — نسخه ظرفیت {r.capacityRevision}
       </li>)}</ul>}
     {data.nextBeforeRequestId&&<button type="button"
       onClick={()=>void load(data.nextBeforeRequestId)}>درخواست‌های دیگر</button>}
     <p>فقط شناسه مستعار UUID متعلق به منبع مجاز؛ اطلاعات شخصی وارد نکنید.</p>
     <label>ارجاع مستعار مخاطب <input aria-label="شناسه UUID مخاطب" value={applicantRef}
       onChange={e=>{setApplicantRef(e.target.value);setRequestState('idle');
         requestId.current=crypto.randomUUID();}} /></label>
     <button type="button" disabled={!data.currentRevision||!ident(applicantRef)||
       requestState==='saving'||requestState==='saved'} onClick={()=>void submit()}>
       ثبت درخواست اولیه بدون رزرو</button>
     {requestState==='saved'&&<p role="status">درخواست در حالت REQUESTED ذخیره شد؛ ثبت‌نام قطعی نیست.</p>}
     {requestState==='denied'&&<p role="alert">مجوز مستقل ثبت درخواست تأیید نشده است.</p>}
     {requestState==='conflict'&&<p role="alert">درخواست تکراری یا منبع/ظرفیت نامعتبر است.</p>}
     {requestState==='error'&&<p role="alert">ثبت درخواست تأیید نشد.</p>}
   </>}
 </section>;
}

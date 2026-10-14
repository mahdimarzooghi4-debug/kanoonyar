import React,{useEffect,useRef,useState} from 'react';
import { CenterWorkResourceNeeds } from './CenterWorkResourceNeeds';
import { CenterWorkSessionLinks } from './CenterWorkSessionLinks';

type LocalPlan={draftId:string;revision:number;programId:string;nationalVersion:number;
 localTitle:string;localActivities:unknown[]};
type Resource={needId:string;workCardId:string;revision:number;resourceLabel:string;
 planningDetail:string;desiredBy:string|null;recordedAt:string;visibility:'PRIVATE_PLANNING';
 fulfillmentStatus:'UNKNOWN';isAllocated:false;isPurchased:false;isFulfilled:false};
type Card={workCardId:string;draftId:string;pinnedPlanRevision:number;revision:number;
 title:string;planningNote:string;plannedDueAt:string|null;recordedAt:string;
 needs:Resource[];needsPreviewLimit:5;nextBeforeNeedId:string|null;
 visibility:'PRIVATE_DRAFT';executionStatus:'UNKNOWN';isExecuted:false};
type Snapshot={sourcePlan:{draftId:string;pinnedPlanRevision:number;localTitle:string;
 programId:string;nationalVersion:number;visibility:'PRIVATE_DRAFT';isPublished:false};
 cards:Card[];nextBeforeCardId:string|null;visibility:'PRIVATE_PLANNING';
 executionStatus:'UNKNOWN';fulfillmentStatus:'UNKNOWN';isExecuted:false;
 isAllocated:false;isPurchased:false;isFulfilled:false};
type View='idle'|'loading'|'ready'|'denied'|'missing'|'error';
const uuid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
function utc(x:unknown):x is string{return typeof x==='string'&&!Number.isNaN(Date.parse(x));}
function plan(x:unknown):x is LocalPlan{
 if(!x||typeof x!=='object')return false;const v=x as Record<string,unknown>;
 return typeof v.draftId==='string'&&uuid.test(v.draftId)&&
 Number.isSafeInteger(v.revision)&&Number(v.revision)>0&&
 typeof v.programId==='string'&&typeof v.localTitle==='string'&&
 Number.isSafeInteger(v.nationalVersion)&&Number(v.nationalVersion)>0&&
 Array.isArray(v.localActivities);
}
function resource(x:unknown):x is Resource{
 if(!x||typeof x!=='object')return false;const v=x as Record<string,unknown>;
 return typeof v.needId==='string'&&uuid.test(v.needId)&&
 typeof v.workCardId==='string'&&uuid.test(v.workCardId)&&
 Number.isSafeInteger(v.revision)&&Number(v.revision)>0&&
 typeof v.resourceLabel==='string'&&v.resourceLabel.trim().length>0&&v.resourceLabel.length<=160&&
 typeof v.planningDetail==='string'&&v.planningDetail.length<=2000&&
 (v.desiredBy===null||utc(v.desiredBy))&&utc(v.recordedAt)&&
 v.visibility==='PRIVATE_PLANNING'&&v.fulfillmentStatus==='UNKNOWN'&&
 v.isAllocated===false&&v.isPurchased===false&&v.isFulfilled===false;
}
function card(x:unknown):x is Card{
 if(!x||typeof x!=='object')return false;const v=x as Record<string,unknown>;
 if(!(typeof v.workCardId==='string'&&uuid.test(v.workCardId)&&
 typeof v.draftId==='string'&&uuid.test(v.draftId)&&
 Number.isSafeInteger(v.pinnedPlanRevision)&&Number(v.pinnedPlanRevision)>0&&
 Number.isSafeInteger(v.revision)&&Number(v.revision)>0&&
 typeof v.title==='string'&&v.title.trim().length>0&&v.title.length<=160&&
 typeof v.planningNote==='string'&&v.planningNote.length<=1200&&
 (v.plannedDueAt===null||utc(v.plannedDueAt))&&utc(v.recordedAt)&&
 v.visibility==='PRIVATE_DRAFT'&&v.executionStatus==='UNKNOWN'&&v.isExecuted===false&&
 v.needsPreviewLimit===5&&Array.isArray(v.needs)&&v.needs.length<=5&&
 v.needs.every(resource)&&
 (v.nextBeforeNeedId===null||
  (typeof v.nextBeforeNeedId==='string'&&uuid.test(v.nextBeforeNeedId)))))return false;
 const needs=v.needs as Resource[];
 if(needs.some((n,i)=>n.workCardId!==v.workCardId||
  (i>0&&needs[i-1].needId.toLowerCase()<=n.needId.toLowerCase()))||
  (v.nextBeforeNeedId!==null&&(needs.length!==5||
   needs[4].needId.toLowerCase()!==String(v.nextBeforeNeedId).toLowerCase())))return false;
 return true;
}
function snapshot(x:unknown,draftId:string,revision:number,before:string|null):x is Snapshot{
 if(!x||typeof x!=='object')return false;const v=x as Record<string,unknown>;
 if(v.visibility!=='PRIVATE_PLANNING'||v.executionStatus!=='UNKNOWN'||
 v.fulfillmentStatus!=='UNKNOWN'||v.isExecuted!==false||
 v.isAllocated!==false||v.isPurchased!==false||
 !v.sourcePlan||typeof v.sourcePlan!=='object')return false;
 const p=v.sourcePlan as Record<string,unknown>;
 if(p.draftId!==draftId||p.pinnedPlanRevision!==revision||
 typeof p.localTitle!=='string'||typeof p.programId!=='string'||
 !Number.isSafeInteger(p.nationalVersion)||Number(p.nationalVersion)<=0||
 p.visibility!=='PRIVATE_DRAFT'||p.isPublished!==false||
 !Array.isArray(v.cards)||v.cards.length>20||!v.cards.every(card)||
 !(v.nextBeforeCardId===null||
   (typeof v.nextBeforeCardId==='string'&&uuid.test(v.nextBeforeCardId))))return false;
 const cards=v.cards as Card[];
 if(cards.some((c,i)=>c.draftId!==draftId||c.pinnedPlanRevision!==revision||
  (before!==null&&c.workCardId.toLowerCase()>=before.toLowerCase())||
  (i>0&&cards[i-1].workCardId.toLowerCase()<=c.workCardId.toLowerCase()))||
 (v.nextBeforeCardId!==null&&(cards.length!==20||
  cards[19].workCardId.toLowerCase()!==String(v.nextBeforeCardId).toLowerCase())))return false;
 return true;
}
export function CenterLocalPlanWorkspace({centerId,token,pinnedPlan}:{
 centerId:string;token:string;
 /** Original S23 campaign's immutable S08/S10 plan identity; S40 verifies source rights again. */
 pinnedPlan?:{draftId:string;revision:number};
}){
 const epoch=useRef(0),prefix='/api/v1/centers/'+encodeURIComponent(centerId);
 const headers={Authorization:'Bearer '+token.trim()};
 const [plans,setPlans]=useState<LocalPlan[]>([]);
 const [planState,setPlanState]=useState<View>('idle');
 const [selection,setSelection]=useState('');
 const [state,setState]=useState<View>('idle'),[cards,setCards]=useState<Card[]>([]);
 const [cursor,setCursor]=useState<string|null>(null),[source,setSource]=useState<Snapshot['sourcePlan']|null>(null);
 const [expanded,setExpanded]=useState('');
 useEffect(()=>()=>{epoch.current++;},[]);
 async function loadPlans(){
  if(pinnedPlan||!token.trim()||!centerId||planState==='loading')return;
  const request=++epoch.current;setPlanState('loading');setPlans([]);
  setSelection('');setCards([]);setSource(null);setCursor(null);setExpanded('');setState('idle');
  try{
   const r=await fetch(prefix+'/local-plan-drafts',{headers,cache:'no-store'});
   if(epoch.current!==request)return;
   if(r.status===401||r.status===403){setPlanState('denied');return;}
   if(!r.ok)throw Error('Missing source');
   const raw:unknown=await r.json();
   if(epoch.current!==request||!raw||typeof raw!=='object')throw Error('Invalid source');
   const data=raw as Record<string,unknown>;
   if(!Array.isArray(data.drafts)||!data.drafts.every(plan)||data.drafts.length>100)
    throw Error('Invalid plan source');
   setPlans(data.drafts as LocalPlan[]);setPlanState('ready');
  }catch{if(epoch.current===request)setPlanState('error');}
 }
 async function load(before:string|null){
  if(!(pinnedPlan||selection)||!token.trim()||state==='loading')return;
  const selected=pinnedPlan??plans.find(p=>p.draftId+':'+p.revision===selection);
  if(!selected)return;
  const request=++epoch.current;setState('loading');
  if(before===null){setCards([]);setCursor(null);setSource(null);setExpanded('');}
  try{
   const url=prefix+'/operations/local-plan-workspace/'+
    encodeURIComponent(selected.draftId)+'/'+selected.revision+
    (before?'?beforeCardId='+encodeURIComponent(before):'');
   const r=await fetch(url,{headers,cache:'no-store'});
   if(epoch.current!==request)return;
   if(r.status===401||r.status===403){
    setCards([]);setSource(null);setCursor(null);setState('denied');return;
   }
   if(r.status===404){setCards([]);setSource(null);setCursor(null);setState('missing');return;}
   if(!r.ok)throw Error('Snapshot failed');
   const raw:unknown=await r.json();
   if(epoch.current!==request||!snapshot(raw,selected.draftId,selected.revision,before))
    throw Error('Untrusted snapshot');
   if(before!==null&&cards.some(c=>raw.cards.some((n:Card)=>n.workCardId===c.workCardId)))
    throw Error('Repeated card');
   setCards(old=>before?[...old,...raw.cards]:raw.cards);
   setCursor(raw.nextBeforeCardId);setSource(raw.sourcePlan);setState('ready');
  }catch{if(epoch.current===request){setCards([]);setCursor(null);setSource(null);
   setExpanded('');setState('error');}}
 }
 return <section aria-label="میزکار یکپارچه طرح بومی" style={{border:'1px solid #aaa',padding:12,marginTop:16}}>
  <h3>میزکار یکپارچه اجرای طرح بومی</h3>
  <p>طرح نسخه‌دار ← کارت‌های کار ← نیازهای منابع ثبت‌شده؛ صرفاً برنامه‌ریزی داخلی.
   هیچ اجرا، موجودی، خرید یا تخصیصی تأیید نشده است.</p>
  {pinnedPlan?<p>نسخه طرح مبنای پویش: {pinnedPlan.draftId} / {pinnedPlan.revision}.
    تغییر طرح مبنا در این نما مجاز نیست و نمایش اطلاعات منوط به مجوز مستقل S40 است.</p>:
   <button type="button" onClick={()=>void loadPlans()} disabled={planState==='loading'||!token.trim()}>
    دریافت طرح‌های واقعی کانون</button>}
  {!pinnedPlan&&planState==='denied'&&<p role="alert">مجوز دیدن طرح‌های مبنا موجود نیست.</p>}
  {!pinnedPlan&&planState==='error'&&<p role="alert">طرح‌های معتبر دریافت نشدند.</p>}
  {!pinnedPlan&&planState==='ready'&&<label>نسخه طرح بومی
   <select aria-label="انتخاب نسخه طرح برای میزکار" value={selection} onChange={e=>{
    epoch.current++;setSelection(e.target.value);setCards([]);setCursor(null);
    setSource(null);setExpanded('');setState('idle');}}>
    <option value="">طرح واقعی را انتخاب کنید</option>
    {plans.map(p=><option key={p.draftId+':'+p.revision} value={p.draftId+':'+p.revision}>
     {p.localTitle} — نسخه {p.revision}</option>)}
   </select></label>}
  <button type="button" onClick={()=>void load(null)}
   disabled={!(pinnedPlan||selection)||!token.trim()||state==='loading'}>
   نمایش کارت‌ها و نیازهای این نسخه طرح</button>
  {state==='denied'&&<p role="alert">مجوز مستقل خواندن طرح، کارت یا نیازهای هر کارت وجود ندارد.</p>}
  {state==='missing'&&<p role="alert">نسخه طرح مبنا دیگر معتبر نیست.</p>}
  {state==='error'&&<p role="alert">نمای یکپارچه معتبر از منابع اصلی دریافت نشد.</p>}
  {state==='ready'&&source&&<div>
   <h4>{source.localTitle}</h4>
   <p>برنامه ملی {source.programId} / نسخه {source.nationalVersion} — طرح بومی نسخه {source.pinnedPlanRevision}</p>
   {cards.length===0&&<p>در این بخش از طرح، کارت برنامه‌ریزی‌شده‌ای ثبت نشده است.</p>}
   <ol>{cards.map(c=><li key={c.workCardId}>
    <strong>{c.title}</strong> — نسخه {c.revision} / انجام: نامشخص
    <p>{c.planningNote}</p>
    <p>موعد پیشنهادی: {c.plannedDueAt??'ثبت نشده'}</p>
    {c.needs.length===0?<p>نیاز منبع ثبت‌شده‌ای در این کارت وجود ندارد.</p>:
     <ul>{c.needs.map(n=><li key={n.needId}>
      {n.resourceLabel} — نسخه {n.revision} / تأمین نامشخص
     </li>)}</ul>}
    {c.nextBeforeNeedId!==null&&<p>نیازهای بیشتری ثبت شده‌اند؛ فهرست کامل در ابزار کارت کار قابل مشاهده است.</p>}
    <CenterWorkSessionLinks key={centerId+':'+token+':'+c.workCardId}
     centerId={centerId} workCardId={c.workCardId} token={token}
     draftId={c.draftId} pinnedRevision={c.pinnedPlanRevision}
     programId={source.programId} nationalVersion={source.nationalVersion}/>
    <button type="button" onClick={()=>setExpanded(x=>x===c.workCardId?'':c.workCardId)}>
     {expanded===c.workCardId?'بستن مدیریت نیازها':'مدیریت کامل نیازهای همین کارت'}</button>
    {expanded===c.workCardId&&<CenterWorkResourceNeeds
     key={centerId+':'+token+':'+c.workCardId}
     centerId={centerId} token={token} workCardId={c.workCardId}/>}
   </li>)}</ol>
   {cursor!==null&&<button type="button" disabled={state==='loading'} onClick={()=>void load(cursor)}>
    کارت‌های بیشتر همین نسخه طرح</button>}
  </div>}
 </section>;
}

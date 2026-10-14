import React,{useEffect,useRef,useState} from 'react';
import {CenterCampaignEvidenceRegister} from './CenterCampaignEvidenceRegister';
import { CenterCampaignSourceReports } from './CenterCampaignSourceReports';
import { CenterLocalPlanWorkspace } from './CenterLocalPlanWorkspace';

type Activity={activityKey:string;title:string};
type LocalPlan={draftId:string;revision:number;localTitle:string;programId:string;nationalVersion:number};
type Campaign={campaignId:string;slug:string;draftId:string;pinnedPlanRevision:number;
 revision:number;title:string;goalText:string;audienceDescription:string;
 plannedActivities:Activity[];recordedAt:string;visibility:'PRIVATE_DRAFT';
 isPublished:false;isExecuted:false};
type Revision={revision:number;title:string;goalText:string;audienceDescription:string;
 plannedActivities:Activity[];recordedAt:string;visibility:'PRIVATE_DRAFT';isPublished:false;isExecuted:false};
type State='idle'|'loading'|'ready'|'denied'|'error';
type Save='idle'|'saving'|'saved'|'denied'|'conflict'|'error';
const uuid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const slugRule=/^[a-z0-9]+(-[a-z0-9]+)*$/;
const keyRule=/^[a-z][a-z0-9_]{0,31}$/;
function privateMeta(x:Record<string,unknown>){return x.visibility==='PRIVATE_DRAFT'&&x.isPublished===false&&x.isExecuted===false;}
function validActivities(x:unknown):x is Activity[]{
 if(!Array.isArray(x)||x.length<1||x.length>12)return false;
 const keys=new Set<string>();
 return x.every(y=>{
  if(!y||typeof y!=='object')return false;
  const a=y as Record<string,unknown>;
  if(typeof a.activityKey!=='string'||!keyRule.test(a.activityKey)||keys.has(a.activityKey)||
     typeof a.title!=='string'||a.title.trim().length<1||a.title.trim().length>160)return false;
  keys.add(a.activityKey);return true;
 });
}
function validRevision(x:unknown):x is Revision{
 if(!x||typeof x!=='object')return false;
 const a=x as Record<string,unknown>;
 return Number.isSafeInteger(a.revision)&&Number(a.revision)>0&&
  typeof a.title==='string'&&a.title.trim().length>0&&a.title.length<=200&&
  typeof a.goalText==='string'&&a.goalText.trim().length>0&&a.goalText.length<=500&&
  typeof a.audienceDescription==='string'&&a.audienceDescription.trim().length>0&&
  a.audienceDescription.length<=240&&validActivities(a.plannedActivities)&&
  typeof a.recordedAt==='string'&&!Number.isNaN(Date.parse(a.recordedAt))&&privateMeta(a);
}
function validCampaign(x:unknown):x is Campaign{
 if(!validRevision(x))return false;
 const y=x as unknown as Record<string,unknown>;
 return typeof y.campaignId==='string'&&uuid.test(y.campaignId)&&
  typeof y.draftId==='string'&&uuid.test(y.draftId)&&
  Number.isSafeInteger(y.pinnedPlanRevision)&&Number(y.pinnedPlanRevision)>0&&
  typeof y.slug==='string'&&y.slug.length<=80&&slugRule.test(y.slug);
}
function validPlans(x:unknown):x is {drafts:LocalPlan[]}{
 if(!x||typeof x!=='object')return false;
 const a=x as Record<string,unknown>;
 return Array.isArray(a.drafts)&&a.drafts.length<=200&&a.drafts.every(p=>{
  if(!p||typeof p!=='object')return false;
  const y=p as Record<string,unknown>;
  return typeof y.draftId==='string'&&uuid.test(y.draftId)&&Number.isSafeInteger(y.revision)&&
   Number(y.revision)>0&&typeof y.localTitle==='string'&&
   typeof y.programId==='string'&&Number.isSafeInteger(y.nationalVersion)&&Number(y.nationalVersion)>0;
 });
}
function validList(x:unknown,before:string|null):x is {campaigns:Campaign[];nextBeforeCampaignId:string|null}{
 if(!x||typeof x!=='object')return false;
 const a=x as Record<string,unknown>;
 if(!Array.isArray(a.campaigns)||a.campaigns.length>20||!a.campaigns.every(validCampaign)||
   !(a.nextBeforeCampaignId===null||
    (typeof a.nextBeforeCampaignId==='string'&&uuid.test(a.nextBeforeCampaignId))))return false;
 const rows=a.campaigns as Campaign[];
 if(rows.some((v,i)=>(before!==null&&v.campaignId.toLowerCase()>=before.toLowerCase())||
    (i>0&&rows[i-1].campaignId.toLowerCase()<=v.campaignId.toLowerCase())))return false;
 return a.nextBeforeCampaignId===null||(rows.length===20&&
   rows[19].campaignId.toLowerCase()===String(a.nextBeforeCampaignId).toLowerCase());
}
export function CenterCampaignPlanning({centerId,token}:{centerId:string;token:string}){
 const epoch=useRef(0);
 const [planState,setPlanState]=useState<State>('idle'),[plans,setPlans]=useState<LocalPlan[]>([]);
 const [listState,setListState]=useState<State>('idle'),[campaigns,setCampaigns]=useState<Campaign[]>([]);
 const [cursor,setCursor]=useState<string|null>(null);
 const [selected,setSelected]=useState<Campaign|null>(null),[planId,setPlanId]=useState('');
 const [slug,setSlug]=useState(''),[title,setTitle]=useState(''),[goal,setGoal]=useState(''),[audience,setAudience]=useState('');
 const [activities,setActivities]=useState<Activity[]>([{activityKey:'step_one',title:''}]);
 const [save,setSave]=useState<Save>('idle');
 const [history,setHistory]=useState<Revision[]>([]),[historyState,setHistoryState]=useState<State>('idle');
 const [historyCursor,setHistoryCursor]=useState<number|null>(null);
 const [openedPlanWorkspace,setOpenedPlanWorkspace]=useState(false);
 const prefix='/api/v1/centers/'+encodeURIComponent(centerId);
 const headers={Authorization:'Bearer '+token.trim()};
 useEffect(()=>{
  epoch.current++;setPlanState('idle');setPlans([]);setListState('idle');setCampaigns([]);
  setCursor(null);setSelected(null);setPlanId('');setSlug('');setTitle('');setGoal('');
  setAudience('');setActivities([{activityKey:'step_one',title:''}]);setSave('idle');
  setHistory([]);setHistoryState('idle');setHistoryCursor(null);
  setOpenedPlanWorkspace(false);
  return()=>{epoch.current++};
 },[centerId,token]);
 function edit(c:Campaign|null){
  epoch.current++;setOpenedPlanWorkspace(false);setSelected(c);setPlanId(c?c.draftId+':'+c.pinnedPlanRevision:'');
  setSlug(c?.slug??'');setTitle(c?.title??'');setGoal(c?.goalText??'');
  setAudience(c?.audienceDescription??'');
  setActivities(c?c.plannedActivities.map(a=>({...a})):[{activityKey:'step_one',title:''}]);
  setSave('idle');setHistory([]);setHistoryState('idle');setHistoryCursor(null);
 }
 async function loadPlans(){
  if(!centerId||!token.trim()||planState==='loading')return;
  const id=++epoch.current;setPlans([]);setPlanState('loading');
  try{
   const resp=await fetch(prefix+'/local-plan-drafts',{headers,cache:'no-store'});
   if(id!==epoch.current)return;
   if(resp.status===401||resp.status===403){setPlanState('denied');return;}
   if(!resp.ok)throw Error('Unavailable');
   const raw:unknown=await resp.json();
   if(id!==epoch.current||!validPlans(raw))throw Error('Invalid plans');
   setPlans(raw.drafts);setPlanState('ready');
  }catch{if(id===epoch.current)setPlanState('error');}
 }
 async function loadList(before:string|null){
  if(!centerId||!token.trim()||listState==='loading')return;
  const id=++epoch.current;setListState('loading');
  if(before===null){setOpenedPlanWorkspace(false);setCampaigns([]);setCursor(null);setSelected(null);setSave('idle');setHistory([]);}
  try{
   const resp=await fetch(prefix+'/campaigns/drafts'+
    (before?'?beforeCampaignId='+encodeURIComponent(before):''),{headers,cache:'no-store'});
   if(id!==epoch.current)return;
   if(resp.status===401||resp.status===403){setCampaigns([]);setListState('denied');return;}
   if(!resp.ok)throw Error('Unavailable');
   const raw:unknown=await resp.json();
   if(id!==epoch.current||!validList(raw,before))throw Error('Invalid campaigns');
   setCampaigns(old=>before===null?raw.campaigns:[...old,...raw.campaigns]);
   setCursor(raw.nextBeforeCampaignId);setListState('ready');
  }catch{if(id===epoch.current){setCampaigns([]);setCursor(null);setListState('error');}}
 }
 function changeActivity(i:number,key:'activityKey'|'title',value:string){
  setActivities(old=>old.map((a,k)=>k===i?{...a,[key]:value}:a));setSave('idle');
 }
 const selectedPlan=plans.find(p=>p.draftId+':'+p.revision===planId);
 const valid=Boolean(selectedPlan)&&slugRule.test(slug)&&slug.length<=80&&
  title.trim().length>0&&title.length<=200&&goal.trim().length>0&&goal.length<=500&&
  audience.trim().length>0&&audience.length<=240&&validActivities(activities);
 async function persist(){
  if(!valid||save==='saving'||save==='saved'||!selectedPlan)return;
  const id=++epoch.current,campaignId=selected?.campaignId??crypto.randomUUID(),
   expectedRevision=selected?.revision??0;
  const payload={campaignId,expectedRevision,slug,draftId:selectedPlan.draftId,
   pinnedPlanRevision:selectedPlan.revision,title:title.trim(),
   goalText:goal.trim(),audienceDescription:audience.trim(),plannedActivities:activities};
  setSave('saving');
  try{
   const resp=await fetch(prefix+'/campaigns/drafts/revisions',{method:'POST',
    headers:{...headers,'Content-Type':'application/json'},body:JSON.stringify(payload)});
   if(id!==epoch.current)return;
   if(resp.status===401||resp.status===403){setSave('denied');return;}
   if(resp.status===409){setSave('conflict');return;}
   if(!resp.ok)throw Error('Unavailable');
   const raw:unknown=await resp.json();
   if(id!==epoch.current||!raw||typeof raw!=='object')throw Error('Invalid acknowledgement');
   const a=raw as Record<string,unknown>;
   if(a.campaignId!==campaignId||a.draftId!==selectedPlan.draftId||
     a.pinnedPlanRevision!==selectedPlan.revision||a.slug!==slug||
     a.revision!==expectedRevision+1||!privateMeta(a))throw Error('Forged status');
   const updated:Campaign={...payload,revision:expectedRevision+1,recordedAt:new Date().toISOString(),
     visibility:'PRIVATE_DRAFT',isPublished:false,isExecuted:false};
   setSelected(updated);setCampaigns(old=>[updated,...old.filter(v=>v.campaignId!==campaignId)]);
   setSave('saved');setHistory([]);setHistoryState('idle');setHistoryCursor(null);
  }catch{if(id===epoch.current)setSave('error');}
 }
 async function loadHistory(before:number|null){
  if(!selected||historyState==='loading')return;
  const active=selected,id=++epoch.current;setHistoryState('loading');
  if(before===null){setHistory([]);setHistoryCursor(null);}
  try{
   const resp=await fetch(prefix+'/campaigns/drafts/'+encodeURIComponent(active.campaignId)+
    '/revisions'+(before===null?'':'?beforeRevision='+before),{headers,cache:'no-store'});
   if(id!==epoch.current)return;
   if(resp.status===401||resp.status===403){setHistory([]);setHistoryState('denied');return;}
   if(!resp.ok)throw Error('Unavailable');
   const raw:unknown=await resp.json();
   if(id!==epoch.current||!raw||typeof raw!=='object')throw Error('Invalid');
   const x=raw as Record<string,unknown>;
   if(x.campaignId!==active.campaignId||x.draftId!==active.draftId||
     x.pinnedPlanRevision!==active.pinnedPlanRevision||x.slug!==active.slug||
      !Array.isArray(x.revisions)||x.revisions.length>20||!x.revisions.every(validRevision)||
      !(x.nextBeforeRevision===null||(Number.isSafeInteger(x.nextBeforeRevision)&&Number(x.nextBeforeRevision)>0)))
    throw Error('Unsafe history');
   const rows=x.revisions as Revision[];
   if(rows.some((v,i)=>(before!==null&&v.revision>=before)||(i>0&&rows[i-1].revision<=v.revision)))throw Error('Bad order');
   if(x.nextBeforeRevision!==null&&(rows.length!==20||rows[19].revision!==x.nextBeforeRevision))
    throw Error('Bad cursor');
   setHistory(old=>before===null?rows:[...old,...rows]);setHistoryCursor(x.nextBeforeRevision as number|null);
   setHistoryState('ready');
  }catch{if(id===epoch.current){setHistory([]);setHistoryState('error');}}
 }
 return <section aria-label="پویش‌ساز خصوصی کانون" style={{marginTop:'2rem'}}>
  <h2>پویش‌ساز — برنامه‌ریزی خصوصی</h2>
  <p>پویش‌ها صرفاً برنامه‌ریزی می‌شوند؛ انتشار، مشارکت واقعی، اجرای قطعی و پرداخت ثبت نمی‌شود.</p>
  <button type="button" disabled={!centerId||planState==='loading'} onClick={()=>void loadPlans()}>
   دریافت طرح‌های محلی مجاز</button>{' '}
  <button type="button" disabled={!centerId||listState==='loading'} onClick={()=>void loadList(null)}>
   دریافت پویش‌های خصوصی</button>{' '}
  <button type="button" disabled={!centerId} onClick={()=>edit(null)}>پویش جدید</button>
  {planState==='denied'&&<p role="alert">مجوز مستقل مشاهده طرح محلی برای پویش وجود ندارد.</p>}
  {planState==='error'&&<p role="alert">طرح محلی معتبر دریافت نشد.</p>}
  {planState==='ready'&&plans.length===0&&<p>طرح محلی موجود نیست؛ پویش بدون طرح ساخته نمی‌شود.</p>}
  {listState==='denied'&&<p role="alert">مجوز مستقل مشاهده پویش وجود ندارد.</p>}
  {listState==='error'&&<p role="alert">فهرست پویش‌ها معتبر دریافت نشد.</p>}
  {listState==='ready'&&<ul>{campaigns.map(c=><li key={c.campaignId}>
   {c.title} — نسخه {c.revision} — فقط PRIVATE_DRAFT
   {' '}<button type="button" onClick={()=>edit(c)}>ویرایش پویش</button>
  </li>)}</ul>}
  {cursor&&<button type="button" onClick={()=>void loadList(cursor)}>پویش‌های قدیمی‌تر</button>}
  {centerId&&<div>
   <h3>{selected?'ثبت نسخه جدید پویش':'تعریف پویش'}</h3>
   <label>طرح محلی مبنا
    <select aria-label="طرح محلی پویش" value={planId}
     disabled={Boolean(selected)} onChange={e=>{setPlanId(e.target.value);setSave('idle');}}>
     <option value="">انتخاب از داده واقعی کانون</option>
     {plans.map(p=><option key={p.draftId+':'+p.revision} value={p.draftId+':'+p.revision}>
       {p.localTitle} — نسخه {p.revision}</option>)}
    </select>
   </label>
   <label>نشانی پویش <input aria-label="نشانی پویش" value={slug} maxLength={80}
     disabled={Boolean(selected)} onChange={e=>{setSlug(e.target.value);setSave('idle');}}/></label>
   <label>عنوان پویش <input aria-label="عنوان پویش" value={title} maxLength={200}
    onChange={e=>{setTitle(e.target.value);setSave('idle');}}/></label>
   <label>هدف پویش <textarea aria-label="هدف پویش" value={goal} maxLength={500}
    onChange={e=>{setGoal(e.target.value);setSave('idle');}}/></label>
   <label>توصیف کلی مخاطب (بدون اطلاعات فردی) <input aria-label="توصیف کلی مخاطب" value={audience} maxLength={240}
    onChange={e=>{setAudience(e.target.value);setSave('idle');}}/></label>
   <h4>فعالیت‌های برنامه‌ریزی‌شده</h4>
   {activities.map((a,i)=><fieldset key={i}><legend>فعالیت {i+1}</legend>
    <label>کلید فعالیت <input aria-label={'کلید فعالیت '+(i+1)} value={a.activityKey}
     maxLength={32} onChange={e=>changeActivity(i,'activityKey',e.target.value)}/></label>
    <label>عنوان فعالیت <input aria-label={'عنوان فعالیت '+(i+1)} value={a.title}
     maxLength={160} onChange={e=>changeActivity(i,'title',e.target.value)}/></label>
    <button type="button" disabled={activities.length===1}
      onClick={()=>{setActivities(old=>old.filter((_,k)=>k!==i));setSave('idle');}}>حذف فعالیت {i+1}</button>
   </fieldset>)}
   <button type="button" disabled={activities.length>=12}
    onClick={()=>{let k=1;while(activities.some(a=>a.activityKey==='step_'+k))k++;
      setActivities(old=>[...old,{activityKey:'step_'+k,title:''}]);setSave('idle');}}>افزودن فعالیت پویش</button>
   <section aria-label="پیش‌نمایش خصوصی پویش">
    <h4>پیش‌نمایش — منتشرنشده و اجرا‌نشده</h4>
    <p>{title}</p><p>{goal}</p><p>{audience}</p>
    <ol>{activities.map(a=><li key={a.activityKey}>{a.title||'فعالیت بدون عنوان'} — برنامه‌ریزی‌شده</li>)}</ol>
   </section>
   <button type="button" disabled={!valid||save==='saving'||save==='saved'} onClick={()=>void persist()}>
    ذخیره نسخه برنامه‌ریزی پویش</button>
   {save==='saved'&&<p role="status">نسخه پویش ذخیره شد؛ اجرا یا انتشار تأیید نشده است.</p>}
   {save==='denied'&&<p role="alert">مجوز مستقل ویرایش پویش تأیید نشده است.</p>}
   {save==='conflict'&&<p role="alert">طرح مبنا، نشانی یا نسخه پویش تعارض دارد؛ دوباره بررسی کنید.</p>}
   {save==='error'&&<p role="alert">ثبت پویش تأیید نشد.</p>}
   {selected&&<>
    <button type="button" onClick={()=>setOpenedPlanWorkspace(v=>!v)}>
     {openedPlanWorkspace?'بستن میزکار منابع طرح مبنای پویش':
      'کارت‌های کار و نیازهای طرح مبنای همین پویش'}</button>
    {openedPlanWorkspace&&<CenterLocalPlanWorkspace
     key={'campaign-plan-work:'+centerId+':'+token+':'+selected.campaignId+':'+selected.draftId+':'+selected.pinnedPlanRevision}
     centerId={centerId} token={token}
     pinnedPlan={{draftId:selected.draftId,revision:selected.pinnedPlanRevision}}/>}
   </>}
   {selected&&<CenterCampaignEvidenceRegister
    key={'register:'+selected.campaignId+':'+selected.revision} centerId={centerId}
    token={token} campaignId={selected.campaignId}
    draftId={selected.draftId} pinnedPlanRevision={selected.pinnedPlanRevision}
    activities={selected.plannedActivities}/>}
   {selected&&<CenterCampaignSourceReports key={selected.campaignId+':'+selected.revision}
    centerId={centerId} token={token} campaignId={selected.campaignId}
    draftId={selected.draftId} campaignRevision={selected.revision}
    activities={selected.plannedActivities}/>}
   {selected&&<>
    <button type="button" onClick={()=>void loadHistory(null)}>تاریخچه پویش</button>
    {historyState==='denied'&&<p role="alert">مجوز مشاهده تاریخچه پویش وجود ندارد.</p>}
    {historyState==='error'&&<p role="alert">تاریخچه معتبر دریافت نشد.</p>}
    {historyState==='ready'&&<ol>{history.map(h=><li key={h.revision}>
      نسخه {h.revision} — {h.title} — {h.plannedActivities.length} فعالیت برنامه‌ریزی‌شده
    </li>)}</ol>}
    {historyCursor!==null&&<button type="button" onClick={()=>void loadHistory(historyCursor)}>
      نسخه‌های قدیمی‌تر پویش</button>}
   </>}
  </div>}
 </section>;
}

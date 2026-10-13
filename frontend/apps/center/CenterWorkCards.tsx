import React,{useEffect,useRef,useState} from 'react';

type Plan={draftId:string;revision:number;localTitle:string;programId:string;nationalVersion:number;localActivities:unknown[]};
type Card={workCardId:string;draftId:string;pinnedPlanRevision:number;revision:number;
 title:string;planningNote:string;plannedDueAt:string|null;recordedAt:string;
 visibility:'PRIVATE_DRAFT';executionStatus:'UNKNOWN';isExecuted:false};
type Version={revision:number;title:string;planningNote:string;plannedDueAt:string|null;
 recordedAt:string;visibility:'PRIVATE_DRAFT';executionStatus:'UNKNOWN';isExecuted:false};
const uuid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const timestamp=(x:unknown):x is string=>typeof x==='string'&&!Number.isNaN(Date.parse(x));
const plan=(v:unknown):v is Plan=>{
 if(!v||typeof v!=='object')return false;const x=v as Record<string,unknown>;
 return typeof x.draftId==='string'&&uuid.test(x.draftId)&&
  Number.isSafeInteger(x.revision)&&Number(x.revision)>0&&
  typeof x.localTitle==='string'&&typeof x.programId==='string'&&
  Number.isSafeInteger(x.nationalVersion)&&Number(x.nationalVersion)>0&&
  Array.isArray(x.localActivities);
};
const version=(v:unknown):v is Version=>{
 if(!v||typeof v!=='object')return false;const x=v as Record<string,unknown>;
 return Number.isSafeInteger(x.revision)&&Number(x.revision)>0&&
  typeof x.title==='string'&&x.title.trim().length>0&&x.title.length<=160&&
  typeof x.planningNote==='string'&&x.planningNote.length<=1200&&
  (x.plannedDueAt===null||timestamp(x.plannedDueAt))&&timestamp(x.recordedAt)&&
  x.visibility==='PRIVATE_DRAFT'&&x.executionStatus==='UNKNOWN'&&x.isExecuted===false;
};
const card=(v:unknown):v is Card=>{
 if(!version(v)||!v||typeof v!=='object')return false;const x=v as Record<string,unknown>;
 return typeof x.workCardId==='string'&&uuid.test(x.workCardId)&&
  typeof x.draftId==='string'&&uuid.test(x.draftId)&&
  Number.isSafeInteger(x.pinnedPlanRevision)&&Number(x.pinnedPlanRevision)>0;
};
type State='idle'|'loading'|'ready'|'denied'|'error';
export function CenterWorkCards({centerId,token}:{centerId:string;token:string}){
 const epoch=useRef(0);const prefix='/api/v1/centers/'+encodeURIComponent(centerId);
 const headers={Authorization:'Bearer '+token.trim()};
 const [plans,setPlans]=useState<Plan[]>([]),[planState,setPlanState]=useState<State>('idle');
 const [cards,setCards]=useState<Card[]>([]),[cardState,setCardState]=useState<State>('idle');
 const [cursor,setCursor]=useState<string|null>(null);
 const [selected,setSelected]=useState<Card|null>(null),[draftKey,setDraftKey]=useState('');
 const [title,setTitle]=useState(''),[note,setNote]=useState(''),[due,setDue]=useState('');
 const [save,setSave]=useState<'idle'|'saving'|'saved'|'denied'|'conflict'|'error'>('idle');
 const [history,setHistory]=useState<Version[]>([]),[historyState,setHistoryState]=useState<State>('idle');
 const [historyCursor,setHistoryCursor]=useState<number|null>(null);
 useEffect(()=>()=>{epoch.current++;},[]);
 function edit(x:Card|null){
  epoch.current++;setSelected(x);setDraftKey(x?x.draftId+':'+x.pinnedPlanRevision:'');
  setTitle(x?.title??'');setNote(x?.planningNote??'');
  setDue(x?.plannedDueAt?x.plannedDueAt.slice(0,16):'');
  setSave('idle');setHistory([]);setHistoryState('idle');setHistoryCursor(null);
 }
 async function loadPlans(){
  if(!centerId||!token.trim()||planState==='loading')return;
  const n=++epoch.current;setPlanState('loading');setPlans([]);
  try{
   const response=await fetch(prefix+'/local-plan-drafts',{headers,cache:'no-store'});
   if(n!==epoch.current)return;
   if(response.status===401||response.status===403){setPlanState('denied');return;}
   if(!response.ok)throw Error('Source unavailable');
   const raw:unknown=await response.json();
   if(n!==epoch.current||!raw||typeof raw!=='object'||!('drafts' in raw)||
    !Array.isArray(raw.drafts)||!raw.drafts.every(plan))throw Error('Invalid plan');
   setPlans(raw.drafts);setPlanState('ready');
  }catch{if(n===epoch.current)setPlanState('error');}
 }
 async function loadCards(before:string|null){
  if(!centerId||!token.trim()||cardState==='loading')return;
  const n=++epoch.current;setCardState('loading');
  if(before===null){setCards([]);setCursor(null);}
  try{
   const response=await fetch(prefix+'/operations/work-cards'+
    (before?'?beforeCardId='+encodeURIComponent(before):''),{headers,cache:'no-store'});
   if(n!==epoch.current)return;
   if(response.status===401||response.status===403){
    setCards([]);setCardState('denied');return;
   }
   if(!response.ok)throw Error('Unavailable');
   const raw:unknown=await response.json();
   if(n!==epoch.current||!raw||typeof raw!=='object')throw Error('Invalid');
   const x=raw as Record<string,unknown>;
   if(x.visibility!=='PRIVATE_DRAFT'||x.executionStatus!=='UNKNOWN'||x.isExecuted!==false||
    !Array.isArray(x.cards)||x.cards.length>20||!x.cards.every(card)||
    !(x.nextBeforeCardId===null||(typeof x.nextBeforeCardId==='string'&&uuid.test(x.nextBeforeCardId))))
     throw Error('Forged task list');
   const rows=x.cards as Card[];
   if(rows.some((v,i)=>(before!==null&&v.workCardId.toLowerCase()>=before.toLowerCase())||
    (i>0&&rows[i-1].workCardId.toLowerCase()<=v.workCardId.toLowerCase()))||
    (x.nextBeforeCardId!==null&&(rows.length!==20||
      rows[19].workCardId.toLowerCase()!==String(x.nextBeforeCardId).toLowerCase())))
    throw Error('Invalid cursor');
   setCards(old=>before?[...old,...rows]:rows);setCursor(x.nextBeforeCardId as string|null);
   setCardState('ready');
  }catch{if(n===epoch.current){setCards([]);setCursor(null);setCardState('error');}}
 }
 async function submit(){
  if(!centerId||!token.trim()||save==='saving')return;
  const chosen=selected?{draftId:selected.draftId,revision:selected.pinnedPlanRevision}:
   plans.find(p=>p.draftId+':'+p.revision===draftKey);
  if(!chosen||title.trim().length<1||title.length>160||note.length>1200)return;
  const n=++epoch.current;setSave('saving');
  const workCardId=selected?.workCardId??crypto.randomUUID();
  const expectedRevision=selected?.revision??0;
  try{
   const response=await fetch(prefix+'/operations/work-cards/revisions',{
    method:'POST',headers:{...headers,'Content-Type':'application/json'},
    body:JSON.stringify({workCardId,expectedRevision,draftId:chosen.draftId,
     pinnedPlanRevision:chosen.revision,title:title.trim(),planningNote:note,
     plannedDueAt:due?due+':00Z':null})
   });
   if(epoch.current!==n)return;
   if(response.status===401||response.status===403){setSave('denied');return;}
   if(response.status===409){setSave('conflict');return;}
   if(!response.ok)throw Error('Unavailable');
   const raw:unknown=await response.json();
   if(epoch.current!==n||!raw||typeof raw!=='object')throw Error('Invalid');
   const x=raw as Record<string,unknown>;
   if(x.workCardId!==workCardId||x.revision!==expectedRevision+1||
    x.draftId!==chosen.draftId||x.pinnedPlanRevision!==chosen.revision||
    x.visibility!=='PRIVATE_DRAFT'||x.executionStatus!=='UNKNOWN'||x.isExecuted!==false)
    throw Error('Forged write');
   setSave('saved');setSelected(null);setDraftKey('');setTitle('');setNote('');setDue('');
   setCards([]);setCardState('idle');setCursor(null);
   setHistory([]);setHistoryCursor(null);setHistoryState('idle');
  }catch{if(epoch.current===n)setSave('error');}
 }
 async function loadHistory(before:number|null){
  if(!selected||historyState==='loading')return;
  const entry=selected;const n=++epoch.current;setHistoryState('loading');
  if(before===null){setHistory([]);setHistoryCursor(null);}
  try{
   const response=await fetch(prefix+'/operations/work-cards/'+encodeURIComponent(entry.workCardId)+
    '/revisions'+(before===null?'':'?beforeRevision='+before),{headers,cache:'no-store'});
   if(epoch.current!==n)return;
   if(response.status===401||response.status===403){
    setHistory([]);setHistoryState('denied');return;
   }
   if(!response.ok)throw Error('Unavailable');
   const raw:unknown=await response.json();
   if(epoch.current!==n||!raw||typeof raw!=='object')throw Error('Invalid');
   const x=raw as Record<string,unknown>;
   if(x.workCardId!==entry.workCardId||x.draftId!==entry.draftId||
    x.pinnedPlanRevision!==entry.pinnedPlanRevision||
    x.visibility!=='PRIVATE_DRAFT'||x.executionStatus!=='UNKNOWN'||x.isExecuted!==false||
    !Array.isArray(x.revisions)||x.revisions.length>20||!x.revisions.every(version)||
    !(x.nextBeforeRevision===null||(Number.isSafeInteger(x.nextBeforeRevision)&&
      Number(x.nextBeforeRevision)>0)))throw Error('Forged history');
   const rows=x.revisions as Version[];
   if(rows.some((v,i)=>(before!==null&&v.revision>=before)||
    (i>0&&rows[i-1].revision<=v.revision))||
    (x.nextBeforeRevision!==null&&(rows.length!==20||
      rows[19].revision!==x.nextBeforeRevision)))throw Error('History cursor invalid');
   setHistory(old=>before===null?rows:[...old,...rows]);
   setHistoryCursor(x.nextBeforeRevision as number|null);setHistoryState('ready');
  }catch{if(epoch.current===n){setHistory([]);setHistoryCursor(null);setHistoryState('error');}}
 }
 return <section aria-label="کارت‌های کار خصوصی کانون" style={{border:'1px solid #aaa',padding:12,marginTop:16}}>
  <h3>برنامه‌ریزی کارت‌های کار محلی</h3>
  <p>این کارت‌ها فقط یادداشت برنامه‌ریزی‌اند؛ مسئول تعیین‌شده، اجرای تأییدشده یا نتیجه محقق‌شده ندارند.</p>
  <button type="button" disabled={!centerId||!token.trim()} onClick={()=>void loadPlans()}>
   دریافت طرح‌های محلی واقعی</button>{' '}
  <button type="button" disabled={!centerId||!token.trim()} onClick={()=>void loadCards(null)}>
   دریافت کارت‌های خصوصی</button>
  {planState==='denied'&&<p role="alert">مجوز مستقل مشاهده طرح محلی وجود ندارد.</p>}
  {planState==='error'&&<p role="alert">طرح محلی معتبر دریافت نشد.</p>}
  {cardState==='denied'&&<p role="alert">مجوز مستقل مشاهده کارت‌های کار وجود ندارد.</p>}
  {cardState==='error'&&<p role="alert">کارت کار معتبر دریافت نشد.</p>}
  {cardState==='ready'&&<ul>{cards.map(x=><li key={x.workCardId}>
   <strong>{x.title}</strong> — نسخه {x.revision} — UNKNOWN
   <p>موعد برنامه‌ریزی UTC: {x.plannedDueAt??'تعیین نشده'}</p>
   <p>طرح اصلی {x.draftId} / نسخه {x.pinnedPlanRevision}</p>
   <button type="button" onClick={()=>edit(x)}>ویرایش کارت برنامه‌ریزی‌شده</button>
  </li>)}</ul>}
  {cursor!==null&&<button type="button" onClick={()=>void loadCards(cursor)}>کارت‌های بعدی</button>}
  {selected&&<button type="button" onClick={()=>edit(null)}>کارت جدید</button>}
  {!selected&&<label>طرح محلی مبنا <select aria-label="انتخاب طرح برای کارت" value={draftKey}
   onChange={e=>setDraftKey(e.target.value)}>
   <option value="">انتخاب نسخه طرح واقعی</option>
   {plans.map(p=><option key={p.draftId+':'+p.revision} value={p.draftId+':'+p.revision}>
    {p.localTitle} — نسخه {p.revision}</option>)}
  </select></label>}
  {selected&&<p>کارت انتخاب‌شده: {selected.workCardId}؛ نسخه مبنا {selected.pinnedPlanRevision} تغییر نمی‌کند.</p>}
  <label>عنوان کارت <input aria-label="عنوان کارت خصوصی" value={title} maxLength={160}
   onChange={e=>setTitle(e.target.value)}/></label>
  <label>یادداشت برنامه‌ریزی <textarea aria-label="یادداشت کارت خصوصی" value={note} maxLength={1200}
   onChange={e=>setNote(e.target.value)}/></label>
  <label>موعد اختیاری UTC <input aria-label="موعد UTC کارت" type="datetime-local" value={due}
   onChange={e=>setDue(e.target.value)}/></label>
  <button type="button" disabled={!title.trim()||save==='saving'||(!selected&&!draftKey)}
   onClick={()=>void submit()}>ذخیره نسخه کارت کار</button>
  {save==='saved'&&<p role="status">نسخه برنامه‌ریزی کارت ذخیره شد؛ انجام کار تأیید نشده است.</p>}
  {save==='conflict'&&<p role="alert">نسخه کارت یا طرح مبنا تغییر کرده است.</p>}
  {save==='denied'&&<p role="alert">مجوز مستقل ثبت کارت کار وجود ندارد.</p>}
  {save==='error'&&<p role="alert">تأیید معتبر ذخیره کارت دریافت نشد.</p>}
  {selected&&<><button type="button" onClick={()=>void loadHistory(null)}>مشاهده تاریخچه کارت</button>
   {historyState==='denied'&&<p role="alert">مجوز تاریخچه کارت وجود ندارد.</p>}
   {historyState==='error'&&<p role="alert">تاریخچه معتبر کارت دریافت نشد.</p>}
   {historyState==='ready'&&<ul>{history.map(h=><li key={h.revision}>
    نسخه {h.revision} — {h.title} — UNKNOWN
   </li>)}</ul>}
   {historyCursor!==null&&<button type="button" onClick={()=>void loadHistory(historyCursor)}>
    تاریخچه قدیمی‌تر</button>}
  </>}
 </section>;
}

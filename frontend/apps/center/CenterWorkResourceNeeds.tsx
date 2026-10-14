import React,{useEffect,useRef,useState} from 'react';
type Need={needId:string;workCardId:string;revision:number;resourceLabel:string;planningDetail:string;
 desiredBy:string|null;recordedAt:string;visibility:'PRIVATE_PLANNING';fulfillmentStatus:'UNKNOWN';
 isAllocated:false;isPurchased:false;isFulfilled:false};
type Version=Omit<Need,'needId'|'workCardId'>;
const uuid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const flags=(v:Record<string,unknown>)=>v.visibility==='PRIVATE_PLANNING'&&
 v.fulfillmentStatus==='UNKNOWN'&&v.isAllocated===false&&v.isPurchased===false&&v.isFulfilled===false;
function version(x:unknown):x is Version{
 if(!x||typeof x!=='object')return false;const v=x as Record<string,unknown>;
 return Number.isSafeInteger(v.revision)&&Number(v.revision)>0&&
 typeof v.resourceLabel==='string'&&v.resourceLabel.trim().length>0&&v.resourceLabel.length<=160&&
 typeof v.planningDetail==='string'&&v.planningDetail.length<=2000&&
 (v.desiredBy===null||(typeof v.desiredBy==='string'&&!Number.isNaN(Date.parse(v.desiredBy))))&&
 typeof v.recordedAt==='string'&&!Number.isNaN(Date.parse(v.recordedAt))&&flags(v);
}
function need(x:unknown):x is Need{
 if(!version(x)||!x||typeof x!=='object')return false;const v=x as Record<string,unknown>;
 return typeof v.needId==='string'&&uuid.test(v.needId)&&
 typeof v.workCardId==='string'&&uuid.test(v.workCardId);
}
type State='idle'|'loading'|'ready'|'denied'|'error';
type Save='idle'|'saving'|'saved'|'denied'|'conflict'|'error';
export function CenterWorkResourceNeeds({centerId,workCardId,token}:{centerId:string;workCardId:string;token:string}){
 const epoch=useRef(0);
 const url='/api/v1/centers/'+encodeURIComponent(centerId)+'/operations/work-cards/'+
  encodeURIComponent(workCardId)+'/resource-needs';
 const headers={Authorization:'Bearer '+token.trim()};
 const [rows,setRows]=useState<Need[]>([]),[cursor,setCursor]=useState<string|null>(null);
 const [state,setState]=useState<State>('idle'),[selected,setSelected]=useState<Need|null>(null);
 const [label,setLabel]=useState(''),[detail,setDetail]=useState(''),[write,setWrite]=useState<Save>('idle');
 const [history,setHistory]=useState<Version[]>([]),[historyCursor,setHistoryCursor]=useState<number|null>(null);
 const [historyState,setHistoryState]=useState<State>('idle');
 useEffect(()=>()=>{epoch.current++;},[]);
 function edit(v:Need|null){
  epoch.current++;setSelected(v);setLabel(v?.resourceLabel??'');setDetail(v?.planningDetail??'');
  setWrite('idle');setHistory([]);setHistoryCursor(null);setHistoryState('idle');
 }
 async function list(before:string|null){
  if(!token.trim()||!centerId||state==='loading')return;
  const run=++epoch.current;setState('loading');if(before===null){setRows([]);setCursor(null);}
  try{
   const r=await fetch(url+(before?'?beforeNeedId='+encodeURIComponent(before):''),
    {headers,cache:'no-store'});
   if(run!==epoch.current)return;
   if(r.status===401||r.status===403){setRows([]);setCursor(null);setState('denied');return;}
   if(!r.ok)throw Error('Access');
   const raw:unknown=await r.json();if(run!==epoch.current||!raw||typeof raw!=='object')throw Error('Invalid');
   const v=raw as Record<string,unknown>;
   if(v.workCardId!==workCardId||!flags(v)||!Array.isArray(v.needs)||v.needs.length>20||
    !v.needs.every(need)||(v.needs as Need[]).some(n=>n.workCardId!==workCardId)||
    !(v.nextBeforeNeedId===null||(typeof v.nextBeforeNeedId==='string'&&uuid.test(v.nextBeforeNeedId))))
    throw Error('Forged resource planning');
   const data=v.needs as Need[];
   if(data.some((n,i)=>(before!==null&&n.needId.toLowerCase()>=before.toLowerCase())||
    (i>0&&data[i-1].needId.toLowerCase()<=n.needId.toLowerCase()))||
    (v.nextBeforeNeedId!==null&&(data.length!==20||
     data[19].needId.toLowerCase()!==String(v.nextBeforeNeedId).toLowerCase())))
    throw Error('Cursor');
   setRows(old=>before?[...old,...data]:data);setCursor(v.nextBeforeNeedId as string|null);
   setState('ready');
  }catch{if(run===epoch.current){setRows([]);setCursor(null);setState('error');}}
 }
 async function submit(){
  if(!token.trim()||!label.trim()||label.length>160||detail.length>2000||write==='saving')return;
  const run=++epoch.current,needId=selected?.needId??crypto.randomUUID(),
   expectedRevision=selected?.revision??0;setWrite('saving');
  try{
   const r=await fetch(url+'/revisions',{method:'POST',
    headers:{...headers,'Content-Type':'application/json'},
    body:JSON.stringify({needId,expectedRevision,resourceLabel:label.trim(),
     planningDetail:detail,desiredBy:null})});
   if(run!==epoch.current)return;
   if(r.status===401||r.status===403){setWrite('denied');return;}
   if(r.status===409){setWrite('conflict');return;}
   if(!r.ok)throw Error('Storage');
   const raw:unknown=await r.json();if(run!==epoch.current||!raw||typeof raw!=='object')throw Error('Invalid');
   const v=raw as Record<string,unknown>;
   if(v.needId!==needId||v.workCardId!==workCardId||
    v.revision!==expectedRevision+1||!flags(v))throw Error('Forged success');
   setSelected(null);setLabel('');setDetail('');setWrite('saved');
   setRows([]);setCursor(null);setState('idle');setHistory([]);setHistoryState('idle');
  }catch{if(run===epoch.current)setWrite('error');}
 }
 async function loadHistory(before:number|null){
  if(!selected||historyState==='loading')return;
  const entry=selected,run=++epoch.current;setHistoryState('loading');
  if(before===null){setHistory([]);setHistoryCursor(null);}
  try{
   const r=await fetch(url+'/'+encodeURIComponent(entry.needId)+'/revisions'+
    (before===null?'':'?beforeRevision='+before),{headers,cache:'no-store'});
   if(run!==epoch.current)return;
   if(r.status===401||r.status===403){setHistory([]);setHistoryState('denied');return;}
   if(!r.ok)throw Error('Storage');
   const raw:unknown=await r.json();if(run!==epoch.current||!raw||typeof raw!=='object')throw Error('Invalid');
   const v=raw as Record<string,unknown>;
   if(v.workCardId!==workCardId||v.needId!==entry.needId||!flags(v)||
    !Array.isArray(v.revisions)||v.revisions.length>20||!v.revisions.every(version)||
    !(v.nextBeforeRevision===null||(Number.isSafeInteger(v.nextBeforeRevision)&&Number(v.nextBeforeRevision)>0)))
    throw Error('Forged history');
   const data=v.revisions as Version[];
   if(data.some((n,i)=>(before!==null&&n.revision>=before)||
    (i>0&&data[i-1].revision<=n.revision))||
    (v.nextBeforeRevision!==null&&(data.length!==20||data[19].revision!==v.nextBeforeRevision)))
    throw Error('History cursor');
   setHistory(old=>before===null?data:[...old,...data]);
   setHistoryCursor(v.nextBeforeRevision as number|null);setHistoryState('ready');
  }catch{if(run===epoch.current){setHistory([]);setHistoryCursor(null);setHistoryState('error');}}
 }
 return <section aria-label={'نیازهای منابع کارت '+workCardId}
  style={{padding:8,border:'1px solid #aaa',marginTop:8}}>
  <h5>نیازهای برنامه‌ریزی منابع این کار</h5>
  <p>این داده موجودی، خرید، تخصیص یا تحویل تأییدشده نیست.</p>
  <button type="button" onClick={()=>void list(null)} disabled={state==='loading'}>
   دریافت نیازهای این کارت کار</button>
  {state==='ready'&&<ul>{rows.map(v=><li key={v.needId}>
   <strong>{v.resourceLabel}</strong> — نسخه {v.revision} / تأمین نامشخص
   <button type="button" onClick={()=>edit(v)}>ویرایش نیاز و تاریخچه</button>
  </li>)}</ul>}
  {cursor&&<button type="button" onClick={()=>void list(cursor)}>نیازهای بیشتر</button>}
  {selected&&<button type="button" onClick={()=>edit(null)}>نیاز جدید</button>}
  {state==='denied'&&<p role="alert">مجوز مستقل مشاهده نیاز منابع موجود نیست.</p>}
  {state==='error'&&<p role="alert">فهرست معتبر نیازهای منابع دریافت نشد.</p>}
  <label>نام نیاز منابع <input aria-label={'نام نیاز منابع '+workCardId} maxLength={160}
   value={label} onChange={e=>{setLabel(e.target.value);setWrite('idle');}}/></label>
  <label>شرح نیاز منابع <textarea aria-label={'شرح نیاز منابع '+workCardId} maxLength={2000}
   value={detail} onChange={e=>setDetail(e.target.value)}/></label>
  <button type="button" disabled={!label.trim()||write==='saving'} onClick={()=>void submit()}>
   ثبت نسخه نیاز منابع</button>
  {write==='saved'&&<p role="status">نیاز خصوصی ثبت شد؛ تأمین یا خرید تأیید نشده است.</p>}
  {write==='denied'&&<p role="alert">مجوز مستقل ثبت نیاز منابع موجود نیست.</p>}
  {write==='conflict'&&<p role="alert">نسخه نیاز یا کارت کار معتبر نیست.</p>}
  {write==='error'&&<p role="alert">تأیید معتبر ذخیره نیاز دریافت نشد.</p>}
  {selected&&<><button type="button" onClick={()=>void loadHistory(null)}>تاریخچه نیاز منابع</button>
   {historyState==='ready'&&<ul>{history.map(v=><li key={v.revision}>
    نسخه {v.revision} — {v.resourceLabel} — تأمین نامشخص</li>)}</ul>}
   {historyState==='denied'&&<p role="alert">مجوز مستقل مشاهده تاریخچه موجود نیست.</p>}
   {historyState==='error'&&<p role="alert">تاریخچه معتبر نیاز دریافت نشد.</p>}
   {historyCursor&&<button type="button" onClick={()=>void loadHistory(historyCursor)}>
    نسخه‌های قبلی نیاز</button>}
  </>}
 </section>;
}

import React,{useEffect,useRef,useState} from 'react';

type Page={pageId:string;slug:string;revision:number;title:string;bodyText:string;
 recordedAt:string;visibility:'PRIVATE_DRAFT';isPublished:false};
type Revision={revision:number;title:string;bodyText:string;recordedAt:string;
 visibility:'PRIVATE_DRAFT';isPublished:false};
type State='idle'|'loading'|'ready'|'denied'|'error';
type Save='idle'|'saving'|'saved'|'denied'|'conflict'|'error';
const uuid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const slug=/^[a-z0-9]+(-[a-z0-9]+)*$/;
const date=(x:unknown):x is string=>typeof x==='string'&&x.endsWith('Z')&&!Number.isNaN(Date.parse(x));
const validRevision=(x:unknown):x is Revision=>{
 if(!x||typeof x!=='object')return false;
 const v=x as Record<string,unknown>;
 return Number.isSafeInteger(v.revision)&&Number(v.revision)>0&&typeof v.title==='string'&&
   v.title.trim().length>0&&typeof v.bodyText==='string'&&date(v.recordedAt)&&
   v.visibility==='PRIVATE_DRAFT'&&v.isPublished===false;
};
const validPage=(x:unknown):x is Page=>{
 if(!x||typeof x!=='object')return false;
 const v=x as Record<string,unknown>;
 return typeof v.pageId==='string'&&uuid.test(v.pageId)&&
  typeof v.slug==='string'&&slug.test(v.slug)&&validRevision(v);
};
type Result={pages:Page[];nextBeforePageId:string|null};
function validList(x:unknown,before:string|null):x is Result{
 if(!x||typeof x!=='object')return false;
 const v=x as Record<string,unknown>;
 if(!Array.isArray(v.pages)||v.pages.length>20||!v.pages.every(validPage)||
  !(v.nextBeforePageId===null||(typeof v.nextBeforePageId==='string'&&uuid.test(v.nextBeforePageId))))
  return false;
 const rows=v.pages as Page[];
 if(rows.some((p,i)=>(before!==null&&p.pageId.toLowerCase()>=before.toLowerCase())||
   (i>0&&rows[i-1].pageId.toLowerCase()<=p.pageId.toLowerCase())))return false;
 return v.nextBeforePageId===null||(rows.length===20&&
  rows[19].pageId.toLowerCase()===String(v.nextBeforePageId).toLowerCase());
}
type HistoryResult={pageId:string;slug:string;revisions:Revision[];nextBeforeRevision:number|null};
function validHistory(x:unknown,page:Page,before:number|null):x is HistoryResult{
 if(!x||typeof x!=='object')return false;
 const v=x as Record<string,unknown>;
 if(v.pageId!==page.pageId||v.slug!==page.slug||
   !Array.isArray(v.revisions)||v.revisions.length>20||!v.revisions.every(validRevision)||
   !(v.nextBeforeRevision===null||
     (Number.isSafeInteger(v.nextBeforeRevision)&&Number(v.nextBeforeRevision)>0)))return false;
 const rows=v.revisions as Revision[];
 if(rows.some((r,i)=>(before!==null&&r.revision>=before)||
   (i>0&&rows[i-1].revision<=r.revision)))return false;
 return v.nextBeforeRevision===null||(rows.length===20&&
   rows[19].revision===v.nextBeforeRevision);
}
export function CenterSitePageDrafts({centerId,token}:{centerId:string;token:string}){
 const epoch=useRef(0);
 const [pages,setPages]=useState<Page[]>([]);
 const [state,setState]=useState<State>('idle');
 const [cursor,setCursor]=useState<string|null>(null);
 const [selected,setSelected]=useState<Page|null>(null);
 const [title,setTitle]=useState(''),[slugInput,setSlugInput]=useState(''),[body,setBody]=useState('');
 const [save,setSave]=useState<Save>('idle');
 const [history,setHistory]=useState<Revision[]>([]);
 const [historyState,setHistoryState]=useState<State>('idle');
 const [historyCursor,setHistoryCursor]=useState<number|null>(null);
 const prefix='/api/v1/centers/'+encodeURIComponent(centerId)+'/site/drafts';
 useEffect(()=>{
  epoch.current++;setPages([]);setState('idle');setCursor(null);
  setSelected(null);setTitle('');setSlugInput('');setBody('');
  setSave('idle');setHistory([]);setHistoryState('idle');setHistoryCursor(null);
  return()=>{epoch.current++};
 },[centerId,token]);
 function edit(page:Page|null){
  epoch.current++;setSelected(page);
  setTitle(page?.title??'');setSlugInput(page?.slug??'');setBody(page?.bodyText??'');
  setSave('idle');setHistory([]);setHistoryState('idle');setHistoryCursor(null);
 }
 async function load(before:string|null){
  if(!centerId||!token.trim()||state==='loading')return;
  const current=++epoch.current;setState('loading');
  if(before===null){
    setPages([]);setCursor(null);setSelected(null);setTitle('');
    setSlugInput('');setBody('');setSave('idle');setHistory([]);
    setHistoryState('idle');setHistoryCursor(null);
  }
  try{
   const response=await fetch(prefix+(before?'?beforePageId='+encodeURIComponent(before):''),
    {headers:{Authorization:'Bearer '+token.trim()},cache:'no-store'});
   if(current!==epoch.current)return;
   if(response.status===401||response.status===403){setPages([]);setState('denied');return;}
   if(!response.ok)throw Error('Unavailable');
   const raw:unknown=await response.json();
   if(current!==epoch.current||!validList(raw,before))throw Error('Invalid pages');
   setPages(previous=>before===null?raw.pages:[...previous,...raw.pages]);
   setCursor(raw.nextBeforePageId);setState('ready');
  }catch{if(current===epoch.current){setPages([]);setCursor(null);setState('error');}}
 }
 async function savePage(){
  if(!centerId||!token.trim()||!slug.test(slugInput)||slugInput.length>80||
    !title.trim()||title.length>200||body.length>20000||save==='saving')return;
  const current=++epoch.current, pageId=selected?.pageId??crypto.randomUUID(),
   expectedRevision=selected?.revision??0;
  setSave('saving');
  try{
   const response=await fetch(prefix+'/revisions',{method:'POST',headers:{
    Authorization:'Bearer '+token.trim(),'Content-Type':'application/json'},
    body:JSON.stringify({pageId,expectedRevision,slug:slugInput,title:title.trim(),bodyText:body})});
   if(current!==epoch.current)return;
   if(response.status===401||response.status===403){setSave('denied');return;}
   if(response.status===409){setSave('conflict');return;}
   if(!response.ok)throw Error('Unavailable');
   const raw:unknown=await response.json();
   if(current!==epoch.current||!raw||typeof raw!=='object')throw Error('Invalid ack');
   const x=raw as Record<string,unknown>;
   if(x.pageId!==pageId||x.slug!==slugInput||x.revision!==expectedRevision+1||
      x.visibility!=='PRIVATE_DRAFT'||x.isPublished!==false)throw Error('Forged publication');
   setSave('saved');
   setSelected({pageId,slug:slugInput,revision:expectedRevision+1,
     title:title.trim(),bodyText:body,recordedAt:new Date().toISOString(),
     visibility:'PRIVATE_DRAFT',isPublished:false});
   setPages(old=>{
    const rest=old.filter(p=>p.pageId!==pageId);
    return [{pageId,slug:slugInput,revision:expectedRevision+1,
      title:title.trim(),bodyText:body,recordedAt:new Date().toISOString(),
      visibility:'PRIVATE_DRAFT',isPublished:false},...rest];
   });
  }catch{if(current===epoch.current)setSave('error');}
 }
 async function loadHistory(before:number|null){
  if(!selected||historyState==='loading')return;
  const chosen=selected;
  const current=++epoch.current;setHistoryState('loading');
  if(before===null){setHistory([]);setHistoryCursor(null);}
  try{
   const response=await fetch(prefix+'/'+encodeURIComponent(chosen.pageId)+'/revisions'+
    (before===null?'':'?beforeRevision='+before),{
     headers:{Authorization:'Bearer '+token.trim()},cache:'no-store'});
   if(current!==epoch.current)return;
   if(response.status===401||response.status===403){setHistory([]);setHistoryState('denied');return;}
   if(!response.ok)throw Error('Unavailable');
   const raw:unknown=await response.json();
   if(current!==epoch.current||!validHistory(raw,chosen,before))throw Error('Invalid revisions');
   setHistory(previous=>before===null?raw.revisions:[...previous,...raw.revisions]);
   setHistoryCursor(raw.nextBeforeRevision);setHistoryState('ready');
  }catch{if(current===epoch.current){setHistory([]);setHistoryState('error');}}
 }
 return <section aria-label="سایت‌ساز پیش‌نویس کانون" style={{marginTop:'2rem'}}>
  <h2>سایت‌ساز کانون — صفحات خصوصی</h2>
  <p>فقط نسخه‌های پیش‌نویس. هیچ صفحه‌ای منتشر یا برای بازدیدکنندگان عمومی قابل مشاهده نمی‌شود.</p>
  <button type="button" onClick={()=>void load(null)} disabled={!centerId||state==='loading'}>
    دریافت صفحات پیش‌نویس</button>{' '}
  <button type="button" onClick={()=>edit(null)} disabled={!centerId}>صفحه جدید</button>
  {state==='loading'&&<p role="status">در حال دریافت صفحات…</p>}
  {state==='denied'&&<p role="alert">مجوز مستقل مشاهده سایت‌ساز تأیید نشده است.</p>}
  {state==='error'&&<p role="alert">دریافت صفحات ممکن نشد.</p>}
  {state==='ready'&&pages.length===0&&<p>صفحه پیش‌نویسی در این کانون ثبت نشده است.</p>}
  {state==='ready'&&<ul>{pages.map(page=><li key={page.pageId}>
   <strong>{page.title}</strong> — /{page.slug} — نسخه {page.revision} (خصوصی)
   {' '}<button type="button" onClick={()=>edit(page)}>ویرایش نسخه پیش‌نویس</button>
  </li>)}</ul>}
  {cursor&&<button type="button" onClick={()=>void load(cursor)}>صفحات پیش‌نویس قدیمی‌تر</button>}
  {centerId&&<div>
   <h3>{selected?'نسخه جدید برای صفحه موجود':'ساخت صفحه پیش‌نویس جدید'}</h3>
   <label>نشانی ثابت صفحه <input aria-label="شناسه نشانی صفحه" value={slugInput}
    disabled={Boolean(selected)} maxLength={80}
    onChange={e=>{setSlugInput(e.target.value);setSave('idle');}}/></label>
   <p>فقط حروف کوچک لاتین، عدد و خط تیره؛ نشانی صفحه پس از ایجاد ثابت است.</p>
   <label>عنوان صفحه <input aria-label="عنوان صفحه سایت" value={title} maxLength={200}
    onChange={e=>{setTitle(e.target.value);setSave('idle');}}/></label>
   <label>متن ساده صفحه <textarea aria-label="متن صفحه سایت" value={body} maxLength={20000}
    rows={5} onChange={e=>{setBody(e.target.value);setSave('idle');}}/></label>
   <p>محتوا به‌صورت متن ساده نمایش داده می‌شود، نه HTML قابل اجرا.</p>
   <button type="button" onClick={()=>void savePage()}
    disabled={save==='saving'||!slug.test(slugInput)||!title.trim()||title.length>200||
     body.length>20000||save==='saved'}>ذخیره نسخه خصوصی صفحه</button>
   {save==='saving'&&<p role="status">در حال ثبت نسخه…</p>}
   {save==='saved'&&<p role="status">نسخه پیش‌نویس ذخیره شد؛ صفحه منتشر نشده است.</p>}
   {save==='denied'&&<p role="alert">مجوز مستقل ویرایش صفحات تأیید نشده است.</p>}
   {save==='conflict'&&<p role="alert">نسخه صفحه تغییر کرده یا نشانی تکراری است؛ مجدد بارگیری کنید.</p>}
   {save==='error'&&<p role="alert">ذخیره پیش‌نویس تأیید نشد.</p>}
   {selected&&<>
    <button type="button" onClick={()=>void loadHistory(null)}>
     مشاهده تاریخچه نسخه‌ها</button>
    {historyState==='denied'&&<p role="alert">مجوز مستقل مشاهده تاریخچه وجود ندارد.</p>}
    {historyState==='error'&&<p role="alert">تاریخچه قابل دریافت نیست.</p>}
    {historyState==='ready'&&<ol>{history.map(h=><li key={h.revision}>
      نسخه {h.revision} — {h.title} (خصوصی)
      <pre style={{whiteSpace:'pre-wrap'}}>{h.bodyText}</pre>
    </li>)}</ol>}
    {historyCursor&&<button type="button" onClick={()=>void loadHistory(historyCursor)}>
      نسخه‌های قدیمی‌تر</button>}
   </>}
  </div>}
 </section>;
}

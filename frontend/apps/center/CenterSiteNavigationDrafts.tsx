import React,{useEffect,useRef,useState} from 'react';

type Page={pageId:string;slug:string;revision:number;title:string;bodyText:string;
 recordedAt:string;visibility:'PRIVATE_DRAFT';isPublished:false};
type Link={position:number;pageId:string;pinnedPageRevision:number;slug:string;title:string};
type Snapshot={revision:number;recordedAt:string;items:Link[];visibility:'PRIVATE_DRAFT';isPublished:false};
type State='idle'|'loading'|'ready'|'denied'|'error';
const uuid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const slug=/^[a-z0-9]+(-[a-z0-9]+)*$/;
const stamp=(x:unknown):x is string=>typeof x==='string'&&!Number.isNaN(Date.parse(x));
function page(x:unknown):x is Page{
 if(!x||typeof x!=='object')return false;
 const a=x as Record<string,unknown>;
 return typeof a.pageId==='string'&&uuid.test(a.pageId)&&
  typeof a.slug==='string'&&slug.test(a.slug)&&a.slug.length<=80&&
  Number.isSafeInteger(a.revision)&&Number(a.revision)>0&&
  typeof a.title==='string'&&a.title.trim().length>0&&a.title.length<=200&&
  typeof a.bodyText==='string'&&a.bodyText.length<=20000&&stamp(a.recordedAt)&&
  a.visibility==='PRIVATE_DRAFT'&&a.isPublished===false;
}
function links(raw:unknown):raw is Link[]{
 if(!Array.isArray(raw))return false;
 const seen=new Set<string>();
 return raw.every((x,i)=>{
  if(!x||typeof x!=='object')return false;
  const a=x as Record<string,unknown>;
  if(a.position!==i+1||typeof a.pageId!=='string'||!uuid.test(a.pageId)||
    seen.has(a.pageId.toLowerCase())||!Number.isSafeInteger(a.pinnedPageRevision)||
    Number(a.pinnedPageRevision)<=0||typeof a.slug!=='string'||!slug.test(a.slug)||
    typeof a.title!=='string'||!a.title.trim()||a.title.length>200)return false;
  seen.add(a.pageId.toLowerCase());return true;
 });
}
function validSnapshot(x:unknown):x is Snapshot{
 if(!x||typeof x!=='object')return false;
 const a=x as Record<string,unknown>;
 return Number.isSafeInteger(a.revision)&&Number(a.revision)>0&&stamp(a.recordedAt)&&
  links(a.items)&&a.visibility==='PRIVATE_DRAFT'&&a.isPublished===false;
}
const renumber=(values:Link[])=>values.map((x,i)=>({...x,position:i+1}));
export function CenterSiteNavigationDrafts({centerId,token}:{centerId:string;token:string}){
 const epoch=useRef(0),prefix='/api/v1/centers/'+encodeURIComponent(centerId)+'/site';
 const headers={Authorization:'Bearer '+token.trim()};
 const [sourcePages,setSourcePages]=useState<Page[]>([]);
 const [pagesStatus,setPagesStatus]=useState<State>('idle'),[pageCursor,setPageCursor]=useState<string|null>(null);
 const [navigation,setNavigation]=useState<Link[]>([]),[revision,setRevision]=useState<number|null>(null);
 const [navStatus,setNavStatus]=useState<State>('idle');
 const [selected,setSelected]=useState('');
 const [save,setSave]=useState<'idle'|'saving'|'saved'|'denied'|'conflict'|'error'>('idle');
 const [history,setHistory]=useState<Snapshot[]>([]);
 const [historyStatus,setHistoryStatus]=useState<State>('idle');
 const [historyCursor,setHistoryCursor]=useState<number|null>(null);
 useEffect(()=>()=>{epoch.current++;},[]);
 function change(values:Link[]){setNavigation(renumber(values));setSave('idle');}
 async function loadPages(before:string|null){
  if(!centerId||!token.trim()||pagesStatus==='loading')return;
  const n=++epoch.current;setPagesStatus('loading');
  if(before===null){setSourcePages([]);setPageCursor(null);}
  try{
   const response=await fetch(prefix+'/drafts'+
    (before?'?beforePageId='+encodeURIComponent(before):''),{headers,cache:'no-store'});
   if(epoch.current!==n)return;
   if(response.status===401||response.status===403){
    setSourcePages([]);setPageCursor(null);setPagesStatus('denied');return;
   }
   if(!response.ok)throw Error('Unavailable');
   const raw:unknown=await response.json();
   if(epoch.current!==n||!raw||typeof raw!=='object')throw Error('Invalid');
   const value=raw as Record<string,unknown>;
   if(!Array.isArray(value.pages)||value.pages.length>20||!value.pages.every(page)||
      !(value.nextBeforePageId===null||(typeof value.nextBeforePageId==='string'&&uuid.test(value.nextBeforePageId))))
    throw Error('Invalid source pages');
   const records=value.pages as Page[];
   if(records.some((x,i)=>(before!==null&&x.pageId.toLowerCase()>=before.toLowerCase())||
      (i>0&&records[i-1].pageId.toLowerCase()<=x.pageId.toLowerCase()))||
     (value.nextBeforePageId!==null&&(records.length!==20||
      records[19].pageId.toLowerCase()!==String(value.nextBeforePageId).toLowerCase())))
    throw Error('Invalid source pagination');
   setSourcePages(old=>before?[...old,...records]:records);
   setPageCursor(value.nextBeforePageId as string|null);setPagesStatus('ready');
  }catch{if(epoch.current===n){setSourcePages([]);setPageCursor(null);setPagesStatus('error');}}
 }
 async function loadNavigation(){
  if(!centerId||!token.trim()||navStatus==='loading')return;
  const n=++epoch.current;setNavStatus('loading');setNavigation([]);setRevision(null);setSave('idle');
  try{
   const response=await fetch(prefix+'/navigation',{headers,cache:'no-store'});
   if(epoch.current!==n)return;
   if(response.status===401||response.status===403){
    setNavStatus('denied');return;
   }
   if(!response.ok)throw Error('Unavailable');
   const raw:unknown=await response.json();
   if(epoch.current!==n||!raw||typeof raw!=='object')throw Error('Invalid');
   const value=raw as Record<string,unknown>;
   if(value.visibility!=='PRIVATE_DRAFT'||value.isPublished!==false||!links(value.items)||
     !(value.revision===null||(Number.isSafeInteger(value.revision)&&Number(value.revision)>0))||
     (value.revision===null&&(value.items as Link[]).length!==0))
    throw Error('Forged site navigation');
   setNavigation(value.items as Link[]);setRevision(value.revision as number|null);
   setNavStatus('ready');
  }catch{if(epoch.current===n){setNavigation([]);setNavStatus('error');}}
 }
 function add(){
  const source=sourcePages.find(p=>p.pageId+':'+p.revision===selected);
  if(!source||navigation.some(x=>x.pageId===source.pageId))return;
  change([...navigation,{position:navigation.length+1,pageId:source.pageId,
   pinnedPageRevision:source.revision,slug:source.slug,title:source.title}]);
  setSelected('');
 }
 function move(i:number,delta:number){
  const index=i+delta;if(index<0||index>=navigation.length)return;
  const next=[...navigation];[next[i],next[index]]=[next[index],next[i]];change(next);
 }
 async function saveNavigation(){
  if(!centerId||!token.trim()||navStatus!=='ready'||save==='saving')return;
  const n=++epoch.current;setSave('saving');
  const expectedRevision=revision??0;
  try{
   const response=await fetch(prefix+'/navigation/revisions',{
    method:'POST',headers:{...headers,'Content-Type':'application/json'},
    body:JSON.stringify({expectedRevision,items:navigation.map(x=>({
     pageId:x.pageId,pinnedPageRevision:x.pinnedPageRevision}))})
   });
   if(epoch.current!==n)return;
   if(response.status===401||response.status===403){setSave('denied');return;}
   if(response.status===409){setSave('conflict');return;}
   if(!response.ok)throw Error('Unavailable');
   const raw:unknown=await response.json();
   if(epoch.current!==n||!raw||typeof raw!=='object')throw Error('Invalid');
   const x=raw as Record<string,unknown>;
   if(x.revision!==expectedRevision+1||x.linkedPages!==navigation.length||
    x.visibility!=='PRIVATE_DRAFT'||x.isPublished!==false)
     throw Error('Forged write result');
   setRevision(expectedRevision+1);setSave('saved');setHistory([]);setHistoryCursor(null);
   setHistoryStatus('idle');
  }catch{if(epoch.current===n)setSave('error');}
 }
 async function loadHistory(before:number|null){
  if(!centerId||!token.trim()||historyStatus==='loading')return;
  const n=++epoch.current;setHistoryStatus('loading');
  if(before===null){setHistory([]);setHistoryCursor(null);}
  try{
   const response=await fetch(prefix+'/navigation/revisions'+
    (before===null?'':'?beforeRevision='+before),{headers,cache:'no-store'});
   if(epoch.current!==n)return;
   if(response.status===401||response.status===403){
    setHistory([]);setHistoryCursor(null);setHistoryStatus('denied');return;
   }
   if(!response.ok)throw Error('Unavailable');
   const raw:unknown=await response.json();
   if(epoch.current!==n||!raw||typeof raw!=='object')throw Error('Invalid');
   const x=raw as Record<string,unknown>;
   if(x.visibility!=='PRIVATE_DRAFT'||x.isPublished!==false||!Array.isArray(x.revisions)||
    x.revisions.length>20||!x.revisions.every(validSnapshot)||
    !(x.nextBeforeRevision===null||(Number.isSafeInteger(x.nextBeforeRevision)&&Number(x.nextBeforeRevision)>0)))
    throw Error('Forged navigation history');
   const rows=x.revisions as Snapshot[];
   if(rows.some((r,i)=>(before!==null&&r.revision>=before)||
     (i>0&&rows[i-1].revision<=r.revision))||
     (x.nextBeforeRevision!==null&&(rows.length!==20||
       rows[19].revision!==x.nextBeforeRevision)))throw Error('Invalid history order');
   setHistory(old=>before===null?rows:[...old,...rows]);
   setHistoryCursor(x.nextBeforeRevision as number|null);setHistoryStatus('ready');
  }catch{if(epoch.current===n){setHistory([]);setHistoryCursor(null);setHistoryStatus('error');}}
 }
 return <section aria-label="ویرایش منوی خصوصی کانون" style={{marginTop:'1rem',padding:12,border:'1px solid #aaa'}}>
  <h3>منوی صفحات سایت — پیش‌نویس خصوصی</h3>
  <p>فقط انتخاب و ترتیب صفحات موجود؛ هیچ لینک عمومی یا انتشار محتوا فعال نیست.</p>
  <button type="button" disabled={!centerId||!token.trim()||pagesStatus==='loading'}
   onClick={()=>void loadPages(null)}>خواندن صفحات واقعی کانون</button>{' '}
  <button type="button" disabled={!centerId||!token.trim()||navStatus==='loading'}
   onClick={()=>void loadNavigation()}>دریافت نسخه منوی خصوصی</button>
  {pagesStatus==='denied'&&<p role="alert">مجوز مستقل مشاهده صفحات وجود ندارد.</p>}
  {pagesStatus==='error'&&<p role="alert">داده معتبر صفحات سایت دریافت نشد.</p>}
  {pagesStatus==='ready'&&sourcePages.length===0&&<p>هنوز صفحه قابل انتخابی دریافت نشده است.</p>}
  {pageCursor!==null&&<button type="button" onClick={()=>void loadPages(pageCursor)}>صفحات قدیمی‌تر</button>}
  {navStatus==='denied'&&<p role="alert">مجوز مستقل مشاهده منوی خصوصی وجود ندارد.</p>}
  {navStatus==='error'&&<p role="alert">دریافت منوی معتبر ناموفق بود.</p>}
  {navStatus==='ready'&&<>
   <p>نسخه منو: {revision??'ثبت‌نشده'}؛ تعداد صفحات انتخاب‌شده: {navigation.length}</p>
   <label>انتخاب صفحه از مالک اصلی
    <select aria-label="انتخاب صفحه ناوبری" value={selected} onChange={e=>setSelected(e.target.value)}>
     <option value="">صفحه معتبر را انتخاب کنید</option>
     {sourcePages.filter(p=>!navigation.some(x=>x.pageId===p.pageId)).map(p=>
      <option key={p.pageId+':'+p.revision} value={p.pageId+':'+p.revision}>
       {p.title} — /{p.slug} — نسخه {p.revision}</option>)}
    </select>
   </label>
   <button type="button" disabled={!selected} onClick={add}>افزودن صفحه به منو</button>
   <nav aria-label="پیش‌نمایش متنی منوی خصوصی">
    <ol>{navigation.map((p,i)=><li key={p.pageId}>
     <strong>{p.title}</strong> — /{p.slug} — نسخه منبع {p.pinnedPageRevision}{' '}
     <button type="button" aria-label={'انتقال صفحه '+p.slug+' به بالا'}
      disabled={i===0} onClick={()=>move(i,-1)}>بالا</button>{' '}
     <button type="button" aria-label={'انتقال صفحه '+p.slug+' به پایین'}
      disabled={i===navigation.length-1} onClick={()=>move(i,1)}>پایین</button>{' '}
     <button type="button" aria-label={'حذف صفحه '+p.slug+' از منو'}
      onClick={()=>change(navigation.filter(x=>x.pageId!==p.pageId))}>حذف</button>
    </li>)}</ol>
   </nav>
   <button type="button" disabled={save==='saving'} onClick={()=>void saveNavigation()}>
    ذخیره نسخه جدید منو</button>
   {save==='saved'&&<p role="status">نسخه خصوصی منو ذخیره شد؛ سایت منتشر نشده است.</p>}
   {save==='denied'&&<p role="alert">مجوز مستقل ذخیره منوی کانون وجود ندارد.</p>}
   {save==='conflict'&&<p role="alert">نسخه منو یا صفحه مبنا تغییر کرده است؛ داده را تازه کنید.</p>}
   {save==='error'&&<p role="alert">تأیید معتبر ذخیره منو دریافت نشد.</p>}
   <button type="button" onClick={()=>void loadHistory(null)}>تاریخچه نسخه‌های منو</button>
   {historyStatus==='denied'&&<p role="alert">مجوز خواندن تاریخچه منو وجود ندارد.</p>}
   {historyStatus==='error'&&<p role="alert">تاریخچه معتبر منو دریافت نشد.</p>}
   {historyStatus==='ready'&&<ul>{history.map(h=><li key={h.revision}>
    نسخه {h.revision} — {h.items.map(x=>x.title).join(' ← ')} — PRIVATE_DRAFT
   </li>)}</ul>}
   {historyCursor!==null&&<button type="button" onClick={()=>void loadHistory(historyCursor)}>
    نسخه‌های قدیمی‌تر منو</button>}
  </>}
 </section>;
}

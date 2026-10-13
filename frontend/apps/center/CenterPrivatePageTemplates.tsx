import React,{useEffect,useRef,useState} from 'react';
type Page={pageId:string;slug:string;revision:number;title:string;bodyText:string;
 recordedAt:string;visibility:'PRIVATE_DRAFT';isPublished:false};
type Template={templateId:string;sourcePageId:string;revision:number;pinnedPageRevision:number;
 label:string;sourceSlug:string;title:string;bodyText:string;recordedAt:string;
 visibility:'PRIVATE_TEMPLATE';isPublished:false;isShared:false};
type History={revision:number;pinnedPageRevision:number;label:string;sourceSlug:string;
 title:string;bodyText:string;recordedAt:string;visibility:'PRIVATE_TEMPLATE';
 isPublished:false;isShared:false};
type Load='idle'|'loading'|'ready'|'denied'|'error';
type Save='idle'|'saving'|'saved'|'denied'|'conflict'|'error';
const uuid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const slugPattern=/^[a-z0-9]+(-[a-z0-9]+)*$/;
const date=(x:unknown):x is string=>typeof x==='string'&&!Number.isNaN(Date.parse(x));
function page(x:unknown):x is Page{
 if(!x||typeof x!=='object')return false;const v=x as Record<string,unknown>;
 return typeof v.pageId==='string'&&uuid.test(v.pageId)&&
  typeof v.slug==='string'&&slugPattern.test(v.slug)&&v.slug.length<=80&&
  Number.isSafeInteger(v.revision)&&Number(v.revision)>0&&
  typeof v.title==='string'&&v.title.trim().length>0&&v.title.length<=200&&
  typeof v.bodyText==='string'&&v.bodyText.length<=20000&&date(v.recordedAt)&&
  v.visibility==='PRIVATE_DRAFT'&&v.isPublished===false;
}
function template(x:unknown):x is Template{
 if(!x||typeof x!=='object')return false;const v=x as Record<string,unknown>;
 return typeof v.templateId==='string'&&uuid.test(v.templateId)&&
  typeof v.sourcePageId==='string'&&uuid.test(v.sourcePageId)&&
  Number.isSafeInteger(v.revision)&&Number(v.revision)>0&&
  Number.isSafeInteger(v.pinnedPageRevision)&&Number(v.pinnedPageRevision)>0&&
  typeof v.label==='string'&&v.label.trim().length>0&&v.label.length<=160&&
  typeof v.sourceSlug==='string'&&slugPattern.test(v.sourceSlug)&&
  typeof v.title==='string'&&v.title.trim().length>0&&v.title.length<=200&&
  typeof v.bodyText==='string'&&v.bodyText.length<=20000&&date(v.recordedAt)&&
  v.visibility==='PRIVATE_TEMPLATE'&&v.isPublished===false&&v.isShared===false;
}
function history(x:unknown):x is History{
 if(!x||typeof x!=='object')return false;const v=x as Record<string,unknown>;
 return Number.isSafeInteger(v.revision)&&Number(v.revision)>0&&
  Number.isSafeInteger(v.pinnedPageRevision)&&Number(v.pinnedPageRevision)>0&&
  typeof v.label==='string'&&v.label.trim().length>0&&v.label.length<=160&&
  typeof v.sourceSlug==='string'&&slugPattern.test(v.sourceSlug)&&
  typeof v.title==='string'&&v.title.trim().length>0&&v.title.length<=200&&
  typeof v.bodyText==='string'&&v.bodyText.length<=20000&&date(v.recordedAt)&&
  v.visibility==='PRIVATE_TEMPLATE'&&v.isPublished===false&&v.isShared===false;
}
export function CenterPrivatePageTemplates({centerId,token}:{centerId:string;token:string}){
 const epoch=useRef(0),prefix='/api/v1/centers/'+encodeURIComponent(centerId)+'/site';
 const headers={Authorization:'Bearer '+token.trim()};
 const [pages,setPages]=useState<Page[]>([]),[pageCursor,setPageCursor]=useState<string|null>(null);
 const [pageState,setPageState]=useState<Load>('idle');
 const [items,setItems]=useState<Template[]>([]),[cursor,setCursor]=useState<string|null>(null);
 const [state,setState]=useState<Load>('idle');
 const [chosen,setChosen]=useState(''),[selected,setSelected]=useState<Template|null>(null);
 const [label,setLabel]=useState(''),[save,setSave]=useState<Save>('idle');
 const [versions,setVersions]=useState<History[]>([]),[historyState,setHistoryState]=useState<Load>('idle');
 const [historyCursor,setHistoryCursor]=useState<number|null>(null);
 const [cloneSource,setCloneSource]=useState<Template|null>(null),[cloneSlug,setCloneSlug]=useState('');
 const [cloneTitle,setCloneTitle]=useState(''),[cloneBody,setCloneBody]=useState('');
 const [cloneState,setCloneState]=useState<Save>('idle');
 useEffect(()=>()=>{epoch.current++;},[]);
 function select(t:Template|null){
  epoch.current++;setSelected(t);setChosen(t?t.sourcePageId+':'+t.pinnedPageRevision:'');
  setLabel(t?.label??'');setSave('idle');setVersions([]);
  setHistoryState('idle');setHistoryCursor(null);
 }
 async function loadPages(before:string|null){
  if(!centerId||!token.trim()||pageState==='loading')return;
  const run=++epoch.current;setPageState('loading');
  if(before===null){setPages([]);setPageCursor(null);}
  try{
   const r=await fetch(prefix+'/drafts'+(before?'?beforePageId='+encodeURIComponent(before):''),
    {headers,cache:'no-store'});
   if(run!==epoch.current)return;
   if(r.status===401||r.status===403){setPages([]);setPageCursor(null);setPageState('denied');return;}
   if(!r.ok)throw Error('Page permission');
   const raw:unknown=await r.json();
   if(run!==epoch.current||!raw||typeof raw!=='object')throw Error('Invalid');
   const x=raw as Record<string,unknown>;
   if(!Array.isArray(x.pages)||x.pages.length>20||!x.pages.every(page)||
     !(x.nextBeforePageId===null||(typeof x.nextBeforePageId==='string'&&uuid.test(x.nextBeforePageId))))
    throw Error('Forged source pages');
   const records=x.pages as Page[];
   if(records.some((p,i)=>(before!==null&&p.pageId.toLowerCase()>=before.toLowerCase())||
    (i>0&&records[i-1].pageId.toLowerCase()<=p.pageId.toLowerCase()))||
    (x.nextBeforePageId!==null&&(records.length!==20||
    records[19].pageId.toLowerCase()!==String(x.nextBeforePageId).toLowerCase())))
    throw Error('Invalid page cursor');
   setPages(old=>before?[...old,...records]:records);
   setPageCursor(x.nextBeforePageId as string|null);setPageState('ready');
  }catch{if(epoch.current===run){setPages([]);setPageCursor(null);setPageState('error');}}
 }
 async function loadTemplates(before:string|null){
  if(!centerId||!token.trim()||state==='loading')return;
  const run=++epoch.current;setState('loading');
  if(before===null){setItems([]);setCursor(null);select(null);}
  try{
   const r=await fetch(prefix+'/templates'+(before?'?beforeTemplateId='+encodeURIComponent(before):''),
    {headers,cache:'no-store'});
   if(run!==epoch.current)return;
   if(r.status===401||r.status===403){setItems([]);setCursor(null);setState('denied');return;}
   if(!r.ok)throw Error('No access');
   const raw:unknown=await r.json();
   if(run!==epoch.current||!raw||typeof raw!=='object')throw Error('Invalid');
   const x=raw as Record<string,unknown>;
   if(x.visibility!=='PRIVATE_TEMPLATE'||x.isPublished!==false||x.isShared!==false||
    !Array.isArray(x.templates)||x.templates.length>20||!x.templates.every(template)||
    !(x.nextBeforeTemplateId===null||
      (typeof x.nextBeforeTemplateId==='string'&&uuid.test(x.nextBeforeTemplateId))))
    throw Error('Forged templates');
   const records=x.templates as Template[];
   if(records.some((p,i)=>(before!==null&&p.templateId.toLowerCase()>=before.toLowerCase())||
    (i>0&&records[i-1].templateId.toLowerCase()<=p.templateId.toLowerCase()))||
    (x.nextBeforeTemplateId!==null&&(records.length!==20||
      records[19].templateId.toLowerCase()!==String(x.nextBeforeTemplateId).toLowerCase())))
    throw Error('Bad cursor');
   setItems(old=>before?[...old,...records]:records);
   setCursor(x.nextBeforeTemplateId as string|null);setState('ready');
  }catch{if(epoch.current===run){setItems([]);setCursor(null);setState('error');}}
 }
 async function append(){
  if(!centerId||!token.trim()||!label.trim()||label.length>160||save==='saving')return;
  const original=selected?{pageId:selected.sourcePageId,revision:selected.pinnedPageRevision}:
   pages.find(p=>p.pageId+':'+p.revision===chosen);
  if(!original)return;
  const run=++epoch.current,templateId=selected?.templateId??crypto.randomUUID(),
   expectedRevision=selected?.revision??0;
  setSave('saving');
  try{
   const r=await fetch(prefix+'/templates/revisions',{
    method:'POST',headers:{...headers,'Content-Type':'application/json'},
    body:JSON.stringify({templateId,sourcePageId:original.pageId,
      pinnedPageRevision:original.revision,expectedRevision,label:label.trim()})});
   if(run!==epoch.current)return;
   if(r.status===401||r.status===403){setSave('denied');return;}
   if(r.status===409){setSave('conflict');return;}
   if(!r.ok)throw Error('Unavailable');
   const raw:unknown=await r.json();
   if(run!==epoch.current||!raw||typeof raw!=='object')throw Error('Invalid');
   const x=raw as Record<string,unknown>;
   if(x.templateId!==templateId||x.sourcePageId!==original.pageId||
    x.pinnedPageRevision!==original.revision||x.revision!==expectedRevision+1||
    x.visibility!=='PRIVATE_TEMPLATE'||x.isPublished!==false||x.isShared!==false)
    throw Error('Forged template acknowledgement');
   setSave('saved');setItems([]);setCursor(null);setState('idle');
   setSelected(null);setVersions([]);setHistoryCursor(null);setHistoryState('idle');
  }catch{if(epoch.current===run)setSave('error');}
 }
 async function loadHistory(before:number|null){
  if(!selected||historyState==='loading')return;
  const item=selected,run=++epoch.current;setHistoryState('loading');
  if(before===null){setVersions([]);setHistoryCursor(null);}
  try{
   const r=await fetch(prefix+'/templates/'+encodeURIComponent(item.templateId)+'/revisions'+
    (before===null?'':'?beforeRevision='+before),{headers,cache:'no-store'});
   if(run!==epoch.current)return;
   if(r.status===401||r.status===403){setVersions([]);setHistoryState('denied');return;}
   if(!r.ok)throw Error('Unavailable');
   const raw:unknown=await r.json();if(run!==epoch.current||!raw||typeof raw!=='object')throw Error('Invalid');
   const x=raw as Record<string,unknown>;
   if(x.templateId!==item.templateId||x.sourcePageId!==item.sourcePageId||
    x.visibility!=='PRIVATE_TEMPLATE'||x.isPublished!==false||x.isShared!==false||
    !Array.isArray(x.revisions)||x.revisions.length>20||!x.revisions.every(history)||
    !(x.nextBeforeRevision===null||
      (Number.isSafeInteger(x.nextBeforeRevision)&&Number(x.nextBeforeRevision)>0)))
     throw Error('Forged template history');
   const rows=x.revisions as History[];
   if(rows.some((v,i)=>(before!==null&&v.revision>=before)||
    (i>0&&rows[i-1].revision<=v.revision))||
    (x.nextBeforeRevision!==null&&(rows.length!==20||
    rows[19].revision!==x.nextBeforeRevision)))throw Error('Bad history');
   setVersions(old=>before===null?rows:[...old,...rows]);
   setHistoryCursor(x.nextBeforeRevision as number|null);setHistoryState('ready');
  }catch{if(epoch.current===run){setVersions([]);setHistoryCursor(null);setHistoryState('error');}}
 }
 function prepareClone(t:Template){
  epoch.current++;setCloneSource(t);setCloneSlug('');setCloneTitle(t.title);
  setCloneBody(t.bodyText);setCloneState('idle');
 }
 async function createPage(){
  const src=cloneSource;
  if(!src||!centerId||!token.trim()||!slugPattern.test(cloneSlug)||
   cloneSlug.length>80||!cloneTitle.trim()||cloneTitle.length>200||
   cloneBody.length>20000||cloneState==='saving')return;
  const run=++epoch.current,pageId=crypto.randomUUID();setCloneState('saving');
  try{
   // This is a normal S20 page creation; permission SITE_DRAFT_APPEND is checked independently.
   const r=await fetch(prefix+'/drafts/revisions',{method:'POST',
    headers:{...headers,'Content-Type':'application/json'},
    body:JSON.stringify({pageId,expectedRevision:0,slug:cloneSlug,
      title:cloneTitle.trim(),bodyText:cloneBody})});
   if(run!==epoch.current)return;
   if(r.status===401||r.status===403){setCloneState('denied');return;}
   if(r.status===409){setCloneState('conflict');return;}
   if(!r.ok)throw Error('Unavailable');
   const raw:unknown=await r.json();if(run!==epoch.current||!raw||typeof raw!=='object')throw Error('Invalid');
   const x=raw as Record<string,unknown>;
   if(x.pageId!==pageId||x.slug!==cloneSlug||x.revision!==1||
    x.visibility!=='PRIVATE_DRAFT'||x.isPublished!==false)
    throw Error('Forged page creation acknowledgement');
   setCloneState('saved');
  }catch{if(epoch.current===run)setCloneState('error');}
 }
 return <section aria-label="کتابخانه قالب خصوصی صفحات" style={{border:'1px solid #aaa',padding:12,marginTop:16}}>
  <h3>کتابخانه قالب‌های صفحات کانون</h3>
  <p>قالب از متن یک نسخه واقعی و خصوصی همین کانون ساخته می‌شود؛
   استفاده مجدد، انتشار یا اشتراک بین کانون‌ها نیست.</p>
  <button type="button" onClick={()=>void loadPages(null)}
   disabled={!centerId||!token.trim()||pageState==='loading'}>دریافت صفحات مبنای قالب</button>{' '}
  <button type="button" onClick={()=>void loadTemplates(null)}
   disabled={!centerId||!token.trim()||state==='loading'}>دریافت قالب‌های خصوصی</button>
  {pageState==='denied'&&<p role="alert">مجوز مشاهده صفحات اصلی وجود ندارد.</p>}
  {pageState==='error'&&<p role="alert">فهرست معتبر صفحات مبنا دریافت نشد.</p>}
  {state==='denied'&&<p role="alert">مجوز مستقل خواندن قالب خصوصی وجود ندارد.</p>}
  {state==='error'&&<p role="alert">قالب‌های معتبر دریافت نشدند.</p>}
  {pageCursor&&<button type="button" onClick={()=>void loadPages(pageCursor)}>صفحات مبنای بیشتر</button>}
  {state==='ready'&&<ul>{items.map(t=><li key={t.templateId}>
   <strong>{t.label}</strong> — {t.title} /{t.sourceSlug} (نسخه اصلی {t.pinnedPageRevision})
   <button type="button" onClick={()=>select(t)}>ویرایش قالب و تاریخچه</button>{' '}
   <button type="button" onClick={()=>prepareClone(t)}>ساخت صفحه خصوصی از این قالب</button>
  </li>)}</ul>}
  {cursor&&<button type="button" onClick={()=>void loadTemplates(cursor)}>قالب‌های بیشتر</button>}
  {selected&&<button type="button" onClick={()=>select(null)}>ساخت قالب جدید</button>}
  {!selected&&<label>صفحه و نسخه مبنا <select aria-label="انتخاب صفحه مبنای قالب" value={chosen}
   onChange={e=>setChosen(e.target.value)}><option value="">انتخاب کنید</option>
   {pages.map(p=><option key={p.pageId} value={p.pageId+':'+p.revision}>
    {p.title} /{p.slug} — نسخه {p.revision}</option>)}</select></label>}
  {selected&&<p>مبنای این قالب ثابت است: /{selected.sourceSlug}</p>}
  <label>نام قالب <input aria-label="نام قالب خصوصی" maxLength={160} value={label}
   onChange={e=>{setLabel(e.target.value);setSave('idle');}}/></label>
  <button type="button" disabled={!label.trim()||save==='saving'||(!selected&&!chosen)}
   onClick={()=>void append()}>ثبت نسخه قالب خصوصی</button>
  {save==='saved'&&<p role="status">نسخه قالب خصوصی ذخیره شد؛ هیچ صفحه‌ای منتشر نشده است.</p>}
  {save==='denied'&&<p role="alert">مجوز مستقل ثبت قالب و خواندن صفحه وجود ندارد.</p>}
  {save==='conflict'&&<p role="alert">نسخه قالب یا صفحه مبنا معتبر نیست؛ اطلاعات را تازه کنید.</p>}
  {save==='error'&&<p role="alert">تأیید معتبر ذخیره قالب دریافت نشد.</p>}
  {selected&&<><button type="button" onClick={()=>void loadHistory(null)}>تاریخچه نسخه‌های قالب</button>
   {historyState==='denied'&&<p role="alert">مجوز مستقل تاریخچه قالب موجود نیست.</p>}
   {historyState==='error'&&<p role="alert">تاریخچه معتبر قالب دریافت نشد.</p>}
   {historyState==='ready'&&<ul>{versions.map(v=><li key={v.revision}>
    نسخه قالب {v.revision} — {v.label} / نسخه صفحه {v.pinnedPageRevision}
   </li>)}</ul>}
   {historyCursor&&<button type="button" onClick={()=>void loadHistory(historyCursor)}>
    تاریخچه قدیمی‌تر قالب</button>}
  </>}
  {cloneSource&&<section aria-label="ساخت صفحه خصوصی از قالب">
   <h4>پیش‌نویس جدید از قالب: {cloneSource.label}</h4>
   <p>متن قالب برای ویرایش محلی کپی می‌شود؛ نسخه مبنا و خود قالب تغییر نمی‌کنند.
    این اقدام نیازمند مجوز جداگانه ایجاد صفحه است.</p>
   <label>نشانی خصوصی جدید <input aria-label="اسلاگ صفحه جدید" value={cloneSlug}
    maxLength={80} onChange={e=>setCloneSlug(e.target.value)}/></label>
   <label>عنوان صفحه جدید <input aria-label="عنوان صفحه جدید" value={cloneTitle}
    maxLength={200} onChange={e=>setCloneTitle(e.target.value)}/></label>
   <label>متن صفحه جدید <textarea aria-label="متن صفحه جدید" value={cloneBody}
    maxLength={20000} onChange={e=>setCloneBody(e.target.value)}/></label>
   <button type="button" disabled={!slugPattern.test(cloneSlug)||!cloneTitle.trim()||
    cloneState==='saving'||cloneState==='saved'} onClick={()=>void createPage()}>
    ایجاد پیش‌نویس صفحه از قالب</button>
   {cloneState==='saved'&&<p role="status">صفحه جدید خصوصی ذخیره شد؛ انتشار یا اشتراک انجام نشده است.</p>}
   {cloneState==='denied'&&<p role="alert">مجوز مستقل ایجاد صفحه جدید تأیید نشد.</p>}
   {cloneState==='conflict'&&<p role="alert">نشانی یا شناسه صفحه جدید تکراری است.</p>}
   {cloneState==='error'&&<p role="alert">تأیید معتبر ایجاد صفحه جدید دریافت نشد.</p>}
  </section>}
 </section>;
}

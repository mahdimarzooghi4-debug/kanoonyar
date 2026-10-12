import React,{useEffect,useRef,useState} from 'react';

type Layout='EDITORIAL'|'HERO'|'COMPACT';
type Palette='OCEAN'|'FOREST'|'EARTH';
type Typeface='SYSTEM'|'SERIF';
type Design={revision:number;layoutKey:Layout;paletteKey:Palette;typeKey:Typeface;
 headerText:string;footerText:string;showIntro:boolean;showFooter:boolean;
 recordedAt?:string;visibility:'PRIVATE_DRAFT';isPublished:false};
type Page={pageId:string;slug:string;revision:number;title:string;bodyText:string;
 recordedAt:string;visibility:'PRIVATE_DRAFT';isPublished:false};
type Load='idle'|'loading'|'ready'|'denied'|'error';
type Save='idle'|'saving'|'saved'|'denied'|'conflict'|'error';
const layouts:Layout[]=['EDITORIAL','HERO','COMPACT'];
const palettes:Palette[]=['OCEAN','FOREST','EARTH'];
const types:Typeface[]=['SYSTEM','SERIF'];
const uuid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const paletteStyle:Record<Palette,{foreground:string;background:string;accent:string}>={
 OCEAN:{foreground:'#123047',background:'#eff8ff',accent:'#17669c'},
 FOREST:{foreground:'#173b2c',background:'#eef8f1',accent:'#2a7550'},
 EARTH:{foreground:'#49341e',background:'#fff6e9',accent:'#965a24'}
};
function isDesign(raw:unknown):raw is Design {
 if(!raw||typeof raw!=='object')return false;
 const x=raw as Record<string,unknown>;
 return Number.isSafeInteger(x.revision)&&Number(x.revision)>0&&
  layouts.includes(x.layoutKey as Layout)&&palettes.includes(x.paletteKey as Palette)&&
  types.includes(x.typeKey as Typeface)&&typeof x.headerText==='string'&&
  x.headerText.trim().length>0&&x.headerText.length<=120&&
  typeof x.footerText==='string'&&x.footerText.length<=240&&
  typeof x.showIntro==='boolean'&&typeof x.showFooter==='boolean'&&
  x.visibility==='PRIVATE_DRAFT'&&x.isPublished===false;
}
function isPage(raw:unknown):raw is Page {
 if(!raw||typeof raw!=='object')return false;
 const x=raw as Record<string,unknown>;
 return typeof x.pageId==='string'&&uuid.test(x.pageId)&&
  typeof x.slug==='string'&&/^[a-z0-9]+(-[a-z0-9]+)*$/.test(x.slug)&&
  Number.isSafeInteger(x.revision)&&Number(x.revision)>0&&
  typeof x.title==='string'&&typeof x.bodyText==='string'&&
  x.visibility==='PRIVATE_DRAFT'&&x.isPublished===false;
}
function SitePreview({theme,page}:{theme:Design;page:Page}){
 const color=paletteStyle[theme.paletteKey];
 const isHero=theme.layoutKey==='HERO',isCompact=theme.layoutKey==='COMPACT';
 return <article aria-label="پیش‌نمایش خصوصی صفحه" dir="rtl"
  style={{marginTop:12,padding:isCompact?12:30,border:'2px dashed '+color.accent,
   background:color.background,color:color.foreground,maxWidth:isCompact?480:700,
   fontFamily:theme.typeKey==='SERIF'?'Georgia, serif':'system-ui, sans-serif'}}>
  <p><strong>پیش‌نمایش خصوصی — منتشر نشده</strong></p>
  <header style={{paddingBottom:12,borderBottom:'1px solid '+color.accent}}>
   <strong>{theme.headerText}</strong>
  </header>
  {theme.showIntro&&<section style={{textAlign:isHero?'center':'right',padding:isHero?24:8}}>
   <p>معرفی این کانون — نسخه نمایشی خصوصی</p>
  </section>}
  <section style={{textAlign:isHero?'center':'right'}}>
   <h3 style={{color:color.accent,fontSize:isHero?28:isCompact?18:24}}>{page.title}</h3>
   <p style={{whiteSpace:'pre-wrap',overflowWrap:'anywhere'}}>{page.bodyText}</p>
  </section>
  {theme.showFooter&&<footer style={{borderTop:'1px solid '+color.accent,paddingTop:12}}>
   {theme.footerText}
  </footer>}
 </article>;
}
export function CenterSiteDesignDrafts({centerId,token}:{centerId:string;token:string}){
 const epoch=useRef(0);
 const [state,setState]=useState<Load>('idle');
 const [saved,setSaved]=useState<Design|null>(null);
 const [layout,setLayout]=useState<Layout>('EDITORIAL');
 const [palette,setPalette]=useState<Palette>('OCEAN');
 const [type,setType]=useState<Typeface>('SYSTEM');
 const [header,setHeader]=useState('');
 const [footer,setFooter]=useState('');
 const [intro,setIntro]=useState(true);
 const [showFooter,setShowFooter]=useState(false);
 const [save,setSave]=useState<Save>('idle');
 const [pages,setPages]=useState<Page[]>([]);
 const [pageState,setPageState]=useState<Load>('idle');
 const [chosen,setChosen]=useState<string>('');
 const [history,setHistory]=useState<Design[]>([]);
 const [historyState,setHistoryState]=useState<Load>('idle');
 const [before,setBefore]=useState<number|null>(null);
 const prefix='/api/v1/centers/'+encodeURIComponent(centerId)+'/site';
 const headers={Authorization:'Bearer '+token.trim()};
 useEffect(()=>{epoch.current++;setState('idle');setSaved(null);setSave('idle');
  setLayout('EDITORIAL');setPalette('OCEAN');setType('SYSTEM');setHeader('');setFooter('');
  setIntro(true);setShowFooter(false);setPages([]);setPageState('idle');setChosen('');
  setHistory([]);setHistoryState('idle');setBefore(null);
  return()=>{epoch.current++};
 },[centerId,token]);
 function hydrate(design:Design){
  setLayout(design.layoutKey);setPalette(design.paletteKey);setType(design.typeKey);
  setHeader(design.headerText);setFooter(design.footerText);
  setIntro(design.showIntro);setShowFooter(design.showFooter);
 }
 async function loadDesign(){
  if(!centerId||!token.trim()||state==='loading')return;
  const request=++epoch.current;
  setState('loading');setSaved(null);setHistory([]);setBefore(null);setSave('idle');
  setPages([]);setPageState('idle');setChosen('');
  try{
   const resp=await fetch(prefix+'/design',{headers,cache:'no-store'});
   if(request!==epoch.current)return;
   if(resp.status===401||resp.status===403){setState('denied');return;}
   if(!resp.ok)throw Error('Unavailable');
   const raw:unknown=await resp.json();
   if(request!==epoch.current||!raw||typeof raw!=='object')throw Error('Invalid body');
   const x=raw as Record<string,unknown>;
   if(x.visibility!=='PRIVATE_DRAFT'||x.isPublished!==false||
     !((x.revision===null&&x.design===null)||
       (isDesign(x.design)&&x.revision===x.design.revision)))throw Error('Published response');
   setSaved(x.design as Design|null);
   if(isDesign(x.design))hydrate(x.design);
   setState('ready');
  }catch{if(request===epoch.current){setSaved(null);setState('error');}}
 }
 async function append(){
  if(!centerId||!token.trim()||!header.trim()||header.length>120||
     footer.length>240||save==='saving')return;
  const request=++epoch.current,expected=saved?.revision??0;
  setSave('saving');
  const payload={expectedRevision:expected,layoutKey:layout,paletteKey:palette,
   typeKey:type,headerText:header.trim(),footerText:footer,showIntro:intro,
   showFooter};
  try{
   const resp=await fetch(prefix+'/design/revisions',{
    method:'POST',headers:{...headers,'Content-Type':'application/json'},body:JSON.stringify(payload)});
   if(request!==epoch.current)return;
   if(resp.status===401||resp.status===403){setSave('denied');return;}
   if(resp.status===409){setSave('conflict');return;}
   if(!resp.ok)throw Error('Unavailable');
   const raw:unknown=await resp.json();
   if(request!==epoch.current||!isDesign(raw)||raw.revision!==expected+1||
      raw.layoutKey!==layout||raw.paletteKey!==palette||raw.typeKey!==type||
      raw.headerText!==header.trim()||raw.footerText!==footer||
      raw.showIntro!==intro||raw.showFooter!==showFooter)
       throw Error('Invalid draft acknowledgement');
   setSaved(raw);setState('ready');setSave('saved');
   setHistory([]);setHistoryState('idle');setBefore(null);
  }catch{if(request===epoch.current)setSave('error');}
 }
 async function loadHistory(cursor:number|null){
  if(!saved||historyState==='loading')return;
  const request=++epoch.current;setHistoryState('loading');
  if(cursor===null){setHistory([]);setBefore(null);}
  try{
   const resp=await fetch(prefix+'/design/revisions'+
     (cursor===null?'':'?beforeRevision='+cursor),{headers,cache:'no-store'});
   if(request!==epoch.current)return;
   if(resp.status===401||resp.status===403){setHistory([]);setHistoryState('denied');return;}
   if(!resp.ok)throw Error('Unavailable');
   const raw:unknown=await resp.json();
   if(request!==epoch.current||!raw||typeof raw!=='object')throw Error('Invalid');
   const x=raw as Record<string,unknown>;
   if(x.visibility!=='PRIVATE_DRAFT'||x.isPublished!==false||
       !Array.isArray(x.revisions)||x.revisions.length>20||
       !x.revisions.every(isDesign)||!
       (x.nextBeforeRevision===null||
          (Number.isSafeInteger(x.nextBeforeRevision)&&Number(x.nextBeforeRevision)>0)))
     throw Error('Forged history');
   const records=x.revisions as Design[];
   if(records.some((item,i)=>(cursor!==null&&item.revision>=cursor)||
        (i>0&&records[i-1].revision<=item.revision)))throw Error('Bad order');
   if(x.nextBeforeRevision!==null&&
     (records.length!==20||records[19].revision!==x.nextBeforeRevision))
     throw Error('Bad cursor');
   setHistory(old=>cursor===null?records:[...old,...records]);
   setBefore(x.nextBeforeRevision as number|null);setHistoryState('ready');
  }catch{if(request===epoch.current){setHistory([]);setHistoryState('error');}}
 }
 async function loadPages(){
  if(!saved||pageState==='loading')return;
  const request=++epoch.current;setPages([]);setChosen('');setPageState('loading');
  try{
   const resp=await fetch(prefix+'/drafts',{headers,cache:'no-store'});
   if(request!==epoch.current)return;
   if(resp.status===401||resp.status===403){setPageState('denied');return;}
   if(!resp.ok)throw Error('Unavailable');
   const raw:unknown=await resp.json();
   if(request!==epoch.current||!raw||typeof raw!=='object')throw Error('Invalid');
   const x=raw as Record<string,unknown>;
   if(!Array.isArray(x.pages)||x.pages.length>20||!x.pages.every(isPage)||
     !(x.nextBeforePageId===null||
       (typeof x.nextBeforePageId==='string'&&uuid.test(x.nextBeforePageId))))
    throw Error('Unsafe page');
   const records=x.pages as Page[];
   setPages(records);setChosen(records[0]?.pageId??'');setPageState('ready');
  }catch{if(request===epoch.current){setPages([]);setPageState('error');}}
 }
 const previewPage=pages.find(p=>p.pageId===chosen);
 return <section aria-label="طراحی خصوصی سایت کانون" style={{marginTop:'1.5rem'}}>
  <h2>قالب و هویت بصری پیش‌نویس کانون</h2>
  <p>این ابزار فقط ظاهر صفحه‌های پیش‌نویس را در پنل نمایش می‌دهد؛ هیچ انتشار عمومی انجام نمی‌شود.</p>
  <button type="button" disabled={!centerId||state==='loading'} onClick={()=>void loadDesign()}>
   دریافت تنظیمات خصوصی سایت</button>
  {state==='loading'&&<p role="status">در حال دریافت تنظیمات…</p>}
  {state==='denied'&&<p role="alert">مجوز مستقل مشاهده تنظیمات سایت وجود ندارد.</p>}
  {state==='error'&&<p role="alert">تنظیمات معتبر دریافت نشد.</p>}
  {state==='ready'&&<p>نسخه فعلی: {saved?.revision??'ثبت‌نشده'} — فقط PRIVATE_DRAFT</p>}
  {state==='ready'&&<fieldset>
   <legend>تنظیمات نسخه خصوصی</legend>
   <label>چیدمان <select aria-label="چیدمان سایت" value={layout}
    onChange={e=>{setLayout(e.target.value as Layout);setSave('idle');}}>
     <option value="EDITORIAL">ستونی</option><option value="HERO">عنوان برجسته</option>
     <option value="COMPACT">فشرده</option>
   </select></label>
   <label>رنگ‌بندی <select aria-label="رنگ‌بندی سایت" value={palette}
    onChange={e=>{setPalette(e.target.value as Palette);setSave('idle');}}>
     <option value="OCEAN">آبی</option><option value="FOREST">سبز</option>
     <option value="EARTH">خاکی</option>
   </select></label>
   <label>قلم <select aria-label="قلم سایت" value={type}
    onChange={e=>{setType(e.target.value as Typeface);setSave('idle');}}>
     <option value="SYSTEM">سیستمی</option><option value="SERIF">سریف</option>
   </select></label>
   <label>عنوان سربرگ <input aria-label="عنوان سربرگ" value={header} maxLength={120}
    onChange={e=>{setHeader(e.target.value);setSave('idle');}} /></label>
   <label>متن پابرگ <input aria-label="متن پابرگ" value={footer} maxLength={240}
    onChange={e=>{setFooter(e.target.value);setSave('idle');}} /></label>
   <label><input type="checkbox" checked={intro} onChange={e=>{
    setIntro(e.target.checked);setSave('idle');}}/> نمایش بخش معرفی</label>
   <label><input type="checkbox" checked={showFooter} onChange={e=>{
    setShowFooter(e.target.checked);setSave('idle');}}/> نمایش پابرگ</label>
   <button type="button" disabled={save==='saving'||save==='saved'||!header.trim()}
    onClick={()=>void append()}>ذخیره نسخه جدید طراحی خصوصی</button>
   {save==='saved'&&<p role="status">نسخه طراحی ذخیره شد؛ سایت منتشر نشده است.</p>}
   {save==='denied'&&<p role="alert">مجوز مستقل تغییر طراحی سایت تأیید نشده است.</p>}
   {save==='conflict'&&<p role="alert">نسخه طراحی تغییر کرده است؛ ابتدا آخرین نسخه را دریافت کنید.</p>}
   {save==='error'&&<p role="alert">ذخیره نسخه طراحی تأیید نشد.</p>}
  </fieldset>}
  {saved&&state==='ready'&&<>
   <button type="button" onClick={()=>void loadHistory(null)}>تاریخچه طراحی سایت</button>
   {historyState==='denied'&&<p role="alert">مجوز مشاهده تاریخچه طراحی تأیید نشده است.</p>}
   {historyState==='error'&&<p role="alert">تاریخچه طراحی قابل دریافت نیست.</p>}
   {historyState==='ready'&&<ol>{history.map(v=><li key={v.revision}>
    نسخه {v.revision} — {v.layoutKey} / {v.paletteKey} (خصوصی)</li>)}</ol>}
   {before!==null&&<button type="button" onClick={()=>void loadHistory(before)}>
    نسخه‌های قدیمی‌تر طراحی</button>}
   <h3>پیش‌نمایش خصوصی صفحه موجود</h3>
   <button type="button" onClick={()=>void loadPages()}>دریافت صفحات خصوصی برای پیش‌نمایش</button>
   {pageState==='denied'&&<p role="alert">مجوز مشاهده صفحات خصوصی برای پیش‌نمایش وجود ندارد.</p>}
   {pageState==='error'&&<p role="alert">صفحات معتبر برای پیش‌نمایش دریافت نشد.</p>}
   {pageState==='ready'&&pages.length===0&&<p>صفحه خصوصی موجود نیست؛ صفحه‌ای جعل نمی‌شود.</p>}
   {pageState==='ready'&&pages.length>0&&<label>صفحه پیش‌نمایش
    <select aria-label="صفحه پیش‌نمایش" value={chosen} onChange={e=>setChosen(e.target.value)}>
     {pages.map(p=><option key={p.pageId} value={p.pageId}>{p.title}</option>)}
    </select>
   </label>}
   {previewPage&&pageState==='ready'&&<SitePreview theme={saved} page={previewPage}/>}
  </>}
 </section>;
}

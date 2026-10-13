import React,{useEffect,useRef,useState} from 'react';
type Design={revision:number;layoutKey:'EDITORIAL'|'HERO'|'COMPACT';paletteKey:'OCEAN'|'FOREST'|'EARTH';
 typeKey:'SYSTEM'|'SERIF';headerText:string;footerText:string;showIntro:boolean;showFooter:boolean;
 visibility:'PRIVATE_DRAFT';isPublished:false};
type Page={position:number;pageId:string;pinnedPageRevision:number;slug:string;title:string;
 bodyText:string;visibility:'PRIVATE_DRAFT';isPublished:false};
type Load='idle'|'loading'|'ready'|'denied'|'error';
const uuid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const themes:Record<Design['paletteKey'],{ink:string,paper:string,accent:string}>={
 OCEAN:{ink:'#123047',paper:'#eff8ff',accent:'#17669c'},
 FOREST:{ink:'#173b2c',paper:'#eef8f1',accent:'#2a7550'},
 EARTH:{ink:'#49341e',paper:'#fff6e9',accent:'#965a24'}};
function designValid(v:unknown):v is Design{
 if(!v||typeof v!=='object')return false;const x=v as Record<string,unknown>;
 return Number.isSafeInteger(x.revision)&&Number(x.revision)>0&&
 ['EDITORIAL','HERO','COMPACT'].includes(String(x.layoutKey))&&
 ['OCEAN','FOREST','EARTH'].includes(String(x.paletteKey))&&
 ['SYSTEM','SERIF'].includes(String(x.typeKey))&&
 typeof x.headerText==='string'&&x.headerText.trim().length>0&&x.headerText.length<=120&&
 typeof x.footerText==='string'&&x.footerText.length<=240&&
 typeof x.showIntro==='boolean'&&typeof x.showFooter==='boolean'&&
 x.visibility==='PRIVATE_DRAFT'&&x.isPublished===false;
}
function pageValid(v:unknown):v is Page{
 if(!v||typeof v!=='object')return false;const x=v as Record<string,unknown>;
 return Number.isSafeInteger(x.position)&&Number(x.position)>0&&
 typeof x.pageId==='string'&&uuid.test(x.pageId)&&
 Number.isSafeInteger(x.pinnedPageRevision)&&Number(x.pinnedPageRevision)>0&&
 typeof x.slug==='string'&&/^[a-z0-9]+(-[a-z0-9]+)*$/.test(x.slug)&&x.slug.length<=80&&
 typeof x.title==='string'&&x.title.length<=200&&
 typeof x.bodyText==='string'&&x.bodyText.length<=20000&&
 x.visibility==='PRIVATE_DRAFT'&&x.isPublished===false;
}
export function CenterAssembledSitePreview({centerId,token}:{centerId:string;token:string}){
 const nonce=useRef(0);
 const [state,setState]=useState<Load>('idle'),[design,setDesign]=useState<Design|null>(null);
 const [nav,setNav]=useState<number|null>(null),[items,setItems]=useState<Page[]>([]);
 const [after,setAfter]=useState<number|null>(null),[selected,setSelected]=useState('');
 const [missing,setMissing]=useState('');
 useEffect(()=>()=>{nonce.current++;},[]);
 async function get(next:number|null){
  if(!centerId||!token.trim()||state==='loading'||(next!==null&&(!design||!nav)))return;
  const n=++nonce.current;setState('loading');
  if(next===null){setDesign(null);setNav(null);setItems([]);setAfter(null);setSelected('');setMissing('');}
  try{
   const params=next===null?'':'?navigationRevision='+nav+'&designRevision='+design!.revision+
    '&afterPosition='+next;
   const response=await fetch('/api/v1/centers/'+encodeURIComponent(centerId)+
    '/site/assembled-preview'+params,{headers:{Authorization:'Bearer '+token.trim()},cache:'no-store'});
   if(n!==nonce.current)return;
   if(response.status===401||response.status===403){
    setDesign(null);setItems([]);setAfter(null);setState('denied');return;
   }
   if(!response.ok)throw Error('Unavailable');
   const raw:unknown=await response.json();
   if(n!==nonce.current||!raw||typeof raw!=='object')throw Error('Invalid');
   const x=raw as Record<string,unknown>;
   if(x.visibility!=='PRIVATE_DRAFT'||x.isPublished!==false||x.deliveryVerified!==false||
    typeof x.ready!=='boolean'||!Array.isArray(x.items)||x.items.length>20||
    !x.items.every(pageValid)||!(x.nextAfterPosition===null||
      (Number.isSafeInteger(x.nextAfterPosition)&&Number(x.nextAfterPosition)>0))||
    !(x.navigationRevision===null||(Number.isSafeInteger(x.navigationRevision)&&Number(x.navigationRevision)>0))||
    !(x.designRevision===null||(Number.isSafeInteger(x.designRevision)&&Number(x.designRevision)>0)))
    throw Error('Unsafe private site');
   if(x.ready!==true){
    if(next!==null||x.items.length!==0||x.nextAfterPosition!==null||
      x.missingNavigation!==(x.navigationRevision===null)||
      x.missingDesign!==(x.designRevision===null)||
      !(x.design===null||designValid(x.design)))throw Error('Forged missing source');
    setMissing([x.missingNavigation?'منوی خصوصی موجود نیست':'',
     x.missingDesign?'طراحی خصوصی موجود نیست':''].filter(Boolean).join('؛ '));
    setState('ready');return;
   }
   if(x.missingNavigation!==false||x.missingDesign!==false||
    !designValid(x.design)||x.designRevision!==x.design.revision||
    !Number.isSafeInteger(x.navigationRevision)||Number(x.navigationRevision)<=0||
    (next!==null&&(nav!==x.navigationRevision||design?.revision!==x.designRevision)))
    throw Error('Forged version lineage');
   const rows=x.items as Page[];
   if(rows.some((p,i)=>(next!==null&&p.position<=next)||
     (i>0&&rows[i-1].position>=p.position))||
     new Set(rows.map(p=>p.pageId)).size!==rows.length||
     rows.some(p=>next!==null&&items.some(old=>old.pageId===p.pageId))||
     (x.nextAfterPosition!==null&&(rows.length!==20||
      rows[19].position!==x.nextAfterPosition)))throw Error('Incorrect page order');
   setDesign(x.design);setNav(x.navigationRevision as number);
   setItems(old=>next===null?rows:[...old,...rows]);
   if(next===null)setSelected(rows[0]?.pageId??'');
   setAfter(x.nextAfterPosition as number|null);setState('ready');
  }catch{if(n===nonce.current){setDesign(null);setItems([]);setAfter(null);
    setSelected('');setState('error');}}
 }
 const active=items.find(p=>p.pageId===selected);
 const colors=design?themes[design.paletteKey]:null;
 return <section aria-label="پیش‌نمایش یکپارچه خصوصی سایت" style={{border:'1px solid #888',padding:12,marginTop:16}}>
  <h3>پیش‌نمایش یکپارچه سایت کانون</h3>
  <p>چیدمان، منو و متن دقیق نسخه‌های ذخیره‌شده، فقط در پنل داخلی؛ انتشار عمومی انجام نمی‌شود.</p>
  <button type="button" disabled={!centerId||!token.trim()||state==='loading'}
   onClick={()=>void get(null)}>نمایش نسخه‌های واقعی سایت</button>
  {state==='denied'&&<p role="alert">مجوز مستقل منو، صفحه و طراحی موجود نیست.</p>}
  {state==='error'&&<p role="alert">پیش‌نمایش معتبر سایت دریافت نشد.</p>}
  {state==='ready'&&missing&&<p role="status">{missing}؛ پیش‌فرض جعلی نمایش داده نشد.</p>}
  {state==='ready'&&design&&colors&&<article aria-label="سایت منتشرنشده کانون" dir="rtl"
   style={{background:colors.paper,color:colors.ink,border:'2px dashed '+colors.accent,
    padding:design.layoutKey==='COMPACT'?12:28,
    maxWidth:design.layoutKey==='COMPACT'?480:760,
    fontFamily:design.typeKey==='SERIF'?'Georgia, serif':'system-ui, sans-serif'}}>
   <p><strong>پیش‌نمایش داخلی — منتشرنشده</strong></p>
   <p>طراحی نسخه {design.revision} / منو نسخه {nav}</p>
   <header style={{borderBottom:'1px solid '+colors.accent}}><strong>{design.headerText}</strong></header>
   {design.showIntro&&<p>معرفی خصوصی کانون</p>}
   <nav aria-label="منوی نسخه‌دار سایت">
    {items.map(p=><button type="button" key={p.pageId}
     aria-current={selected===p.pageId?'page':undefined}
     onClick={()=>setSelected(p.pageId)}>{p.title} — /{p.slug}</button>)}
   </nav>
   {after!==null&&<button type="button" onClick={()=>void get(after)}>صفحات بیشتر این نسخه منو</button>}
   {active?<section aria-label="محتوای نسخه پین‌شده" style={{textAlign:design.layoutKey==='HERO'?'center':'right'}}>
    <h4 style={{color:colors.accent}}>{active.title}</h4>
    <p>نسخه صفحه {active.pinnedPageRevision}</p>
    <p style={{whiteSpace:'pre-wrap',overflowWrap:'anywhere'}}>{active.bodyText}</p>
   </section>:<p>صفحه‌ای در این نسخه منو ثبت نشده است.</p>}
   {design.showFooter&&<footer style={{borderTop:'1px solid '+colors.accent}}>{design.footerText}</footer>}
  </article>}
 </section>;
}

import React,{useEffect,useRef,useState} from 'react';

type Kind='SHORT_TEXT'|'LONG_TEXT'|'SINGLE_SELECT';
type Field={key:string;label:string;kind:Kind;required:boolean;options:string[]};
type FormDraft={formId:string;slug:string;revision:number;title:string;fields:Field[];
 recordedAt:string;visibility:'PRIVATE_DRAFT';isPublished:false;acceptsSubmissions:false};
type Revision={revision:number;title:string;fields:Field[];recordedAt:string;
 visibility:'PRIVATE_DRAFT';isPublished:false;acceptsSubmissions:false};
type State='idle'|'loading'|'ready'|'denied'|'error';
type Save='idle'|'saving'|'saved'|'denied'|'conflict'|'error';
const kinds:Kind[]=['SHORT_TEXT','LONG_TEXT','SINGLE_SELECT'];
const uuid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const slugRule=/^[a-z0-9]+(-[a-z0-9]+)*$/;
const keyRule=/^[a-z][a-z0-9_]{0,31}$/;
function validFields(fields:unknown):fields is Field[]{
 if(!Array.isArray(fields)||fields.length<1||fields.length>12)return false;
 const seen=new Set<string>();
 return fields.every(f=>{
  if(!f||typeof f!=='object')return false;
  const x=f as Record<string,unknown>;
  if(typeof x.key!=='string'||!keyRule.test(x.key)||seen.has(x.key)||
    typeof x.label!=='string'||x.label.trim().length<1||x.label.trim().length>120||
    !kinds.includes(x.kind as Kind)||typeof x.required!=='boolean'||!Array.isArray(x.options))
    return false;
  const options=x.options as unknown[];
  if(!options.every(o=>typeof o==='string'&&o.trim().length>0&&o.trim().length<=80))return false;
  if(x.kind==='SINGLE_SELECT'){
   if(options.length<2||options.length>8||new Set(options).size!==options.length)return false;
  }else if(options.length!==0)return false;
  seen.add(x.key);
  return true;
 });
}
const privateMeta=(x:Record<string,unknown>)=>x.visibility==='PRIVATE_DRAFT'&&
 x.isPublished===false&&x.acceptsSubmissions===false;
function validForm(raw:unknown):raw is FormDraft{
 if(!raw||typeof raw!=='object')return false;
 const x=raw as Record<string,unknown>;
 return typeof x.formId==='string'&&uuid.test(x.formId)&&
  typeof x.slug==='string'&&x.slug.length<=80&&slugRule.test(x.slug)&&
  Number.isSafeInteger(x.revision)&&Number(x.revision)>0&&
  typeof x.title==='string'&&x.title.trim().length>0&&x.title.length<=200&&
  validFields(x.fields)&&typeof x.recordedAt==='string'&&
  !Number.isNaN(Date.parse(x.recordedAt))&&privateMeta(x);
}
function validRevision(raw:unknown):raw is Revision{
 if(!raw||typeof raw!=='object')return false;
 const x=raw as Record<string,unknown>;
 return Number.isSafeInteger(x.revision)&&Number(x.revision)>0&&
  typeof x.title==='string'&&x.title.trim().length>0&&x.title.length<=200&&
  validFields(x.fields)&&typeof x.recordedAt==='string'&&
  !Number.isNaN(Date.parse(x.recordedAt))&&privateMeta(x);
}
function validList(raw:unknown,before:string|null):raw is {forms:FormDraft[];nextBeforeFormId:string|null}{
 if(!raw||typeof raw!=='object')return false;
 const x=raw as Record<string,unknown>;
 if(!Array.isArray(x.forms)||x.forms.length>20||!x.forms.every(validForm)||
   !(x.nextBeforeFormId===null||(typeof x.nextBeforeFormId==='string'&&uuid.test(x.nextBeforeFormId))))
  return false;
 const pages=x.forms as FormDraft[];
 if(pages.some((f,i)=>(before!==null&&f.formId.toLowerCase()>=before.toLowerCase())||
   (i>0&&pages[i-1].formId.toLowerCase()<=f.formId.toLowerCase())))return false;
 return x.nextBeforeFormId===null||(pages.length===20&&
   pages[19].formId.toLowerCase()===String(x.nextBeforeFormId).toLowerCase());
}
function validHistory(raw:unknown,form:FormDraft,before:number|null):raw is {
 formId:string;slug:string;revisions:Revision[];nextBeforeRevision:number|null}{
 if(!raw||typeof raw!=='object')return false;
 const x=raw as Record<string,unknown>;
 if(x.formId!==form.formId||x.slug!==form.slug||!Array.isArray(x.revisions)||
    x.revisions.length>20||!x.revisions.every(validRevision)||
   !(x.nextBeforeRevision===null||(Number.isSafeInteger(x.nextBeforeRevision)&&
     Number(x.nextBeforeRevision)>0)))return false;
 const rows=x.revisions as Revision[];
 if(rows.some((v,i)=>(before!==null&&v.revision>=before)||
   (i>0&&rows[i-1].revision<=v.revision)))return false;
 return x.nextBeforeRevision===null||(rows.length===20&&
   rows[19].revision===x.nextBeforeRevision);
}
function ReadOnlyPreview({title,fields}:{title:string;fields:Field[]}){
 return <section aria-label="پیش‌نمایش خصوصی و غیرقابل ارسال فرم"
  style={{border:'2px dashed #aaa',padding:12,marginTop:12}}>
  <h4>پیش‌نمایش فقط خواندنی — فرم منتشر نشده</h4>
  <strong>{title}</strong>
  {fields.map(f=><div key={f.key} style={{marginTop:8}}>
   <label>{f.label}{f.required?' (در طرح الزامی)':''}
    {f.kind==='SHORT_TEXT'&&<input aria-label={'پیش‌نمایش '+f.label}
      type="text" value="" readOnly disabled />}
    {f.kind==='LONG_TEXT'&&<textarea aria-label={'پیش‌نمایش '+f.label}
      value="" readOnly disabled />}
    {f.kind==='SINGLE_SELECT'&&<select aria-label={'پیش‌نمایش '+f.label} disabled defaultValue="">
      <option value="">گزینه‌ای انتخاب نشده</option>
      {f.options.map(o=><option key={o}>{o}</option>)}
    </select>}
   </label>
  </div>)}
  <p>امکان ارسال، دریافت پاسخ، ذخیره اطلاعات شخصی یا انتشار وجود ندارد.</p>
 </section>;
}
const empty=():Field=>({key:'field1',label:'فیلد جدید',kind:'SHORT_TEXT',required:false,options:[]});
export function CenterFormDraftBuilder({centerId,token}:{centerId:string;token:string}){
 const epoch=useRef(0);
 const [listState,setListState]=useState<State>('idle');
 const [forms,setForms]=useState<FormDraft[]>([]);
 const [next,setNext]=useState<string|null>(null);
 const [selected,setSelected]=useState<FormDraft|null>(null);
 const [slug,setSlug]=useState('');
 const [title,setTitle]=useState('');
 const [fields,setFields]=useState<Field[]>([empty()]);
 const [save,setSave]=useState<Save>('idle');
 const [history,setHistory]=useState<Revision[]>([]);
 const [historyState,setHistoryState]=useState<State>('idle');
 const [historyCursor,setHistoryCursor]=useState<number|null>(null);
 const prefix='/api/v1/centers/'+encodeURIComponent(centerId)+'/site/forms/drafts';
 const headers={Authorization:'Bearer '+token.trim()};
 useEffect(()=>{
  epoch.current++;setListState('idle');setForms([]);setNext(null);
  setSelected(null);setSlug('');setTitle('');setFields([empty()]);
  setSave('idle');setHistory([]);setHistoryState('idle');setHistoryCursor(null);
  return()=>{epoch.current++};
 },[centerId,token]);
 function edit(value:FormDraft|null){
  epoch.current++;setSelected(value);setSlug(value?.slug??'');
  setTitle(value?.title??'');setFields(value?value.fields.map(f=>({...f,options:[...f.options]})):[empty()]);
  setSave('idle');setHistory([]);setHistoryState('idle');setHistoryCursor(null);
 }
 function change(i:number,patch:Partial<Field>){
  setFields(old=>old.map((f,k)=>k===i?{...f,...patch}:f));setSave('idle');
 }
 function addField(){
  if(fields.length>=12)return;
  let k=1;while(fields.some(f=>f.key==='field'+k))k++;
  setFields(old=>[...old,{...empty(),key:'field'+k}]);setSave('idle');
 }
 async function load(before:string|null){
  if(!centerId||!token.trim()||listState==='loading')return;
  const id=++epoch.current;setListState('loading');
  if(before===null){setForms([]);setNext(null);setSelected(null);
   setSlug('');setTitle('');setFields([empty()]);setSave('idle');
   setHistory([]);setHistoryState('idle');setHistoryCursor(null);}
  try{
   const resp=await fetch(prefix+(before?'?beforeFormId='+encodeURIComponent(before):''),
     {headers,cache:'no-store'});
   if(id!==epoch.current)return;
   if(resp.status===401||resp.status===403){setForms([]);setListState('denied');return;}
   if(!resp.ok)throw Error('Unavailable');
   const raw:unknown=await resp.json();
   if(id!==epoch.current||!validList(raw,before))throw Error('Unsafe form response');
   setForms(old=>before===null?raw.forms:[...old,...raw.forms]);
   setNext(raw.nextBeforeFormId);setListState('ready');
  }catch{if(id===epoch.current){setForms([]);setNext(null);setListState('error');}}
 }
 async function saveForm(){
  if(!centerId||!token.trim()||!title.trim()||title.length>200||
    slug.length>80||!slugRule.test(slug)||!validFields(fields)||save==='saving'||save==='saved')
    return;
  const id=++epoch.current,formId=selected?.formId??crypto.randomUUID(),
    expectedRevision=selected?.revision??0;
  setSave('saving');
  try{
   const resp=await fetch(prefix+'/revisions',{
    method:'POST',headers:{...headers,'Content-Type':'application/json'},
    body:JSON.stringify({formId,expectedRevision,slug,title:title.trim(),fields})
   });
   if(id!==epoch.current)return;
   if(resp.status===401||resp.status===403){setSave('denied');return;}
   if(resp.status===409){setSave('conflict');return;}
   if(!resp.ok)throw Error('Unavailable');
   const raw:unknown=await resp.json();
   if(id!==epoch.current||!raw||typeof raw!=='object')throw Error('Invalid response');
   const x=raw as Record<string,unknown>;
   if(x.formId!==formId||x.slug!==slug||x.revision!==expectedRevision+1||!privateMeta(x))
     throw Error('Published or forged response');
   const updated:FormDraft={formId,slug,title:title.trim(),revision:expectedRevision+1,
    fields:fields.map(f=>({...f,options:[...f.options]})),recordedAt:new Date().toISOString(),
    visibility:'PRIVATE_DRAFT',isPublished:false,acceptsSubmissions:false};
   setSelected(updated);
   setForms(old=>[updated,...old.filter(x=>x.formId!==formId)]);
   setSave('saved');setHistory([]);setHistoryState('idle');setHistoryCursor(null);
  }catch{if(id===epoch.current)setSave('error');}
 }
 async function loadHistory(cursor:number|null){
  if(!selected||historyState==='loading')return;
  const active=selected,id=++epoch.current;setHistoryState('loading');
  if(cursor===null){setHistory([]);setHistoryCursor(null);}
  try{
   const resp=await fetch(prefix+'/'+encodeURIComponent(active.formId)+'/revisions'+
      (cursor===null?'':'?beforeRevision='+cursor),{headers,cache:'no-store'});
   if(id!==epoch.current)return;
   if(resp.status===401||resp.status===403){setHistory([]);setHistoryState('denied');return;}
   if(!resp.ok)throw Error('Unavailable');
   const raw:unknown=await resp.json();
   if(id!==epoch.current||!validHistory(raw,active,cursor))throw Error('Invalid history');
   setHistory(old=>cursor===null?raw.revisions:[...old,...raw.revisions]);
   setHistoryCursor(raw.nextBeforeRevision);setHistoryState('ready');
  }catch{if(id===epoch.current){setHistory([]);setHistoryState('error');}}
 }
 return <section aria-label="فرم‌ساز خصوصی کانون" style={{marginTop:'2rem'}}>
  <h2>فرم‌ساز کانون — تعریف خصوصی و نسخه‌دار</h2>
  <p>فرم‌ها فقط برای طراحی‌اند؛ ارسال پاسخ، انتشار و جمع‌آوری اطلاعات شخصی غیرفعال‌اند.</p>
  <button type="button" disabled={!centerId||listState==='loading'}
    onClick={()=>void load(null)}>دریافت فرم‌های خصوصی</button>{' '}
  <button type="button" disabled={!centerId} onClick={()=>edit(null)}>تعریف فرم جدید</button>
  {listState==='loading'&&<p role="status">در حال دریافت فرم‌ها…</p>}
  {listState==='denied'&&<p role="alert">مجوز مستقل مشاهده فرم‌های خصوصی وجود ندارد.</p>}
  {listState==='error'&&<p role="alert">فهرست فرم‌ها معتبر دریافت نشد.</p>}
  {listState==='ready'&&forms.length===0&&<p>هنوز فرم خصوصی ثبت نشده است.</p>}
  {listState==='ready'&&<ul>{forms.map(f=><li key={f.formId}>
   <strong>{f.title}</strong> — /{f.slug} — نسخه {f.revision} (PRIVATE_DRAFT)
   {' '}<button type="button" onClick={()=>edit(f)}>ویرایش تعریف فرم</button>
  </li>)}</ul>}
  {next&&<button type="button" onClick={()=>void load(next)}>فرم‌های قدیمی‌تر</button>}
  {centerId&&<div>
   <h3>{selected?'ویرایش نسخه جدید فرم':'ساخت فرم خصوصی جدید'}</h3>
   <label>نشانی فرم <input aria-label="نشانی فرم خصوصی" value={slug} maxLength={80}
    disabled={Boolean(selected)}
    onChange={e=>{setSlug(e.target.value);setSave('idle');}} /></label>
   <label>عنوان فرم <input aria-label="عنوان فرم خصوصی" value={title} maxLength={200}
    onChange={e=>{setTitle(e.target.value);setSave('idle');}} /></label>
   <h4>فیلدهای قابل تنظیم</h4>
   {fields.map((f,i)=><fieldset key={i}>
    <legend>فیلد {i+1}</legend>
    <label>کلید فیلد <input aria-label={'کلید فیلد '+(i+1)} value={f.key}
      onChange={e=>change(i,{key:e.target.value})} maxLength={32} /></label>
    <label>عنوان فیلد <input aria-label={'عنوان فیلد '+(i+1)} value={f.label}
      maxLength={120} onChange={e=>change(i,{label:e.target.value})} /></label>
    <label>نوع فیلد <select aria-label={'نوع فیلد '+(i+1)} value={f.kind}
      onChange={e=>{const kind=e.target.value as Kind;
       change(i,{kind,options:kind==='SINGLE_SELECT'?['گزینه اول','گزینه دوم']:[]});}}>
      <option value="SHORT_TEXT">متن کوتاه</option>
      <option value="LONG_TEXT">متن بلند</option>
      <option value="SINGLE_SELECT">انتخاب یک گزینه</option>
    </select></label>
    {f.kind==='SINGLE_SELECT'&&<label>گزینه‌ها (هر خط یک گزینه)
      <textarea aria-label={'گزینه‌های فیلد '+(i+1)} value={f.options.join('\n')}
       onChange={e=>change(i,{options:e.target.value.split('\n')})}/></label>}
    <label><input type="checkbox" checked={f.required}
      onChange={e=>change(i,{required:e.target.checked})}/> الزامی در طرح (نه مجوز جمع‌آوری)</label>
    <button type="button" disabled={fields.length===1}
      onClick={()=>{setFields(old=>old.filter((_,k)=>k!==i));setSave('idle');}}>
      حذف فیلد {i+1}</button>
   </fieldset>)}
   <button type="button" disabled={fields.length>=12} onClick={addField}>افزودن فیلد</button>
   <ReadOnlyPreview title={title} fields={validFields(fields)?fields:[]} />
   <button type="button" disabled={!slugRule.test(slug)||!title.trim()||
       !validFields(fields)||save==='saving'||save==='saved'}
       onClick={()=>void saveForm()}>ذخیره نسخه خصوصی فرم</button>
   {save==='saved'&&<p role="status">نسخه فرم ثبت شد؛ دریافت پاسخ غیرفعال است.</p>}
   {save==='denied'&&<p role="alert">مجوز مستقل ویرایش فرم تأیید نشده است.</p>}
   {save==='conflict'&&<p role="alert">نسخه فرم تغییر کرده یا نشانی تکراری است؛ مجدد دریافت کنید.</p>}
   {save==='error'&&<p role="alert">ثبت فرم خصوصی تأیید نشد.</p>}
   {selected&&<>
    <button type="button" onClick={()=>void loadHistory(null)}>مشاهده تاریخچه تعریف فرم</button>
    {historyState==='denied'&&<p role="alert">مجوز مشاهده تاریخچه فرم تأیید نشده است.</p>}
    {historyState==='error'&&<p role="alert">تاریخچه فرم قابل دریافت نیست.</p>}
    {historyState==='ready'&&<ol>{history.map(h=><li key={h.revision}>
     نسخه {h.revision}: {h.title} — {h.fields.length} فیلد — PRIVATE_DRAFT
    </li>)}</ol>}
    {historyCursor!==null&&<button type="button" onClick={()=>void loadHistory(historyCursor)}>
      نسخه‌های قدیمی‌تر فرم</button>}
   </>}
  </div>}
 </section>;
}

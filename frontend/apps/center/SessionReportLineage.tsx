import React, { useEffect, useRef, useState } from 'react';

type Status = 'idle' | 'loading' | 'ready' | 'denied' | 'error';
type Report = {
  reportSequence:number; reportId:string; draftId:string; linkId:string;
  issuanceId:string; programId:string; nationalVersion:number; pinnedRevision:number;
  reportedSummary:string; evidenceRef:string; claimedOccurredAt:string|null;
  recordedAt:string; verificationStatus:'UNVERIFIED';
};
type Dossier = { classId:string; sessionTitle:string; classTitle:string;
  scheduledAt:string|null; activityReports:Report[]; nextBeforeSequence:number|null };
const uuid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const utc=(v:unknown):v is string=>typeof v==='string'&&v.endsWith('Z')&&!Number.isNaN(Date.parse(v));
const id=(v:unknown):v is string=>typeof v==='string'&&uuid.test(v);
function validReport(v:unknown):v is Report{
  if(!v||typeof v!=='object')return false;
  const r=v as Record<string,unknown>;
  return Number.isSafeInteger(r.reportSequence)&&Number(r.reportSequence)>0&&
    id(r.reportId)&&id(r.draftId)&&id(r.linkId)&&id(r.issuanceId)&&
    typeof r.programId==='string'&&r.programId.trim().length>0&&
    Number.isSafeInteger(r.nationalVersion)&&Number(r.nationalVersion)>0&&
    Number.isSafeInteger(r.pinnedRevision)&&Number(r.pinnedRevision)>0&&
    typeof r.reportedSummary==='string'&&r.reportedSummary.trim().length>0&&
    typeof r.evidenceRef==='string'&&r.evidenceRef.trim().length>0&&
    (r.claimedOccurredAt===null||utc(r.claimedOccurredAt))&&
    utc(r.recordedAt)&&r.verificationStatus==='UNVERIFIED';
}
function validDossier(v:unknown,sessionId:string,before:number|null):v is Dossier{
  if(!v||typeof v!=='object')return false;
  const r=v as Record<string,unknown>;
  if(r.sessionId!==sessionId||!id(r.classId)||
     typeof r.sessionTitle!=='string'||!r.sessionTitle.trim()||
     typeof r.classTitle!=='string'||!r.classTitle.trim()||
     !(r.scheduledAt===null||utc(r.scheduledAt))||
     !Array.isArray(r.activityReports)||r.activityReports.length>20||
     !r.activityReports.every(validReport)||
     !(r.nextBeforeSequence===null||
       (Number.isSafeInteger(r.nextBeforeSequence)&&Number(r.nextBeforeSequence)>0)))return false;
  const reports=r.activityReports as Report[];
  if(reports.some((item,i)=>
    (before!==null&&item.reportSequence>=before)||
    (i>0&&reports[i-1].reportSequence<=item.reportSequence)))return false;
  return r.nextBeforeSequence===null||(reports.length===20&&
    reports[19].reportSequence===r.nextBeforeSequence);
}
/** Operational source lineage only, not attendance or confirmed class delivery. */
export function SessionReportLineage({token,centerId,sessionId}:{
  token:string;centerId:string;sessionId:string;
}){
  const epoch=useRef(0);
  const [status,setStatus]=useState<Status>('idle');
  const [dossier,setDossier]=useState<Dossier|null>(null);
  useEffect(()=>{
    epoch.current++;setStatus('idle');setDossier(null);
    return()=>{epoch.current++;};
  },[token,centerId,sessionId]);
  async function load(before:number|null){
    if(!token.trim()||!centerId||!sessionId||status==='loading')return;
    const current=++epoch.current;
    setStatus('loading');
    if(before===null)setDossier(null);
    try{
      const url='/api/v1/centers/'+encodeURIComponent(centerId)+
        '/class-sessions/'+encodeURIComponent(sessionId)+'/report-lineage'+
        (before===null?'':'?beforeSequence='+before);
      const response=await fetch(url,{headers:{Authorization:'Bearer '+token.trim()},cache:'no-store'});
      if(current!==epoch.current)return;
      if(response.status===401||response.status===403){
        setDossier(null);setStatus('denied');return;
      }
      if(!response.ok)throw new Error('Unavailable');
      const raw:unknown=await response.json();
      if(current!==epoch.current)return;
      if(!validDossier(raw,sessionId,before))throw new Error('Invalid report lineage');
      setDossier(old=>before===null||old===null?raw:{
        ...raw,activityReports:[...old.activityReports,...raw.activityReports]
      });
      setStatus('ready');
    }catch{
      if(current===epoch.current){setDossier(null);setStatus('error');}
    }
  }
  return <section aria-label="پرونده گزارش عملیاتی جلسه">
    <h4>پرونده گزارش عملیاتی و ردیابی شواهد</h4>
    <p>پیوندها فقط به داده‌های منبع معتبر ارجاع دارند؛ گزارش فعالیت و مدرک هنوز راستی‌آزمایی نشده‌اند. هیچ آمار رسمی تولید نمی‌شود.</p>
    <button type="button" disabled={!centerId||status==='loading'}
      onClick={()=>void load(null)}>دریافت پرونده گزارش جلسه</button>
    {status==='loading'&&<p role="status">در حال دریافت پرونده جلسه…</p>}
    {status==='denied'&&<p role="alert">مجوز مستقل پرونده گزارش جلسه تأیید نشده است.</p>}
    {status==='error'&&<p role="alert">پرونده گزارش جلسه قابل دریافت نیست.</p>}
    {status==='ready'&&dossier&&<>
      <p>کلاس مالک: {dossier.classTitle} — جلسه: {dossier.sessionTitle}</p>
      <p>برنامه‌ریزی: {dossier.scheduledAt===null?'نامعلوم':
        new Date(dossier.scheduledAt).toLocaleString('fa-IR')}</p>
      {dossier.activityReports.length===0?
        <p role="status">گزارش فعالیت دارای پیوند معتبر ثبت نشده؛ اجرای جلسه نامعلوم است.</p>:
        <ul>{dossier.activityReports.map(r=><li key={r.reportId}>
          <strong>{r.reportedSummary} — راستی‌آزمایی‌نشده</strong>
          <p>برنامه ملی: {r.programId} / نسخه {r.nationalVersion}</p>
          <p>طرح بومی: {r.draftId} / بازنگری تثبیت‌شده {r.pinnedRevision}</p>
          <p>ابلاغ: {r.issuanceId} / پیوند: {r.linkId}</p>
          <p>ارجاع مدرک (تأییدنشده): {r.evidenceRef}</p>
          <p>زمان ادعایی: {r.claimedOccurredAt===null?'نامعلوم':
            new Date(r.claimedOccurredAt).toLocaleString('fa-IR')}</p>
        </li>)}</ul>}
      {dossier.nextBeforeSequence!==null&&
        <button type="button" onClick={()=>void load(dossier.nextBeforeSequence)}>
          گزارش‌های مرتبط قدیمی‌تر</button>}
    </>}
  </section>;
}

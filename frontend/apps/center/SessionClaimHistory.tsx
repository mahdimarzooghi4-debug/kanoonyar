import React, { useEffect, useRef, useState } from 'react';

type Cursor={at:string;id:string};
type Status='idle'|'loading'|'ready'|'denied'|'error';
type Occurrence={
  occurrenceReportId:string;reportedNote:string;evidenceRef:string|null;
  claimedStartedAt:string|null;claimedEndedAt:string|null;
  recordedAt:string;verificationStatus:'UNVERIFIED';
};
type Attendance={
  claimId:string;participantRef:string;
  reportedPresence:'REPORTED_PRESENT'|'REPORTED_ABSENT';
  recordedAt:string;verificationStatus:'UNVERIFIED';
};
const uuid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const utc=(v:unknown):v is string=>typeof v==='string'&&v.endsWith('Z')&&
  !Number.isNaN(Date.parse(v));
function validOccurrence(value:unknown):value is Occurrence{
  if(!value||typeof value!=='object')return false;
  const r=value as Record<string,unknown>;
  return typeof r.occurrenceReportId==='string'&&uuid.test(r.occurrenceReportId)&&
    typeof r.reportedNote==='string'&&r.reportedNote.trim().length>0&&
    (r.evidenceRef===null||typeof r.evidenceRef==='string')&&
    (r.claimedStartedAt===null||utc(r.claimedStartedAt))&&
    (r.claimedEndedAt===null||utc(r.claimedEndedAt))&&
    utc(r.recordedAt)&&r.verificationStatus==='UNVERIFIED';
}
function validAttendance(value:unknown):value is Attendance{
  if(!value||typeof value!=='object')return false;
  const a=value as Record<string,unknown>;
  return typeof a.claimId==='string'&&uuid.test(a.claimId)&&
    typeof a.participantRef==='string'&&a.participantRef.trim().length>0&&
    (a.reportedPresence==='REPORTED_PRESENT'||a.reportedPresence==='REPORTED_ABSENT')&&
    utc(a.recordedAt)&&a.verificationStatus==='UNVERIFIED';
}

type HistoryRecord=Occurrence|Attendance;
function historyId(record:HistoryRecord,kind:'occurrence'|'attendance'):
    string {
  return kind==='occurrence'?(record as Occurrence).occurrenceReportId:
    (record as Attendance).claimId;
}
function timestampKey(value:string):string|null {
  const match=/^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?:\.(\d{1,7}))?Z$/.exec(value);
  if(!match||Number.isNaN(Date.parse(value)))return null;
  return match[1]+'.'+(match[2]??'').padEnd(7,'0')+'Z';
}
function strictlyDescending(records:HistoryRecord[],kind:'occurrence'|'attendance'):
    boolean {
  const ids=new Set<string>();
  for(let i=0;i<records.length;i++){
    const id=historyId(records[i],kind).toLowerCase();
    if(ids.has(id))return false;
    ids.add(id);
    if(i===0)continue;
    const previous=records[i-1];
    const previousAt=timestampKey(previous.recordedAt);
    const currentAt=timestampKey(records[i].recordedAt);
    if(!previousAt||!currentAt)return false;
    const previousId=historyId(previous,kind).toLowerCase();
    if(!(previousAt>currentAt||
      (previousAt===currentAt&&previousId>id)))return false;
  }
  return true;
}
function isOlderThanCursor(record:HistoryRecord,cursor:Cursor,
    kind:'occurrence'|'attendance'):boolean {
  const recordAt=timestampKey(record.recordedAt);
  const cursorAt=timestampKey(cursor.at);
  if(!recordAt||!cursorAt)return false;
  const id=historyId(record,kind).toLowerCase();
  const cursorId=cursor.id.toLowerCase();
  return recordAt<cursorAt||(recordAt===cursorAt&&id<cursorId);
}

/** No totals: an absent claim never implies absence or zero. */
export function SessionClaimHistory({centerId,sessionId,token}:{
  centerId:string;sessionId:string;token:string;
}){
  const epoch=useRef(0);
  const [occurrences,setOccurrences]=useState<Occurrence[]>([]);
  const [occCursor,setOccCursor]=useState<Cursor|null>(null);
  const [occStatus,setOccStatus]=useState<Status>('idle');
  const [selected,setSelected]=useState<string|null>(null);
  const [attendance,setAttendance]=useState<Attendance[]>([]);
  const [attCursor,setAttCursor]=useState<Cursor|null>(null);
  const [attStatus,setAttStatus]=useState<Status>('idle');
  useEffect(()=>{
    epoch.current++;
    setOccurrences([]);setOccCursor(null);setOccStatus('idle');
    setSelected(null);setAttendance([]);setAttCursor(null);setAttStatus('idle');
    return()=>{epoch.current++;};
  },[centerId,sessionId,token]);

  async function request(kind:'occurrence'|'attendance',id:string|null,cursor:Cursor|null){
    if(!centerId||!sessionId||!token.trim())return;
    const e=++epoch.current;
    if(kind==='occurrence'){
      setSelected(null);setAttendance([]);setAttCursor(null);setAttStatus('idle');
      if(cursor===null){setOccurrences([]);setOccCursor(null);}
      setOccStatus('loading');
    }else{
      setSelected(id);
      if(cursor===null){setAttendance([]);setAttCursor(null);}
      setAttStatus('loading');
    }
    try{
      const root='/api/v1/centers/'+encodeURIComponent(centerId)+
        '/class-sessions/'+encodeURIComponent(sessionId)+'/occurrence-claims';
      const url=root+(kind==='attendance'?'/'+encodeURIComponent(id??'')+'/attendance-claims':'')+
        (cursor?'?beforeAt='+encodeURIComponent(cursor.at)+'&beforeId='+encodeURIComponent(cursor.id):'');
      const response=await fetch(url,{
        headers:{Authorization:'Bearer '+token.trim()},cache:'no-store'
      });
      if(e!==epoch.current)return;
      if(response.status===401||response.status===403){
        if(kind==='occurrence'){setOccurrences([]);setOccCursor(null);setOccStatus('denied');}
        else{setAttendance([]);setAttCursor(null);setAttStatus('denied');}
        return;
      }
      if(!response.ok)throw new Error('Unavailable');
      const raw:unknown=await response.json();
      if(e!==epoch.current||!raw||typeof raw!=='object')throw new Error('Malformed');
      const body=raw as Record<string,unknown>;
      const data=kind==='occurrence'?body.occurrenceReports:body.attendanceClaims;
      if(body.sessionId!==sessionId||
         (kind==='attendance'&&body.occurrenceReportId!==id)||
         !Array.isArray(data)||data.length>20||
         !data.every(kind==='occurrence'?validOccurrence:validAttendance))
        throw new Error('Invalid records');
      const records=data as (Occurrence|Attendance)[];
      if(!strictlyDescending(records,kind))throw new Error('Invalid page ordering');
      const nextAt=body.nextBeforeAt,nextId=body.nextBeforeId;
      const hasMore=nextAt!==null||nextId!==null;
      if(hasMore){
        const last=records[records.length-1];
        if(!utc(nextAt)||typeof nextId!=='string'||!uuid.test(nextId)||
          records.length!==20||!last||nextAt!==last.recordedAt||
          nextId.toLowerCase()!==historyId(last,kind).toLowerCase())
          throw new Error('Invalid cursor');
      }
      if(cursor&&records.some(r=>!isOlderThanCursor(r,cursor,kind)))
        throw new Error('Stale page');
      const next:Cursor|null=hasMore?{at:nextAt as string,id:nextId as string}:null;
      if(kind==='occurrence'){
        setOccurrences(old=>cursor===null?records as Occurrence[]:
          [...old,...records as Occurrence[]]);
        setOccCursor(next);setOccStatus('ready');
      }else{
        setAttendance(old=>cursor===null?records as Attendance[]:
          [...old,...records as Attendance[]]);
        setAttCursor(next);setAttStatus('ready');
      }
    }catch{
      if(e===epoch.current){
        if(kind==='occurrence'){setOccurrences([]);setOccCursor(null);setOccStatus('error');}
        else{setAttendance([]);setAttCursor(null);setAttStatus('error');}
      }
    }
  }
  return <section aria-label="تاریخچه ادعاهای جلسه">
    <h4>تاریخچه گزارش‌ها (راستی‌آزمایی‌نشده)</h4>
    <p>این سوابق ادعای وقوع و حضور هستند؛ هیچ رکورد ناموجودی غایب یا برگزارشده فرض نمی‌شود.</p>
    <button type="button" disabled={!centerId||occStatus==='loading'}
      onClick={()=>void request('occurrence',null,null)}>دریافت تاریخچه ادعاهای جلسه</button>
    {occStatus==='denied'&&<p role="alert">مجوز مستقل تاریخچه وقوع جلسه تأیید نشده است.</p>}
    {occStatus==='error'&&<p role="alert">دریافت تاریخچه وقوع جلسه ممکن نیست.</p>}
    {occStatus==='loading'&&<p role="status">در حال دریافت تاریخچه وقوع…</p>}
    {occStatus==='ready'&&(occurrences.length===0?
      <p role="status">گزارشی ثبت نشده؛ وضعیت وقوع نامعلوم است.</p>:
      <ul>{occurrences.map(o=><li key={o.occurrenceReportId}>
        <strong>{o.reportedNote}</strong>
        <p>زمان ثبت: {new Date(o.recordedAt).toLocaleString('fa-IR')}</p>
        <p>زمان ادعایی شروع: {o.claimedStartedAt===null?'نامعلوم':new Date(o.claimedStartedAt).toLocaleString('fa-IR')}</p>
        <p>ارجاع مدرک ادعاشده: {o.evidenceRef??'ثبت نشده'}</p>
        <p>وضعیت: راستی‌آزمایی‌نشده</p>
        <button type="button" onClick={()=>void request('attendance',o.occurrenceReportId,null)}>
          مشاهده ادعاهای حضور این گزارش</button>
        {selected===o.occurrenceReportId&&<section aria-label="ادعاهای حضور گزارش">
          {attStatus==='denied'&&<p role="alert">مجوز مستقل تاریخچه حضور و غیاب تأیید نشده است.</p>}
          {attStatus==='error'&&<p role="alert">دریافت تاریخچه حضور و غیاب ممکن نیست.</p>}
          {attStatus==='loading'&&<p role="status">در حال دریافت ادعاهای حضور…</p>}
          {attStatus==='ready'&&(attendance.length===0?
            <p role="status">ادعای حضوری ثبت نشده؛ حضور یا غیاب نامعلوم است.</p>:
            <ul>{attendance.map(a=><li key={a.claimId}>
              <span>{a.participantRef} — {a.reportedPresence==='REPORTED_PRESENT'?
                'حاضر گزارش‌شده':'غایب گزارش‌شده'} — تأییدنشده</span>
            </li>)}</ul>)}
          {attStatus==='ready'&&attCursor&&
            <button type="button" onClick={()=>void request('attendance',o.occurrenceReportId,attCursor)}>
              ادعاهای حضور قدیمی‌تر</button>}
        </section>}
      </li>)}</ul>)}
    {occStatus==='ready'&&occCursor&&
      <button type="button" onClick={()=>void request('occurrence',null,occCursor)}>
        گزارش‌های قدیمی‌تر</button>}
  </section>;
}

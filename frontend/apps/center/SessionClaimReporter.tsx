import React, { useEffect, useRef, useState } from 'react';
import { SessionClaimHistory } from './SessionClaimHistory';

type Phase = 'idle' | 'saving' | 'saved' | 'denied' | 'conflict' | 'error';
type ReportedPresence = '' | 'REPORTED_PRESENT' | 'REPORTED_ABSENT';
const uuid = /^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i;

/** Human-submitted claims only. No real participant identity or verified delivery is asserted. */
export function SessionClaimReporter({ centerId, sessionId, token, onClose }: {
  centerId: string; sessionId: string; token: string; onClose: () => void;
}) {
  const epoch=useRef(0);
  const occurrenceRequest=useRef(crypto.randomUUID());
  const attendanceRequest=useRef(crypto.randomUUID());
  const [note,setNote]=useState('');
  const [evidenceRef,setEvidenceRef]=useState('');
  const [occurrenceId,setOccurrenceId]=useState<string|null>(null);
  const [occurrenceStatus,setOccurrenceStatus]=useState<Phase>('idle');
  const [participantRef,setParticipantRef]=useState('');
  const [presence,setPresence]=useState<ReportedPresence>('');
  const [attendanceStatus,setAttendanceStatus]=useState<Phase>('idle');
  useEffect(() => {
    epoch.current++;
    occurrenceRequest.current=crypto.randomUUID();
    attendanceRequest.current=crypto.randomUUID();
    setNote('');setEvidenceRef('');setOccurrenceId(null);
    setOccurrenceStatus('idle');setParticipantRef('');setPresence('');
    setAttendanceStatus('idle');
    return () => { epoch.current++; };
  },[centerId,sessionId,token]);

  async function submitOccurrence() {
    if (!centerId || !token.trim() || !note.trim() ||
        note.length>2048 || evidenceRef.length>512 ||
        occurrenceId!==null || occurrenceStatus==='saving') return;
    const version=++epoch.current; setOccurrenceStatus('saving');
    const id=occurrenceRequest.current;
    try {
      const response=await fetch('/api/v1/centers/'+encodeURIComponent(centerId)+
        '/class-sessions/'+encodeURIComponent(sessionId)+'/occurrence-claims',{
        method:'POST',headers:{
          'Authorization':'Bearer '+token.trim(),'Content-Type':'application/json'
        },
        body:JSON.stringify({
          occurrenceReportId:id,reportedNote:note.trim(),
          evidenceRef:evidenceRef.trim()||null,claimedStartedAt:null,claimedEndedAt:null
        })
      });
      if(version!==epoch.current)return;
      if(response.status===401||response.status===403){setOccurrenceStatus('denied');return;}
      if(response.status===409){setOccurrenceStatus('conflict');return;}
      if(!response.ok)throw new Error('Failed');
      const data:unknown=await response.json();
      if(version!==epoch.current || !data || typeof data!=='object')throw new Error('Invalid');
      const body=data as Record<string,unknown>;
      if(body.occurrenceReportId!==id||body.sessionId!==sessionId||
         body.verificationStatus!=='UNVERIFIED'||!uuid.test(id))throw new Error('Invalid');
      setOccurrenceId(id);setOccurrenceStatus('saved');
    }catch{if(version===epoch.current)setOccurrenceStatus('error');}
  }

  async function submitAttendance() {
    if (!occurrenceId || !token.trim() || !participantRef.trim() ||
       participantRef.length>256 || !presence || attendanceStatus==='saving')return;
    const version=++epoch.current;setAttendanceStatus('saving');
    const claimId=attendanceRequest.current;
    try {
      const response=await fetch('/api/v1/centers/'+encodeURIComponent(centerId)+
       '/class-sessions/'+encodeURIComponent(sessionId)+'/attendance-claims',{
        method:'POST',headers:{
          'Authorization':'Bearer '+token.trim(),'Content-Type':'application/json'
        },
        body:JSON.stringify({claimId,occurrenceReportId:occurrenceId,
           participantRef:participantRef.trim(),reportedPresence:presence})
      });
      if(version!==epoch.current)return;
      if(response.status===401||response.status===403){setAttendanceStatus('denied');return;}
      if(response.status===409){setAttendanceStatus('conflict');return;}
      if(!response.ok)throw new Error('Failed');
      const data:unknown=await response.json();
      if(version!==epoch.current||!data||typeof data!=='object')throw new Error('Invalid');
      const body=data as Record<string,unknown>;
      if(body.claimId!==claimId||body.occurrenceReportId!==occurrenceId||
         body.sessionId!==sessionId||body.verificationStatus!=='UNVERIFIED')
        throw new Error('Invalid');
      attendanceRequest.current=crypto.randomUUID();
      setParticipantRef('');setPresence('');setAttendanceStatus('saved');
    }catch{if(version===epoch.current)setAttendanceStatus('error');}
  }

  return <section aria-label="گزارش وقوع و حضور جلسه" style={{border:'1px solid #ccc',padding:'1rem'}}>
    <h4>گزارش ادعای جلسه</h4>
    <p>این اطلاعات صرفاً ادعاهای ثبت‌شده هستند، نه تأیید برگزاری یا آمار حضور رسمی.</p>
    <p>شناسه جلسه: {sessionId}</p>
    <SessionClaimHistory centerId={centerId} sessionId={sessionId} token={token} />
    <button type="button" onClick={onClose}>بستن گزارش جلسه</button>
    <label>شرح گزارش <input aria-label="شرح ادعای برگزاری" value={note}
      disabled={occurrenceId!==null}
      onChange={e=>{setNote(e.target.value);setOccurrenceStatus('idle');}} /></label>
    <label>ارجاع شواهد (اختیاری) <input aria-label="ارجاع مدرک ادعاشده"
      value={evidenceRef} disabled={occurrenceId!==null}
      onChange={e=>setEvidenceRef(e.target.value)}/></label>
    <button type="button" disabled={!note.trim()||!!occurrenceId||occurrenceStatus==='saving'}
      onClick={()=>void submitOccurrence()}>ثبت گزارش ادعای جلسه</button>
    {occurrenceStatus==='saved'&&<p role="status">گزارش ثبت شد؛ راستی‌آزمایی‌نشده است.</p>}
    {occurrenceStatus==='denied'&&<p role="alert">مجوز مستقل گزارش وقوع جلسه تأیید نشده است.</p>}
    {occurrenceStatus==='conflict'&&<p role="alert">تعارض گزارش جلسه؛ رکورد موجود را از سرور بررسی کنید.</p>}
    {occurrenceStatus==='error'&&<p role="alert">ثبت گزارش وقوع جلسه تأیید نشد.</p>}
    {occurrenceId!==null&&<div>
      <h4>ثبت ادعای حضور و غیاب برای این گزارش</h4>
      <p>نام، شماره تلفن یا اطلاعات شخصی وارد نکنید؛ فقط شناسه مستعار مجاز.</p>
      <label>ارجاع مستعار مخاطب <input aria-label="شناسه مستعار مخاطب"
        value={participantRef} onChange={e=>{setParticipantRef(e.target.value);setAttendanceStatus('idle');}}/></label>
      <label>گزارش حضور <select aria-label="وضعیت ادعاشده حضور" value={presence}
        onChange={e=>{setPresence(e.target.value as ReportedPresence);setAttendanceStatus('idle');}}>
        <option value="">نامعلوم / انتخاب نشده</option>
        <option value="REPORTED_PRESENT">حاضر گزارش‌شده</option>
        <option value="REPORTED_ABSENT">غایب گزارش‌شده</option>
      </select></label>
      <button type="button" disabled={!participantRef.trim()||!presence||attendanceStatus==='saving'}
        onClick={()=>void submitAttendance()}>ثبت ادعای حضور/غیاب</button>
      {attendanceStatus==='saved'&&<p role="status">ادعای حضور/غیاب ثبت شد؛ تأیید نشده است.</p>}
      {attendanceStatus==='denied'&&<p role="alert">مجوز مستقل ثبت حضور/غیاب تأیید نشده است.</p>}
      {attendanceStatus==='conflict'&&<p role="alert">ادعای تکراری یا ناسازگار؛ رکورد قبلی را بررسی کنید.</p>}
      {attendanceStatus==='error'&&<p role="alert">ثبت ادعای حضور/غیاب تأیید نشد.</p>}
    </div>}
  </section>;
}

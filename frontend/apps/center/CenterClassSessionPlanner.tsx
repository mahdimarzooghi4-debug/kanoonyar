import React, { useEffect, useRef, useState } from 'react';
import { ClassCapacityIntake } from './ClassCapacityIntake';

type State = 'idle' | 'saving' | 'saved' | 'denied' | 'conflict' | 'error';
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function CenterClassSessionPlanner({ token, centerId, existingClass }: {
  token: string; centerId: string;
  /** Previously persisted, original S12 class identified by a separate catalogue grant. */
  existingClass?: { classId: string; title: string };
}) {
  const epoch = useRef(0);
  const classRequest = useRef(crypto.randomUUID());
  const sessionRequest = useRef(crypto.randomUUID());
  const [classTitle, setClassTitle] = useState('');
  const [classId, setClassId] = useState<string | null>(null);
  const [sessionTitle, setSessionTitle] = useState('');
  const [scheduledLocal, setScheduledLocal] = useState('');
  const [classState, setClassState] = useState<State>('idle');
  const [showIntake, setShowIntake] = useState(false);
  const [sessionState, setSessionState] = useState<State>('idle');
  const [lastSessionId, setLastSessionId] = useState<string | null>(null);

  useEffect(() => {
    epoch.current++;
    classRequest.current = crypto.randomUUID();
    sessionRequest.current = crypto.randomUUID();
    setClassId(existingClass?.classId ?? null); setClassTitle(existingClass?.title ?? ''); setSessionTitle('');
    setScheduledLocal(''); setClassState('idle'); setSessionState('idle'); setShowIntake(false);
    setLastSessionId(null);
    return () => { epoch.current++; };
  }, [centerId, token, existingClass?.classId, existingClass?.title]);

  async function createClass() {
    if (!centerId || !token.trim() || !classTitle.trim() || classTitle.length > 512 ||
      classState === 'saving' || classId !== null || existingClass !== undefined) return;
    const current = ++epoch.current;
    setClassState('saving');
    const pendingId = classRequest.current;
    try {
      const response = await fetch('/api/v1/centers/' + encodeURIComponent(centerId) + '/classes', {
        method: 'POST', headers: {
          Authorization: 'Bearer ' + token.trim(), 'Content-Type': 'application/json'
        },
        body: JSON.stringify({ classId: pendingId, title: classTitle.trim() })
      });
      if (current !== epoch.current) return;
      if (response.status === 401 || response.status === 403) { setClassState('denied'); return; }
      if (response.status === 409) { setClassState('conflict'); return; }
      if (!response.ok) throw new Error('Unavailable');
      const payload: unknown = await response.json();
      if (current !== epoch.current || !payload || typeof payload !== 'object' ||
          (payload as Record<string, unknown>).classId !== pendingId || !uuid.test(pendingId))
        throw new Error('Invalid class acknowledgement');
      setClassId(pendingId);
      setClassState('saved');
    } catch { if (current === epoch.current) setClassState('error'); }
  }

  async function scheduleSession() {
    if (!centerId || !token.trim() || !classId || !sessionTitle.trim() ||
      sessionTitle.length > 512 || sessionState === 'saving') return;
    const current = ++epoch.current;
    setSessionState('saving');
    const sessionId = sessionRequest.current;
    let scheduledAt: string | null = null;
    if (scheduledLocal) {
      const date = new Date(scheduledLocal);
      if (Number.isNaN(date.getTime())) { setSessionState('error'); return; }
      scheduledAt = date.toISOString();
    }
    try {
      const response = await fetch('/api/v1/centers/' + encodeURIComponent(centerId) +
        '/classes/sessions', {
        method: 'POST', headers: {
          Authorization: 'Bearer ' + token.trim(), 'Content-Type': 'application/json'
        },
        body: JSON.stringify({
          sessionId, classId, title: sessionTitle.trim(), scheduledAt
        })
      });
      if (current !== epoch.current) return;
      if (response.status === 401 || response.status === 403) { setSessionState('denied'); return; }
      if (response.status === 409) { setSessionState('conflict'); return; }
      if (!response.ok) throw new Error('Unavailable');
      const payload: unknown = await response.json();
      if (current !== epoch.current || !payload || typeof payload !== 'object') throw new Error('Invalid response');
      const data = payload as Record<string, unknown>;
      if (data.sessionId !== sessionId || data.classId !== classId ||
          (scheduledAt === null ? data.scheduledAt !== null :
            typeof data.scheduledAt !== 'string' ||
            Number.isNaN(Date.parse(data.scheduledAt)) ||
            Date.parse(data.scheduledAt) !== Date.parse(scheduledAt)))
        throw new Error('Invalid session acknowledgement');
      sessionRequest.current = crypto.randomUUID();
      setLastSessionId(sessionId);
      setSessionTitle(''); setScheduledLocal('');
      setSessionState('saved');
    } catch { if (current === epoch.current) setSessionState('error'); }
  }

  function newClass() {
    if (existingClass) return;
    epoch.current++;
    classRequest.current = crypto.randomUUID();
    sessionRequest.current = crypto.randomUUID();
    setClassId(null); setClassTitle(''); setSessionTitle('');
    setScheduledLocal(''); setClassState('idle'); setSessionState('idle');
    setLastSessionId(null);
  }

  return <section aria-label={existingClass ? 'برنامه‌ریزی جلسه کلاس موجود' : 'برنامه‌ریزی کلاس و جلسه'} style={{ border: '1px solid #aaa', padding: '1rem', marginTop: '1rem' }}>
    <h3>{existingClass ? 'برنامه‌ریزی جلسه بعدی کلاس موجود' : 'ساخت کلاس و برنامه‌ریزی جلسه'}</h3>
    <p>ثبت کلاس و زمان جلسه، برگزاری واقعی یا حضور مخاطب را تأیید نمی‌کند.</p>
    {existingClass ? <p>کلاس منبع اصلی: <strong>{existingClass.title}</strong> — شناسه: {existingClass.classId}</p> : <>
    <label>عنوان کلاس <input aria-label="عنوان کلاس جدید" value={classTitle}
      disabled={classId !== null} onChange={e => { setClassTitle(e.target.value); setClassState('idle'); }} /></label>{' '}
    <button type="button" onClick={() => void createClass()} disabled={!centerId || !token.trim() ||
      !classTitle.trim() || classId !== null || classState === 'saving'}>ثبت کلاس</button>{' '}
    <button type="button" onClick={newClass}>شروع کلاس جدید</button>
    {classState === 'saved' && <p role="status">کلاس توسط API ثبت شد. شناسه: {classId}</p>}
    {classState === 'denied' && <p role="alert">مجوز مستقل ثبت کلاس تأیید نشده است.</p>}
    {classState === 'conflict' && <p role="alert">شناسه کلاس از قبل ثبت شده است. از مسیر مشاهده منابع، رکورد موجود را بررسی کنید.</p>}
    {classState === 'error' && <p role="alert">ثبت کلاس تأیید نشد؛ وضعیت را از سرور بررسی کنید.</p>}
    </>}
    {classId !== null && <div>
      {!existingClass && <>
      <button type="button" onClick={()=>setShowIntake(value=>!value)}>
        {showIntake?'بستن مدیریت ظرفیت':'مدیریت ظرفیت کلاس جدید'}
      </button>
      {showIntake && <ClassCapacityIntake centerId={centerId} classId={classId} token={token}
        onClose={()=>setShowIntake(false)} />}
      </>}
      <h4>جلسه کلاس «{classTitle}»</h4>
      <label>عنوان جلسه <input aria-label="عنوان جلسه جدید" value={sessionTitle}
        onChange={e => { setSessionTitle(e.target.value); setSessionState('idle'); }} /></label>{' '}
      <label>زمان پیشنهادی (اختیاری) <input aria-label="زمان برنامه‌ریزی جلسه"
        type="datetime-local" value={scheduledLocal}
        onChange={e => { setScheduledLocal(e.target.value); setSessionState('idle'); }} /></label>{' '}
      <button type="button" onClick={() => void scheduleSession()}
        disabled={!sessionTitle.trim() || sessionState === 'saving'}>ثبت جلسه برنامه‌ریزی‌شده</button>
      {sessionState === 'saved' && <p role="status">جلسه برنامه‌ریزی‌شده در API ثبت شد؛ برگزاری تأیید نشده است. شناسه جلسه: {lastSessionId}</p>}
      {sessionState === 'denied' && <p role="alert">مجوز مستقل برنامه‌ریزی جلسه تأیید نشده است.</p>}
      {sessionState === 'conflict' && <p role="alert">تعارض در ثبت جلسه؛ رکورد موجود را از سرور بررسی کنید.</p>}
      {sessionState === 'error' && <p role="alert">ثبت جلسه تأیید نشد.</p>}
    </div>}
  </section>;
}

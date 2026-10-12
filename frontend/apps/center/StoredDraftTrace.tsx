import React, { useEffect, useRef, useState } from 'react';

type Trace = {
  linkId: string; issuanceId: string; pinnedRevision: number; latestRevision: number;
  programId: string; nationalVersion: number; localTitle: string; plannedActivities: unknown[];
};
function isTrace(data: unknown): data is Trace {
  if (!data || typeof data !== 'object') return false;
  const d = data as Record<string, unknown>;
  return typeof d.linkId === 'string' && typeof d.issuanceId === 'string' &&
    typeof d.programId === 'string' && typeof d.localTitle === 'string' &&
    Number.isSafeInteger(d.nationalVersion) && Number(d.nationalVersion) > 0 &&
    Number.isSafeInteger(d.pinnedRevision) && Number(d.pinnedRevision) > 0 &&
    Number.isSafeInteger(d.latestRevision) && Number(d.latestRevision) >= Number(d.pinnedRevision) &&
    Array.isArray(d.plannedActivities);
}

export function StoredDraftTrace({ token, centerId, draftId, onClose }: {
  token: string; centerId: string; draftId: string; onClose: () => void;
}) {
  const epoch = useRef(0);
  const [trace, setTrace] = useState<Trace | null>(null);
  const [status, setStatus] = useState<'idle' | 'loading' | 'ready' | 'denied' | 'error'>('idle');
  useEffect(() => () => { epoch.current++; }, []);

  async function refresh() {
    if (!centerId || !token.trim()) return;
    const current = ++epoch.current;
    setTrace(null);
    setStatus('loading');
    try {
      const response = await fetch('/api/v1/centers/' + encodeURIComponent(centerId) +
        '/local-plan-drafts/' + encodeURIComponent(draftId) + '/execution-trace',
        { headers: { Authorization: 'Bearer ' + token.trim() }, cache: 'no-store' });
      if (current !== epoch.current) return;
      if (response.status === 401 || response.status === 403) { setStatus('denied'); return; }
      if (!response.ok) throw new Error('Unavailable');
      const payload: unknown = await response.json();
      if (current !== epoch.current || !payload || typeof payload !== 'object') return;
      const data = payload as Record<string, unknown>;
      if (data.draftId !== draftId ||
          (data.trace !== null && !isTrace(data.trace)))
        throw new Error('Invalid trace');
      setTrace(data.trace as Trace | null);
      setStatus('ready');
    } catch { if (current === epoch.current) setStatus('error'); }
  }

  return <section aria-label="پیوند برنامه ملی و طرح محلی" style={{ border: '1px solid #bbb', padding: '0.75rem' }}>
    <h4>ردیابی ابلاغ ملی به طرح بومی</h4>
    <p>فقط اطلاعات برنامه‌ریزی است؛ وجود این پیوند به معنی اجرا یا تأیید نتیجه نیست.</p>
    <button type="button" onClick={onClose}>بستن پیوند</button>{' '}
    <button type="button" disabled={status === 'loading'} onClick={() => void refresh()}>دریافت پیوند برنامه</button>
    {status === 'loading' && <p role="status">در حال دریافت پیوند…</p>}
    {status === 'denied' && <p role="alert">مجوز مشاهده پیوند برنامه تأیید نشده است.</p>}
    {status === 'error' && <p role="alert">خواندن پیوند برنامه ممکن نشد.</p>}
    {status === 'ready' && (trace === null ? <p role="status">پیوند ثبت‌شده‌ای برای این طرح وجود ندارد.</p> :
      <div>
        <p>نسخه ملی {trace.nationalVersion} — شناسه برنامه {trace.programId}</p>
        <p>ابلاغ: {trace.issuanceId}</p>
        <p>نسخه اولیه متصل: {trace.pinnedRevision} — آخرین نسخه طرح: {trace.latestRevision}</p>
        <p>عنوان طرح: {trace.localTitle}</p>
        <strong>فعالیت‌های برنامه‌ریزی‌شده، نه اجرای قطعی</strong>
        <ul>{trace.plannedActivities.map((item, i) =>
          <li key={i}>{typeof item === 'string' ? item : JSON.stringify(item)}</li>)}</ul>
      </div>)}
  </section>;
}

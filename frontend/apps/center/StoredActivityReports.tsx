import React, { useEffect, useRef, useState } from 'react';

type ActivityReport = {
  reportSequence: number; reportId: string; linkId: string; sourceKind: string;
  sourceRecordRef: string; evidenceRef: string; reportedSummary: string;
  claimedOccurredAt: string | null; recordedAt: string; verificationStatus: 'UNVERIFIED'
};
function validReport(value: unknown): value is ActivityReport {
  if (!value || typeof value !== 'object') return false;
  const item = value as Record<string, unknown>;
  return Number.isSafeInteger(item.reportSequence) && Number(item.reportSequence) > 0 &&
    typeof item.reportId === 'string' && typeof item.linkId === 'string' &&
    typeof item.sourceKind === 'string' && typeof item.sourceRecordRef === 'string' &&
    typeof item.evidenceRef === 'string' && typeof item.reportedSummary === 'string' &&
    typeof item.recordedAt === 'string' && !Number.isNaN(Date.parse(item.recordedAt)) &&
    (item.claimedOccurredAt === null ||
      (typeof item.claimedOccurredAt === 'string' && !Number.isNaN(Date.parse(item.claimedOccurredAt)))) &&
    item.verificationStatus === 'UNVERIFIED';
}

export function StoredActivityReports({ token, centerId, draftId, onClose }: {
  token: string; centerId: string; draftId: string; onClose: () => void;
}) {
  const epoch = useRef(0);
  const [reports, setReports] = useState<ActivityReport[]>([]);
  const [cursor, setCursor] = useState<number | null>(null);
  const [status, setStatus] = useState<'idle' | 'loading' | 'ready' | 'denied' | 'error'>('idle');
  useEffect(() => () => { epoch.current++; }, []);

  async function load(before: number | null) {
    if (!centerId || !token.trim() || status === 'loading') return;
    const current = ++epoch.current;
    setStatus('loading');
    if (before === null) { setReports([]); setCursor(null); }
    try {
      const url = '/api/v1/centers/' + encodeURIComponent(centerId) +
        '/local-plan-drafts/' + encodeURIComponent(draftId) + '/activity-reports' +
        (before === null ? '' : '?beforeSequence=' + before);
      const response = await fetch(url, {
        headers: { Authorization: 'Bearer ' + token.trim() }, cache: 'no-store'
      });
      if (current !== epoch.current) return;
      if (response.status === 401 || response.status === 403) {
        setReports([]); setCursor(null); setStatus('denied'); return;
      }
      if (!response.ok) throw new Error('Unavailable');
      const payload: unknown = await response.json();
      if (current !== epoch.current) return;
      if (!payload || typeof payload !== 'object') throw new Error('Invalid');
      const data = payload as Record<string, unknown>;
      if (data.draftId !== draftId || !Array.isArray(data.reports) ||
          data.reports.length > 20 || !data.reports.every(validReport) ||
          (data.nextBeforeSequence !== null && 
            (!Number.isSafeInteger(data.nextBeforeSequence) || Number(data.nextBeforeSequence) <= 0)))
        throw new Error('Invalid reports');
      const entries = data.reports as ActivityReport[];
      if (entries.some((item, index) =>
          (before !== null && item.reportSequence >= before) ||
          (index > 0 && entries[index - 1].reportSequence <= item.reportSequence)) ||
          (data.nextBeforeSequence !== null &&
            (entries.length !== 20 ||
              entries[entries.length - 1].reportSequence !== data.nextBeforeSequence)))
        throw new Error('Invalid pagination');
      setReports(old => before === null ? entries : [...old, ...entries]);
      setCursor(data.nextBeforeSequence as number | null);
      setStatus('ready');
    } catch {
      if (current === epoch.current) { setReports([]); setCursor(null); setStatus('error'); }
    }
  }

  return <section aria-label="گزارش فعالیت و ارجاع شواهد" style={{ border: '1px solid #aaa', padding: '0.75rem' }}>
    <h4>گزارش‌های فعالیت و ارجاع شواهد</h4>
    <p>این موارد صرفاً گزارش‌های ثبت‌شده و راستی‌آزمایی‌نشده‌اند؛ نشانه اجرای قطعی، تأیید مدرک یا نتیجه نیستند.</p>
    <button type="button" onClick={onClose}>بستن گزارش‌ها</button>{' '}
    <button type="button" disabled={status === 'loading'} onClick={() => void load(null)}>دریافت گزارش‌ها</button>
    {status === 'loading' && <p role="status">در حال دریافت گزارش‌ها…</p>}
    {status === 'denied' && <p role="alert">مجوز مستقل مشاهده گزارش فعالیت تأیید نشده است.</p>}
    {status === 'error' && <p role="alert">دریافت گزارش‌های فعالیت ممکن نشد.</p>}
    {status === 'ready' && (reports.length === 0 ? <p role="status">گزارش قابل مشاهده‌ای وجود ندارد.</p> :
      <ul>{reports.map(item => <li key={item.reportId}>
        <strong>{item.reportedSummary} — راستی‌آزمایی‌نشده</strong>
        <p>منبع گزارش: {item.sourceKind} / {item.sourceRecordRef}</p>
        <p>ارجاع مدرک (تأییدنشده): {item.evidenceRef}</p>
        <p>زمان ادعاشده: {item.claimedOccurredAt === null ? 'نامشخص' :
          new Date(item.claimedOccurredAt).toLocaleString('fa-IR')}</p>
      </li>)}</ul>)}
    {status === 'ready' && cursor !== null &&
      <button type="button" onClick={() => void load(cursor)}>گزارش‌های قدیمی‌تر</button>}
  </section>;
}

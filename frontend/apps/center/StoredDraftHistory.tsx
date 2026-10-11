import React, { useEffect, useRef, useState } from 'react';

type Revision = {
  revision: number; programId: string; nationalVersion: number;
  localTitle: string; localActivities: unknown[]; recordedAt: string;
};
function validRevision(value: unknown): value is Revision {
  if (!value || typeof value !== 'object') return false;
  const item = value as Record<string, unknown>;
  return Number.isSafeInteger(item.revision) && Number(item.revision) > 0 &&
    typeof item.programId === 'string' && typeof item.localTitle === 'string' &&
    Number.isSafeInteger(item.nationalVersion) && Number(item.nationalVersion) > 0 &&
    Array.isArray(item.localActivities) && typeof item.recordedAt === 'string' &&
    !Number.isNaN(Date.parse(item.recordedAt));
}

type Props = { centerId: string; draftId: string; token: string; onClose: () => void };

export function StoredDraftHistory({ centerId, draftId, token, onClose }: Props) {
  const epoch = useRef(0);
  const [items, setItems] = useState<Revision[]>([]);
  const [next, setNext] = useState<number | null>(null);
  const [status, setStatus] = useState<'idle' | 'loading' | 'ready' | 'denied' | 'error'>('idle');
  useEffect(() => () => { epoch.current++; }, []);

  async function load(before: number | null) {
    if (!centerId || !token.trim() || status === 'loading') return;
    const current = ++epoch.current;
    setStatus('loading');
    if (before === null) { setItems([]); setNext(null); }
    const url = '/api/v1/centers/' + encodeURIComponent(centerId) +
      '/local-plan-drafts/' + encodeURIComponent(draftId) + '/revisions' +
      (before === null ? '' : '?beforeRevision=' + before);
    try {
      const response = await fetch(url, {
        headers: { Authorization: 'Bearer ' + token.trim() }, cache: 'no-store'
      });
      if (current !== epoch.current) return;
      if (response.status === 401 || response.status === 403) {
        setItems([]); setNext(null); setStatus('denied'); return;
      }
      if (!response.ok) throw new Error('Unavailable');
      const data: unknown = await response.json();
      if (current !== epoch.current) return;
      if (!data || typeof data !== 'object') throw new Error('Invalid');
      const value = data as Record<string, unknown>;
      if (value.draftId !== draftId || !Array.isArray(value.revisions) ||
          !value.revisions.every(validRevision) || value.revisions.length > 20 ||
          (value.nextBeforeRevision !== null &&
            (!Number.isSafeInteger(value.nextBeforeRevision) || Number(value.nextBeforeRevision) <= 0)))
        throw new Error('Invalid revisions');
      const revisions = value.revisions as Revision[];
      if (revisions.some((revision, i) =>
          (before !== null && revision.revision >= before) ||
          (i > 0 && revisions[i - 1].revision <= revision.revision)) ||
          (value.nextBeforeRevision !== null &&
            (revisions.length !== 20 ||
              Number(value.nextBeforeRevision) !== revisions[revisions.length - 1].revision)))
        throw new Error('Invalid cursor');
      if (before === null) setItems(revisions);
      else setItems(previous => [...previous, ...revisions]);
      setNext(value.nextBeforeRevision as number | null);
      setStatus('ready');
    } catch {
      if (current === epoch.current) { setItems([]); setNext(null); setStatus('error'); }
    }
  }

  return <section aria-label="تاریخچه پیش‌نویس" style={{ border: '1px solid #aaa', padding: '0.75rem' }}>
    <h4>تاریخچه نسخه‌های ثبت‌شده</h4>
    <p>فقط مشاهده؛ بازیابی یا جایگزینی نسخه قدیمی مجاز نشده است.</p>
    <button type="button" onClick={onClose}>بستن تاریخچه</button>{' '}
    <button type="button" disabled={status === 'loading'} onClick={() => void load(null)}>دریافت تاریخچه</button>
    {status === 'loading' && <p role="status">در حال دریافت تاریخچه…</p>}
    {status === 'denied' && <p role="alert">مجوز مستقل مشاهده تاریخچه تأیید نشده است.</p>}
    {status === 'error' && <p role="alert">تاریخچه قابل دریافت نیست.</p>}
    {status === 'ready' && (items.length === 0 ? <p role="status">نسخه‌ای در این محدوده ثبت نشده است.</p> :
      <ol>{items.map(item => <li key={item.revision}>
        <strong>نسخه {item.revision} — {item.localTitle}</strong>
        <p>نسخه ملی {item.nationalVersion} — {new Date(item.recordedAt).toLocaleString('fa-IR')}</p>
        <ul>{item.localActivities.map((activity, i) =>
          <li key={i}>{typeof activity === 'string' ? activity : JSON.stringify(activity)}</li>)}</ul>
      </li>)}</ol>)}
    {status === 'ready' && next !== null &&
      <button type="button" onClick={() => void load(next)}>نسخه‌های قدیمی‌تر</button>}
  </section>;
}

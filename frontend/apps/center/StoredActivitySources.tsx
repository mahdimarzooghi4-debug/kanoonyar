import React, { useEffect, useRef, useState } from 'react';
import { SessionClaimReporter } from './SessionClaimReporter';

type SourceKind = 'CLASS_SESSION' | 'CENTER_EVENT';
type Source = {
  sourceId: string; title: string; classId: string | null; classTitle: string | null;
  scheduledAt: string | null;
};
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
function validSource(value: unknown, kind: SourceKind): value is Source {
  if (!value || typeof value !== 'object') return false;
  const s = value as Record<string, unknown>;
  return typeof s.sourceId === 'string' && uuid.test(s.sourceId) &&
    typeof s.title === 'string' && s.title.trim().length > 0 &&
    (kind === 'CLASS_SESSION'
      ? typeof s.classId === 'string' && uuid.test(s.classId) && typeof s.classTitle === 'string'
      : s.classId === null && s.classTitle === null) &&
    (s.scheduledAt === null ||
      (typeof s.scheduledAt === 'string' && !Number.isNaN(Date.parse(s.scheduledAt))));
}

export function StoredActivitySources({ token, centerId }: { token: string; centerId: string }) {
  const epoch = useRef(0);
  const [kind, setKind] = useState<SourceKind>('CLASS_SESSION');
  const [items, setItems] = useState<Source[]>([]);
  const [cursor, setCursor] = useState<string | null>(null);
  const [selectedSession, setSelectedSession] = useState<string | null>(null);
  const [status, setStatus] = useState<'idle' | 'loading' | 'ready' | 'denied' | 'error'>('idle');
  useEffect(() => {
    epoch.current++;
    setItems([]); setCursor(null); setSelectedSession(null); setStatus('idle');
    return () => { epoch.current++; };
  }, [token, centerId]);

  async function load(source: SourceKind, before: string | null) {
    if (!centerId || !token.trim()) return;
    const current = ++epoch.current;
    setKind(source); setSelectedSession(null); setStatus('loading');
    if (source !== kind || before === null) { setItems([]); setCursor(null); }
    try {
      const type = source === 'CLASS_SESSION' ? 'class-sessions' : 'events';
      const uri = '/api/v1/centers/' + encodeURIComponent(centerId) +
        '/activity-sources/' + type + (before === null ? '' : '?beforeSourceId=' + encodeURIComponent(before));
      const response = await fetch(uri, {
        headers: { Authorization: 'Bearer ' + token.trim() }, cache: 'no-store'
      });
      if (current !== epoch.current) return;
      if (response.status === 401 || response.status === 403) {
        setItems([]); setCursor(null); setStatus('denied'); return;
      }
      if (!response.ok) throw new Error('Not available');
      const payload: unknown = await response.json();
      if (current !== epoch.current || !payload || typeof payload !== 'object')
        throw new Error('Invalid response');
      const data = payload as Record<string, unknown>;
      if (data.sourceKind !== source || !Array.isArray(data.records) ||
          data.records.length > 20 || !data.records.every(item => validSource(item, source)) ||
          (data.nextBeforeSourceId !== null &&
            (typeof data.nextBeforeSourceId !== 'string' || !uuid.test(data.nextBeforeSourceId))))
        throw new Error('Invalid sources');
      const records = data.records as Source[];
      if (records.some((item, i) =>
        (before !== null && item.sourceId.toLowerCase() >= before.toLowerCase()) ||
        (i > 0 && records[i - 1].sourceId.toLowerCase() <= item.sourceId.toLowerCase())) ||
        (data.nextBeforeSourceId !== null &&
          (records.length !== 20 ||
            records[records.length - 1].sourceId.toLowerCase() !== String(data.nextBeforeSourceId).toLowerCase())))
        throw new Error('Invalid page');
      setItems(old => before === null ? records : [...old, ...records]);
      setCursor(data.nextBeforeSourceId as string | null);
      setStatus('ready');
    } catch {
      if (current === epoch.current) { setItems([]); setCursor(null); setStatus('error'); }
    }
  }

  return <section aria-label="کاتالوگ جلسات و رویدادهای کانون" style={{ marginTop: '1.5rem' }}>
    <h3>جلسات کلاس و رویدادهای ثبت‌شده</h3>
    <p>این اطلاعات فقط برنامه‌ریزی و شناسه منبع هستند؛ برگزاری جلسه یا اجرای رویداد را تأیید نمی‌کنند.</p>
    <button type="button" disabled={!centerId || status === 'loading'} onClick={() => void load('CLASS_SESSION', null)}>دریافت جلسات کلاس</button>{' '}
    <button type="button" disabled={!centerId || status === 'loading'} onClick={() => void load('CENTER_EVENT', null)}>دریافت رویدادها</button>
    {status === 'loading' && <p role="status">در حال دریافت منبع…</p>}
    {status === 'denied' && <p role="alert">مجوز مستقل مشاهده جلسات و رویدادها تأیید نشده است.</p>}
    {status === 'error' && <p role="alert">دریافت اطلاعات جلسات و رویدادها ممکن نیست.</p>}
    {status === 'ready' && (items.length === 0 ? <p role="status">منبع ثبت‌شده‌ای در این محدوده موجود نیست.</p> :
      <ul>{items.map(item => <li key={item.sourceId}>
        <strong>{item.title}</strong>
        {kind === 'CLASS_SESSION' && <p>کلاس: {item.classTitle}</p>}
        <p>شناسه منبع: {item.sourceId}</p>
        {kind === 'CLASS_SESSION' && <button type="button"
          onClick={() => setSelectedSession(item.sourceId)}>گزارش وقوع و حضور جلسه</button>}
        {kind === 'CLASS_SESSION' && selectedSession === item.sourceId &&
          <SessionClaimReporter token={token} centerId={centerId} sessionId={item.sourceId}
            onClose={() => setSelectedSession(null)} />}
        <p>زمان برنامه‌ریزی: {item.scheduledAt === null ? 'نامشخص' :
          new Date(item.scheduledAt).toLocaleString('fa-IR')}</p>
      </li>)}</ul>)}
    {status === 'ready' && cursor !== null &&
      <button type="button" onClick={() => void load(kind, cursor)}>منابع قدیمی‌تر</button>}
  </section>;
}

import React, { useRef, useState } from 'react';

type Membership = { centerId: string; displayName: string; roleCode: string };
export type Draft = { draftId: string; revision: number; programId: string; nationalVersion: number; localTitle: string; localActivities: unknown[] };
function validDraft(value: unknown): value is Draft {
  if (!value || typeof value !== 'object') return false;
  const d = value as Record<string, unknown>;
  return typeof d.draftId === 'string' && typeof d.programId === 'string' &&
    typeof d.localTitle === 'string' && Number.isInteger(d.revision) && Number(d.revision) > 0 &&
    Number.isInteger(d.nationalVersion) && Number(d.nationalVersion) > 0 && Array.isArray(d.localActivities);
}
export function StoredDrafts({ token, memberships, selectedCenter, onCenterChange, onEditDraft }: {
  token: string; memberships: Membership[]; selectedCenter: string;
  onCenterChange: (centerId: string) => void; onEditDraft: (draft: Draft) => void;
}) {
  const readEpoch = useRef(0);
  const [drafts, setDrafts] = useState<Draft[]>([]);
  const [status, setStatus] = useState<'idle' | 'loading' | 'ready' | 'denied' | 'error'>('idle');
  async function load() {
    if (!selectedCenter || !token.trim()) return;
    const epoch = ++readEpoch.current;
    setDrafts([]);
    setStatus('loading');
    try {
      const response = await fetch('/api/v1/centers/' + encodeURIComponent(selectedCenter) + '/local-plan-drafts', {
        headers: { Authorization: 'Bearer ' + token.trim() }, cache: 'no-store'
      });
      if (epoch !== readEpoch.current) return;
      if (response.status === 401 || response.status === 403) { setStatus('denied'); return; }
      if (!response.ok) throw new Error('Unavailable');
      const body: unknown = await response.json();
      if (epoch !== readEpoch.current) return;
      if (!body || typeof body !== 'object' || !('drafts' in body) || !Array.isArray(body.drafts) ||
          !body.drafts.every(validDraft)) throw new Error('Invalid drafts');
      setDrafts(body.drafts);
      setStatus('ready');
    } catch { if (epoch === readEpoch.current) setStatus('error'); }
  }
  return <section aria-label="پیش‌نویس‌های ثبت‌شده" style={{ marginTop: '2rem' }}>
    <h2>آخرین نسخه پیش‌نویس‌های ثبت‌شده</h2>
    <p>مشاهده پیش‌نویس‌ها نیز به مجوز مستقل نیاز دارد؛ عضویت کانون به‌تنهایی مجوز مشاهده یا ویرایش نیست.</p>
    <label>انتخاب کانون <select aria-label="انتخاب کانون برای پیش‌نویس‌ها" value={selectedCenter}
      onChange={e => { readEpoch.current++; onCenterChange(e.target.value); setDrafts([]); setStatus('idle'); }}>
      <option value="">انتخاب کنید</option>
      {Array.from(new Map(memberships.map(m => [m.centerId, m.displayName])).entries()).map(([id, name]) =>
        <option key={id} value={id}>{name}</option>)}
    </select></label>
    <button type="button" disabled={!selectedCenter || status === 'loading'} onClick={() => void load()}>دریافت پیش‌نویس‌های ثبت‌شده</button>
    {status === 'loading' && <p role="status">در حال دریافت پیش‌نویس‌ها…</p>}
    {status === 'denied' && <p role="alert">مجوز مستقل مشاهده پیش‌نویس تأیید نشده است.</p>}
    {status === 'error' && <p role="alert">دریافت پیش‌نویس‌ها ممکن نشد.</p>}
    {status === 'ready' && (drafts.length === 0 ? <p role="status">پیش‌نویس ثبت‌شده‌ای وجود ندارد.</p> :
      <ul>{drafts.map(d => <li key={d.draftId}>
        <strong>{d.localTitle}</strong> — نسخه محلی {d.revision}، نسخه ملی {d.nationalVersion}{' '}
        <button type="button" onClick={() => onEditDraft(d)}>ویرایش پیش‌نویس</button>
        <ul>{d.localActivities.map((a, i) => <li key={i}>{typeof a === 'string' ? a : JSON.stringify(a)}</li>)}</ul>
      </li>)}</ul>)}
  </section>;
}

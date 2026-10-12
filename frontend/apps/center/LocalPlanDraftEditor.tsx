import React, { useEffect, useRef, useState } from 'react';
import type { Draft } from './StoredDrafts';

export type NationalProgram = {
  programId: string; version: number; title: string;
  nationalObjectives: unknown[]; mandatoryRequirements: unknown[];
};

type SaveState = 'idle' | 'saving' | 'saved' | 'denied' | 'conflict' | 'error';
type Props = { program: NationalProgram; token: string; centerId: string; editingDraft: Draft | null };

export function LocalPlanDraftEditor({ program, token, centerId, editingDraft }: Props) {
  const writeEpoch = useRef(0);
  const [draftId, setDraftId] = useState(() => crypto.randomUUID());
  const [revision, setRevision] = useState(0);
  const [title, setTitle] = useState('');
  const [activity, setActivity] = useState('');
  const [activities, setActivities] = useState<string[]>([]);
  const [status, setStatus] = useState<SaveState>('idle');
  const matches = editingDraft?.programId === program.programId &&
    editingDraft.nationalVersion === program.version ? editingDraft : null;
  // Existing JSON arrays can contain objects; the string-only editor must not drop these silently.
  const unsupportedActivities = matches !== null &&
    matches.localActivities.some(a => typeof a !== 'string');

  useEffect(() => {
    writeEpoch.current++;
    setDraftId(matches?.draftId ?? crypto.randomUUID());
    setRevision(matches?.revision ?? 0);
    setTitle(matches?.localTitle ?? '');
    setActivities(matches?.localActivities.filter((a): a is string => typeof a === 'string') ?? []);
    setActivity('');
    setStatus('idle');
  }, [centerId, token, program.programId, program.version, matches?.draftId, matches?.revision]);

  function addActivity() {
    const value = activity.trim();
    if (!value) return;
    setActivities(current => [...current, value]);
    setActivity('');
    setStatus('idle');
  }

  async function save() {
    if (!centerId || !token.trim() || !title.trim() || status === 'saving' || unsupportedActivities) return;
    const epoch = ++writeEpoch.current;
    setStatus('saving');
    try {
      const response = await fetch('/api/v1/centers/' + encodeURIComponent(centerId) + '/local-plan-drafts/revisions', {
        method: 'POST',
        headers: { Authorization: 'Bearer ' + token.trim(), 'Content-Type': 'application/json' },
        body: JSON.stringify({
          draftId, expectedRevision: revision, programId: program.programId,
          nationalVersion: program.version, localTitle: title.trim(), localActivities: activities
        })
      });
      if (epoch !== writeEpoch.current) return;
      if (response.status === 401 || response.status === 403) { setStatus('denied'); return; }
      if (response.status === 409) { setStatus('conflict'); return; }
      if (!response.ok) { setStatus('error'); return; }
      const payload: unknown = await response.json();
      if (epoch !== writeEpoch.current) return;
      if (!payload || typeof payload !== 'object') throw new Error('Invalid response');
      const value = payload as Record<string, unknown>;
      if (value.draftId !== draftId || !Number.isInteger(value.revision) ||
          Number(value.revision) !== revision + 1 ||
          value.programId !== program.programId || value.nationalVersion !== program.version)
        throw new Error('Invalid revision acknowledgement');
      setRevision(Number(value.revision));
      setStatus('saved');
    } catch { if (epoch === writeEpoch.current) setStatus('error'); }
  }

  return <section aria-label={'پیش‌نویس محلی ' + program.title}
    style={{ border: '1px solid #aaa', padding: '1rem', margin: '1rem 0' }}>
    <h4>پیش‌نویس برنامه اجرایی محلی</h4>
    <p>برنامه ملی: {program.title} — نسخه {program.version}. اهداف و الزامات ملی قابل تغییر نیستند.</p>
    <label>عنوان محلی <input aria-label={'عنوان محلی ' + program.programId}
      value={title} onChange={e => { setTitle(e.target.value); setStatus('idle'); }} /></label>
    <div><label>فعالیت محلی <input aria-label={'فعالیت محلی ' + program.programId}
      value={activity} onChange={e => setActivity(e.target.value)} /></label>
      <button type="button" onClick={addActivity} disabled={!activity.trim()}>افزودن فعالیت</button></div>
    <ul>{activities.map((item, i) => <li key={i}>{item}{' '}
      <button type="button" aria-label={'حذف فعالیت ' + (i + 1)}
        onClick={() => { setActivities(old => old.filter((_, index) => index !== i)); setStatus('idle'); }}>حذف</button>
    </li>)}</ul>
    <p>نسخه پیش‌نویس: {revision} — ثبت نسخه جدید منوط به مجوز مستقل عملیات کانون است.</p>
    {unsupportedActivities && <p role="alert">این پیش‌نویس شامل فعالیت‌های غیرمتنی است و در این ویرایشگر قابل تغییر نیست.</p>}
    {status === 'idle' && <p role="status">این پیش‌نویس تا دریافت پاسخ موفق سرور ثبت‌شده محسوب نمی‌شود.</p>}
    {status === 'saving' && <p role="status">در حال ارسال درخواست ثبت…</p>}
    {status === 'saved' && <p role="status">نسخه {revision} توسط API ثبت شد.</p>}
    {status === 'denied' && <p role="alert">مجوز مستقل ثبت پیش‌نویس تأیید نشده است؛ هیچ نسخه‌ای ثبت نشد.</p>}
    {status === 'conflict' && <p role="alert">تعارض ویرایش هم‌زمان: نسخه تازه را دوباره دریافت کنید.</p>}
    {status === 'error' && <p role="alert">ثبت پیش‌نویس تأیید نشد. وضعیت را از سرور بررسی کنید.</p>}
    <button type="button" disabled={!centerId || !token.trim() || !title.trim() || status === 'saving' || unsupportedActivities}
      onClick={() => void save()}>
      {centerId ? 'ثبت پیش‌نویس نسخه‌دار' : 'ثبت برنامه اجرایی (نیازمند مجوز و اتصال امن)'}
    </button>
  </section>;
}

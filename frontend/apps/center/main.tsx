import React, { useState } from 'react';
import { createRoot } from 'react-dom/client';
import { Shell } from '../../packages/ui/Shell';
import { StoredDrafts } from './StoredDrafts';
import type { Draft } from './StoredDrafts';
import { LocalPlanDraftEditor } from './LocalPlanDraftEditor';

type Membership = { centerId: string; displayName: string; roleCode: string };
type State = { status: 'idle' | 'loading' | 'ready' | 'error'; memberships: Membership[]; message: string };

type NationalProgram = { programId: string; version: number; title: string; nationalObjectives: unknown[]; mandatoryRequirements: unknown[] };
function validNationalProgram(item: unknown): item is NationalProgram {
  if (!item || typeof item !== 'object') return false;
  const p = item as Record<string, unknown>;
  return typeof p.programId === 'string' && typeof p.title === 'string' &&
    typeof p.version === 'number' && Number.isInteger(p.version) && p.version > 0 &&
    Array.isArray(p.nationalObjectives) && Array.isArray(p.mandatoryRequirements);
}
function NationalProgramDiscovery({ token, centerId, editingDraft }: { token: string; centerId: string; editingDraft: Draft | null }) {
  const [programs, setPrograms] = useState<NationalProgram[]>([]);
  const [status, setStatus] = useState<'idle' | 'loading' | 'ready' | 'error'>('idle');
  async function refresh() {
    setStatus('loading');
    setPrograms([]);
    try {
      const response = await fetch('/api/v1/programs/published', { cache: 'no-store' });
      if (!response.ok) throw new Error('Unavailable');
      const body: unknown = await response.json();
      if (!body || typeof body !== 'object' || !('programs' in body) || !Array.isArray(body.programs) ||
          !body.programs.every(validNationalProgram)) throw new Error('Invalid response');
      setPrograms(body.programs);
      setStatus('ready');
    } catch { setStatus('error'); }
  }
  return <section aria-label="برنامه‌های ملی" style={{ marginTop: '2rem' }}>
    <h2>برنامه‌های ملی منتشرشده</h2>
    <p>اطلاعات فقط برای مطالعه است. طراحی و ثبت برنامه اجرایی محلی هنوز فعال نیست.</p>
    <button type="button" disabled={status === 'loading'} onClick={() => void refresh()}>نمایش برنامه‌های ملی</button>
    {status === 'loading' && <p role="status">در حال دریافت برنامه‌ها…</p>}
    {status === 'error' && <p role="alert">دریافت برنامه‌های ملی ممکن نشد.</p>}
    {status === 'ready' && (programs.length === 0 ? <p role="status">برنامه منتشرشده‌ای وجود ندارد.</p> :
      <ul>{programs.map(p => <li key={`${p.programId}-${p.version}`}>
        <h3>{p.title} — نسخه {p.version}</h3>
        <strong>اهداف ملی</strong><ul>{p.nationalObjectives.map((item, i) => <li key={i}>{typeof item === 'string' ? item : JSON.stringify(item)}</li>)}</ul>
        <strong>الزامات اجباری</strong><ul>{p.mandatoryRequirements.map((item, i) => <li key={i}>{typeof item === 'string' ? item : JSON.stringify(item)}</li>)}</ul>
        <LocalPlanDraftEditor key={p.programId + ':' + p.version} program={p} token={token} centerId={centerId} editingDraft={editingDraft} />
      </li>)}</ul>)}
  </section>;
}

function CenterWorkspace() {
  const [token, setToken] = useState('');
  const [selectedCenter, setSelectedCenter] = useState('');
  const [editingDraft, setEditingDraft] = useState<Draft | null>(null);
  const [state, setState] = useState<State>({ status: 'idle', memberships: [], message: '' });
  async function loadMemberships() {
    if (!token.trim()) { setState({ status: 'error', memberships: [], message: 'توکن احراز هویت لازم است.' }); return; }
    setState({ status: 'loading', memberships: [], message: '' });
    try {
      const response = await fetch('/api/v1/identity/center-memberships', {
        headers: { Authorization: `Bearer ${token.trim()}` }, cache: 'no-store'
      });
      if (!response.ok) {
        const message = response.status === 401 ? 'نشست معتبر نیست.' : response.status === 403 ? 'شناسه کاربر معتبر نیست.' : 'دریافت عضویت‌ها ممکن نشد.';
        setState({ status: 'error', memberships: [], message });
        return;
      }
      const body: unknown = await response.json();
      if (!body || typeof body !== 'object' || !('memberships' in body) || !Array.isArray(body.memberships) ||
        !body.memberships.every((item: unknown) => item !== null && typeof item === 'object' &&
          'centerId' in item && typeof item.centerId === 'string' &&
          'displayName' in item && typeof item.displayName === 'string' &&
          'roleCode' in item && typeof item.roleCode === 'string')) throw new Error('Invalid response');
      setState({ status: 'ready', memberships: body.memberships as Membership[], message: '' });
    } catch {
      setState({ status: 'error', memberships: [], message: 'ارتباط با سرویس برقرار نشد.' });
    }
  }
  return <>
    <Shell title="پنل کانون" />
    <main dir="rtl" style={{ maxWidth: 760, margin: '2rem auto', padding: '1rem', fontFamily: 'sans-serif' }}>
      <h2>عضویت‌های فعال من</h2>
      <p>این صفحه صرفاً عضویت‌های ثبت‌شده را نمایش می‌دهد و مجوز اجرای عملیات صادر نمی‌کند.</p>
      <label htmlFor="center-token">توکن دسترسی آزمایشی</label>
      <input id="center-token" type="password" autoComplete="off" value={token} onChange={e => setToken(e.target.value)} aria-label="توکن دسترسی" />
      <button type="button" disabled={state.status === 'loading'} onClick={loadMemberships}>دریافت عضویت‌ها</button>
      {state.status === 'loading' && <p role="status">در حال دریافت…</p>}
      {state.status === 'error' && <p role="alert">{state.message}</p>}
      {state.status === 'ready' && (state.memberships.length ? <ul>{state.memberships.map((m, i) => <li key={`${m.centerId}-${m.roleCode}-${i}`}>{m.displayName} ({m.centerId}) — {m.roleCode}</li>)}</ul> : <p role="status">عضویت فعالی ثبت نشده است.</p>)}
      {state.status === 'ready' && state.memberships.length > 0 && <StoredDrafts
        token={token} memberships={state.memberships}
        selectedCenter={state.memberships.some(m => m.centerId === selectedCenter) ? selectedCenter : ''}
        onCenterChange={value => { setSelectedCenter(value); setEditingDraft(null); }}
        onEditDraft={draft => setEditingDraft(draft)} />}
      <NationalProgramDiscovery token={token}
        centerId={state.status === 'ready' && state.memberships.some(m => m.centerId === selectedCenter) ? selectedCenter : ''}
        editingDraft={editingDraft} />
    </main>
  </>;
}
createRoot(document.getElementById('root')!).render(<React.StrictMode><CenterWorkspace /></React.StrictMode>);

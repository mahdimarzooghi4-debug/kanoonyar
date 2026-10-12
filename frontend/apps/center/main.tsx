import React, { useState } from 'react';
import { createRoot } from 'react-dom/client';
import { Shell } from '../../packages/ui/Shell';

type Membership = { centerId: string; displayName: string; roleCode: string };
type State = { status: 'idle' | 'loading' | 'ready' | 'error'; memberships: Membership[]; message: string };

function CenterWorkspace() {
  const [token, setToken] = useState('');
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
    </main>
  </>;
}
createRoot(document.getElementById('root')!).render(<React.StrictMode><CenterWorkspace /></React.StrictMode>);

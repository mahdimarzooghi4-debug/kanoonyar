import React, { useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { Shell } from '../../packages/ui/Shell';

type Program = { programId: string; version: number; title: string; nationalObjectives: unknown[]; mandatoryRequirements: unknown[] };
type CatalogState = { status: 'loading' | 'ready' | 'error'; programs: Program[]; error: string };
function validProgram(value: unknown): value is Program {
  if (!value || typeof value !== 'object') return false;
  const p = value as Record<string, unknown>;
  return typeof p.programId === 'string' && typeof p.title === 'string' &&
    typeof p.version === 'number' && Number.isInteger(p.version) && p.version > 0 &&
    Array.isArray(p.nationalObjectives) && Array.isArray(p.mandatoryRequirements);
}
function renderItems(items: unknown[]) {
  return <ul>{items.map((item, i) => <li key={i}>{typeof item === 'string' ? item : JSON.stringify(item)}</li>)}</ul>;
}
function NationalProgramCatalog() {
  const [state, setState] = useState<CatalogState>({ status: 'loading', programs: [], error: '' });
  useEffect(() => {
    const controller = new AbortController();
    async function load() {
      try {
        const response = await fetch('/api/v1/programs/published', { signal: controller.signal, cache: 'no-store' });
        if (!response.ok) throw new Error('Service unavailable');
        const body: unknown = await response.json();
        if (!body || typeof body !== 'object' || !('programs' in body) || !Array.isArray(body.programs) ||
          !body.programs.every(validProgram)) throw new Error('Invalid catalog');
        if (!controller.signal.aborted) setState({ status: 'ready', programs: body.programs, error: '' });
      } catch {
        if (!controller.signal.aborted) setState({ status: 'error', programs: [], error: 'فهرست برنامه‌های ملی در دسترس نیست.' });
      }
    }
    void load();
    return () => controller.abort();
  }, []);
  return <>
    <Shell title="پنل ستاد" />
    <main dir="rtl" style={{ maxWidth: 960, margin: '2rem auto', padding: '1rem', fontFamily: 'sans-serif' }}>
      <h2>برنامه‌های ملی منتشرشده</h2>
      <p>نمایش فقط خواندنی نسخه‌های منتشرشده؛ ثبت یا انتشار برنامه از این صفحه ممکن نیست.</p>
      {state.status === 'loading' && <p role="status">در حال دریافت برنامه‌ها…</p>}
      {state.status === 'error' && <p role="alert">{state.error}</p>}
      {state.status === 'ready' && (state.programs.length === 0 ? <p role="status">برنامه منتشرشده‌ای وجود ندارد.</p> :
        state.programs.map(p => <article key={`${p.programId}-${p.version}`}>
          <h3>{p.title} — نسخه {p.version}</h3>
          <p>شناسه برنامه: {p.programId}</p>
          <h4>اهداف ملی</h4>{renderItems(p.nationalObjectives)}
          <h4>الزامات اجباری</h4>{renderItems(p.mandatoryRequirements)}
        </article>))}
    </main>
  </>;
}
createRoot(document.getElementById('root')!).render(<React.StrictMode><NationalProgramCatalog /></React.StrictMode>);

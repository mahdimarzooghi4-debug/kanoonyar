import React, { useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { Shell } from '../../packages/ui/Shell';
import { NationalInspectionRollupWorkbench } from './NationalInspectionRollupWorkbench';

type Program = { programId: string; version: number; title: string;
  nationalObjectives: string[]; mandatoryRequirements: string[] };
type Cursor = { afterProgramId: string; afterVersion: number };
type CatalogState = { status: 'loading' | 'ready' | 'error'; programs: Program[]; error: string };
function parsePage(value: unknown): { programs: Program[]; nextCursor: Cursor | null } | null {
  if (!value || typeof value !== 'object') return null;
  const payload = value as Record<string, unknown>;
  if (!Array.isArray(payload.programs) || payload.programs.length > 500) return null;
  const seen = new Set<string>();
  const programs: Program[] = [];
  for (const value of payload.programs) {
    if (!value || typeof value !== 'object') return null;
    const p = value as Record<string, unknown>;
    if (typeof p.programId !== 'string' || !p.programId.trim() || p.programId.length > 256 ||
        typeof p.title !== 'string' || !p.title.trim() || p.title.length > 500 ||
        typeof p.version !== 'number' || !Number.isSafeInteger(p.version) || p.version <= 0 ||
        !Array.isArray(p.nationalObjectives) || !Array.isArray(p.mandatoryRequirements) ||
        p.nationalObjectives.length > 100 || p.mandatoryRequirements.length > 100 ||
        !p.nationalObjectives.every(x => typeof x === 'string' && x.trim() && x.length <= 2000) ||
        !p.mandatoryRequirements.every(x => typeof x === 'string' && x.trim() && x.length <= 2000)) return null;
    const key = JSON.stringify([p.programId, p.version]);
    if (seen.has(key)) return null;
    seen.add(key);
    programs.push({
      programId: p.programId, version: p.version, title: p.title,
      nationalObjectives: [...p.nationalObjectives] as string[],
      mandatoryRequirements: [...p.mandatoryRequirements] as string[],
    });
  }
  const next = payload.nextCursor;
  if (next === undefined && programs.length === 500) return null;
  if (next !== undefined && next !== null) {
    if (!next || typeof next !== 'object' || programs.length !== 500) return null;
    const c = next as Record<string, unknown>;
    const last = programs[programs.length - 1];
    if (typeof c.afterProgramId !== 'string' || c.afterProgramId.length > 256 ||
        typeof c.afterVersion !== 'number' || !Number.isSafeInteger(c.afterVersion) ||
        c.afterProgramId !== last.programId || c.afterVersion !== last.version) return null;
    return { programs, nextCursor: {
      afterProgramId: c.afterProgramId, afterVersion: c.afterVersion,
    } };
  }
  return { programs, nextCursor: null };
}

function NationalProgramCatalog() {
  const [state, setState] = useState<CatalogState>({
    status: 'loading', programs: [], error: '',
  });
  useEffect(() => {
    const controller = new AbortController();
    async function load() {
      try {
        const programs: Program[] = [];
        const seen = new Set<string>();
        const cursors = new Set<string>();
        let cursor: Cursor | null = null;
        do {
          const url = cursor ? '/api/v1/programs/published?' + new URLSearchParams({
            afterProgramId: cursor.afterProgramId,
            afterVersion: String(cursor.afterVersion),
          }) : '/api/v1/programs/published';
          const response = await fetch(url, {
            signal: controller.signal, cache: 'no-store', credentials: 'omit',
          });
          if (!response.ok) throw new Error('Published source unavailable');
          const body: unknown = await response.json();
          const page = parsePage(body);
          if (!page || (cursor && page.programs.length === 0))
            throw new Error('Invalid published page');
          for (const item of page.programs) {
            const key = JSON.stringify([item.programId,item.version]);
            if (seen.has(key)) throw new Error('Repeated published version');
            seen.add(key);
            programs.push(item);
          }
          cursor = page.nextCursor;
          if (cursor) {
            const key = JSON.stringify([cursor.afterProgramId,cursor.afterVersion]);
            if (cursors.has(key)) throw new Error('Repeated published cursor');
            cursors.add(key);
          }
        } while (cursor && !controller.signal.aborted);
        if (!controller.signal.aborted)
          setState({ status: 'ready', programs, error: '' });
      } catch {
        if (!controller.signal.aborted) setState({
          status: 'error', programs: [],
          error: 'فهرست برنامه‌های ملی در دسترس نیست.',
        });
      }
    }
    void load();
    return () => controller.abort();
  }, []);
  return <>
    <Shell title="پنل ستاد" />
    <main dir="rtl" style={{ maxWidth: 960, margin: '2rem auto',
      padding: '1rem', fontFamily: 'sans-serif' }}>
      <h2>برنامه‌های ملی منتشرشده</h2>
      <p>فهرست فقط پس از دریافت همه صفحات نسخه‌های منتشرشده نمایش داده می‌شود.
        این صفحه اختیار ثبت یا انتشار برنامه ندارد.</p>
      {state.status === 'loading' && <p role="status">در حال دریافت نسخه‌های منتشرشده…</p>}
      {state.status === 'error' && <p role="alert">{state.error}</p>}
      {state.status === 'ready' && (state.programs.length === 0 ?
        <p role="status">برنامه منتشرشده‌ای وجود ندارد.</p> :
        state.programs.map(p => <article key={p.programId + ':' + p.version}>
          <h3>{p.title} — نسخه {p.version}</h3>
          <p>شناسه برنامه: {p.programId}</p>
          <h4>اهداف ملی</h4>
          <ul>{p.nationalObjectives.map((item,i) => <li key={i}>{item}</li>)}</ul>
          <h4>الزامات اجباری</h4>
          <ul>{p.mandatoryRequirements.map((item,i) => <li key={i}>{item}</li>)}</ul>
        </article>))}
      <NationalInspectionRollupWorkbench />
    </main>
  </>;
}
createRoot(document.getElementById('root')!).render(
  <React.StrictMode><NationalProgramCatalog /></React.StrictMode>);

import React, { useEffect, useMemo, useState } from 'react';

export type PublicProgram = {
  programId: string;
  version: number;
  title: string;
  nationalObjectives: string[];
  mandatoryRequirements: string[];
};
type State = { phase: 'loading' | 'ready' | 'error'; rows: PublicProgram[] };
type Cursor = { afterProgramId: string; afterVersion: number };
type PublishedPage = { rows: PublicProgram[]; nextCursor: Cursor | null };
const ENDPOINT = '/api/v1/programs/published';

/** The *published-only* S04 response is the sole acceptable audience source.
 * It contains no class enrolments, local center drafts or actor/member metadata.
 * Never render arbitrary unknown response keys or inferred participation.
 */
export function parsePublishedResponse(value: unknown): PublishedPage | null {
  if (!value || typeof value !== 'object' || !('programs' in value)) return null;
  const rows = (value as { programs: unknown }).programs;
  if (!Array.isArray(rows) || rows.length > 500) return null;
  const seen = new Set<string>();
  const out: PublicProgram[] = [];
  for (const row of rows) {
    if (!row || typeof row !== 'object') return null;
    const p = row as Record<string, unknown>;
    if (typeof p.programId !== 'string' || !p.programId.trim() || p.programId.length > 256 ||
        typeof p.title !== 'string' || !p.title.trim() || p.title.length > 500 ||
        typeof p.version !== 'number' || !Number.isSafeInteger(p.version) || p.version <= 0 ||
        !Array.isArray(p.nationalObjectives) || !Array.isArray(p.mandatoryRequirements) ||
        p.nationalObjectives.length > 100 || p.mandatoryRequirements.length > 100 ||
        !p.nationalObjectives.every(x => typeof x === 'string' && x.trim() && x.length <= 2000) ||
        !p.mandatoryRequirements.every(x => typeof x === 'string' && x.trim() && x.length <= 2000)) return null;
    const id = JSON.stringify([p.programId, p.version]);
    if (seen.has(id)) return null;
    seen.add(id);
    out.push({
      programId: p.programId, version: p.version, title: p.title,
      nationalObjectives: [...p.nationalObjectives] as string[],
      mandatoryRequirements: [...p.mandatoryRequirements] as string[],
    });
  }
  const next = (value as Record<string, unknown>).nextCursor;
  if (next === undefined && out.length === 500) return null; // Old truncated sources are unsafe.
  if (next !== null && next !== undefined) {
    if (!next || typeof next !== 'object') return null;
    const c = next as Record<string, unknown>;
    if (typeof c.afterProgramId !== 'string' || !c.afterProgramId.trim() ||
        c.afterProgramId.length > 256 ||
        typeof c.afterVersion !== 'number' || !Number.isSafeInteger(c.afterVersion) ||
        c.afterVersion <= 0 || out.length !== 500) return null;
    const last = out[out.length - 1];
    if (last.programId !== c.afterProgramId || last.version !== c.afterVersion) return null;
    return { rows: out, nextCursor: { afterProgramId: c.afterProgramId,
      afterVersion: c.afterVersion } };
  }
  return { rows: out, nextCursor: null };
}

export function NationalVersion({ version, suffix }: { version: PublicProgram; suffix: string }) {
  return <section aria-label={'نسخه ' + version.version + ' — ' + suffix}>
    <h4>{version.title} — نسخه منتشرشده {version.version}</h4>
    <p><a href={'?' + new URLSearchParams({
      programId: version.programId, version: String(version.version),
    })}>پیوند مستقیم همین نسخه منتشرشده</a></p>
    <h5>اهداف ملی ثابت این نسخه</h5>
    {version.nationalObjectives.length ?
      <ul>{version.nationalObjectives.map((v, i) => <li key={i}>{v}</li>)}</ul> :
      <p>هدف قابل نمایشی در این نسخه ثبت نشده است.</p>}
    <h5>الزامات اجباری این نسخه</h5>
    {version.mandatoryRequirements.length ?
      <ul>{version.mandatoryRequirements.map((v, i) => <li key={i}>{v}</li>)}</ul> :
      <p>الزام قابل نمایشی در این نسخه ثبت نشده است.</p>}
  </section>;
}

export function PublicNationalPrograms() {
  const [state, setState] = useState<State>({ phase: 'loading', rows: [] });
  const [reload, setReload] = useState(0);
  const [search, setSearch] = useState('');
  const [opened, setOpened] = useState<string | null>(null);
  const [previous, setPrevious] = useState<number | null>(null);
  useEffect(() => {
    const controller = new AbortController();
    setState({ phase: 'loading', rows: [] });
    setOpened(null);
    setPrevious(null);
    const fetchPublished = async () => {
      try {
        const rows: PublicProgram[] = [];
        const unique = new Set<string>();
        const usedCursors = new Set<string>();
        let cursor: Cursor | null = null;
        do {
          const url = cursor ? ENDPOINT + '?' + new URLSearchParams({
            afterProgramId: cursor.afterProgramId,
            afterVersion: String(cursor.afterVersion),
          }) : ENDPOINT;
          const response = await fetch(url, {
            method: 'GET', cache: 'no-store', credentials: 'omit',
            headers: { Accept: 'application/json' }, signal: controller.signal,
          });
          if (!response.ok) throw new Error('Published source unavailable');
          const value: unknown = await response.json();
          const page = parsePublishedResponse(value);
          if (!page || (cursor && page.rows.length === 0))
            throw new Error('Invalid published page');
          for (const item of page.rows) {
            const key = JSON.stringify([item.programId, item.version]);
            if (unique.has(key)) throw new Error('Duplicate published page source');
            unique.add(key);
            rows.push(item);
          }
          cursor = page.nextCursor;
          if (cursor) {
            const key = JSON.stringify([cursor.afterProgramId, cursor.afterVersion]);
            if (usedCursors.has(key)) throw new Error('Repeated published cursor');
            usedCursors.add(key);
          }
        } while (cursor && !controller.signal.aborted);
        if (!controller.signal.aborted) setState({ phase: 'ready', rows });
      } catch {
        if (!controller.signal.aborted) {
          setState({ phase: 'error', rows: [] });
          setOpened(null);
          setPrevious(null);
        }
      }
    };
    void fetchPublished();
    return () => controller.abort();
  }, [reload]);

  const groups = useMemo(() => {
    const grouped = new Map<string, PublicProgram[]>();
    for (const program of state.rows) {
      const versions = grouped.get(program.programId) ?? [];
      versions.push(program);
      grouped.set(program.programId, versions);
    }
    return [...grouped].map(([id, versions]) => ({
      id, versions: versions.sort((a, b) => b.version - a.version),
    })).sort((a, b) => a.versions[0].title.localeCompare(b.versions[0].title, 'fa') ||
      a.id.localeCompare(b.id));
  }, [state.rows]);

  const normalized = search.trim().toLocaleLowerCase('fa');
  const visible = groups.filter(g =>
    !normalized || g.id.toLocaleLowerCase('fa').includes(normalized) ||
    g.versions.some(v => v.title.toLocaleLowerCase('fa').includes(normalized)));
  const selected = groups.find(g => g.id === opened);
  const prior = selected?.versions.find(v => v.version === previous) ?? null;

  return <section dir="rtl" aria-label="کاوش برنامه‌های ملی منتشرشده"
    style={{ maxWidth: 960, margin: '1rem auto', padding: 16, fontFamily: 'Tahoma,Arial,sans-serif' }}>
    <h2>برنامه‌های ملی قابل مشاهده برای عموم</h2>
    <p>این صفحه فقط نسخه‌هایی را نشان می‌دهد که در منبع رسمی سامانه منتشر شده‌اند.
      انتشار برنامه ملی به معنی برگزاری کلاس یا رویداد در کانون، ظرفیت ثبت‌نام، پذیرش
      مخاطب یا ثبت مشارکت نیست.</p>
    <button type="button" onClick={() => setReload(v => v + 1)}>بازخوانی نسخه‌های منتشرشده</button>
    {state.phase === 'loading' && <p role="status">در حال دریافت نسخه‌های منتشرشده…</p>}
    {state.phase === 'error' && <p role="alert">اطلاعات معتبر برنامه‌های منتشرشده در دسترس نیست.</p>}
    {state.phase === 'ready' && <section aria-label="فهرست برنامه‌های ملی منتشرشده">
      {groups.length === 0 ?
        <p role="status">هنوز برنامه ملی منتشرشده‌ای در منبع سامانه وجود ندارد.</p> :
        <>
          <label htmlFor="audience-program-search">جست‌وجوی نام یا شناسه برنامه منتشرشده</label>{' '}
          <input id="audience-program-search" type="search" value={search}
            onChange={e => setSearch(e.target.value)} />
          <p role="status">{visible.length} برنامه از {groups.length} برنامه منتشرشده</p>
          {visible.length === 0 && <p>برای این جست‌وجو برنامه منتشرشده‌ای یافت نشد.</p>}
          <ul>{visible.map(g => <li key={g.id}>
            <h3>{g.versions[0].title}</h3>
            <p>شناسه برنامه: {g.id}</p>
            <p>آخرین نسخه منتشرشده: {g.versions[0].version}؛ تعداد نسخه‌های منتشرشده: {g.versions.length}</p>
            <button type="button" aria-expanded={opened === g.id}
              onClick={() => { setOpened(v => v === g.id ? null : g.id); setPrevious(null); }}>
              {opened === g.id ? 'بستن جزئیات برنامه' : 'مشاهده نسخه‌ها و الزامات برنامه'}
            </button>
          </li>)}</ul>
        </>}
    </section>}
    {state.phase === 'ready' && selected && visible.some(g => g.id === selected.id) &&
      <section aria-label="جزئیات برنامه ملی انتخاب‌شده">
        <h3>جزئیات رسمی برنامه: {selected.id}</h3>
        <NationalVersion version={selected.versions[0]} suffix="آخرین نسخه منتشرشده"/>
        {selected.versions.length > 1 && <>
          <label htmlFor="audience-program-compare">نسخه منتشرشده پیشین برای مقایسه</label>{' '}
          <select id="audience-program-compare"
            value={previous === null ? '' : String(previous)}
            onChange={e => setPrevious(e.target.value ? Number(e.target.value) : null)}>
            <option value="">انتخاب نسخه قبلی</option>
            {selected.versions.slice(1).map(p =>
              <option key={p.version} value={p.version}>نسخه {p.version}</option>)}
          </select>
          {prior && <NationalVersion version={prior} suffix="مقایسه با نسخه پیشین" />}
          <p>مقایسه فقط میان نسخه‌های منتشرشده همین برنامه است؛ نسخه پیش‌نویس یا اصلاحات
            اجراشده در کانون از این صفحه قابل استنتاج نیست.</p>
        </>}
      </section>}
    <p>خدمات ثبت‌نام، حساب شخصی و پیگیری مخاطب هنوز فعال نشده‌اند؛
      هیچ درخواست ثبت‌نامی از این صفحه ارسال نمی‌شود.</p>
  </section>;
}

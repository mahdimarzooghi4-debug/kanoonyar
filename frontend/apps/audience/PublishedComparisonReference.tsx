import React, { useEffect, useMemo, useState } from 'react';
import { NationalVersion, parsePublishedResponse, type PublicProgram } from './PublicNationalPrograms';
import { PublishedTextChanges } from './PublishedTextChanges';

export type PublishedComparisonReference = {
  programId: string;
  earlierVersion: number;
  currentVersion: number;
} | 'invalid' | null;

type Result = {
  phase: 'loading' | 'ready' | 'unavailable';
  earlier: PublicProgram | null;
  current: PublicProgram | null;
};

/** A URL requests a comparison; only two exact, currently published source reads can establish it. */
export function requestedPublishedComparison(search: string): PublishedComparisonReference {
  const params = new URLSearchParams(search);
  const hasComparisonParameter = ['compareProgramId', 'earlierVersion', 'currentVersion']
    .some(key => params.has(key));
  if (!hasComparisonParameter) return null;
  const ids = params.getAll('compareProgramId');
  const earlierValues = params.getAll('earlierVersion');
  const currentValues = params.getAll('currentVersion');
  if (ids.length !== 1 || earlierValues.length !== 1 || currentValues.length !== 1 ||
      params.size !== 3) return 'invalid';

  const programId = ids[0];
  const earlierRaw = earlierValues[0];
  const currentRaw = currentValues[0];
  const earlierVersion = Number(earlierRaw);
  const currentVersion = Number(currentRaw);
  const validId = programId.length > 0 && programId.trim() === programId &&
    programId.length <= 256 && ![...programId].some(c => /[\u0000-\u001f\u007f]/.test(c));
  const validVersion = (raw: string, value: number) =>
    /^[1-9][0-9]*$/.test(raw) && Number.isSafeInteger(value) &&
    value <= 2147483647 && String(value) === raw;
  if (!validId || !validVersion(earlierRaw, earlierVersion) ||
      !validVersion(currentRaw, currentVersion) || earlierVersion >= currentVersion)
    return 'invalid';
  return { programId, earlierVersion, currentVersion };
}

export function publishedComparisonHref(programId: string, earlierVersion: number,
  currentVersion: number, origin = window.location.origin,
  pathname = window.location.pathname): string {
  const url = new URL(pathname, origin);
  url.search = new URLSearchParams({
    compareProgramId: programId,
    earlierVersion: String(earlierVersion),
    currentVersion: String(currentVersion),
  }).toString();
  url.hash = '';
  return url.pathname + url.search;
}

async function readPublishedVersion(programId: string, version: number,
  signal: AbortSignal): Promise<PublicProgram> {
  const response = await fetch('/api/v1/programs/published/version?' +
    new URLSearchParams({ programId, version: String(version) }), {
      method: 'GET', cache: 'no-store', credentials: 'omit',
      headers: { Accept: 'application/json' }, signal,
    });
  if (!response.ok) throw new Error('Published version unavailable');
  const raw: unknown = await response.json();
  const parsed = parsePublishedResponse({ programs: [raw], nextCursor: null });
  const source = parsed?.rows[0] ?? null;
  if (!source || source.programId !== programId || source.version !== version)
    throw new Error('Published version identity mismatch');
  return source;
}

export function PublishedComparisonReferencePage() {
  const reference = useMemo(
    () => requestedPublishedComparison(window.location.search), []);
  const [result, setResult] = useState<Result>({
    phase: 'loading', earlier: null, current: null,
  });

  useEffect(() => {
    if (!reference || reference === 'invalid') return;
    const controller = new AbortController();
    setResult({ phase: 'loading', earlier: null, current: null });
    void Promise.all([
      readPublishedVersion(reference.programId, reference.earlierVersion, controller.signal),
      readPublishedVersion(reference.programId, reference.currentVersion, controller.signal),
    ]).then(([earlier, current]) => {
      if (!controller.signal.aborted)
        setResult({ phase: 'ready', earlier, current });
    }).catch(() => {
      if (!controller.signal.aborted)
        setResult({ phase: 'unavailable', earlier: null, current: null });
    });
    return () => controller.abort();
  }, [reference]);

  if (!reference) return null;
  return <section dir="rtl" aria-label="مقایسه قابل اشتراک نسخه‌های منتشرشده"
    style={{ maxWidth: 960, margin: '1rem auto', padding: 16 }}>
    <h2>مقایسه دقیق دو نسخه منتشرشده برنامه ملی</h2>
    {reference === 'invalid' &&
      <p role="alert">پیوند مقایسه معتبر نیست؛ هیچ نسخه یا مقایسه‌ای تأیید نشد.</p>}
    {reference !== 'invalid' && result.phase === 'loading' &&
      <p role="status">در حال بررسی جداگانه انتشار رسمی هر دو نسخه…</p>}
    {reference !== 'invalid' && result.phase === 'unavailable' &&
      <p role="alert">انتشار رسمی هر دو نسخه تأیید نشد؛ مقایسه نمایش داده نمی‌شود و نسخه دیگری جایگزین نخواهد شد.</p>}
    {reference !== 'invalid' && result.phase === 'ready' && result.earlier && result.current &&
      <>
        <p role="status">شناسه و شماره هر دو نسخه با منبع رسمی انتشار تطبیق داده شد.</p>
        <p>شناسه برنامه: {reference.programId}</p>
        <NationalVersion version={result.earlier} suffix="نسخه پیشینِ پیوند مقایسه" />
        <NationalVersion version={result.current} suffix="نسخه جاریِ پیوند مقایسه" />
        <PublishedTextChanges label="تغییر متنی اهداف ملی"
          previousVersion={result.earlier.version} currentVersion={result.current.version}
          previous={result.earlier.nationalObjectives} current={result.current.nationalObjectives} />
        <PublishedTextChanges label="تغییر متنی الزامات اجباری"
          previousVersion={result.earlier.version} currentVersion={result.current.version}
          previous={result.earlier.mandatoryRequirements} current={result.current.mandatoryRequirements} />
        <p>این پیوند فقط جفت نسخه مشخص‌شده را درخواست می‌کند. اگر هر نسخه از منبع عمومی در دسترس نباشد،
          هیچ نسخه جایگزین یا نتیجه‌ای از روی URL نمایش داده نمی‌شود.</p>
      </>}
    <p><a href="./">بازگشت به فهرست عمومی برنامه‌های ملی</a></p>
  </section>;
}

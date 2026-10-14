import React, { useEffect, useMemo, useState } from 'react';
import { NationalVersion, parsePublishedResponse, type PublicProgram } from './PublicNationalPrograms';

type Reference = { programId: string; version: number } | 'invalid' | null;
type Result = { phase: 'loading' | 'ready' | 'unavailable'; record: PublicProgram | null };

/**
 * The URL is only a request to read a pinned public publication. The server
 * confirms an original publication; URL values never prove it exists.
 */
function requestedReference(search: string): Reference {
  const params = new URLSearchParams(search);
  const ids = params.getAll('programId');
  const versions = params.getAll('version');
  if (ids.length === 0 && versions.length === 0) return null;
  if (ids.length !== 1 || versions.length !== 1 || params.size !== 2)
    return 'invalid';
  const id = ids[0];
  const raw = versions[0];
  const version = Number(raw);
  if (!id || id.trim() !== id || id.length > 256 ||
      [...id].some(c => /[\u0000-\u001f\u007f]/.test(c)) ||
      !/^[1-9][0-9]*$/.test(raw) || !Number.isSafeInteger(version) ||
      version > 2147483647 || String(version) !== raw) return 'invalid';
  return { programId: id, version };
}

export function PublishedVersionReference() {
  const reference = useMemo(() => requestedReference(window.location.search), []);
  const [result, setResult] = useState<Result>({ phase: 'loading', record: null });
  const id = reference && reference !== 'invalid' ? reference.programId : null;
  const version = reference && reference !== 'invalid' ? reference.version : null;

  useEffect(() => {
    if (!id || !version) return;
    const controller = new AbortController();
    setResult({ phase: 'loading', record: null });
    const readSource = async () => {
      try {
        const response = await fetch(
          '/api/v1/programs/published/version?' + new URLSearchParams({
            programId: id, version: String(version),
          }), {
            method: 'GET', cache: 'no-store', credentials: 'omit',
            headers: { Accept: 'application/json' }, signal: controller.signal,
          });
        if (!response.ok) throw new Error('No verified publication at that reference');
        const raw: unknown = await response.json();
        const validated = parsePublishedResponse({ programs: [raw], nextCursor: null });
        const source = validated?.rows[0] ?? null;
        if (!source || source.programId !== id || source.version !== version)
          throw new Error('Untrusted or mismatched published identity');
        if (!controller.signal.aborted) setResult({ phase: 'ready', record: source });
      } catch {
        if (!controller.signal.aborted)
          setResult({ phase: 'unavailable', record: null });
      }
    };
    void readSource();
    return () => controller.abort();
  }, [id, version]);

  if (reference === null) return null;
  return <section aria-label="ارجاع به نسخه منتشرشده" dir="rtl"
    style={{ maxWidth: 960, margin: '1rem auto', padding: 16 }}>
    <h2>ارجاع مستقیم به یک نسخه مشخص از برنامه ملی</h2>
    {reference === 'invalid' &&
      <p role="alert">شناسه یا شماره نسخه در پیوند معتبر نیست؛ هیچ نسخه‌ای تأیید نشد.</p>}
    {reference !== 'invalid' && result.phase === 'loading' &&
      <p role="status">در حال بررسی انتشار رسمی همین نسخه…</p>}
    {reference !== 'invalid' && result.phase === 'unavailable' &&
      <p role="alert">انتشار رسمی نسخه درخواستی تأیید نشد؛ نسخه دیگری جایگزین نمی‌شود.</p>}
    {reference !== 'invalid' && result.phase === 'ready' && result.record &&
      <>
        <p role="status">نسخه مشخص‌شده از منبع رسمی انتشار تأیید شد.</p>
        <p>شناسه برنامه: {result.record.programId}</p>
        <NationalVersion version={result.record} suffix="ارجاع ثابت به نسخه منتشرشده" />
      </>}
    <p><a href="./">بازگشت به فهرست عمومی برنامه‌های ملی</a></p>
  </section>;
}

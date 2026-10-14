import React, { useRef, useState } from 'react';

type Phase = 'idle' | 'loading' | 'verified' | 'denied' | 'error';
type Identity = { phase: Phase; subjectId: string; message: string };
const EMPTY: Identity = { phase: 'idle', subjectId: '', message: '' };

/**
 * S54: a deliberately narrow proof of the caller's own signed OIDC subject.
 * No audience profile, consent, center membership, enrolment or guardian rights
 * are inferred from a successful response. No browser persistence or token sharing.
 */
export function AudienceIdentityBoundary() {
  const [token, setToken] = useState('');
  const [identity, setIdentity] = useState<Identity>(EMPTY);
  const requestEpoch = useRef(0);
  const aborter = useRef<AbortController | null>(null);

  function resetRequest() {
    requestEpoch.current += 1;
    aborter.current?.abort();
    aborter.current = null;
    setIdentity(EMPTY);
  }

  function changeCredential(next: string) {
    resetRequest();
    setToken(next);
  }

  function forgetCredential() {
    resetRequest();
    setToken('');
  }

  async function verifyIdentity() {
    resetRequest();
    const bearer = token.trim();
    if (!bearer || bearer.length > 8192) {
      setIdentity({ phase: 'error', subjectId: '', message: 'توکن احراز هویت آزمایشی معتبر وارد نشده است.' });
      return;
    }
    const current = ++requestEpoch.current;
    const controller = new AbortController();
    aborter.current = controller;
    setIdentity({ phase: 'loading', subjectId: '', message: '' });
    try {
      const response = await fetch('/api/v1/identity/me', {
        method: 'GET',
        headers: { Authorization: `Bearer ${bearer}`, Accept: 'application/json' },
        credentials: 'omit',
        cache: 'no-store',
        referrerPolicy: 'no-referrer',
        signal: controller.signal,
      });
      if (current !== requestEpoch.current || controller.signal.aborted) return;
      if (!response.ok) {
        const message = response.status === 401
          ? 'اعتبار امضا، صادرکننده، مخاطب یا زمان اعتبار توکن تأیید نشد.'
          : response.status === 403
            ? 'شناسه احرازشده معتبر در پاسخ هویت موجود نیست.'
            : 'سرویس هویت در دسترس نیست؛ هویت تأیید نشده است.';
        setIdentity({ phase: response.status === 401 || response.status === 403 ? 'denied' : 'error',
          subjectId: '', message });
        return;
      }
      const data: unknown = await response.json();
      if (current !== requestEpoch.current || controller.signal.aborted) return;
      if (!data || typeof data !== 'object' || !('subjectId' in data) ||
        typeof data.subjectId !== 'string' || !data.subjectId.trim() ||
        data.subjectId.length > 256) {
        throw new Error('Untrusted self subject response');
      }
      setIdentity({ phase: 'verified', subjectId: data.subjectId, message: '' });
    } catch {
      if (current === requestEpoch.current && !controller.signal.aborted) {
        setIdentity({ phase: 'error', subjectId: '', message: 'پاسخ معتبر هویت دریافت نشد.' });
      }
    } finally {
      if (current === requestEpoch.current) aborter.current = null;
    }
  }

  return <section aria-label="بررسی هویت خود مخاطب" style={{ margin: '1rem auto', padding: 16 }}>
    <h2>بررسی هویت خودم</h2>
    <p>این بخش صرفاً کنترل آزمایشی توکن OIDC روی سرور است و جایگزین فرایند
      ورود کاربر در نسخه نهایی نیست. حتی هویت تأییدشده به‌تنهایی حساب مخاطب،
      رابطه با کانون، رضایت، نمایندگی قانونی یا حق ثبت‌نام ایجاد نمی‌کند.</p>
    <label htmlFor="audience-access-credential">توکن دسترسی آزمایشی (فقط در حافظه همین صفحه)</label>{' '}
    <input id="audience-access-credential" aria-label="توکن هویت آزمایشی"
      type="password" autoComplete="off" spellCheck={false}
      value={token} onChange={e => changeCredential(e.target.value)} />
    {' '}
    <button type="button" disabled={identity.phase === 'loading'}
      onClick={() => void verifyIdentity()}>بررسی هویت احرازشده</button>
    {' '}
    <button type="button" onClick={forgetCredential}>پاک‌کردن اطلاعات ورود</button>
    {identity.phase === 'loading' && <p role="status">در حال بررسی اعتبار توکن نزد سرور…</p>}
    {(identity.phase === 'error' || identity.phase === 'denied') &&
      <p role="alert">{identity.message}</p>}
    {identity.phase === 'verified' && <section aria-label="هویت بررسی‌شده خودم">
      <p role="status">هویت توکن توسط سرور تأیید شد؛ هیچ مجوز عملیاتی اعطا نشده است.</p>
      <p>شناسه احرازشده صادرکننده: <output>{identity.subjectId}</output></p>
      <p>پرونده مخاطب، ثبت‌نام‌ها، نمایندگی قانونی و داده‌های خصوصی در این مرحله
        قابل دریافت نیستند. هیچ سابقه یا عضویتی بر اساس شناسه استنتاج نمی‌شود.</p>
    </section>}
  </section>;
}

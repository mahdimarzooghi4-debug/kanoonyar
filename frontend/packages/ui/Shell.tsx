import type { ReactNode } from 'react';
export function Shell({ title, children }: { title: string; children?: ReactNode }) {
  return <main dir="rtl" lang="fa" style={{fontFamily:'Tahoma,Arial,sans-serif',maxWidth:960,margin:'auto',padding:24}}><header><strong>کانون‌یار</strong></header><section aria-label={title}><h1>{title}</h1><p>محیط آزمایشی Foundation — بدون داده عملیاتی</p>{children}</section></main>;
}

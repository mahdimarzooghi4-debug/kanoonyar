import React from 'react';
import { createRoot } from 'react-dom/client';
import './style.css';
const experiences = {
  headquarters: 'پنل ستاد',
  center: 'پنل کانون',
  audience: 'پرتال مخاطبان',
} as const;
type Experience = keyof typeof experiences;
const selected = new URLSearchParams(location.search).get('experience');
const current: Experience = selected === 'headquarters' || selected === 'center' || selected === 'audience' ? selected : 'center';
function App() {
  return <main><header><strong>کانون‌یار</strong><span>بنیاد رابط کاربری — بدون داده عملیاتی</span></header><section><h1>{experiences[current]}</h1><p>این محیط در حال توسعه است و هنوز به سرویس عملیاتی متصل نیست.</p><nav aria-label="انتخاب محیط">{Object.entries(experiences).map(([key, label]) => <a key={key} href={`?experience=${key}`}>{label}</a>)}</nav></section></main>;
}
createRoot(document.getElementById('root')!).render(<React.StrictMode><App /></React.StrictMode>);

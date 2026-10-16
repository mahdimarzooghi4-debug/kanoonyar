import React from 'react';

export type PublishedTextChangeStatus = 'unchanged' | 'added' | 'removed';
export type PublishedTextChange = { text: string; status: PublishedTextChangeStatus };

/**
 * Compare source strings exactly, preserving duplicates and source order.
 * This reports literal additions/removals only; it does not infer meaning.
 */
export function comparePublishedText(previous: string[], current: string[]): PublishedTextChange[] {
  const currentCounts = new Map<string, number>();
  for (const text of current)
    currentCounts.set(text, (currentCounts.get(text) ?? 0) + 1);

  const earlier = previous.map(text => {
    const count = currentCounts.get(text) ?? 0;
    if (count > 0) {
      currentCounts.set(text, count - 1);
      return { text, status: 'unchanged' as const };
    }
    return { text, status: 'removed' as const };
  });

  const previousCounts = new Map<string, number>();
  for (const text of previous)
    previousCounts.set(text, (previousCounts.get(text) ?? 0) + 1);

  const additions: PublishedTextChange[] = [];
  for (const text of current) {
    const count = previousCounts.get(text) ?? 0;
    if (count > 0) previousCounts.set(text, count - 1);
    else additions.push({ text, status: 'added' });
  }
  return [...earlier, ...additions];
}

const statusLabel: Record<PublishedTextChangeStatus, string> = {
  unchanged: 'عبارت بدون تغییر',
  added: 'عبارت افزوده‌شده در نسخه جاری',
  removed: 'عبارت موجود در نسخه پیشین و غایب از نسخه جاری',
};

export function PublishedTextChanges({ label, previousVersion, currentVersion,
  previous, current }: {
  label: string;
  previousVersion: number;
  currentVersion: number;
  previous: string[];
  current: string[];
}) {
  const changes = comparePublishedText(previous, current);
  return <section aria-label={label}>
    <h5>{label}: مقایسه متنی نسخه {previousVersion} با نسخه {currentVersion}</h5>
    {changes.length === 0 ? <p>در این بخش عبارتی برای مقایسه ثبت نشده است.</p> :
      <ul>{changes.map((change, index) => <li key={index}>
        <span>{statusLabel[change.status]}: </span>
        <span>{change.text}</span>
      </li>)}</ul>}
    <p>این مقایسه فقط برابری دقیق عبارت‌های منتشرشده را گزارش می‌کند؛
      معنای حقوقی، اولویت اجرا یا اثر عملی را تعیین نمی‌کند.</p>
  </section>;
}

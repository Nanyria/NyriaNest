// Lend, due and return dates are calendar days. The API sends them as midnight
// UTC without a "Z", so work with the "yyyy-MM-dd" part and never shift them into
// local time, or a lend date could show as the day before.

/** Today in the user's own time zone, as "yyyy-MM-dd" (what <input type="date"> uses) */
export function todayIso(): string {
  const d = new Date();
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

/** "2026-11-21T00:00:00" → "2026-11-21" */
export function dateOnly(apiDate: string): string {
  return apiDate.slice(0, 10);
}

/** A calendar day for display: "21 Nov 2026" */
export function formatDay(apiDate: string | null | undefined): string {
  if (!apiDate) return '';
  return new Date(`${dateOnly(apiDate)}T00:00:00Z`).toLocaleDateString('en-GB', {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
    timeZone: 'UTC',
  });
}

/** A moment (a history row's timestamp), shown as the day it was in local time */
export function formatMoment(apiDate: string | Date | null | undefined): string {
  if (!apiDate) return '';
  // StatusHistoryItem types it as Date, but JSON gives a string
  const s = String(apiDate);
  const moment = apiDate instanceof Date ? apiDate : new Date(/[zZ]|[+-]\d\d:\d\d$/.test(s) ? s : `${s}Z`);
  return moment.toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' });
}

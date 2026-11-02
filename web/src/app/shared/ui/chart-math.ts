/** The smallest "round" number (1, 2, 5 x 10^n) that is at least `value`, so the top of an axis reads cleanly. */
export function niceMax(value: number): number {
  if (!(value > 0)) return 1;
  const magnitude = 10 ** Math.floor(Math.log10(value));
  for (const step of [1, 2, 5, 10]) {
    if (step * magnitude >= value) return step * magnitude;
  }
  return 10 * magnitude;
}

/** 95 -> "1 h 35 min", 45 -> "45 min", 0.4 -> "< 1 min", null -> "-". */
export function formatDuration(minutes: number | null | undefined): string {
  if (minutes == null) return '-';
  if (minutes < 1) return '< 1 min';
  const total = Math.round(minutes);
  const h = Math.floor(total / 60);
  const m = total % 60;
  return h === 0 ? `${m} min` : m === 0 ? `${h} h` : `${h} h ${m} min`;
}

/** yyyy-mm-dd of a Date in the viewer's own time zone. */
export function toDateInput(date: Date): string {
  const p = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${p(date.getMonth() + 1)}-${p(date.getDate())}`;
}

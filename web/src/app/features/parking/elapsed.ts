/** 3725 -> "01:02:05"; a stay of a day or more -> "1d 02:03:04". Never negative. */
export function formatElapsed(totalSeconds: number): string {
  const s = Math.max(0, Math.floor(totalSeconds));
  const days = Math.floor(s / 86_400);
  const hms = [Math.floor((s % 86_400) / 3_600), Math.floor((s % 3_600) / 60), s % 60]
    .map((n) => String(n).padStart(2, '0'))
    .join(':');
  return days > 0 ? `${days}d ${hms}` : hms;
}

/** What a screen reader says instead of a ticking clock: "2 hours 5 minutes". Seconds are left out on purpose. */
export function spokenDuration(totalSeconds: number): string {
  const s = Math.max(0, Math.floor(totalSeconds));
  const days = Math.floor(s / 86_400);
  const hours = Math.floor((s % 86_400) / 3_600);
  const minutes = Math.floor((s % 3_600) / 60);

  const parts = [
    days && `${days} ${days === 1 ? 'day' : 'days'}`,
    hours && `${hours} ${hours === 1 ? 'hour' : 'hours'}`,
    minutes && `${minutes} ${minutes === 1 ? 'minute' : 'minutes'}`,
  ].filter(Boolean);
  return parts.length ? parts.join(' ') : 'less than a minute';
}

/** Seconds of the free period still left (0 once it is over). */
export function freeSecondsLeft(elapsedSeconds: number, freeMinutes: number | null): number {
  return freeMinutes === null ? 0 : Math.max(0, freeMinutes * 60 - elapsedSeconds);
}

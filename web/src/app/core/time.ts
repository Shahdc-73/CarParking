/**
 * The API speaks UTC ("2026-06-01T10:00:00Z"); people think in local time. These helpers are the only place that converts.
 * <input type="datetime-local"> gives "2026-06-01T13:00" with NO zone, meaning "13:00 on this computer's clock".
 */

/** "2026-06-01T13:00" (local wall clock) -> "2026-06-01T10:00:00.000Z" (UTC). */
export function localInputToUtcIso(local: string): string {
  const date = new Date(local);
  if (Number.isNaN(date.getTime())) throw new Error(`Not a date/time: ${local}`);
  return date.toISOString();
}

/** A Date -> the "YYYY-MM-DDTHH:mm" string a datetime-local input expects, in LOCAL time. */
export function toLocalInput(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

/** Rounds up to the next multiple of `minutes` (e.g. 10:07 -> 10:15), so a default start time looks sensible. */
export function roundUpToMinutes(date: Date, minutes: number): Date {
  const ms = minutes * 60_000;
  return new Date(Math.ceil(date.getTime() / ms) * ms);
}

/**
 * .NET serializes a TimeSpan as "hh:mm:ss", "d.hh:mm:ss" or with fractional seconds ("01:45:00.1234567").
 * Returns whole seconds, or 0 for anything unrecognised.
 */
export function parseTimeSpan(value: string): number {
  const match = /^(?:(\d+)\.)?(\d{1,2}):(\d{2}):(\d{2})(?:\.\d+)?$/.exec(value.trim());
  if (!match) return 0;
  const [, days, h, m, s] = match;
  return Number(days ?? 0) * 86_400 + Number(h) * 3_600 + Number(m) * 60 + Number(s);
}

/**
 * A fresh unique key per payment ATTEMPT. The same key is re-sent when that attempt is retried, so a double click or a
 * network retry can never charge twice. crypto.randomUUID needs a secure context (https or localhost), hence the fallback.
 */
export function newIdempotencyKey(): string {
  const c = globalThis.crypto;
  if (c?.randomUUID) return c.randomUUID();
  return `k-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 12)}`;
}

/** A date filter ("2026-06-01", from <input type="date">) -> the instant that local day STARTS, as UTC. */
export function localDateStartToUtcIso(date: string): string {
  const [y, m, d] = date.split('-').map(Number);
  return new Date(y, m - 1, d).toISOString();
}

/** The instant the NEXT local day starts: use it as an exclusive upper bound so the whole chosen day is included. */
export function localDateEndToUtcIso(date: string): string {
  const [y, m, d] = date.split('-').map(Number);
  return new Date(y, m - 1, d + 1).toISOString();
}

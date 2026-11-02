import {
  localDateEndToUtcIso,
  localDateStartToUtcIso,
  localInputToUtcIso,
  newIdempotencyKey,
  parseTimeSpan,
  roundUpToMinutes,
  toLocalInput,
} from './time';

describe('local <-> UTC conversion', () => {
  it('interprets a datetime-local value as LOCAL wall-clock time and returns the UTC instant', () => {
    // Built from components, so the expectation is correct whatever time zone this test machine uses.
    const expected = new Date(2026, 5, 1, 13, 30).toISOString();
    expect(localInputToUtcIso('2026-06-01T13:30')).toBe(expected);
  });

  it('round-trips: local input -> UTC -> back to the same local input', () => {
    for (const value of [
      '2026-06-01T00:00',
      '2026-12-31T23:45',
      '2026-03-29T01:30',
      '2026-10-25T02:30',
    ]) {
      expect(toLocalInput(new Date(localInputToUtcIso(value)))).toBe(value);
    }
  });

  it('formats a Date as the string a datetime-local input expects', () => {
    expect(toLocalInput(new Date(2026, 0, 5, 7, 4))).toBe('2026-01-05T07:04');
  });

  it('rejects text that is not a date instead of sending garbage to the API', () => {
    expect(() => localInputToUtcIso('')).toThrow();
    expect(() => localInputToUtcIso('tomorrow')).toThrow();
  });
});

describe('roundUpToMinutes', () => {
  const at = (h: number, m: number, s = 0) => new Date(2026, 5, 1, h, m, s);

  it.each([
    [at(10, 7), 15, at(10, 15)],
    [at(10, 15), 15, at(10, 15)], // already on the boundary
    [at(10, 15, 1), 15, at(10, 30)],
    [at(23, 55), 15, new Date(2026, 5, 2, 0, 0)],
  ])('%s -> next %i-minute mark', (input, step, expected) => {
    expect(roundUpToMinutes(input, step).getTime()).toBe(expected.getTime());
  });
});

describe('parseTimeSpan (.NET TimeSpan strings)', () => {
  it.each([
    ['00:00:00', 0],
    ['00:00:09', 9],
    ['01:45:00', 6300],
    ['23:59:59', 86_399],
    ['1.03:00:11', 97_211],
    ['3.00:00:00', 259_200],
    ['01:45:00.1234567', 6300], // fractional seconds are dropped
    ['  02:00:00  ', 7200],
  ])('%j -> %i seconds', (text, seconds) => expect(parseTimeSpan(text)).toBe(seconds));

  it('returns 0 for anything unrecognised instead of NaN', () => {
    for (const bad of ['', 'abc', '12', '1:2:3', '-01:00:00']) expect(parseTimeSpan(bad)).toBe(0);
  });
});

describe('newIdempotencyKey', () => {
  it('is unique per call and never empty', () => {
    const keys = new Set(Array.from({ length: 200 }, () => newIdempotencyKey()));
    expect(keys.size).toBe(200);
    for (const k of keys) expect(k.length).toBeGreaterThanOrEqual(10);
  });

  it('still works where crypto.randomUUID is unavailable (plain http on a LAN address)', () => {
    const original = globalThis.crypto;
    Object.defineProperty(globalThis, 'crypto', { value: undefined, configurable: true });
    try {
      expect(newIdempotencyKey()).toMatch(/^k-/);
    } finally {
      Object.defineProperty(globalThis, 'crypto', { value: original, configurable: true });
    }
  });
});

describe('date-range filters (local days -> UTC instants)', () => {
  it('a chosen day starts at local midnight', () => {
    expect(localDateStartToUtcIso('2026-06-01')).toBe(new Date(2026, 5, 1).toISOString());
  });

  it('the end of a chosen day is the start of the next one, so the whole day is included (exclusive upper bound)', () => {
    expect(localDateEndToUtcIso('2026-06-01')).toBe(new Date(2026, 5, 2).toISOString());
    expect(localDateEndToUtcIso('2026-06-30')).toBe(new Date(2026, 6, 1).toISOString()); // month rollover
    expect(localDateEndToUtcIso('2026-12-31')).toBe(new Date(2027, 0, 1).toISOString()); // year rollover
  });

  it('a one-day filter spans exactly one local day', () => {
    const from = Date.parse(localDateStartToUtcIso('2026-06-01'));
    const to = Date.parse(localDateEndToUtcIso('2026-06-01'));
    expect(to - from).toBeGreaterThanOrEqual(23 * 3_600_000); // 23h on a daylight-saving day, otherwise 24h
    expect(to - from).toBeLessThanOrEqual(25 * 3_600_000);
  });
});

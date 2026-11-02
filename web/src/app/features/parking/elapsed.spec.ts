import { formatElapsed, freeSecondsLeft, spokenDuration } from './elapsed';

describe('formatElapsed', () => {
  it.each([
    [0, '00:00:00'],
    [9, '00:00:09'],
    [59, '00:00:59'],
    [60, '00:01:00'],
    [3599, '00:59:59'],
    [3600, '01:00:00'],
    [3725, '01:02:05'],
    [86_399, '23:59:59'],
    [86_400, '1d 00:00:00'],
    [97_211, '1d 03:00:11'],
    [3 * 86_400 + 61, '3d 00:01:01'],
  ])('%i seconds -> %s', (seconds, expected) => expect(formatElapsed(seconds)).toBe(expected));

  it('never shows a negative or fractional time (e.g. a car "from the future" when clocks disagree)', () => {
    expect(formatElapsed(-5)).toBe('00:00:00');
    expect(formatElapsed(61.9)).toBe('00:01:01');
  });
});

describe('spokenDuration', () => {
  it.each([
    [0, 'less than a minute'],
    [59, 'less than a minute'],
    [60, '1 minute'],
    [3600, '1 hour'],
    [3725, '1 hour 2 minutes'],
    [7200, '2 hours'],
    [90_000, '1 day 1 hour'],
    [2 * 86_400 + 5 * 3600 + 120, '2 days 5 hours 2 minutes'],
  ])('%i seconds -> %s', (seconds, expected) => expect(spokenDuration(seconds)).toBe(expected));
});

describe('freeSecondsLeft', () => {
  it('counts down the free period and stops at zero', () => {
    expect(freeSecondsLeft(0, 15)).toBe(900);
    expect(freeSecondsLeft(551, 15)).toBe(349);
    expect(freeSecondsLeft(900, 15)).toBe(0);
    expect(freeSecondsLeft(5000, 15)).toBe(0);
  });

  it('is zero when the free period is unknown', () => {
    expect(freeSecondsLeft(10, null)).toBe(0);
  });
});

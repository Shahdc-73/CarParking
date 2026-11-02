import { formatDuration, niceMax, toDateInput } from './chart-math';

describe('chart math', () => {
  it.each([
    [0, 1],
    [-5, 1],
    [0.7, 1],
    [1, 1],
    [1.1, 2],
    [3, 5],
    [7, 10],
    [42, 50],
    [101, 200],
    [999, 1000],
  ])('niceMax(%s) = %s', (v, expected) => expect(niceMax(v)).toBe(expected));

  it.each([
    [null, '-'],
    [0.4, '< 1 min'],
    [45, '45 min'],
    [60, '1 h'],
    [95, '1 h 35 min'],
    [1500, '25 h'],
  ])('formatDuration(%s) = %s', (v, expected) => expect(formatDuration(v)).toBe(expected));

  it('formats a local date with zero padding', () => {
    expect(toDateInput(new Date(2026, 0, 5))).toBe('2026-01-05');
  });
});

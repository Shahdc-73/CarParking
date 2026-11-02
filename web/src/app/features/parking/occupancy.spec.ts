import { fillStatus, percentFull, spotTypeIcon } from './occupancy';
import { SpotType } from './parking.models';

describe('fillStatus', () => {
  it.each([
    [32, 32, 'plenty', 'Plenty of space'],
    [10, 5, 'plenty', 'Plenty of space'],
    [10, 4, 'busy', 'Filling up'], // exactly 60% full
    [10, 2, 'busy', 'Filling up'], // 80%
    [20, 3, 'almost-full', 'Almost full'], // exactly 85%
    [10, 1, 'almost-full', 'Almost full'],
    [10, 0, 'full', 'Full'],
    [0, 0, 'no-spots', 'No spots yet'],
  ])('%i spots, %i free -> %s', (total, available, level, label) => {
    expect(fillStatus(total, available)).toMatchObject({ level, label });
  });

  it('always pairs a status with an icon and a text label, so colour is never the only signal', () => {
    for (const [total, free] of [
      [10, 10],
      [10, 4],
      [10, 1],
      [10, 0],
      [0, 0],
    ]) {
      const s = fillStatus(total, free);
      expect(s.icon.length).toBeGreaterThan(0);
      expect(s.label.length).toBeGreaterThan(0);
    }
  });

  it('treats a negative free count (bad data) as full instead of crashing', () => {
    expect(fillStatus(10, -2).level).toBe('full');
  });
});

describe('percentFull', () => {
  it('rounds and handles empty lots', () => {
    expect(percentFull(32, 12)).toBe(63);
    expect(percentFull(10, 10)).toBe(0);
    expect(percentFull(10, 0)).toBe(100);
    expect(percentFull(0, 0)).toBe(0);
  });
});

describe('spotTypeIcon', () => {
  it('has a distinct, non-empty icon for every spot type', () => {
    const types: SpotType[] = ['Standard', 'Compact', 'EV', 'Disabled', 'Motorcycle'];
    for (const t of types) expect(spotTypeIcon(t)).toBeTruthy();
    expect(spotTypeIcon('EV')).not.toBe(spotTypeIcon('Standard'));
    expect(spotTypeIcon('Motorcycle')).not.toBe(spotTypeIcon('Standard'));
  });
});

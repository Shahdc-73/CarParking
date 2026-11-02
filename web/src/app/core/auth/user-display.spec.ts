import { initials, primaryRole } from './user-display';

describe('initials', () => {
  it.each([
    ['Dana Driver', 'DD'],
    ['  dana   driver  ', 'DD'],
    ['Madonna', 'M'],
    ['Jean Claude Van Damme', 'JD'], // first and last word
    ['ali', 'A'],
  ])('%j -> %s', (name, expected) => expect(initials(name)).toBe(expected));

  it('falls back to a placeholder for an empty name', () => {
    expect(initials('')).toBe('?');
    expect(initials('   ')).toBe('?');
  });
});

describe('primaryRole', () => {
  it('shows the most powerful role', () => {
    expect(primaryRole(['Driver'])).toBe('Driver');
    expect(primaryRole(['Attendant'])).toBe('Attendant');
    expect(primaryRole(['Driver', 'Admin'])).toBe('Admin');
    expect(primaryRole(['Driver', 'Attendant'])).toBe('Attendant');
    expect(primaryRole([])).toBe('Driver');
  });
});

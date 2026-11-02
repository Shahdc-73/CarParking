import {
  AUDIT_ACTIONS,
  AUDIT_ENTITY_TYPES,
  humanizeAction,
  prettyJson,
  shortId,
} from './audit-views';

describe('humanizeAction', () => {
  it.each([
    ['Spot.TakenOutOfService', 'Spot: taken out of service'],
    ['Lot.Created', 'Lot: created'],
    ['Session.LostTicketCheckOut', 'Session: lost ticket check out'],
    ['Reservation.CancelledNoSpot', 'Reservation: cancelled no spot'],
    ['Pricing.LostTicketFeeChanged', 'Pricing: lost ticket fee changed'],
  ])('%s -> %s', (action, expected) => expect(humanizeAction(action)).toBe(expected));

  it('returns text without a dot unchanged', () =>
    expect(humanizeAction('Something')).toBe('Something'));
});

describe('prettyJson', () => {
  it('formats valid JSON for reading', () => {
    expect(prettyJson('{"a":1,"b":{"c":2}}')).toBe('{\n  "a": 1,\n  "b": {\n    "c": 2\n  }\n}');
  });

  it('shows invalid JSON as it is instead of failing', () => {
    expect(prettyJson('not json')).toBe('not json');
  });

  it('returns an empty string for nothing', () => {
    expect(prettyJson(null)).toBe('');
    expect(prettyJson('')).toBe('');
  });
});

describe('shortId', () => {
  it('shortens ids and names the system when there is no actor', () => {
    expect(shortId('0198a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b')).toBe('0198a1b2');
    expect(shortId(null)).toBe('System');
  });
});

describe('audit filter lists', () => {
  it('contain no duplicates and every action is "Entity.Verb"', () => {
    expect(new Set(AUDIT_ACTIONS).size).toBe(AUDIT_ACTIONS.length);
    expect(new Set(AUDIT_ENTITY_TYPES).size).toBe(AUDIT_ENTITY_TYPES.length);
    for (const a of AUDIT_ACTIONS) expect(a).toMatch(/^[A-Za-z]+\.[A-Za-z]+$/);
  });
});

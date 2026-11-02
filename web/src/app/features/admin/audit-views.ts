/** Every action the API records, so the filter offers real choices instead of free text that would never match. */
export const AUDIT_ENTITY_TYPES = [
  'ParkingLot',
  'Level',
  'Spot',
  'ParkingSession',
  'Reservation',
  'PricingRule',
  'LostTicketFee',
] as const;

export const AUDIT_ACTIONS = [
  'Lot.Created',
  'Lot.Updated',
  'Lot.Activated',
  'Lot.Deactivated',
  'Lot.Deleted',
  'Level.Created',
  'Level.Updated',
  'Level.Deleted',
  'Spot.Created',
  'Spot.Updated',
  'Spot.TakenOutOfService',
  'Spot.ReturnedToService',
  'Spot.Deleted',
  'Session.CheckedIn',
  'Session.CheckedOut',
  'Session.LostTicketCheckOut',
  'Pricing.RuleChanged',
  'Pricing.LostTicketFeeChanged',
  'Reservation.Created',
  'Reservation.Cancelled',
  'Reservation.Expired',
  'Reservation.Reassigned',
  'Reservation.CancelledNoSpot',
] as const;

/** "Spot.TakenOutOfService" -> "Spot: taken out of service". */
export function humanizeAction(action: string): string {
  const [subject, ...rest] = action.split('.');
  const verb = rest
    .join('.')
    .replace(/([a-z])([A-Z])/g, '$1 $2')
    .toLowerCase();
  return verb ? `${subject}: ${verb}` : action;
}

/** Pretty-prints a stored JSON snapshot; anything that is not valid JSON is shown as it is. */
export function prettyJson(text: string | null): string {
  if (!text) return '';
  try {
    return JSON.stringify(JSON.parse(text), null, 2);
  } catch {
    return text;
  }
}

/** Ids are long; the first 8 characters are enough to tell them apart on screen (the full value is in the tooltip). */
export function shortId(id: string | null): string {
  return id ? id.slice(0, 8) : 'System';
}

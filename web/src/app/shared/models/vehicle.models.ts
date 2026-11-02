export type VehicleType = 'Car' | 'SmallCar' | 'ElectricCar' | 'Motorcycle';

export const VEHICLE_TYPES: readonly { value: VehicleType; label: string; icon: string }[] = [
  { value: 'Car', label: 'Car', icon: 'directions_car' },
  { value: 'SmallCar', label: 'Small car', icon: 'directions_car' },
  { value: 'ElectricCar', label: 'Electric car', icon: 'ev_station' },
  { value: 'Motorcycle', label: 'Motorcycle', icon: 'two_wheeler' },
];

export function vehicleTypeLabel(type: VehicleType): string {
  return VEHICLE_TYPES.find((t) => t.value === type)?.label ?? type;
}

export interface Vehicle {
  id: string;
  plateNumber: string;
  type: VehicleType;
  color: string;
  hasDisabledPermit: boolean;
  ownerUserId: string | null;
}

export interface SaveVehicleRequest {
  plateNumber: string;
  type: VehicleType;
  color: string;
  hasDisabledPermit?: boolean;
}

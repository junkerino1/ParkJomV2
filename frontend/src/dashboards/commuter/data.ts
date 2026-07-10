import { ParkingSpot } from './types';

export const stationsList = [
  'Subang Jaya LRT',
  'Kelana Jaya LRT',
  'Wangsa Maju LRT',
  'Taman Connaught MRT'
];

// Real GPS coordinates for parking spots near LRT/MRT stations (Klang Valley, Malaysia)
export const simulatedSpots: ParkingSpot[] = [
  // ---- Subang Jaya LRT (3.0849, 101.5873) ----
  { id: 'SJ-01', station: 'Subang Jaya LRT', name: 'Casa Subang Condominium - Bay 12', pricePerHour: 3.50, distance: 150, lat: 3.0836, lng: 101.5882, available: true, type: 'Condo Bay', owner: 'Lim K. H.' },
  { id: 'SJ-02', station: 'Subang Jaya LRT', name: 'Casa Subang Condominium - Bay 45', pricePerHour: 3.50, distance: 120, lat: 3.0839, lng: 101.5886, available: true, type: 'Condo Bay', owner: 'Lim K. H.' },
  { id: 'SJ-03', station: 'Subang Jaya LRT', name: 'Jalan SS15 Landed Driveway #4', pricePerHour: 4.00, distance: 350, lat: 3.0818, lng: 101.5880, available: true, type: 'Landed Driveway', owner: 'John Tan' },
  { id: 'SJ-04', station: 'Subang Jaya LRT', name: 'Subang Park Homes - Block B-5', pricePerHour: 3.00, distance: 480, lat: 3.0805, lng: 101.5865, available: true, type: 'Condo Bay', owner: 'Ravi S.' },

  // ---- Kelana Jaya LRT (3.1125, 101.6045) ----
  { id: 'KJ-01', station: 'Kelana Jaya LRT', name: 'Kelana Puteri Condo - Bay 211', pricePerHour: 3.00, distance: 210, lat: 3.1138, lng: 101.6029, available: true, type: 'Condo Bay', owner: 'Yong S. M.' },
  { id: 'KJ-02', station: 'Kelana Jaya LRT', name: 'Kelana Puteri Condo - Driveway A', pricePerHour: 4.00, distance: 240, lat: 3.1142, lng: 101.6035, available: true, type: 'Landed Driveway', owner: 'Siti Aminah' },
  { id: 'KJ-03', station: 'Kelana Jaya LRT', name: 'Jalan SS7 Terrace Driveway', pricePerHour: 4.50, distance: 180, lat: 3.1118, lng: 101.6055, available: true, type: 'Landed Driveway', owner: 'Chaw Chun Jia' },

  // ---- Wangsa Maju LRT (3.2058, 101.7319) ----
  { id: 'WM-01', station: 'Wangsa Maju LRT', name: 'PV9 Residences - Parking L6-102', pricePerHour: 3.00, distance: 80, lat: 3.2052, lng: 101.7325, available: true, type: 'Condo Bay', owner: 'Ooi Jun Kang' },
  { id: 'WM-02', station: 'Wangsa Maju LRT', name: 'PV9 Residences - Parking L4-22', pricePerHour: 3.00, distance: 100, lat: 3.2050, lng: 101.7327, available: true, type: 'Condo Bay', owner: 'Ooi Jun Kang' },
  { id: 'WM-03', station: 'Wangsa Maju LRT', name: 'Jalan Wangsa Melawati 3 - Driveway', pricePerHour: 3.50, distance: 420, lat: 3.2040, lng: 101.7278, available: true, type: 'Landed Driveway', owner: 'Chung W. F.' },

  // ---- Taman Connaught MRT (3.0792, 101.7451) ----
  { id: 'TC-01', station: 'Taman Connaught MRT', name: 'Cheras Hartamas - Driveway Lane 2', pricePerHour: 3.50, distance: 280, lat: 3.0782, lng: 101.7472, available: true, type: 'Landed Driveway', owner: 'Michelle S.' },
  { id: 'TC-02', station: 'Taman Connaught MRT', name: 'Altitude 236 Condominium - L1-4', pricePerHour: 3.00, distance: 340, lat: 3.0805, lng: 101.7435, available: true, type: 'Condo Bay', owner: 'Leong S. K.' },
];

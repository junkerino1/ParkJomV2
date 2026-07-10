import React, { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { MapContainer, TileLayer, Marker, Popup, useMap, GeoJSON } from 'react-leaflet';
import L from 'leaflet';
import 'leaflet/dist/leaflet.css';
import stationData from '../data/mrt_lrt_stations.json';
import { ParkingSpot } from '../types';
import { 
  Search, Navigation, MapPin, Car, Train, X, 
  Clock, DollarSign, ChevronRight, Layers, Loader2
} from 'lucide-react';

// ---- GTFS GeoJSON types ----
interface RailLineFeature {
  type: 'Feature';
  properties: { route_id: string; route_name: string; route_color: string };
  geometry: { type: 'LineString'; coordinates: [number, number][] };
}
interface RailStopFeature {
  type: 'Feature';
  properties: { stop_id: string; stop_name: string };
  geometry: { type: 'Point'; coordinates: [number, number] };
}
interface GeoJsonCollection<T> {
  type: 'FeatureCollection';
  features: T[];
}

// ---- Custom Marker Icons ----
const stationIcon = L.divIcon({
  className: 'custom-station-icon',
  html: `<div style="
    background: #2563EB; 
    width: 16px; height: 16px; 
    border-radius: 50%; 
    border: 3px solid white;
    box-shadow: 0 2px 6px rgba(0,0,0,0.3);
    display: flex; align-items: center; justify-content: center;
    font-size: 8px;
  ">🚇</div>`,
  iconSize: [22, 22],
  iconAnchor: [11, 11],
  popupAnchor: [0, -14],
});

const parkingIcon = L.divIcon({
  className: 'custom-parking-icon',
  html: `<div style="
    background: #10B981;
    width: 28px; height: 28px;
    border-radius: 50%;
    border: 3px solid white;
    box-shadow: 0 2px 8px rgba(0,0,0,0.25);
    display: flex; align-items: center; justify-content: center;
    font-size: 10px; font-weight: 900; color: white;
  ">P</div>`,
  iconSize: [34, 34],
  iconAnchor: [17, 17],
  popupAnchor: [0, -18],
});

const selectedStationIcon = L.divIcon({
  className: 'custom-selected-station-icon',
  html: `<div style="
    background: #EF4444;
    width: 20px; height: 20px;
    border-radius: 50%;
    border: 3px solid white;
    box-shadow: 0 0 0 4px rgba(239,68,68,0.3), 0 2px 8px rgba(0,0,0,0.3);
    display: flex; align-items: center; justify-content: center;
    font-size: 9px;
  ">🚇</div>`,
  iconSize: [28, 28],
  iconAnchor: [14, 14],
  popupAnchor: [0, -18],
});

// ---- Props ----
interface CommuterMapProps {
  spots: ParkingSpot[];
  onStationSelect: (stationName: string, lat: number, lng: number) => void;
  selectedStation: string | null;
  onSpotClick: (spot: ParkingSpot) => void;
}

// ---- FlyTo component — flies to station when clicked, no snap-back on scroll ----
function FlyToStation({ lat, lng }: { lat: number; lng: number }) {
  const map = useMap();
  useEffect(() => {
    map.flyTo([lat, lng], 15, { duration: 0.6 });
  }, [lat, lng, map]);
  return null;
}

// ---- Creates a custom pane above markers so rail lines/stops render on top ----
function RailPane() {
  const map = useMap();
  useEffect(() => {
    if (!map.getPane('railPane')) {
      map.createPane('railPane');
      const pane = map.getPane('railPane');
      if (pane) pane.style.zIndex = '625'; // above markerPane (600), below popupPane (700)
    }
  }, [map]);
  return null;
}

// ---- Route name mapping: route_id code → display name ----
const ROUTE_NAME_MAP: Record<string, string> = {
  AGL: 'Ampang Line',
  KJL: 'Kelana Jaya Line',
  SPL: 'Sri Petaling Line',
  KGL: 'Kajang Line',
  PYL: 'Putrajaya Line',
  MRL: 'KL Monorail',
  BRT: 'BRT Sunway Line',
  SAL: 'Shah Alam Line',
};

// ---- Mock parking spot type ----
interface MockParkingSpot {
  id: string;
  lat: number;
  lon: number;
  address: string;
  photoUrl: string;
  price: number; // RM per hour
}

// ---- Mock parking spot data (coordinates within 500m of LRT/MRT stations) ----
const MOCK_PARKING_SPOTS: MockParkingSpot[] = [
  // ===== Subang Jaya LRT area (≈3.083, 101.588) =====
  {
    id: 'mock-1',
    lat: 3.0839, lon: 101.5886,
    address: 'Jalan SS15/4D, Subang Jaya — Landed Driveway #4',
    photoUrl: 'https://images.unsplash.com/photo-1590674899484-d5640d9da574?w=400&h=250&fit=crop',
    price: 4.00,
  },
  {
    id: 'mock-2',
    lat: 3.0818, lon: 101.5855,
    address: 'Casa Subang Condominium — Bay 12, Jalan Kemajuan',
    photoUrl: 'https://images.unsplash.com/photo-1472224371017-08207f84aaae?w=400&h=250&fit=crop',
    price: 3.50,
  },
  {
    id: 'mock-2b',
    lat: 3.0850, lon: 101.5900,
    address: 'SS15 Courtyard — Gated Parking Slot B3, Subang Jaya',
    photoUrl: 'https://images.unsplash.com/photo-1600585154340-be6161a56a0c?w=400&h=250&fit=crop',
    price: 3.80,
  },
  // ===== Kelana Jaya LRT area (≈3.113, 101.603) =====
  {
    id: 'mock-3',
    lat: 3.1156, lon: 101.6044,
    address: 'Kelana Puteri Condo — Bay 211, Jalan SS7/26',
    photoUrl: 'https://images.unsplash.com/photo-1570129477492-45c003edd2be?w=400&h=250&fit=crop',
    price: 3.00,
  },
  {
    id: 'mock-4',
    lat: 3.1118, lon: 101.6011,
    address: 'Jalan SS7/19 Terrace — Driveway, Kelana Jaya',
    photoUrl: 'https://images.unsplash.com/photo-1564013799919-ab600027ffc6?w=400&h=250&fit=crop',
    price: 4.50,
  },
  {
    id: 'mock-4b',
    lat: 3.1145, lon: 101.5995,
    address: 'Parklane Commercial Hub — Basement L2, Kelana Jaya',
    photoUrl: 'https://images.unsplash.com/photo-1600596542815-ffad4c1539a9?w=400&h=250&fit=crop',
    price: 2.50,
  },
  // ===== Wangsa Maju LRT area (≈3.205, 101.732) =====
  {
    id: 'mock-5',
    lat: 3.2045, lon: 101.7300,
    address: 'PV9 Residences — Parking L6-102, Wangsa Maju',
    photoUrl: 'https://images.unsplash.com/photo-1582268611958-ebfd161ef9cf?w=400&h=250&fit=crop',
    price: 3.00,
  },
  {
    id: 'mock-6',
    lat: 3.2068, lon: 101.7335,
    address: 'Jalan Wangsa Melawati 3 — Driveway, Wangsa Maju',
    photoUrl: 'https://images.unsplash.com/photo-1600566753086-00f18f6b0050?w=400&h=250&fit=crop',
    price: 3.50,
  },
  {
    id: 'mock-6b',
    lat: 3.2030, lon: 101.7295,
    address: 'Seksyen 2 Wangsa Maju — Terrace House Car Porch',
    photoUrl: 'https://images.unsplash.com/photo-1590674899484-d5640d9da574?w=400&h=250&fit=crop',
    price: 3.20,
  },
  // ===== Taman Connaught MRT area (≈3.078, 101.747) =====
  {
    id: 'mock-7',
    lat: 3.0795, lon: 101.7450,
    address: 'Altitude 236 Condominium — L1-4, Taman Connaught',
    photoUrl: 'https://images.unsplash.com/photo-1600596542815-ffad4c1539a9?w=400&h=250&fit=crop',
    price: 3.00,
  },
  {
    id: 'mock-8',
    lat: 3.0770, lon: 101.7485,
    address: 'Cheras Hartamas — Driveway Lane 2, Taman Connaught',
    photoUrl: 'https://images.unsplash.com/photo-1600585154340-be6161a56a0c?w=400&h=250&fit=crop',
    price: 3.50,
  },
  {
    id: 'mock-8b',
    lat: 3.0802, lon: 101.7495,
    address: 'Jalan Cerdas 3 — Semi-D Corner Lot, Taman Connaught',
    photoUrl: 'https://images.unsplash.com/photo-1472224371017-08207f84aaae?w=400&h=250&fit=crop',
    price: 4.00,
  },
  // ===== Masjid Jamek LRT area (≈3.149, 101.696) =====
  {
    id: 'mock-9',
    lat: 3.1480, lon: 101.6940,
    address: 'Jalan Tun Perak — Private Parking Lot, Masjid Jamek',
    photoUrl: 'https://images.unsplash.com/photo-1570129477492-45c003edd2be?w=400&h=250&fit=crop',
    price: 5.00,
  },
  {
    id: 'mock-10',
    lat: 3.1505, lon: 101.6980,
    address: 'Lebuh Ampang — Heritage Shophouse Bay, KL City',
    photoUrl: 'https://images.unsplash.com/photo-1564013799919-ab600027ffc6?w=400&h=250&fit=crop',
    price: 6.00,
  },
  // ===== KL Sentral / Muzium Negara MRT area (≈3.134, 101.687) =====
  {
    id: 'mock-11',
    lat: 3.1360, lon: 101.6895,
    address: 'Brickfields — Jalan Tun Sambanthan Apartment Bay',
    photoUrl: 'https://images.unsplash.com/photo-1582268611958-ebfd161ef9cf?w=400&h=250&fit=crop',
    price: 4.50,
  },
  {
    id: 'mock-12',
    lat: 3.1320, lon: 101.6850,
    address: 'KL Sentral Premier — Office Tower P2, Brickfields',
    photoUrl: 'https://images.unsplash.com/photo-1600566753086-00f18f6b0050?w=400&h=250&fit=crop',
    price: 8.00,
  },
  // ===== Maluri LRT/MRT area (≈3.123, 101.727) =====
  {
    id: 'mock-13',
    lat: 3.1245, lon: 101.7290,
    address: 'Jalan Cheras — AEON Maluri Nearby Private Lot',
    photoUrl: 'https://images.unsplash.com/photo-1590674899484-d5640d9da574?w=400&h=250&fit=crop',
    price: 3.00,
  },
  {
    id: 'mock-14',
    lat: 3.1210, lon: 101.7240,
    address: 'Taman Maluri — Corner Lot Driveway, Cheras',
    photoUrl: 'https://images.unsplash.com/photo-1600596542815-ffad4c1539a9?w=400&h=250&fit=crop',
    price: 2.80,
  },
  // ===== Ampang LRT area (≈3.150, 101.760) =====
  {
    id: 'mock-15',
    lat: 3.1515, lon: 101.7620,
    address: 'Jalan Ampang — Condo Visitor Bay, Ampang Point Area',
    photoUrl: 'https://images.unsplash.com/photo-1472224371017-08207f84aaae?w=400&h=250&fit=crop',
    price: 3.50,
  },
  {
    id: 'mock-16',
    lat: 3.1485, lon: 101.7575,
    address: 'Taman U Thant — Bungalow Driveway, Ampang Hilir',
    photoUrl: 'https://images.unsplash.com/photo-1570129477492-45c003edd2be?w=400&h=250&fit=crop',
    price: 5.50,
  },
];

// ---- Haversine formula: straight-line distance between two GPS points (meters) ----
function haversineDistance(lat1: number, lon1: number, lat2: number, lon2: number): number {
  const R = 6371000; // Earth's radius in meters
  const toRad = (deg: number) => (deg * Math.PI) / 180;
  const dLat = toRad(lat2 - lat1);
  const dLon = toRad(lon2 - lon1);
  const a =
    Math.sin(dLat / 2) * Math.sin(dLat / 2) +
    Math.cos(toRad(lat1)) * Math.cos(toRad(lat2)) * Math.sin(dLon / 2) * Math.sin(dLon / 2);
  const c = 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
  return R * c;
}

// ---- LineControlPanel: rail line visibility control panel ----
function LineControlPanel({
  visibleLines,
  onToggle,
  showHeader = true,
}: {
  visibleLines: Record<string, boolean>;
  onToggle: (routeName: string) => void;
  showHeader?: boolean;
}) {
  return (
    <div
      className={`${showHeader ? 'absolute top-[68px] left-4 z-[1000]' : ''} bg-white/95 backdrop-blur rounded-xl border border-slate-200 shadow-lg p-3 text-xs min-w-[180px] w-auto`}
    >
      {showHeader && (
        <div className="flex items-center gap-2 mb-2 pb-2 border-b border-slate-100">
          <Layers className="w-3.5 h-3.5 text-blue-600" />
          <span className="font-bold text-slate-700">Rail Lines</span>
        </div>
      )}
      <div className="space-y-1.5 max-h-[260px] md:max-h-[300px] overflow-y-auto">
        {Object.keys(visibleLines).map((routeName) => {
          const displayName = ROUTE_NAME_MAP[routeName] || routeName;
          const isChecked = visibleLines[routeName];
          return (
            <label
              key={routeName}
              className="flex items-center gap-2 cursor-pointer hover:bg-slate-50 rounded-lg px-2 py-1.5 transition-colors select-none"
            >
              <input
                type="checkbox"
                checked={isChecked}
                onChange={() => onToggle(routeName)}
                className="w-3.5 h-3.5 rounded border-slate-300 text-blue-600 focus:ring-blue-200 focus:ring-2 accent-blue-600"
              />
              <span
                className={`font-medium leading-tight ${
                  isChecked ? 'text-slate-800' : 'text-slate-400'
                }`}
              >
                {displayName}
              </span>
            </label>
          );
        })}
      </div>
    </div>
  );
}

// ---- Legend Overlay ----
function MapLegend() {
  return (
    <div className="absolute bottom-[100px] right-6 z-[1000] bg-white/95 backdrop-blur rounded-xl border border-slate-200 shadow-lg p-3 text-[10px] space-y-2">
      <div className="flex items-center gap-2">
        <div className="w-5 h-1 rounded-full bg-[#3388ff]" />
        <span className="text-slate-600 font-medium">Rail Line</span>
      </div>
      <div className="flex items-center gap-2">
        <div className="w-2.5 h-2.5 rounded-full bg-white border border-slate-700" />
        <span className="text-slate-600 font-medium">Rail Stop</span>
      </div>
      <div className="flex items-center gap-2">
        <div className="w-4 h-4 rounded-full bg-[#2563EB] border-2 border-white shadow flex items-center justify-center text-[7px]">🚇</div>
        <span className="text-slate-600 font-medium">LRT/MRT Station</span>
      </div>
      <div className="flex items-center gap-2">
        <div className="w-4 h-4 rounded-full bg-[#EF4444] border-2 border-white shadow flex items-center justify-center text-[7px]">🚇</div>
        <span className="text-slate-600 font-medium">Selected Station</span>
      </div>
      <div className="flex items-center gap-2">
        <div className="w-4 h-4 rounded-full bg-[#10B981] border-2 border-white shadow flex items-center justify-center text-[7px] text-white font-black">P</div>
        <span className="text-slate-600 font-medium">Available Parking</span>
      </div>
    </div>
  );
}

// ---- Main CommuterMap Component ----
export default function CommuterMap({ spots, onStationSelect, selectedStation, onSpotClick }: CommuterMapProps) {
  const klCenter: [number, number] = [3.1390, 101.6869];
  const [flyToCoords, setFlyToCoords] = useState<[number, number] | null>(null);
  const [showStationList, setShowStationList] = useState(false);
  const [stationFilter, setStationFilter] = useState('');
  const [distanceRadius, setDistanceRadius] = useState(500);

  // ---- Find parking flow state ----
  // Station coordinates clicked by user (for passing to detail page for walking distance calc)
  const [selectedStationCoords, setSelectedStationCoords] = useState<{ lat: number; lon: number } | null>(null);
  // Nearby parking spots filtered by Haversine distance
  const [nearbyParking, setNearbyParking] = useState<MockParkingSpot[]>([]);

  // react-router navigation
  const navigate = useNavigate();

  // ---- GTFS Rail Lines: full source data (read-only) ----
  const [allLines, setAllLines] = useState<GeoJsonCollection<RailLineFeature> | null>(null);

  // ---- Line visibility map: route_name → boolean, all visible by default ----
  const [visibleLines, setVisibleLines] = useState<Record<string, boolean>>({});

  const [railStops, setRailStops] = useState<GeoJsonCollection<RailStopFeature> | null>(null);
  const [gtfsLoading, setGtfsLoading] = useState(true);
  const [gtfsError, setGtfsError] = useState<string | null>(null);

  // ---- Load GTFS data, initialize allLines and visibleLines ----
  useEffect(() => {
    async function loadGtfsData() {
      try {
        const [linesRes, stopsRes] = await Promise.all([
          fetch('/data/kl_rail_lines.json'),
          fetch('/data/kl_rail_stops.json'),
        ]);
        if (!linesRes.ok || !stopsRes.ok) throw new Error('Failed to fetch GTFS data');
        const [linesData, stopsData] = await Promise.all([
          linesRes.json(),
          stopsRes.json(),
        ]);
        setAllLines(linesData);

        // Extract all unique route_names from data, all enabled by default
        const uniqueRoutes = [
          ...new Set<string>(linesData.features.map((f: RailLineFeature) => f.properties.route_name)),
        ];
        const initVisible: Record<string, boolean> = {};
        uniqueRoutes.forEach((route) => {
          initVisible[route] = true;
        });
        setVisibleLines(initVisible);

        setRailStops(stopsData);
        setGtfsLoading(false);
      } catch (err: any) {
        setGtfsError(err.message);
        setGtfsLoading(false);
      }
    }
    loadGtfsData();
  }, []);

  // ---- Compute filteredLines directly during render (no useEffect to avoid GeoJSON stale redraw) ----
  const filteredLines: GeoJsonCollection<RailLineFeature> | null = React.useMemo(() => {
    if (!allLines) return null;
    const filtered = allLines.features.filter(
      (feature) => visibleLines[feature.properties.route_name] === true
    );
    return { type: 'FeatureCollection', features: filtered };
  }, [allLines, visibleLines]);

  // Filter stations: only LRT, MRT, Monorail (exclude KTM Komuter, ERL, etc.)
  const transitStations = (stationData as any).features.filter((f: any) => {
    const name = f.properties?.name || '';
    const network = f.properties?.network || '';
    const station = f.properties?.station || '';
    const railway = f.properties?.railway || '';

    // Keep only LRT, MRT, Monorail stations
    return (
      station === 'light_rail' ||
      station === 'monorail' ||
      network?.toLowerCase().includes('mrt') ||
      network?.toLowerCase().includes('lrt') ||
      name?.includes('MRT') ||
      name?.match(/^(AG|SP|KJ|KG|PY|MR)\d/) // LRT/MRT/Monorail station codes
    );
  });

  // Get unique station names for the list
  const allNames: string[] = transitStations
    .map((f: any) => String(f.properties?.name || ''))
    .filter((n: string) => n.length > 0);
  const stationNames: string[] = [...new Set<string>(allNames)].sort();

  const filteredList = stationNames.filter(n =>
    n.toLowerCase().includes(stationFilter.toLowerCase())
  );

  // Currently selected station name (to pass to detail page)
  const [clickedStationName, setClickedStationName] = useState<string>('');

  const handleStationClick = (feature: any) => {
    const coords = feature.geometry.coordinates;
    const name = feature.properties?.name || feature.properties?.['name:en'] || 'Unknown Station';
    if (!coords || coords.length < 2) {
      console.warn('Invalid station coordinates');
      return;
    }
    setFlyToCoords([coords[1], coords[0]]);
    onStationSelect(name, coords[1], coords[0]);

    // Feature A: Filter mock parking spots within distanceRadius using Haversine
    const stationLat = coords[1];
    const stationLon = coords[0];
    setSelectedStationCoords({ lat: stationLat, lon: stationLon });
    setClickedStationName(name);

    const nearby = MOCK_PARKING_SPOTS.filter((spot) => {
      const dist = haversineDistance(stationLat, stationLon, spot.lat, spot.lon);
      return dist <= distanceRadius;
    });
    setNearbyParking(nearby);
  };

  const handleListStationClick = (name: string) => {
    const feature = transitStations.find((f: any) =>
      f.properties?.name === name || f.properties?.['name:en'] === name
    );
    if (feature) {
      const coords = feature.geometry.coordinates;
      if (!coords || coords.length < 2) return;
      setFlyToCoords([coords[1], coords[0]]);
      onStationSelect(name, coords[1], coords[0]);

      // Also trigger Haversine filtering
      const stationLat = coords[1];
      const stationLon = coords[0];
      setSelectedStationCoords({ lat: stationLat, lon: stationLon });
      setClickedStationName(name);

      const nearby = MOCK_PARKING_SPOTS.filter((spot) => {
        const dist = haversineDistance(stationLat, stationLon, spot.lat, spot.lon);
        return dist <= distanceRadius;
      });
      setNearbyParking(nearby);

      setShowStationList(false);
    }
  };

  const handleFindNearby = () => {
    if (!selectedStation) {
      alert('Please select an LRT/MRT station first (click on a station marker).');
      return;
    }
    if (!selectedStationCoords) {
      alert('Please click on a station marker on the map to set its coordinates.');
      return;
    }
    // Re-trigger parent station selection to ensure filtered spots render
    onStationSelect(selectedStation, selectedStationCoords.lat, selectedStationCoords.lon);
  };

  // ---- Toggle visibility of a line ----
  const handleLineToggle = (routeName: string) => {
    setVisibleLines((prev) => ({
      ...prev,
      [routeName]: !prev[routeName],
    }));
  };

  // ---- Mobile rail lines panel toggle ----
  const [showMobileLines, setShowMobileLines] = useState(false);

  // Don't show parking spots until a station is explicitly selected
  const shouldShowParking = !!selectedStation && selectedStation.length > 0;

  return (
    <div className="relative w-full h-full min-h-[400px] rounded-2xl overflow-hidden border border-slate-200 shadow-lg">
      {/* Map Container */}
      <MapContainer
        center={klCenter}
        zoom={12}
        style={{ height: '100%', width: '100%' }}
        zoomControl={false}
      >
        {/* CartoDB Dataviz Light Tile Layer — clean light style, suitable for data overlay */}
        <TileLayer
          url="https://{s}.basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png"
          attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors &copy; <a href="https://carto.com/">CARTO</a>'
        />

        {/* Custom pane: z-index 625, above markers so rail renders on top of station icons */}
        <RailPane />

        {/* ---- GTFS Rail Lines (GeoJSON LineStrings) ----
             Use dynamic key={JSON.stringify(visibleLines)} to force React-Leaflet
             to destroy and rebuild GeoJSON layer on each toggle change ---- */}
        {filteredLines && (
          <GeoJSON
            key={JSON.stringify(visibleLines)}
            data={filteredLines}
            pane="railPane"
            style={(feature) => ({
              color: feature?.properties?.route_color || '#3388ff',
              weight: 5,
              opacity: 0.85,
            })}
          />
        )}

        {/* ---- GTFS Rail Stops (GeoJSON Points → CircleMarkers) ---- */}
        {railStops && (
          <GeoJSON
            data={railStops}
            pane="railPane"
            pointToLayer={(_feature, latlng) =>
              L.circleMarker(latlng, {
                radius: 4,
                fillColor: '#ffffff',
                fillOpacity: 1,
                color: '#1e293b',
                weight: 1.5,
              })
            }
            onEachFeature={(feature, layer) => {
              const name = feature.properties?.stop_name;
              if (name) layer.bindPopup(`<strong>${name}</strong>`);
            }}
          />
        )}

        {/* Zoom Control (positioned left) */}
        <div className="leaflet-top leaflet-left" style={{ top: '80px' }}>
          <div className="leaflet-control leaflet-bar">
            <a href="#" onClick={(e) => { e.preventDefault(); }} title="Zoom in">+</a>
            <a href="#" onClick={(e) => { e.preventDefault(); }} title="Zoom out">−</a>
          </div>
        </div>

        {/* Render Transit Station Markers */}
        {transitStations.map((feature: any, idx: number) => {
          const coords = feature.geometry.coordinates;
          const name = feature.properties?.name || feature.properties?.['name:en'] || '';
          const network = feature.properties?.network || '';
          const isSelected = selectedStation === name;

          if (!coords || coords.length < 2) return null;

          return (
            <Marker
              key={`station-${idx}`}
              position={[coords[1], coords[0]]}
              icon={isSelected ? selectedStationIcon : stationIcon}
              eventHandlers={{
                click: () => handleStationClick(feature),
              }}
            >
              <Popup>
                <div className="text-xs min-w-[140px]">
                  <strong className="text-slate-800">{name}</strong>
                  <p className="text-slate-500 mt-0.5">{network || 'Transit Station'}</p>
                  <button
                    onClick={(e) => {
                      e.stopPropagation();
                      handleStationClick(feature);
                    }}
                    className="mt-2 w-full bg-blue-600 hover:bg-blue-700 text-white text-[10px] font-bold py-1.5 px-3 rounded-lg transition-colors"
                  >
                    Find Parking Nearby
                  </button>
                </div>
              </Popup>
            </Marker>
          );
        })}

        {/* Render parking spots only after station selection — filtered to selected station */}
        {shouldShowParking && spots.filter(s => {
          const spotBase = s.station.toLowerCase().replace(' lrt','').replace(' mrt','');
          const selBase = (selectedStation || '').toLowerCase().replace(' lrt','').replace(' mrt','');
          return spotBase.includes(selBase) || selBase.includes(spotBase);
        }).map((spot) => (
          <Marker
            key={spot.id}
            position={[spot.lat, spot.lng]}
            icon={parkingIcon}
          >
            <Popup>
              <div className="text-xs min-w-[180px]">
                <strong className="text-slate-800">{spot.name}</strong>
                <div className="flex items-center gap-1 text-slate-500 mt-1">
                  <Clock className="w-3 h-3" />
                  <span>RM {spot.pricePerHour.toFixed(2)}/hr</span>
                </div>
                <div className="flex items-center gap-1 text-slate-500 mt-0.5">
                  <Navigation className="w-3 h-3" />
                  <span>{spot.distance}m from station</span>
                </div>
                <div className="flex items-center gap-1 text-slate-500 mt-0.5">
                  <Car className="w-3 h-3" />
                  <span>{spot.type} &middot; {spot.owner}</span>
                </div>
                <button
                  onClick={(e) => {
                    e.stopPropagation();
                    navigate(`/commuter/parking/${spot.id}`, {
                      state: {
                        spot: { id: spot.id, lat: spot.lat, lon: spot.lng, address: spot.name, photoUrl: 'https://images.unsplash.com/photo-1590674899484-d5640d9da574?w=400&h=250&fit=crop', price: spot.pricePerHour },
                        stationCoords: selectedStationCoords,
                        stationName: spot.station,
                      },
                    });
                  }}
                  className="mt-2 w-full bg-[#2563eb] hover:bg-[#1d4ed8] text-white text-[10px] font-bold py-1.5 px-3 rounded-lg transition-colors"
                >
                  View Details &amp; Book
                </button>
              </div>
            </Popup>
          </Marker>
        ))}

        {/* Fly to selected station */}
        {flyToCoords && <FlyToStation lat={flyToCoords[0]} lng={flyToCoords[1]} />}
      </MapContainer>

      {/* Map Legend */}
      <MapLegend />

      {/* Line visibility control panel — desktop floats top-left, mobile bottom sheet */}
      {/* Desktop: always visible at top-left */}
      <div className="hidden md:block">
        <LineControlPanel
          visibleLines={visibleLines}
          onToggle={handleLineToggle}
        />
      </div>

      {/* Mobile: Rail lines toggle button & dropdown panel (inside the map container) */}
      <div className="md:hidden absolute top-[72px] right-4 z-[1000] flex flex-col items-end">
        <button
          onClick={() => setShowMobileLines(!showMobileLines)}
          className="bg-white/95 backdrop-blur rounded-xl border border-slate-200 shadow-lg px-3 py-2 flex items-center gap-1.5 text-xs font-semibold text-slate-700 hover:bg-slate-50 active:scale-95 transition-all"
        >
          <Layers className="w-4 h-4 text-blue-600" />
          Lines
          <ChevronRight className={`w-3.5 h-3.5 text-slate-400 transition-transform ${showMobileLines ? 'rotate-90' : ''}`} />
        </button>

        {/* Dropdown expanded lines panel */}
        {showMobileLines && (
          <div className="mt-2 animate-slide-up origin-top-right">
            <LineControlPanel
              visibleLines={visibleLines}
              onToggle={handleLineToggle}
              showHeader={false}
            />
          </div>
        )}
      </div>

      {/* ---- No nearby parking message ---- (centered to avoid blocking top-right buttons) */}
      {clickedStationName && nearbyParking.length === 0 && (
        <div className="absolute top-[80px] left-1/2 -translate-x-1/2 z-[1001] animate-slide-up w-[90%] max-w-[300px]">
          <div className="bg-amber-50/95 backdrop-blur border border-amber-200 rounded-xl px-4 py-3 shadow-lg flex items-start gap-2.5">
            <MapPin size={16} className="text-amber-500 shrink-0 mt-0.5" />
            <div className="text-[11px] text-amber-800 leading-relaxed">
              <strong className="block text-xs font-bold">No parking spots nearby</strong>
              <span>No available spaces within <strong>{distanceRadius}m</strong> of this station. Try a larger radius or a different station.</span>
            </div>
          </div>
        </div>
      )}

      {/* GTFS Data Loading Overlay */}
      {gtfsLoading && (
        <div className="absolute inset-0 z-[1001] bg-white/70 backdrop-blur-sm flex items-center justify-center rounded-2xl">
          <div className="flex items-center gap-3 bg-white border border-slate-200 shadow-lg px-5 py-3 rounded-xl">
            <Loader2 size={20} className="animate-spin text-blue-600" />
            <span className="text-sm font-semibold text-slate-700">Loading map data...</span>
          </div>
        </div>
      )}

      {/* GTFS Error Banner */}
      {gtfsError && (
        <div className="absolute top-16 left-4 right-4 z-[1001] bg-red-50 border border-red-200 text-red-700 text-xs px-4 py-2 rounded-lg shadow">
          ⚠️ Rail data unavailable: {gtfsError}
        </div>
      )}

      {/* Top Bar: Station Search & Filter */}
      <div className="absolute top-4 left-4 right-4 z-[1000] flex gap-2">
        {/* Station Selector Button */}
        <button
          onClick={() => setShowStationList(!showStationList)}
          className="bg-white/95 backdrop-blur rounded-xl border border-slate-200 shadow-lg px-4 py-2.5 flex items-center gap-2 text-sm font-semibold text-slate-700 hover:bg-slate-50 transition-colors"
        >
          <Train className="w-4 h-4 text-blue-600" />
          {selectedStation || 'Select Station'}
          <ChevronRight className={`w-3.5 h-3.5 text-slate-400 transition-transform ${showStationList ? 'rotate-90' : ''}`} />
        </button>

        {/* Radius Filter — auto-refilters nearby spots on change */}
        <select
          value={distanceRadius}
          onChange={(e) => {
            const newRadius = Number(e.target.value);
            setDistanceRadius(newRadius);

            // If a station is already selected, auto-refilter with the new radius
            if (selectedStationCoords) {
              const nearby = MOCK_PARKING_SPOTS.filter((spot) => {
                const dist = haversineDistance(
                  selectedStationCoords.lat, selectedStationCoords.lon,
                  spot.lat, spot.lon,
                );
                return dist <= newRadius;
              });
              setNearbyParking(nearby);
            }
          }}
          className="bg-white/95 backdrop-blur rounded-xl border border-slate-200 shadow-lg px-3 py-2.5 text-xs font-semibold text-slate-600 focus:outline-none"
        >
          <option value={300}>Within 300m</option>
          <option value={500}>Within 500m</option>
          <option value={1000}>Within 1km</option>
          <option value={2000}>Within 2km</option>
        </select>

        {/* Find Nearby Button */}
        {selectedStation && (
          <button
            onClick={handleFindNearby}
            className="bg-blue-600 hover:bg-blue-700 text-white rounded-xl shadow-lg px-4 py-2.5 flex items-center gap-1.5 text-xs font-bold transition-colors"
          >
            <Search className="w-3.5 h-3.5" />
            Find Parking
          </button>
        )}
      </div>

      {/* Station List Dropdown */}
      {showStationList && (
        <div className="absolute top-[60px] left-4 z-[1000] w-72 max-h-80 bg-white/98 backdrop-blur rounded-xl border border-slate-200 shadow-xl overflow-hidden">
          <div className="p-3 border-b border-slate-100">
            <div className="relative">
              <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 w-3.5 h-3.5 text-slate-400" />
              <input
                type="text"
                placeholder="Filter stations..."
                value={stationFilter}
                onChange={(e) => setStationFilter(e.target.value)}
                className="w-full pl-8 pr-3 py-2 text-xs border border-slate-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-100 focus:border-blue-600"
              />
            </div>
          </div>
          <div className="overflow-y-auto max-h-64">
            {filteredList.slice(0, 50).map((name) => (
              <button
                key={name}
                onClick={() => handleListStationClick(name)}
                className={`w-full text-left px-4 py-2 text-xs hover:bg-slate-50 transition-colors flex items-center gap-2 ${
                  selectedStation === name ? 'bg-blue-50 text-blue-700 font-semibold' : 'text-slate-700'
                }`}
              >
                <Train className={`w-3.5 h-3.5 ${selectedStation === name ? 'text-blue-600' : 'text-slate-400'}`} />
                {name}
              </button>
            ))}
            {filteredList.length === 0 && (
              <div className="px-4 py-4 text-xs text-slate-400 text-center">
                No stations found
              </div>
            )}
          </div>
        </div>
      )}
    </div>
  );
}

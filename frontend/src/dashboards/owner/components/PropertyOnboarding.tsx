import React, { useState, useRef, useEffect } from 'react';
import { UploadCloud, CheckCircle, FileText, Landmark, ShieldCheck, HelpCircle, FileCheck, Map, Train, Loader2, Search } from 'lucide-react';
import OwnerRegistrationMap from './OwnerRegistrationMap';

interface PropertyOnboardingProps {
  onOnboardProperty: (property: {
    propertyName: string;
    stationName: string;
    bayNumber: string;
    level: string;
    docName: string;
    lat?: number;
    lon?: number;
  }) => void;
}

export default function PropertyOnboarding({ onOnboardProperty }: PropertyOnboardingProps) {
  // Form states
  const [propName, setPropName] = useState('');
  const [stationName, setStationName] = useState('');
  const [bayNumber, setBayNumber] = useState('');
  const [level, setLevel] = useState('');

  // Auto-detect station state
  const [allStops, setAllStops] = useState<{ name: string; lat: number; lon: number }[]>([]);
  const [isSearchingStation, setIsSearchingStation] = useState(false);
  const [searchError, setSearchError] = useState<string | null>(null);

  // Map-detected data (from OwnerRegistrationMap callback)
  const [mapLat, setMapLat] = useState<number | undefined>();
  const [mapLon, setMapLon] = useState<number | undefined>();
  const [showMap, setShowMap] = useState(false);

  // Upload States
  const [isDragOver, setIsDragOver] = useState(false);
  const [uploadedFile, setUploadedFile] = useState<{ name: string; size: string } | null>(null);
  const [isUploading, setIsUploading] = useState(false);
  const [uploadProgress, setUploadProgress] = useState(0);

  const fileInputRef = useRef<HTMLInputElement>(null);

  // ---- Fetch rail stop data on mount for nearest-station lookup ----
  useEffect(() => {
    async function loadStops() {
      try {
        const res = await fetch('/data/kl_rail_stops.json');
        const data = await res.json();
        const stops: { name: string; lat: number; lon: number }[] = data.features.map((f: any) => ({
          name: f.properties.stop_name,
          lat: f.geometry.coordinates[1],
          lon: f.geometry.coordinates[0],
        }));
        setAllStops(stops);
      } catch (err) {
        console.error('Failed to load rail stops:', err);
      }
    }
    loadStops();
  }, []);

  // ---- Haversine (straight-line meters) ----
  const haversine = (lat1: number, lon1: number, lat2: number, lon2: number): number => {
    const R = 6371000;
    const dLat = ((lat2 - lat1) * Math.PI) / 180;
    const dLon = ((lon2 - lon1) * Math.PI) / 180;
    const a =
      Math.sin(dLat / 2) ** 2 +
      Math.cos((lat1 * Math.PI) / 180) *
        Math.cos((lat2 * Math.PI) / 180) *
        Math.sin(dLon / 2) ** 2;
    return R * 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
  };

  // ---- Search property name → Nominatim geocode → find nearest station ----
  const handlePropertySearch = async () => {
    const q = propName.trim();
    if (!q) {
      setSearchError('Please enter a property/condo name first.');
      return;
    }
    setIsSearchingStation(true);
    setSearchError(null);
    setStationName('');

    try {
      // 1. Geocode the property address via Nominatim
      const geoUrl = `https://nominatim.openstreetmap.org/search?format=json&q=${encodeURIComponent(q)}&countrycodes=my&limit=3`;
      const geoRes = await fetch(geoUrl);
      const geoData = await geoRes.json();

      if (!geoData || geoData.length === 0) {
        setSearchError('Address not found in Malaysia. Try a more specific name (e.g. "PV9 Condominium, Setapak").');
        setIsSearchingStation(false);
        return;
      }

      const loc = geoData[0];
      const lat = parseFloat(loc.lat);
      const lon = parseFloat(loc.lon);
      setMapLat(lat);
      setMapLon(lon);

      // 2. Find the nearest LRT/MRT station
      if (allStops.length === 0) {
        setSearchError('Station data not loaded yet. Please wait or try again.');
        setIsSearchingStation(false);
        return;
      }

      let closest = allStops[0];
      let minDist = haversine(lat, lon, closest.lat, closest.lon);

      for (let i = 1; i < allStops.length; i++) {
        const d = haversine(lat, lon, allStops[i].lat, allStops[i].lon);
        if (d < minDist) {
          minDist = d;
          closest = allStops[i];
        }
      }

      setStationName(closest.name);
    } catch (err) {
      setSearchError('Network error. Please check your connection and try again.');
    } finally {
      setIsSearchingStation(false);
    }
  };

  const handleDragEnter = (e: React.DragEvent) => {
    e.preventDefault();
    setIsDragOver(true);
  };

  const handleDragLeave = (e: React.DragEvent) => {
    e.preventDefault();
    setIsDragOver(false);
  };

  const simulateUpload = (name: string, sizeBytes: number) => {
    setIsUploading(true);
    setUploadProgress(0);
    setUploadedFile(null);

    const sizeMB = (sizeBytes / (1024 * 1024)).toFixed(2) + ' MB';

    const interval = setInterval(() => {
      setUploadProgress((prev) => {
        if (prev >= 100) {
          clearInterval(interval);
          setIsUploading(false);
          setUploadedFile({ name, size: sizeMB });
          return 100;
        }
        return prev + 15;
      });
    }, 100);
  };

  const handleDrop = (e: React.DragEvent) => {
    e.preventDefault();
    setIsDragOver(false);
    if (e.dataTransfer.files && e.dataTransfer.files.length > 0) {
      const file = e.dataTransfer.files[0];
      simulateUpload(file.name, file.size);
    }
  };

  const handleFileSelect = (e: React.ChangeEvent<HTMLInputElement>) => {
    if (e.target.files && e.target.files.length > 0) {
      const file = e.target.files[0];
      simulateUpload(file.name, file.size);
    }
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!uploadedFile) {
      alert('Please upload your Strata Title or Identification document for verification.');
      return;
    }

    onOnboardProperty({
      propertyName: propName,
      stationName,
      bayNumber,
      level,
      docName: uploadedFile.name,
      lat: mapLat,
      lon: mapLon,
    });

    // Alert feedback
    alert(
      `Onboarding Submission Successful!\n\nProperty: ${propName}\nBay: ${bayNumber} (Level ${level})\nStation: ${stationName}\nCoordinates: ${mapLat ? `[${mapLat.toFixed(5)}, ${mapLon?.toFixed(5)}]` : 'Not mapped'}\nStrata File: ${uploadedFile.name}\n\nOur compliance administrators will verify property ownership. Once approved, we will ship your ESP32 smart bollard bundle.`
    );

    // Reset Form
    setPropName('');
    setStationName('');
    setBayNumber('');
    setLevel('');
    setMapLat(undefined);
    setMapLon(undefined);
    setUploadedFile(null);
  };

  return (
    <div className="space-y-6">
      {/* Title */}
      <div>
        <h1 className="text-2xl font-extrabold text-slate-900 tracking-tight">Onboard Parking Supply</h1>
        <p className="text-slate-500 text-xs mt-1 leading-normal">
          Register new private parking spaces near LRT/MRT stations. Verification of Strata Titles guarantees platform safety and legal compliance.
        </p>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-12 gap-6">
        {/* Registration Form Card */}
        <div className="lg:col-span-8">
          <div className="bg-white p-6 rounded-2xl border border-slate-200 shadow-sm space-y-6">
            <div className="flex items-center gap-3 border-b border-slate-100 pb-4">
              <div className="p-2 bg-slate-100 text-slate-700 rounded-lg">
                <Landmark className="w-5 h-5 text-slate-800" />
              </div>
              <div>
                <h2 className="font-bold text-slate-900 text-sm">Regulatory Compliance & Registry Form</h2>
                <span className="text-[10px] text-slate-400 font-medium">Malaysia Strata Management Act 2013 (SMA 2013) Form</span>
              </div>
            </div>

            <form onSubmit={handleSubmit} className="space-y-4">
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                {/* Condominium Name — press Enter to auto-detect nearest station */}
                <div>
                  <label className="block text-xs font-bold text-slate-600 mb-1.5">Condominium / Property Name *</label>
                  <div className="relative">
                    <input
                      type="text"
                      placeholder='e.g. "PV9 Condominium, Setapak" or "Casa Subang, Subang Jaya"'
                      value={propName}
                      onChange={(e) => { setPropName(e.target.value); setSearchError(null); }}
                      onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); handlePropertySearch(); } }}
                      className="w-full text-xs border border-slate-200 rounded-lg pl-3 pr-10 py-2.5 focus:outline-none focus:ring-2 focus:ring-blue-100 focus:border-blue-600"
                      required
                    />
                    {/* Search icon — also clickable */}
                    <button
                      type="button"
                      onClick={handlePropertySearch}
                      disabled={isSearchingStation}
                      className="absolute right-1.5 top-1/2 -translate-y-1/2 p-1.5 rounded-lg hover:bg-slate-100 transition disabled:opacity-50"
                    >
                      {isSearchingStation ? (
                        <Loader2 size={15} className="animate-spin text-blue-600" />
                      ) : (
                        <Search size={15} className="text-slate-400" />
                      )}
                    </button>
                  </div>
                  {searchError && (
                    <p className="text-[10px] text-rose-600 mt-1 flex items-center gap-1">
                      <span>⚠️</span> {searchError}
                    </p>
                  )}
                </div>

                {/* Transit station — auto-detected, read-only */}
                <div>
                  <label className="block text-xs font-bold text-slate-600 mb-1.5">
                    Closest RapidKL Transit Station *
                  </label>
                  <div className="relative">
                    {isSearchingStation ? (
                      <div className="flex items-center gap-2 border border-slate-200 rounded-lg px-3 py-2.5 bg-slate-50">
                        <Loader2 size={14} className="animate-spin text-blue-600" />
                        <span className="text-xs text-slate-500">Detecting nearest station...</span>
                      </div>
                    ) : (
                      <input
                        type="text"
                        value={stationName}
                        readOnly
                        disabled
                        placeholder={propName ? 'Press Enter on Property Name to auto-detect' : 'Enter property name above first'}
                        className="w-full text-xs border border-slate-200 rounded-lg px-3 py-2.5 bg-slate-50 text-slate-700 cursor-not-allowed disabled:opacity-70"
                      />
                    )}
                  </div>
                  {stationName && (
                    <p className="text-[10px] text-emerald-600 mt-1 flex items-center gap-1">
                      <CheckCircle size={11} /> Nearest station auto-detected. Re‑enter property name to change.
                    </p>
                  )}
                </div>
              </div>

              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                {/* Bay Number */}
                <div>
                  <label className="block text-xs font-bold text-slate-600 mb-1.5">Exact Bay Number *</label>
                  <input
                    type="text"
                    placeholder="e.g. Bay 104"
                    value={bayNumber}
                    onChange={(e) => setBayNumber(e.target.value)}
                    className="w-full text-xs border border-slate-200 rounded-lg px-3 py-2.5 focus:outline-none focus:ring-2 focus:ring-blue-100 focus:border-blue-600"
                    required
                  />
                </div>

                {/* Level */}
                <div>
                  <label className="block text-xs font-bold text-slate-600 mb-1.5">Level / Floor *</label>
                  <input
                    type="text"
                    placeholder="e.g. Level 3 (Basement, Block A)"
                    value={level}
                    onChange={(e) => setLevel(e.target.value)}
                    className="w-full text-xs border border-slate-200 rounded-lg px-3 py-2.5 focus:outline-none focus:ring-2 focus:ring-blue-100 focus:border-blue-600"
                    required
                  />
                </div>
              </div>

              {/* Drag and Drop Zone */}
              <div>
                <label className="block text-xs font-bold text-slate-600 mb-1.5">
                  Proof of Accessory Parcel Ownership * (Strata Title, IC, or SPA)
                </label>
                
                <div
                  onDragEnter={handleDragEnter}
                  onDragOver={handleDragEnter}
                  onDragLeave={handleDragLeave}
                  onDrop={handleDrop}
                  onClick={() => fileInputRef.current?.click()}
                  className={`border-2 border-dashed rounded-xl p-8 text-center cursor-pointer transition-all duration-150 flex flex-col items-center justify-center
                    ${isDragOver 
                      ? 'border-blue-600 bg-blue-50/15' 
                      : 'border-slate-200 hover:border-slate-300 bg-slate-50/40 hover:bg-slate-50/70'
                    }
                  `}
                >
                  <UploadCloud className="w-10 h-10 text-slate-400 mb-3" />
                  <h3 className="font-bold text-xs text-slate-700 mb-1">Drag & drop strata PDF document here</h3>
                  <p className="text-[10px] text-slate-400 mb-3">PDF, JPEG, or PNG files up to 10MB</p>
                  <span className="bg-white border border-slate-200 text-slate-700 px-3 py-1.5 rounded-lg text-[10px] font-bold shadow-sm">
                    Browse Files
                  </span>
                  
                  <input
                    ref={fileInputRef}
                    type="file"
                    className="hidden"
                    onChange={handleFileSelect}
                    accept=".pdf,.png,.jpg,.jpeg"
                  />
                </div>

                {/* Upload Loading Simulator */}
                {isUploading && (
                  <div className="mt-3 p-3 bg-slate-50 border border-slate-100 rounded-xl space-y-2">
                    <div className="flex justify-between items-center text-[10px]">
                      <span className="font-bold text-slate-600 flex items-center gap-1.5 animate-pulse">
                        <FileText className="w-3.5 h-3.5 text-slate-400" /> Uploading Strata File...
                      </span>
                      <span className="font-mono font-bold text-slate-700">{uploadProgress}%</span>
                    </div>
                    <div className="w-full bg-slate-200 rounded-full h-1.5">
                      <div className="bg-blue-600 h-1.5 rounded-full transition-all duration-100" style={{ width: `${uploadProgress}%` }}></div>
                    </div>
                  </div>
                )}

                {/* Upload Status Card */}
                {uploadedFile && (
                  <div className="mt-3 p-3 bg-blue-50/40 border border-blue-100 rounded-xl flex items-center justify-between">
                    <div className="flex items-center gap-3">
                      <div className="p-2 bg-blue-500/10 text-blue-600 rounded-lg">
                        <FileCheck className="w-5 h-5" />
                      </div>
                      <div>
                        <span className="block font-bold text-xs text-slate-800">{uploadedFile.name}</span>
                        <span className="block text-[10px] text-slate-400 font-mono">{uploadedFile.size}</span>
                      </div>
                    </div>
                    <span className="bg-blue-600 text-white font-mono text-[9px] font-extrabold px-2 py-0.5 rounded flex items-center gap-0.5 uppercase tracking-wider">
                      <CheckCircle className="w-3 h-3" /> LOADED
                    </span>
                  </div>
                )}
              </div>

              {/* Legal Declaration */}
              <div className="p-3 bg-slate-50 border border-slate-100 rounded-xl flex gap-3 text-[10px] leading-relaxed text-slate-500">
                <ShieldCheck className="w-5 h-5 text-blue-600 shrink-0 mt-0.5" />
                <div>
                  <strong className="text-slate-800 font-bold">Onboarding Legal Affirmation</strong>
                  <p className="mt-0.5">
                    By submitting this registration, I declare that I am the legitimate holder/accessory parcel deed owner of the specified bay. I authorize the ParkJom compliance committee to process this whitelisting profile for RapidKL transit micro-leasing under Joint Management Body (JMB) bylaws.
                  </p>
                </div>
              </div>

              {/* Submit Action */}
              <div className="pt-2 flex justify-end">
                <button
                  type="submit"
                  className="bg-[#0f172a] hover:bg-[#1e293b] text-white font-bold text-xs py-3 px-6 rounded-xl transition-colors shadow flex items-center gap-2 cursor-pointer"
                >
                  <Landmark className="w-4 h-4 text-blue-400" />
                  Submit Onboarding Request
                </button>
              </div>
            </form>
          </div>
        </div>

        {/* Regulatory Guidelines sidebar card */}
        <div className="lg:col-span-4 space-y-6">
          {/* FAQ panel */}
          <div className="bg-white p-6 rounded-2xl border border-slate-200 shadow-sm space-y-4">
            <h3 className="font-bold text-slate-900 text-xs uppercase tracking-wider flex items-center gap-1.5 border-b border-slate-100 pb-2.5">
              <HelpCircle className="w-4 h-4 text-slate-500" />
              Onboarding Checklist
            </h3>

            <div className="space-y-3.5 text-xs text-slate-600">
              <div className="flex gap-2.5">
                <span className="w-5 h-5 rounded-full bg-slate-100 text-slate-700 font-bold font-mono text-[10px] flex items-center justify-center shrink-0">1</span>
                <div>
                  <strong className="text-slate-800">Submit Strata Verification</strong>
                  <p className="text-[10.5px] text-slate-400 mt-0.5">Strata documents are reviewed within 24 hours to whitelisting accessory parcels.</p>
                </div>
              </div>

              <div className="flex gap-2.5">
                <span className="w-5 h-5 rounded-full bg-slate-100 text-slate-700 font-bold font-mono text-[10px] flex items-center justify-center shrink-0">2</span>
                <div>
                  <strong className="text-slate-800">Receive ESP32 Bollard</strong>
                  <p className="text-[10.5px] text-slate-400 mt-0.5">We dispatch a plug-and-play battery-powered smart parking bollard directly to your guardhouse.</p>
                </div>
              </div>

              <div className="flex gap-2.5">
                <span className="w-5 h-5 rounded-full bg-slate-100 text-slate-700 font-bold font-mono text-[10px] flex items-center justify-center shrink-0">3</span>
                <div>
                  <strong className="text-slate-800">Activate IoT Link</strong>
                  <p className="text-[10.5px] text-slate-400 mt-0.5">Simply stick the device to your bay, scan the pairing QR code, and open schedule leasing!</p>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

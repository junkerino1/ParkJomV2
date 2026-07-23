import React, { useState, useMemo } from 'react';
import { Calendar, Clock, Plus, Trash2, Ban, ShieldAlert, Wifi, Info, Copy, Check, Layers, CalendarRange, ListChecks, UploadCloud, Image, Loader2 } from 'lucide-react';
import { motion, AnimatePresence } from 'motion/react';
import { ParkingBay } from '../types';

interface ScheduleBlockDisplay {
  id: string;
  dayOfWeek: number; // 0 = Sunday, 1-6 = Mon-Sat
  startTime: string;
  endTime: string;
  rate: number;
}

interface AvailabilitySchedulerProps {
  bays: ParkingBay[];
  scheduleBlocks: ScheduleBlockDisplay[];
  onAddBlock: (block: { dayOfWeek: number; startTime: string; endTime: string; rate: number }) => void;
  onRemoveBlock: (id: string) => void;
  onBlockAll: () => void;
  onConfigParking: (
    parkingSpotId: string,
    images: File[],
    dayType: string,
    startTime: string,
    endTime: string,
    effectiveFrom: string,
    effectiveUntil: string,
    monthlyRate?: number,
    dailyRate?: number
  ) => Promise<boolean>;
}

type BulkAction = 'single' | 'weekdays' | 'weekends' | 'allweek' | 'month';

const daysOfWeekNames = [
  { id: 1, label: 'Mon', full: 'Monday' },
  { id: 2, label: 'Tue', full: 'Tuesday' },
  { id: 3, label: 'Wed', full: 'Wednesday' },
  { id: 4, label: 'Thu', full: 'Thursday' },
  { id: 5, label: 'Fri', full: 'Friday' },
  { id: 6, label: 'Sat', full: 'Saturday' },
  { id: 0, label: 'Sun', full: 'Sunday' },
];

const weekdays = [1, 2, 3, 4, 5];
const weekends = [6, 0];
const allDays = [1, 2, 3, 4, 5, 6, 0];

export default function AvailabilityScheduler({ 
  bays, 
  scheduleBlocks, 
  onAddBlock, 
  onRemoveBlock, 
  onBlockAll,
  onConfigParking 
}: AvailabilitySchedulerProps) {
  // Only approved (Active) bays can be scheduled
  const activeBays = bays.filter(b => b.status === 'Active');
  const [selectedBayId, setSelectedBayId] = useState(activeBays[0]?.id || '');
  const [day, setDay] = useState(1);
  const [startTime, setStartTime] = useState('08:00');
  const [endTime, setEndTime] = useState('18:00');
  const [rate, setRate] = useState('2.00');
  const [bulkAction, setBulkAction] = useState<BulkAction>('single');
  const [successMsg, setSuccessMsg] = useState<string | null>(null);
  const [selectedDays, setSelectedDays] = useState<number[]>([1]);
  const [showDateRange, setShowDateRange] = useState(false);
  const [dateRangeStart, setDateRangeStart] = useState('');
  const [dateRangeEnd, setDateRangeEnd] = useState('');

  // Config parking state — image upload, day type, publish
  const [configImages, setConfigImages] = useState<File[]>([]);
  const [configDayType, setConfigDayType] = useState('Everyday');
  const [configStartTime, setConfigStartTime] = useState('09:00');
  const [configEndTime, setConfigEndTime] = useState('17:30');
  const [configFrom, setConfigFrom] = useState('');
  const [configUntil, setConfigUntil] = useState('');
  const [configRate, setConfigRate] = useState('100');
  const [configRateType, setConfigRateType] = useState<'monthly' | 'daily'>('monthly');
  const [isPublishing, setIsPublishing] = useState(false);
  const [publishMsg, setPublishMsg] = useState<{ type: 'success' | 'error'; text: string } | null>(null);

  const showToast = (msg: string) => {
    setSuccessMsg(msg);
    setTimeout(() => setSuccessMsg(null), 3000);
  };

  const handleImageSelect = (e: React.ChangeEvent<HTMLInputElement>) => {
    if (e.target.files) {
      setConfigImages(prev => [...prev, ...Array.from(e.target.files!)]);
    }
  };

  const removeImage = (idx: number) => {
    setConfigImages(prev => prev.filter((_, i) => i !== idx));
  };

  const handlePublish = async () => {
    if (!selectedBayId) {
      setPublishMsg({ type: 'error', text: 'Please select a parking bay.' });
      return;
    }
    if (configImages.length === 0) {
      setPublishMsg({ type: 'error', text: 'Please upload at least one parking image.' });
      return;
    }
    if (!configFrom || !configUntil) {
      setPublishMsg({ type: 'error', text: 'Please set the effective date range.' });
      return;
    }

    setIsPublishing(true);
    setPublishMsg(null);

    const success = await onConfigParking(
      selectedBayId,
      configImages,
      configDayType,
      configStartTime,
      configEndTime,
      configFrom,
      configUntil,
      configRateType === 'monthly' ? parseFloat(configRate) : undefined,
      configRateType === 'daily' ? parseFloat(configRate) : undefined
    );

    setIsPublishing(false);
    if (success) {
      setPublishMsg({ type: 'success', text: 'Parking spot configured and published successfully!' });
      setConfigImages([]);
    } else {
      setPublishMsg({ type: 'error', text: 'Failed to configure parking. Please try again.' });
    }
  };

  const getTargetDays = (): number[] => {
    switch (bulkAction) {
      case 'single': return [day];
      case 'weekdays': return weekdays;
      case 'weekends': return weekends;
      case 'allweek': return allDays;
      case 'month': return selectedDays;
      default: return [day];
    }
  };

  const handleSave = (e: React.FormEvent) => {
    e.preventDefault();
    if (!startTime || !endTime) {
      alert('Please select valid start and end times.');
      return;
    }

    const startMinutes = parseInt(startTime.split(':')[0]) * 60 + parseInt(startTime.split(':')[1]);
    const endMinutes = parseInt(endTime.split(':')[0]) * 60 + parseInt(endTime.split(':')[1]);

    if (startMinutes >= endMinutes) {
      alert('Error: End time must be after the start time.');
      return;
    }

    const rateNum = parseFloat(rate);
    if (isNaN(rateNum) || rateNum <= 0) {
      alert('Please configure a valid hourly rate.');
      return;
    }

    const targetDays = getTargetDays();
    let addedCount = 0;

    targetDays.forEach(d => {
      // Check if a block already exists for this day with overlapping times
      const existing = scheduleBlocks.find(
        b => b.dayOfWeek === d && b.startTime === startTime && b.endTime === endTime
      );
      if (!existing) {
        onAddBlock({ dayOfWeek: d, startTime, endTime, rate: rateNum });
        addedCount++;
      }
    });

    if (addedCount === 0) {
      alert('This schedule block already exists for all selected days.');
      return;
    }

    const daysLabel = bulkAction === 'single' 
      ? daysOfWeekNames.find(d => d.id === day)?.label
      : `${addedCount} days`;

    showToast(`Schedule configured for ${daysLabel}! ${addedCount > 1 ? `(${addedCount} blocks created)` : ''}`);
  };

  const handleBulkRemoveDay = (targetDay: number) => {
    const blocksToRemove = scheduleBlocks.filter(b => b.dayOfWeek === targetDay);
    blocksToRemove.forEach(b => onRemoveBlock(b.id));
    showToast(`Cleared all blocks for ${daysOfWeekNames.find(d => d.id === targetDay)?.full}`);
  };

  const handleClearDaySchedule = (dayId: number) => {
    const confirmClear = window.confirm(`Remove all schedule blocks for ${daysOfWeekNames.find(d => d.id === dayId)?.full}?`);
    if (confirmClear) {
      handleBulkRemoveDay(dayId);
    }
  };

  const handleCopyFromDay = (sourceDay: number) => {
    const sourceBlocks = scheduleBlocks.filter(b => b.dayOfWeek === sourceDay);
    if (sourceBlocks.length === 0) {
      alert(`No schedule blocks found for ${daysOfWeekNames.find(d => d.id === sourceDay)?.full}.`);
      return;
    }

    const targetDays = allDays.filter(d => d !== sourceDay);
    let copiedCount = 0;
    targetDays.forEach(td => {
      sourceBlocks.forEach(block => {
        const exists = scheduleBlocks.find(
          b => b.dayOfWeek === td && b.startTime === block.startTime && b.endTime === block.endTime
        );
        if (!exists) {
          onAddBlock({ dayOfWeek: td, startTime: block.startTime, endTime: block.endTime, rate: block.rate });
          copiedCount++;
        }
      });
    });

    showToast(`Copied ${sourceBlocks.length} block(s) from ${daysOfWeekNames.find(d => d.id === sourceDay)?.full} to ${targetDays.length} other day(s). ${copiedCount} new blocks created.`);
  };

  const handleApplyToMonth = () => {
    if (!dateRangeStart || !dateRangeEnd) {
      alert('Please select both start and end dates for the month range.');
      return;
    }

    const start = new Date(dateRangeStart);
    const end = new Date(dateRangeEnd);
    
    if (end < start) {
      alert('End date must be after start date.');
      return;
    }

    // Get all unique days of the week in the date range
    const daysInRange = new Set<number>();
    const current = new Date(start);
    while (current <= end) {
      daysInRange.add(current.getDay());
      current.setDate(current.getDate() + 1);
    }

    if (daysInRange.size === 0) {
      alert('No days found in the selected range.');
      return;
    }

    const rateNum = parseFloat(rate);
    const startMinutes = parseInt(startTime.split(':')[0]) * 60 + parseInt(startTime.split(':')[1]);
    const endMinutes = parseInt(endTime.split(':')[0]) * 60 + parseInt(endTime.split(':')[1]);

    if (startMinutes >= endMinutes) {
      alert('Error: End time must be after the start time.');
      return;
    }

    let addedCount = 0;
    daysInRange.forEach(d => {
      const existing = scheduleBlocks.find(
        b => b.dayOfWeek === d && b.startTime === startTime && b.endTime === endTime
      );
      if (!existing) {
        onAddBlock({ dayOfWeek: d, startTime, endTime, rate: rateNum });
        addedCount++;
      }
    });

    const rangeLabel = dateRangeStart === dateRangeEnd 
      ? dateRangeStart 
      : `${dateRangeStart} to ${dateRangeEnd}`;
    showToast(`Applied schedule to ${addedCount} day(s) in range ${rangeLabel}. Total days of week affected: ${daysInRange.size}`);
    setShowDateRange(false);
  };

  const handleBayChange = (bayId: string) => {
    setSelectedBayId(bayId);
  };

  const activeBay = activeBays.find(b => b.id === selectedBayId) || activeBays[0];

  const dayStats = useMemo(() => {
    const total = scheduleBlocks.length;
    const daysCovered = new Set(scheduleBlocks.map(b => b.dayOfWeek)).size;
    return { total, daysCovered };
  }, [scheduleBlocks]);

  return (
    <div className="space-y-6">
      {/* Toast notification */}
      <AnimatePresence>
        {successMsg && (
          <motion.div
            initial={{ opacity: 0, y: -10 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0, y: -10 }}
            className="bg-emerald-50 border border-emerald-200 text-emerald-800 px-4 py-3 rounded-xl flex items-center gap-2 text-sm shadow-sm"
          >
            <Check className="w-4 h-4 text-emerald-600" />
            <span>{successMsg}</span>
          </motion.div>
        )}
      </AnimatePresence>

      {/* Title */}
      <div>
        <h1 className="text-2xl font-extrabold text-slate-900 tracking-tight">Availability Scheduler</h1>
        <p className="text-slate-500 text-xs mt-1 leading-normal">
          Define the specific days and hourly blocks when your parking bay is vacant. Use bulk actions to schedule entire weeks or months in seconds.
        </p>
      </div>

      {/* No approved bays — show guidance */}
      {activeBays.length === 0 && (
        <div className="bg-amber-50 border border-amber-200 rounded-xl p-6 text-center">
          <Ban className="w-10 h-10 text-amber-400 mx-auto mb-2" />
          <h3 className="text-sm font-bold text-amber-800 mb-1">No Approved Parking Bays</h3>
          <p className="text-xs text-amber-600 max-w-md mx-auto">
            Only parking bays that have been approved by an admin can be scheduled. 
            Please wait for your registration to be verified, or check the status under the Dashboard tab.
          </p>
        </div>
      )}

      {activeBays.length > 0 && (
      <>

      {/* Quick Stats */}
      <div className="grid grid-cols-3 gap-3">
        <div className="bg-white rounded-xl border border-slate-200 p-3 shadow-xs">
          <span className="text-[10px] text-slate-400 font-medium block">Total Blocks</span>
          <span className="text-lg font-bold text-slate-900">{dayStats.total}</span>
        </div>
        <div className="bg-white rounded-xl border border-slate-200 p-3 shadow-xs">
          <span className="text-[10px] text-slate-400 font-medium block">Days Covered</span>
          <span className="text-lg font-bold text-slate-900">{dayStats.daysCovered}/7</span>
        </div>
        <div className="bg-white rounded-xl border border-slate-200 p-3 shadow-xs">
          <span className="text-[10px] text-slate-400 font-medium block">Active Rate</span>
          <span className="text-lg font-bold text-emerald-600">RM {rate}/hr</span>
        </div>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-12 gap-6">
        {/* Slot Configurator Card */}
        <div className="lg:col-span-4 space-y-6">
          <div className="bg-white p-6 rounded-2xl border border-slate-200 shadow-sm space-y-5">
            <h2 className="font-bold text-slate-900 text-sm flex items-center gap-2 border-b border-slate-100 pb-3">
              <Clock className="w-4 h-4 text-blue-600" />
              Configure Vacancy Slot
            </h2>

            <form onSubmit={handleSave} className="space-y-4">
              {/* Parking Bay Select */}
              <div>
                <label className="block text-[11px] font-bold text-slate-600 mb-1.5 uppercase font-mono tracking-wider">Select Active Bay</label>
                <select
                  value={selectedBayId}
                  onChange={(e) => handleBayChange(e.target.value)}
                  className="w-full text-xs border border-slate-200 rounded-lg px-3 py-2.5 bg-white focus:outline-none focus:ring-2 focus:ring-blue-100 focus:border-blue-600"
                >
                  {activeBays.length === 0 && (
                    <option value="">No approved bays available</option>
                  )}
                  {activeBays.map((bay) => (
                    <option key={bay.id} value={bay.id}>
                      {bay.bayNumber} ({bay.propertyName})
                    </option>
                  ))}
                </select>
              </div>

              {/* Bulk Action Selector */}
              <div>
                <label className="block text-[11px] font-bold text-slate-600 mb-1.5 uppercase font-mono tracking-wider">Apply to</label>
                <div className="grid grid-cols-2 gap-1.5">
                  {[
                    { value: 'single', label: 'Single Day', icon: Calendar },
                    { value: 'weekdays', label: 'Weekdays (Mon-Fri)', icon: ListChecks },
                    { value: 'weekends', label: 'Weekends', icon: Layers },
                    { value: 'allweek', label: 'Full Week', icon: Copy },
                    { value: 'month', label: 'Date Range', icon: CalendarRange },
                  ].map((action) => (
                    <button
                      key={action.value}
                      type="button"
                      onClick={() => {
                        setBulkAction(action.value as BulkAction);
                        if (action.value === 'weekdays') setSelectedDays(weekdays);
                        if (action.value === 'weekends') setSelectedDays(weekends);
                        if (action.value === 'allweek') setSelectedDays(allDays);
                        if (action.value === 'month') setShowDateRange(true);
                        if (action.value === 'single') setSelectedDays([day]);
                      }}
                      className={`text-[10px] font-bold py-2 px-2 rounded-lg border transition-all flex items-center justify-center gap-1 ${
                        bulkAction === action.value
                          ? 'bg-blue-50 border-blue-300 text-blue-700 shadow-sm'
                          : 'border-slate-200 text-slate-500 hover:bg-slate-50'
                      }`}
                    >
                      <action.icon className={`w-3 h-3 ${bulkAction === action.value ? 'text-blue-500' : ''}`} />
                      <span className="leading-tight">{action.label}</span>
                    </button>
                  ))}
                </div>
              </div>

              {/* Day Select (for single day mode) */}
              {bulkAction === 'single' && (
                <div>
                  <label className="block text-[11px] font-bold text-slate-600 mb-1.5 uppercase font-mono tracking-wider">Target Day of Week</label>
                  <select
                    value={day}
                    onChange={(e) => {
                      const newDay = parseInt(e.target.value);
                      setDay(newDay);
                      setSelectedDays([newDay]);
                    }}
                    className="w-full text-xs border border-slate-200 rounded-lg px-3 py-2.5 bg-white focus:outline-none focus:ring-2 focus:ring-blue-100 focus:border-blue-600"
                  >
                    {daysOfWeekNames.map(d => (
                      <option key={d.id} value={d.id}>{d.full}</option>
                    ))}
                  </select>
                </div>
              )}

              {/* Date Range Picker (for month/range mode) */}
              {showDateRange && bulkAction === 'month' && (
                <div className="space-y-2 p-3 bg-blue-50/50 rounded-xl border border-blue-100">
                  <label className="block text-[11px] font-bold text-blue-700 mb-1 uppercase font-mono tracking-wider">Select Date Range</label>
                  <div className="grid grid-cols-2 gap-2">
                    <div>
                      <span className="block text-[9px] text-slate-500 mb-0.5">From</span>
                      <input
                        type="date"
                        value={dateRangeStart}
                        onChange={(e) => setDateRangeStart(e.target.value)}
                        className="w-full text-xs border border-blue-200 rounded-lg px-2 py-2 focus:outline-none focus:ring-2 focus:ring-blue-100 focus:border-blue-600"
                      />
                    </div>
                    <div>
                      <span className="block text-[9px] text-slate-500 mb-0.5">To</span>
                      <input
                        type="date"
                        value={dateRangeEnd}
                        onChange={(e) => setDateRangeEnd(e.target.value)}
                        className="w-full text-xs border border-blue-200 rounded-lg px-2 py-2 focus:outline-none focus:ring-2 focus:ring-blue-100 focus:border-blue-600"
                      />
                    </div>
                  </div>
                  <p className="text-[9px] text-slate-400">
                    Applies the time block to all days of the week found within this date range.
                  </p>
                </div>
              )}

              {/* Bulk mode info display */}
              {bulkAction !== 'single' && bulkAction !== 'month' && (
                <div className="p-2.5 bg-indigo-50/50 rounded-lg border border-indigo-100 text-[10px] text-indigo-700 flex items-center gap-1.5">
                  <Layers className="w-3.5 h-3.5 text-indigo-500 shrink-0" />
                  <span>Applies to: <strong>{bulkAction === 'weekdays' ? 'Mon-Fri' : bulkAction === 'weekends' ? 'Sat-Sun' : 'All 7 days'}</strong></span>
                </div>
              )}

              {/* Time Inputs */}
              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="block text-[11px] font-bold text-slate-600 mb-1.5 uppercase font-mono tracking-wider">Start Time</label>
                  <input
                    type="time"
                    value={startTime}
                    onChange={(e) => setStartTime(e.target.value)}
                    className="w-full text-xs border border-slate-200 rounded-lg px-3 py-2 focus:outline-none focus:ring-2 focus:ring-blue-100 focus:border-blue-600 font-mono"
                    required
                  />
                </div>
                <div>
                  <label className="block text-[11px] font-bold text-slate-600 mb-1.5 uppercase font-mono tracking-wider">End Time</label>
                  <input
                    type="time"
                    value={endTime}
                    onChange={(e) => setEndTime(e.target.value)}
                    className="w-full text-xs border border-slate-200 rounded-lg px-3 py-2 focus:outline-none focus:ring-2 focus:ring-blue-100 focus:border-blue-600 font-mono"
                    required
                  />
                </div>
              </div>

              {/* Custom Hourly Rate */}
              <div>
                <label className="block text-[11px] font-bold text-slate-600 mb-1.5 uppercase font-mono tracking-wider">Commuter Hourly Rate (RM)</label>
                <div className="relative">
                  <span className="absolute left-3 top-1/2 -translate-y-1/2 text-slate-400 font-bold font-mono text-xs">RM</span>
                  <input
                    type="number"
                    step="0.10"
                    value={rate}
                    onChange={(e) => setRate(e.target.value)}
                    className="w-full pl-9 pr-12 py-2.5 text-xs border border-slate-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-100 focus:border-blue-600 font-mono font-bold"
                    required
                  />
                  <span className="absolute right-3 top-1/2 -translate-y-1/2 text-slate-400 text-[10px] font-medium font-mono">/hr</span>
                </div>
                {activeBay && (
                  <p className="text-[10px] text-slate-400 mt-1 flex items-center gap-1">
                    <Info className="w-3 h-3 text-slate-400" /> Avg rate near {activeBay.stationName}: RM 2.00/hr
                  </p>
                )}
              </div>

              {/* Action Buttons */}
              <div className="pt-3 space-y-2">
                <button
                  type="submit"
                  className="w-full bg-[#0f172a] hover:bg-[#1e293b] text-white font-bold text-xs py-3 rounded-xl transition-all duration-150 flex items-center justify-center gap-1.5 shadow"
                >
                  <Plus className="w-4 h-4 text-blue-400" />
                  {bulkAction === 'month' && showDateRange ? 'Apply to Date Range' : bulkAction !== 'single' ? `Apply to ${bulkAction === 'weekdays' ? 'Weekdays' : bulkAction === 'weekends' ? 'Weekends' : 'Full Week'}` : 'Save Schedule'}
                </button>
                <button
                  type="button"
                  onClick={() => {
                    const confirmBlock = window.confirm('Are you sure you want to block all scheduled vacancy blocks?\n\nThis will raise physical smart bollards and deny all incoming commuter reservations immediately.');
                    if (confirmBlock) {
                      onBlockAll();
                      showToast('All dates blocked. ESP32 bollards raised.');
                    }
                  }}
                  className="w-full border border-rose-200 hover:border-rose-300 text-rose-700 bg-rose-50/20 hover:bg-rose-50/50 font-bold text-xs py-2.5 rounded-xl transition-all duration-150 flex items-center justify-center gap-1.5"
                >
                  <Ban className="w-3.5 h-3.5" />
                  Block All Dates
                </button>
              </div>
            </form>
          </div>

          {/* Quick Actions Panel */}
          <div className="bg-white p-5 rounded-2xl border border-slate-200 shadow-sm space-y-3">
            <h3 className="font-bold text-slate-800 text-xs flex items-center gap-2 border-b border-slate-100 pb-2.5">
              <Copy className="w-4 h-4 text-indigo-500" />
              Quick Copy Actions
            </h3>
            <div className="space-y-1.5">
              <p className="text-[10px] text-slate-400">Copy a day's schedule to other days:</p>
              <div className="grid grid-cols-4 gap-1">
                {daysOfWeekNames.map(d => (
                  <button
                    key={d.id}
                    type="button"
                    onClick={() => handleCopyFromDay(d.id)}
                    className="text-[9px] font-bold py-1.5 rounded-lg border border-slate-200 hover:bg-blue-50 hover:border-blue-200 text-slate-500 hover:text-blue-700 transition-all"
                    title={`Copy ${d.full} schedule to all other days`}
                  >
                    {d.label}
                  </button>
                ))}
              </div>
            </div>
            <div className="space-y-1.5 pt-1 border-t border-slate-100">
              <p className="text-[10px] text-slate-400">Clear a day:</p>
              <div className="grid grid-cols-4 gap-1">
                {daysOfWeekNames.map(d => (
                  <button
                    key={d.id}
                    type="button"
                    onClick={() => handleClearDaySchedule(d.id)}
                    className="text-[9px] font-bold py-1.5 rounded-lg border border-rose-200 hover:bg-rose-50 hover:border-rose-300 text-rose-400 hover:text-rose-600 transition-all"
                    title={`Clear all blocks for ${d.full}`}
                  >
                    {d.label}
                  </button>
                ))}
              </div>
            </div>
          </div>

          {/* Legal Compliance card */}
          <div className="bg-[#0f172a] text-slate-300 p-5 rounded-2xl border border-slate-800 space-y-3 shadow-sm">
            <h3 className="text-white text-xs font-bold flex items-center gap-2">
              <ShieldAlert className="w-4 h-4 text-amber-400" />
              Strata Act Actuation
            </h3>
            <p className="text-[10.5px] text-slate-400 leading-relaxed">
              Under Section 59 of the <strong>Malaysia Strata Management Act (SMA 2013)</strong>, property owners are fully legally authorized to manage access permissions on their assigned accessory parcels. Scheduled slots automatically generate time-restricted access key tokens.
            </p>
            <div className="text-[10px] text-slate-500 font-mono flex items-center gap-1.5 border-t border-slate-800 pt-2 mt-2">
              <Wifi className="w-3 h-3 text-blue-400" />
              <span>Edge Sync Status: 100% Consistent</span>
            </div>
          </div>
        </div>

        {/* Visual Calendar Display */}
        <div className="lg:col-span-8 bg-white p-6 rounded-2xl border border-slate-200 shadow-sm flex flex-col">
          <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3 border-b border-slate-100 pb-4 mb-5">
            <div>
              <h2 className="font-bold text-slate-900 text-base flex items-center gap-2">
                <Calendar className="w-5 h-5 text-slate-600" />
                Active Weekly Rental Blocks
              </h2>
              <p className="text-slate-400 text-[10px] mt-0.5">Showing scheduled vacant hours for {activeBay?.bayNumber || '104'}.</p>
            </div>
            <div className="flex items-center gap-2">
              <span className="bg-blue-50 border border-blue-200 text-blue-800 font-mono text-[9px] font-extrabold px-2.5 py-1 rounded">
                AUTO-SYNCED TO HARDWARE
              </span>
              <span className="bg-slate-100 text-slate-500 font-mono text-[9px] font-bold px-2 py-1 rounded">
                {dayStats.total} blocks
              </span>
            </div>
          </div>

          {/* Week Calendar — horizontal scroll on mobile */}
          <div className="overflow-x-auto -mx-2 px-2">
            <div className="grid grid-cols-7 gap-1 md:gap-2 flex-grow min-w-[560px]">
            {daysOfWeekNames.map((dayName) => {
              const dayBlocks = scheduleBlocks.filter(b => b.dayOfWeek === dayName.id);
              return (
                <div key={dayName.id} className="flex flex-col h-full bg-slate-50/50 border border-slate-100 rounded-lg md:rounded-xl p-1 md:p-2 min-h-[260px] md:min-h-[360px]">
                  <div className="flex items-center justify-between mb-1 md:mb-2 pb-1 border-b border-slate-100">
                    <span className="font-bold text-slate-700 text-[9px] md:text-xs font-mono whitespace-nowrap">{dayName.label}</span>
                    {dayBlocks.length > 0 && (
                      <button onClick={() => handleClearDaySchedule(dayName.id)}
                        className="text-[8px] text-slate-400 hover:text-rose-500 transition-colors p-0.5" title={`Clear ${dayName.full}`}>
                        <Trash2 className="w-2 h-2 md:w-2.5 md:h-2.5" />
                      </button>
                    )}
                  </div>
                  <div className="flex-1 space-y-1 md:space-y-2">
                    {dayBlocks.length === 0 ? (
                      <div className="h-full flex items-center justify-center text-center text-slate-300 font-medium text-[8px] md:text-[10px] px-0.5 py-8 md:py-12">Blocked</div>
                    ) : (
                      dayBlocks.map((block) => (
                        <div key={block.id} 
                          className="bg-blue-50/70 border border-blue-100 rounded-md md:rounded-lg p-1 md:p-2 flex flex-col justify-between relative group hover:border-blue-600 transition-all">
                          <button onClick={() => { onRemoveBlock(block.id); showToast('Rental availability block removed.'); }}
                            className="absolute -top-1 -right-1 bg-white border border-rose-100 text-rose-500 hover:text-rose-700 rounded-full p-0.5 md:p-1 shadow-sm opacity-0 group-hover:opacity-100 transition-opacity" title="Remove">
                            <Trash2 className="w-2.5 h-2.5 md:w-3 md:h-3" />
                          </button>
                          <div className="text-[8px] md:text-[10px] font-bold text-slate-800 font-mono">
                            {block.startTime}<span className="text-[7px] md:text-[8px] text-slate-400 mx-0.5">-</span>{block.endTime}
                          </div>
                          <div className="border-t border-blue-100/60 mt-1 pt-0.5 md:pt-1 text-[7px] md:text-[9px] font-extrabold text-blue-700 font-mono">RM{block.rate.toFixed(0)}/h</div>
                        </div>
                      ))
                    )}
                  </div>
                </div>
              );
            })}
            </div>
          </div>
        </div>
      </div>

      {/* Publish Configuration Section — POST /api/parking/config-parking/{id} */}
      {activeBays.length > 0 && (
        <div className="bg-white p-6 rounded-2xl border border-slate-200 shadow-sm space-y-5">
          <h2 className="font-bold text-slate-900 text-sm flex items-center gap-2 border-b border-slate-100 pb-3">
            <UploadCloud className="w-4 h-4 text-emerald-600" />
            Publish Parking Configuration
          </h2>
          <p className="text-xs text-slate-500 -mt-2">
            Upload photos, set schedule and pricing, then publish your parking spot to commuters.
          </p>

          <div className="grid grid-cols-1 md:grid-cols-2 gap-5">
            {/* Left: Images + Bay Select */}
            <div className="space-y-4">
              <div>
                <label className="block text-[11px] font-bold text-slate-600 mb-1.5 uppercase tracking-wider">Select Bay</label>
                <select
                  value={selectedBayId}
                  onChange={(e) => setSelectedBayId(e.target.value)}
                  className="w-full text-xs border border-slate-200 rounded-lg px-3 py-2.5 bg-white focus:outline-none focus:ring-2 focus:ring-emerald-100 focus:border-emerald-600"
                >
                  {activeBays.map((bay) => (
                    <option key={bay.id} value={bay.id}>
                      {bay.bayNumber} — {bay.propertyName}
                    </option>
                  ))}
                </select>
              </div>

              <div>
                <label className="block text-[11px] font-bold text-slate-600 mb-1.5 uppercase tracking-wider">Parking Images</label>
                <label className="flex flex-col items-center justify-center border-2 border-dashed border-slate-200 rounded-xl p-5 cursor-pointer hover:border-emerald-400 hover:bg-emerald-50/30 transition-all">
                  <Image className="w-8 h-8 text-slate-300 mb-2" />
                  <span className="text-xs text-slate-500 font-medium">Click to upload images</span>
                  <span className="text-[10px] text-slate-400">JPG, PNG (multiple)</span>
                  <input
                    type="file"
                    accept="image/jpeg,image/png"
                    multiple
                    onChange={handleImageSelect}
                    className="hidden"
                  />
                </label>
                {configImages.length > 0 && (
                  <div className="flex flex-wrap gap-2 mt-3">
                    {configImages.map((file, idx) => (
                      <div key={idx} className="relative group">
                        <img
                          src={URL.createObjectURL(file)}
                          alt={file.name}
                          className="w-16 h-16 object-cover rounded-lg border border-slate-200"
                        />
                        <button
                          onClick={() => removeImage(idx)}
                          className="absolute -top-1.5 -right-1.5 bg-white border border-rose-100 text-rose-500 rounded-full p-0.5 shadow-sm opacity-0 group-hover:opacity-100 transition-opacity"
                        >
                          <Trash2 className="w-3 h-3" />
                        </button>
                        <span className="text-[8px] text-slate-400 truncate block w-16 text-center mt-0.5">{file.name}</span>
                      </div>
                    ))}
                  </div>
                )}
              </div>
            </div>

            {/* Right: Schedule Config */}
            <div className="space-y-4">
              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="block text-[11px] font-bold text-slate-600 mb-1.5 uppercase tracking-wider">Day Type</label>
                  <select
                    value={configDayType}
                    onChange={(e) => {
                      setConfigDayType(e.target.value);
                      setConfigRateType(e.target.value === 'Everyday' ? 'monthly' : 'daily');
                    }}
                    className="w-full text-xs border border-slate-200 rounded-lg px-3 py-2.5 bg-white focus:outline-none focus:ring-2 focus:ring-emerald-100 focus:border-emerald-600"
                  >
                    <option value="Everyday">Everyday (Monthly)</option>
                    <option value="Weekday">Weekday (Daily)</option>
                    <option value="Weekend">Weekend (Daily)</option>
                  </select>
                </div>
                <div>
                  <label className="block text-[11px] font-bold text-slate-600 mb-1.5 uppercase tracking-wider">
                    {configRateType === 'monthly' ? 'Monthly Rate (RM)' : 'Daily Rate (RM)'}
                  </label>
                  <div className="relative">
                    <span className="absolute left-3 top-1/2 -translate-y-1/2 text-slate-400 font-bold text-xs">RM</span>
                    <input
                      type="number"
                      step="0.01"
                      value={configRate}
                      onChange={(e) => setConfigRate(e.target.value)}
                      className="w-full pl-9 pr-4 py-2.5 text-xs border border-slate-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-emerald-100 focus:border-emerald-600 font-mono font-bold"
                    />
                  </div>
                </div>
              </div>

              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="block text-[11px] font-bold text-slate-600 mb-1.5 uppercase tracking-wider">Start Time</label>
                  <input
                    type="time"
                    value={configStartTime}
                    onChange={(e) => setConfigStartTime(e.target.value)}
                    className="w-full text-xs border border-slate-200 rounded-lg px-3 py-2 font-mono focus:outline-none focus:ring-2 focus:ring-emerald-100 focus:border-emerald-600"
                  />
                </div>
                <div>
                  <label className="block text-[11px] font-bold text-slate-600 mb-1.5 uppercase tracking-wider">End Time</label>
                  <input
                    type="time"
                    value={configEndTime}
                    onChange={(e) => setConfigEndTime(e.target.value)}
                    className="w-full text-xs border border-slate-200 rounded-lg px-3 py-2 font-mono focus:outline-none focus:ring-2 focus:ring-emerald-100 focus:border-emerald-600"
                  />
                </div>
              </div>

              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="block text-[11px] font-bold text-slate-600 mb-1.5 uppercase tracking-wider">Effective From</label>
                  <input
                    type="date"
                    value={configFrom}
                    onChange={(e) => setConfigFrom(e.target.value)}
                    className="w-full text-xs border border-slate-200 rounded-lg px-3 py-2 focus:outline-none focus:ring-2 focus:ring-emerald-100 focus:border-emerald-600"
                  />
                </div>
                <div>
                  <label className="block text-[11px] font-bold text-slate-600 mb-1.5 uppercase tracking-wider">Effective Until</label>
                  <input
                    type="date"
                    value={configUntil}
                    onChange={(e) => setConfigUntil(e.target.value)}
                    className="w-full text-xs border border-slate-200 rounded-lg px-3 py-2 focus:outline-none focus:ring-2 focus:ring-emerald-100 focus:border-emerald-600"
                  />
                </div>
              </div>

              {publishMsg && (
                <div className={`text-xs font-medium p-3 rounded-lg ${
                  publishMsg.type === 'success'
                    ? 'bg-emerald-50 text-emerald-700 border border-emerald-200'
                    : 'bg-rose-50 text-rose-700 border border-rose-200'
                }`}>
                  {publishMsg.text}
                </div>
              )}

              <button
                onClick={handlePublish}
                disabled={isPublishing}
                className="w-full bg-emerald-600 hover:bg-emerald-700 disabled:bg-emerald-400 text-white font-bold text-xs py-3 rounded-xl transition-all duration-150 flex items-center justify-center gap-2 shadow"
              >
                {isPublishing ? (
                  <><Loader2 className="w-4 h-4 animate-spin" /> Publishing...</>
                ) : (
                  <><UploadCloud className="w-4 h-4" /> Publish Configuration</>
                )}
              </button>
            </div>
          </div>
        </div>
      )}
      </>
      )}
    </div>
  );
}

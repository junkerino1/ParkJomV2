import React, { useState, useEffect } from 'react';
import { LayoutDashboard, CalendarDays, PlusSquare, ClipboardList, Sliders } from 'lucide-react';
import { useAuth } from '../../contexts/AuthContext';
import Sidebar from './components/Sidebar';
import Header from './components/Header';
import BottomNav from '../../components/ui/BottomNav';
import PageTransition from '../../components/ui/PageTransition';
import DashboardHome from './components/DashboardHome';
import AvailabilityScheduler from './components/AvailabilityScheduler';
import PropertyOnboarding from './components/PropertyOnboarding';
import SettingsPanel from './components/SettingsPanel';
import SupportTickets from './components/SupportTickets';
import { ParkingBay, Booking, Notification, WalletTransaction } from './types';

const API_BASE = import.meta.env.VITE_API_BASE ||
  (window.location.hostname !== 'localhost' && window.location.hostname !== '127.0.0.1'
    ? 'https://parkjom-api-gbgcbycbcjghczgu.malaysiawest-01.azurewebsites.net/api'
    : '/api');

function loadNotifications(): Notification[] {
  try { const s = localStorage.getItem('parkjom_owner_notifs'); return s ? JSON.parse(s) : []; }
  catch { return []; }
}
function saveNotifications(notifs: Notification[]) {
  localStorage.setItem('parkjom_owner_notifs', JSON.stringify(notifs));
}

export default function OwnerDashboard() {
  const { user } = useAuth();
  // Navigation View Router
  const [activeView, setActiveView] = useState('dashboard');
  const [isSidebarOpen, setIsSidebarOpen] = useState(false);

  // 1. Wallet Balance (RM) — TODO: fetch from backend
  const [walletBalance, setWalletBalance] = useState(0);

  // 2. Active Registered Parking Bays — fetched from backend
  const [bays, setBays] = useState<ParkingBay[]>([]);
  const [baysLoading, setBaysLoading] = useState(true);

  // 3. Recent Bookings History — TODO: fetch from backend
  const [bookings, setBookings] = useState<Booking[]>([]);

  // 4. Notifications — persisted to localStorage
  const [notifications, setNotifications] = useState<Notification[]>(loadNotifications);

  // 5. Weekly calendar schedule blocks — TODO: fetch from backend
  const [scheduleBlocks, setScheduleBlocks] = useState<{ id: string; dayOfWeek: number; startTime: string; endTime: string; rate: number }[]>([]);

  // 6. Bank Beneficiary — TODO: fetch from backend
  const [activeBank, setActiveBank] = useState({ name: '-', accNo: '-', holder: '-' });

  // ---- Fetch parking spots from backend ----
  // Re-fetch whenever the user returns to the dashboard or availability view
  // so that admin updates (approvals/rejections) are reflected immediately.
  useEffect(() => {
    if (activeView !== 'dashboard' && activeView !== 'availability') return;

    async function fetchMyParking() {
      setBaysLoading(true);
      try {
        const token = user?.token ?? '';
        if (!token) { setBaysLoading(false); return; }

        const res = await fetch(`${API_BASE}/parking/my-parking`, {
          headers: { Authorization: `Bearer ${token}` },
        });
        if (!res.ok) { setBaysLoading(false); return; }

        const data = await res.json();
        if (data.success && data.data) {
          const mapped: ParkingBay[] = data.data.map((ps: any) => ({
            id: `b-${ps.parkingSpotId}`,
            propertyName: `Property #${ps.propertyId}`,
            stationName: '-',
            bayNumber: ps.parkingLabel ?? `Spot #${ps.parkingSpotId}`,
            level: '-',
            status: ps.isPublished ? 'Active' as const :
                    ps.verificationStatus === 'Rejected' ? 'Rejected' as const :
                    'Pending Verification' as const,
            hourlyRate: ps.dailyRate ?? 0,
            verificationRequestId: ps.verificationRequestId,
            verificationSubmittedAt: ps.verificationSubmittedAt,
          }));
          setBays(mapped);
        }
      } catch { /* API not available */ }
      finally { setBaysLoading(false); }
    }
    fetchMyParking();
  }, [user?.token, activeView]);

  // Sync notifications to localStorage
  useEffect(() => { saveNotifications(notifications); }, [notifications]);

  // --- INTERACTION ACTION HANDLERS ---

  // Handle wallet withdrawals
  const handleWithdrawFunds = (amount: number) => {
    // 1. Subtract balance
    setWalletBalance(prev => prev - amount);

    // 2. Append transaction row to the tables list
    const today = new Date();
    const dateStr = today.getDate().toString().padStart(2, '0') + ' ' + 
                    today.toLocaleString('en-US', { month: 'short' }) + ' ' + 
                    today.getFullYear();

    const newWithdrawalRow: Booking = {
      id: `w-${Math.floor(Math.random() * 1000)}`,
      date: dateStr,
      renterPlate: 'WITHDRAWAL',
      renterName: 'Direct Bank Settlement',
      bayId: 'N/A',
      bayInfo: 'Fund Settlement',
      propertyName: 'Platform Wallet',
      duration: `${activeBank.name.split(' ')[0]} Transfer`,
      totalEarned: -amount,
      commissionPaid: 0,
      status: 'Upcoming' // Will show pending styled matching 'Upcoming'
    };

    setBookings(prev => [newWithdrawalRow, ...prev]);

    // 3. Create a success notification
    const newNotif: Notification = {
      id: `n-${Date.now()}`,
      title: 'Withdrawal Pending',
      message: `RM ${amount.toFixed(2)} requested for transfer to ${activeBank.name.split(' ')[0]} account Ending in ${activeBank.accNo.slice(-4)}.`,
      time: 'Just now',
      unread: true,
      type: 'payment'
    };
    setNotifications(prev => [newNotif, ...prev]);
  };

  // Resolve disputes (Acknowledge overstay penalty and credit the wallet!)
  const handleResolveDispute = (bookingId: string) => {
    setBookings(prev => prev.map(b => {
      if (b.id === bookingId) {
        return { ...b, status: 'Completed', totalEarned: b.totalEarned + 3.60 }; // Adds penalty RM 3.60
      }
      return b;
    }));

    // Credit overstay fine to balance
    setWalletBalance(prev => prev + 3.60);

    // Alert notification
    const newNotif: Notification = {
      id: `n-${Date.now()}`,
      title: 'Dispute Resolved & Paid',
      message: `Overstay penalty of RM 3.60 has been credited to your withdrawable wallet balance.`,
      time: 'Just now',
      unread: true,
      type: 'payment'
    };
    setNotifications(prev => [newNotif, ...prev]);
  };

  // Add Availability slot
  const handleAddScheduleSlot = (block: { dayOfWeek: number; startTime: string; endTime: string; rate: number }) => {
    const newBlock = {
      id: `sc-${Date.now()}`,
      dayOfWeek: block.dayOfWeek,
      startTime: block.startTime,
      endTime: block.endTime,
      rate: block.rate
    };
    setScheduleBlocks(prev => [...prev, newBlock]);
  };

  // Remove availability block
  const handleRemoveScheduleSlot = (blockId: string) => {
    setScheduleBlocks(prev => prev.filter(b => b.id !== blockId));
  };

  // Block All calendar slots (IoT lockout actuation!)
  const handleBlockAllSchedule = () => {
    setScheduleBlocks([]);
  };

  // Config parking: upload images, schedule, pricing → POST /api/parking/config-parking/{id}
  const handleConfigParking = async (
    parkingSpotId: string,
    images: File[],
    dayType: string,
    startTime: string,
    endTime: string,
    effectiveFrom: string,
    effectiveUntil: string,
    monthlyRate?: number,
    dailyRate?: number
  ): Promise<boolean> => {
    try {
      const token = user?.token ?? '';
      if (!token) return false;

      const formData = new FormData();
      images.forEach((file) => formData.append('parkingImage', file));
      formData.append('dayType', dayType);
      formData.append('startTime', startTime);
      formData.append('endTime', endTime);
      formData.append('effectiveFrom', effectiveFrom);
      formData.append('effectiveUntil', effectiveUntil);
      if (monthlyRate !== undefined) formData.append('monthlyPrice', monthlyRate.toString());
      if (dailyRate !== undefined) formData.append('dailyRate', dailyRate.toString());

      // Extract numeric ID from "b-{id}" format
      const numericId = parkingSpotId.replace('b-', '');

      const res = await fetch(`${API_BASE}/parking/config-parking/${numericId}`, {
        method: 'POST',
        headers: { Authorization: `Bearer ${token}` },
        body: formData,
      });

      const data = await res.json();
      return data.success === true;
    } catch {
      return false;
    }
  };

  // Register New Parking Spot Near Transit Stations
  // Property creation is now handled by POST /api/property/create-property in PropertyOnboarding
  const handleOnboardProperty = (property: {
    propertyName: string;
    stationName: string;
    bayNumber: string;
    level: string;
    docName: string;
  }) => {
    // TODO: Refresh bays list from backend after successful property creation
    // const newBay = await fetch('/api/parking-spots', { ... })

    const newNotif: Notification = {
      id: `n-${Date.now()}`,
      title: 'Registration Submitted',
      message: `${property.propertyName} (${property.bayNumber}) submitted for admin verification.`,
      time: 'Just now',
      unread: true,
      type: 'system'
    };
    setNotifications(prev => [newNotif, ...prev]);
  };

  // Save payout Bank Account settings
  const handleSaveBank = (bankDetails: { name: string; accNo: string; holder: string }) => {
    setActiveBank(bankDetails);
  };

  // Clear unread notification badge count
  const handleMarkAllRead = () => {
    setNotifications(prev => prev.map(n => ({ ...n, unread: false })));
  };

  return (
    <div className="page-shell font-sans text-[#1d1d1f] flex">
      {/* Responsive Sidebar */}
      <Sidebar 
        activeView={activeView} 
        onViewChange={setActiveView} 
        isOpen={isSidebarOpen}
        setIsOpen={setIsSidebarOpen}
      />

      {/* Main content viewport wrapper */}
      <div className="flex-1 flex flex-col lg:ml-64 min-h-screen pb-16 lg:pb-0">
        {/* Top Navbar */}
        <Header
          notifications={notifications}
          onMarkAllRead={handleMarkAllRead}
          onToggleSidebar={() => setIsSidebarOpen(!isSidebarOpen)}
        />

        {/* Render View Panels */}
        <main className="flex-1 p-4 md:p-8 max-w-7xl w-full mx-auto">
          <PageTransition transitionKey={activeView}>
          {activeView === 'dashboard' && (
            <DashboardHome 
              walletBalance={walletBalance} 
              onWithdraw={handleWithdrawFunds}
              bookings={bookings}
              bays={bays}
              baysLoading={baysLoading}
              activeBank={activeBank}
              onResolveDispute={handleResolveDispute}
            />
          )}

          {activeView === 'availability' && (
            <AvailabilityScheduler 
              bays={bays}
              scheduleBlocks={scheduleBlocks}
              onAddBlock={handleAddScheduleSlot}
              onRemoveBlock={handleRemoveScheduleSlot}
              onBlockAll={handleBlockAllSchedule}
              onConfigParking={handleConfigParking}
            />
          )}

          {activeView === 'registration' && (
            <PropertyOnboarding 
              onOnboardProperty={handleOnboardProperty}
            />
          )}

          {activeView === 'settings' && (
            <SettingsPanel 
              bank={activeBank}
              onSaveBank={handleSaveBank}
            />
          )}

          {activeView === 'tickets' && (
            <SupportTickets />
          )}
          </PageTransition>
        </main>

        {/* Global Footer */}
        <footer className="bg-white border-t border-slate-200 py-5 text-center text-slate-400 text-xs">
          <p className="font-medium">&copy; 2026 ParkJom Malaysia. All Rights Reserved.</p>
        </footer>
      </div>

      <BottomNav
        items={[
          { id: 'dashboard', icon: LayoutDashboard, label: 'Overview' },
          { id: 'availability', icon: CalendarDays, label: 'Schedule' },
          { id: 'registration', icon: PlusSquare, label: 'Register' },
          { id: 'tickets', icon: ClipboardList, label: 'Support' },
          { id: 'settings', icon: Sliders, label: 'Settings' },
        ]}
        activeId={activeView}
        onChange={setActiveView}
      />
    </div>
  );
}

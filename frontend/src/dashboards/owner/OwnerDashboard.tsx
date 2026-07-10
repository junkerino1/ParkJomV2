import React, { useState } from 'react';
import { LayoutDashboard, CalendarDays, PlusSquare, ClipboardList, Sliders } from 'lucide-react';
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

export default function OwnerDashboard() {
  // Navigation View Router
  const [activeView, setActiveView] = useState('dashboard');
  const [isSidebarOpen, setIsSidebarOpen] = useState(false);

  // 1. Wallet Balance (RM)
  const [walletBalance, setWalletBalance] = useState(450.00);

  // 2. Active Registered Parking Bays
  const [bays, setBays] = useState<ParkingBay[]>([
    { id: 'b-104', propertyName: 'Wangsa Latian Condominium', stationName: 'Wangsa Maju LRT', bayNumber: 'Bay 104', level: 'Level 3 (Basement A)', status: 'Active', hourlyRate: 2.00 },
    { id: 'b-208', propertyName: 'Gombak Height Residence', stationName: 'Gombak LRT', bayNumber: 'Bay 208', level: 'Level 1 (Ground)', status: 'Active', hourlyRate: 2.00 },
  ]);

  // 3. Recent Bookings History (realistic dummy data in Malaysian context)
  const [bookings, setBookings] = useState<Booking[]>([
    { id: 'bk-01', date: '05 Jul 2026', renterPlate: 'VCS 8824', renterName: 'Mohd Fadhil', bayId: 'b-104', bayInfo: 'Bay 104', duration: '08:00 AM - 06:00 PM (10.0 hrs)', totalEarned: 18.00, commissionPaid: 2.00, status: 'Completed' },
    { id: 'bk-02', date: '04 Jul 2026', renterPlate: 'WRA 9031', renterName: 'Chong Wei Min', bayId: 'b-104', bayInfo: 'Bay 104', duration: '09:00 AM - 05:00 PM (8.0 hrs)', totalEarned: 14.40, commissionPaid: 1.60, status: 'Completed' },
    { id: 'bk-03', date: '03 Jul 2026', renterPlate: 'ALL 5110', renterName: 'Arul Dev', bayId: 'b-208', bayInfo: 'Bay 208', duration: '08:00 AM - 06:00 PM (10.0 hrs)', totalEarned: 20.00, commissionPaid: 2.00, status: 'Disputed', disputeReason: 'Vehicle overstayed by 23 minutes. ESP32 ultrasonic telemetry logged physical presence past reservation block.' },
    { id: 'bk-04', date: '02 Jul 2026', renterPlate: 'VDE 6729', renterName: 'Siti Aminah', bayId: 'b-104', bayInfo: 'Bay 104', duration: '07:30 AM - 04:30 PM (9.0 hrs)', totalEarned: 16.20, commissionPaid: 1.80, status: 'Completed' },
    { id: 'bk-05', date: '30 Jun 2026', renterPlate: 'PMD 3020', renterName: 'Tan Kok Seng', bayId: 'b-208', bayInfo: 'Bay 208', duration: '08:00 AM - 05:00 PM (9.0 hrs)', totalEarned: 16.20, commissionPaid: 1.80, status: 'Completed' },
  ]);

  // 4. Notifications Dropdown list
  const [notifications, setNotifications] = useState<Notification[]>([
    { id: 'n-1', title: 'Withdrawal Success', message: 'RM 120.00 transferred successfully to your Maybank Account.', time: '1h ago', unread: true, type: 'payment' },
    { id: 'n-2', title: 'New Booking Confirmed', message: 'Mohd Fadhil booked Bay 104 today 08:00 AM - 06:00 PM.', time: '3h ago', unread: true, type: 'booking' },
    { id: 'n-3', title: 'Smart Spot Disputed', message: 'Dispute raised for Bay 208. Renter ALL 5110 overstayed over booking time limit.', time: '1d ago', unread: false, type: 'dispute' },
  ]);

  // 5. Weekly calendar schedule blocks
  // (dayOfWeek: 0 = Sun, 1-5 = Mon-Fri, 6 = Sat)
  const [scheduleBlocks, setScheduleBlocks] = useState([
    { id: 'sc-1', dayOfWeek: 1, startTime: '08:00', endTime: '18:00', rate: 2.00 },
    { id: 'sc-2', dayOfWeek: 2, startTime: '08:00', endTime: '18:00', rate: 2.00 },
    { id: 'sc-3', dayOfWeek: 3, startTime: '08:00', endTime: '18:00', rate: 2.00 },
    { id: 'sc-4', dayOfWeek: 4, startTime: '08:00', endTime: '18:00', rate: 2.00 },
    { id: 'sc-5', dayOfWeek: 5, startTime: '08:00', endTime: '18:00', rate: 2.00 },
  ]);

  // 6. Configured Bank Beneficiary Payout (Defaults)
  const [activeBank, setActiveBank] = useState({
    name: 'Malayan Banking Berhad (Maybank)',
    accNo: '114012345678',
    holder: 'CHAW CHUN JIA'
  });

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

  // Register New Parking Spot Near Transit Stations
  const handleOnboardProperty = (property: {
    propertyName: string;
    stationName: string;
    bayNumber: string;
    level: string;
    docName: string;
  }) => {
    const newBay: ParkingBay = {
      id: `b-${Date.now()}`,
      propertyName: property.propertyName,
      stationName: property.stationName,
      bayNumber: property.bayNumber,
      level: property.level,
      status: 'Pending Verification',
      hourlyRate: 2.00,
      verificationDocName: property.docName,
      verificationProgress: 0
    };

    setBays(prev => [...prev, newBay]);

    // Push notification
    const newNotif: Notification = {
      id: `n-${Date.now()}`,
      title: 'Registration Pending Review',
      message: `Your bay ${property.bayNumber} at ${property.propertyName} was successfully uploaded. Administrator is verifying compliance papers.`,
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

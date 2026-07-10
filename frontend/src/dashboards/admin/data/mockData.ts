import { IoTBollard, ListingRequest, OwnerPayout, Transaction, OverstayRecord, SupportTicket, PlatformStats } from '../types';

export const initialStats: PlatformStats = {
  totalRevenue: 34850.50,
  platformCommission: 5227.58,
  activeBookings: 18,
  totalUsers: 1420,
  onlineBollardsRate: 94.5,
  pendingListingsCount: 4,
  openDisputesCount: 3,
  activeOverstaysCount: 2
};

export const initialBollards: IoTBollard[] = [
  { id: "BLD-SS15-01", bayNumber: "Bay 12", location: "SS15 Courtyard, Subang Jaya", status: "online", batteryLevel: 89, barrierState: "raised", rssi: -64, lastHeartbeat: "2 mins ago", firmwareVersion: "v2.1.4-esp32" },
  { id: "BLD-SS15-02", bayNumber: "Bay 13", location: "SS15 Courtyard, Subang Jaya", status: "online", batteryLevel: 94, barrierState: "lowered", rssi: -61, lastHeartbeat: "Just now", firmwareVersion: "v2.1.4-esp32" },
  { id: "BLD-KLCC-09", bayNumber: "Bay B2-44", location: "KLCC Parkview Residences", status: "online", batteryLevel: 12, barrierState: "raised", rssi: -78, lastHeartbeat: "5 mins ago", firmwareVersion: "v2.1.2-esp32" },
  { id: "BLD-CHIN-05", bayNumber: "Bay A-03", location: "Chinatown Lot 10, KL", status: "offline", batteryLevel: 0, barrierState: "raised", rssi: -105, lastHeartbeat: "3 hours ago", firmwareVersion: "v2.1.0-esp32" },
  { id: "BLD-BANG-03", bayNumber: "Bay G-15", location: "Bangsar Heights Condominium", status: "online", batteryLevel: 76, barrierState: "lowered", rssi: -70, lastHeartbeat: "1 min ago", firmwareVersion: "v2.1.4-esp32" },
  { id: "BLD-CHER-22", bayNumber: "Bay P3-11", location: "Cheras Leisure Mall Block C", status: "maintenance", batteryLevel: 45, barrierState: "transitioning", rssi: -72, lastHeartbeat: "12 mins ago", firmwareVersion: "v2.1.3-esp32" }
];

export const initialListings: ListingRequest[] = [
  { id: "LST-2026-001", ownerName: "Tan Kah Seng", ownerEmail: "tan.ks@gmail.com", location: "SS15 Courtyard Apartments, Subang Jaya", bayNumber: "Bay A-302", hourlyRate: 3.50, documents: { titleDeed: "Tan_KS_Deed_SS15_A302.pdf (Registered Strata Title - Block A, Floor 3, Lot 302, Subang Jaya)", utilityBill: "Tan_KS_Syabas_Bill_SS15_May2026.pdf (Water Utility matching address Tan Kah Seng)", identityCard: "IC_940812145523_TanKS.pdf" }, submittedAt: "2026-07-04T14:30:00Z", status: "pending" },
  { id: "LST-2026-002", ownerName: "Sarah Amira", ownerEmail: "sarah.amira@outlook.com", location: "Bangsar Heights Condominium, KL", bayNumber: "Bay B1-14", hourlyRate: 4.00, documents: { titleDeed: "StrataTitle_Sarah_Bangsar_B1-14.pdf (Strata Ownership Strata Cert 94432/90)", utilityBill: "TNB_Bill_Sarah_Bangsar_June2026.pdf (TNB Electricity showing matching customer name)", identityCard: "IC_971104106512_SarahAmira.pdf" }, submittedAt: "2026-07-04T16:15:00Z", status: "pending" },
  { id: "LST-2026-003", ownerName: "Muralitharan Pillay", ownerEmail: "murali_pillay@yahoo.com", location: "Chinatown Square Residences, KL", bayNumber: "Bay CP-88", hourlyRate: 5.00, documents: { titleDeed: "Chinatown_Residences_Deed_Murali.pdf (Deed of Assignment Lot CP-88)", utilityBill: "WaterBill_Chinatown_Murali.pdf (Water Joint Billing Receipt)", identityCard: "IC_830115086119_Murali.pdf" }, submittedAt: "2026-07-05T01:20:00Z", status: "pending" },
  { id: "LST-2026-004", ownerName: "Wong Chee Keong", ownerEmail: "ckeong.wong@gmail.com", location: "Main Place Residence, USJ 21", bayNumber: "Bay P4-99", hourlyRate: 3.00, documents: { titleDeed: "Deed_WongCK_MainPlace_P4-99.pdf (Strata Owner Title Ref: 4421/USJ)", utilityBill: "AstroBill_WongCK_MainPlace_May.pdf (Astro Media/Internet Utility matching name)", identityCard: "IC_780924105231_WongCK.pdf" }, submittedAt: "2026-07-05T07:45:00Z", status: "pending" },
  { id: "LST-2026-000", ownerName: "David Miller", ownerEmail: "david.m@gmail.com", location: "Mont Kiara Pines Condominium", bayNumber: "Bay MK-405", hourlyRate: 6.00, documents: { titleDeed: "MK_Pines_Deed_DavidM.pdf", utilityBill: "MK_Pines_TNB_DavidM.pdf", identityCard: "IC_Passport_DavidM.pdf" }, submittedAt: "2026-07-03T10:00:00Z", status: "approved" }
];

export const initialPayouts: OwnerPayout[] = [
  { id: "PAY-001", ownerName: "Wong Chee Keong", email: "ckeong.wong@gmail.com", bankName: "Maybank", accountNumber: "114012445582", amount: 320.50, requestedAt: "2026-07-04T08:00:00Z", status: "pending" },
  { id: "PAY-002", ownerName: "Tan Kah Seng", email: "tan.ks@gmail.com", bankName: "CIMB Bank", accountNumber: "800344551219", amount: 175.00, requestedAt: "2026-07-04T12:00:00Z", status: "pending" },
  { id: "PAY-003", ownerName: "Lim Bee Ling", email: "beeling.l@gmail.com", bankName: "Public Bank", accountNumber: "3122459912", amount: 540.20, requestedAt: "2026-07-03T09:30:00Z", status: "completed" }
];

export const initialTransactions: Transaction[] = [
  { id: "TXN-9021", bookingId: "BKG-2026-1029", userEmail: "jason.lee@gmail.com", ownerName: "Tan Kah Seng", location: "SS15 Courtyard, Subang Jaya", amount: 14.00, commission: 2.10, status: "completed", timestamp: "2026-07-05T08:12:00Z" },
  { id: "TXN-9022", bookingId: "BKG-2026-1030", userEmail: "amira.razak@hotmail.com", ownerName: "Sarah Amira", location: "Bangsar Heights Condominium", amount: 24.00, commission: 3.60, status: "completed", timestamp: "2026-07-05T07:45:00Z" },
  { id: "TXN-9023", bookingId: "BKG-2026-1025", userEmail: "nicholas.chew@gmail.com", ownerName: "David Miller", location: "Mont Kiara Pines", amount: 18.00, commission: 2.70, status: "completed", timestamp: "2026-07-05T06:30:00Z" },
  { id: "TXN-9024", bookingId: "BKG-2026-1011", userEmail: "bobby.jones@yahoo.com", ownerName: "Wong Chee Keong", location: "Main Place Residence", amount: 9.00, commission: 1.35, status: "refunded", timestamp: "2026-07-04T18:20:00Z" }
];

export const initialOverstays: OverstayRecord[] = [
  { id: "OVR-001", bookingId: "BKG-2026-1024", vehicleNo: "WXC 8892", userPhone: "+6012-3456789", location: "SS15 Courtyard, Subang Jaya", bayNumber: "Bay 12", scheduledEndTime: "2026-07-05T08:00:00-07:00", currentOverstayMinutes: 36, calculatedPenalty: 12.00, status: "detected" },
  { id: "OVR-002", bookingId: "BKG-2026-1019", vehicleNo: "VAK 501", userPhone: "+6019-9988112", location: "KLCC Parkview Residences", bayNumber: "Bay B2-44", scheduledEndTime: "2026-07-05T07:30:00-07:00", currentOverstayMinutes: 66, calculatedPenalty: 22.00, status: "warning_sent" }
];

export const initialTickets: SupportTicket[] = [
  { id: "TKT-101", bookingId: "BKG-2026-1024", userRole: "owner", userName: "Tan Kah Seng", email: "tan.ks@gmail.com", subject: "Bollard not lowering (SS15 Bay 12)", category: "hardware", description: "The incoming driver is trying to park but the bollard stays raised despite reservation being active. Please issue manual command or reboot.", status: "open", createdAt: "2026-07-05T08:20:00Z", chatHistory: [{ sender: "user", message: "Hi support, a driver has booked SS15 Bay 12 but the barrier is stuck raised. It's not receiving the Bluetooth command.", timestamp: "08:20 AM" }] },
  { id: "TKT-102", bookingId: "BKG-2026-1011", userRole: "driver", userName: "Bobby Jones", email: "bobby.jones@yahoo.com", subject: "Accidental booking of wrong bay", category: "payment", description: "I booked Main Place Residence Bay P4-99 by mistake, but I couldn't enter the residential building's security gate as security didn't allow me. I had to leave immediately. Please refund.", status: "pending", createdAt: "2026-07-04T18:15:00Z", chatHistory: [{ sender: "user", message: "I want to apply for refund because security refused visitor entry to USJ Main Place. The booking was never used.", timestamp: "06:15 PM" }, { sender: "admin", message: "We are checking with the property owner. If they verify you did not access the parking, we will refund you.", timestamp: "06:45 PM" }, { sender: "user", message: "Thank you, please do. I waited outside for 20 minutes.", timestamp: "06:50 PM" }] },
  { id: "TKT-103", bookingId: "BKG-2026-0994", userRole: "driver", userName: "Farhan Daniel", email: "farhan.d@gmail.com", subject: "Double charged on wallet top-up", category: "payment", description: "My wallet top-up failed first, then I completed it again. However, my bank statement shows two deductions of RM50.00. Please refund the duplicate transaction.", status: "resolved", createdAt: "2026-07-03T11:45:00Z", chatHistory: [{ sender: "user", message: "I top up my wallet with credit card and it got charged twice. Only RM50 reflects in my balance.", timestamp: "11:45 AM" }, { sender: "admin", message: "We have audited the payment gateway logs and identified a gateway sync latency issue. The duplicate charge has been voided.", timestamp: "02:15 PM" }, { sender: "user", message: "Got the notification. Refund received, thank you!", timestamp: "03:00 PM" }] }
];

export const initialActivityLogs = [
  { id: "LOG-1", type: "bollard_state", message: "Bollard BLD-SS15-02 lowered automatically (User BKG-2026-1030 arrived)", timestamp: "07:45 AM", user: "Amira Razak" },
  { id: "LOG-2", type: "system", message: "Daily Payout processing cycle completed successfully", timestamp: "07:00 AM", user: "System Cron" },
  { id: "LOG-3", type: "governance", message: "Listing LST-2026-000 approved by Operator", timestamp: "06:12 AM", user: "Admin (Ch Chun Jia)" },
  { id: "LOG-4", type: "overstay", message: "Vehicle WXC 8892 flag: Overstay detected in SS15 Bay 12", timestamp: "08:01 AM", user: "Enforcement Engine" },
  { id: "LOG-5", type: "dispute", message: "Dispute TKT-101 raised by Owner Tan Kah Seng", timestamp: "08:20 AM", user: "Tan Kah Seng" }
];

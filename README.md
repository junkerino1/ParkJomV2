# ParkJom V2 — Smart Transit Parking Platform

**ParkJom** is a peer-to-peer parking platform for the Klang Valley, Malaysia. It connects condominium/residential parking spot owners with commuters near LRT/MRT stations, secured by IoT smart bollards.

> "Secure your LRT parking before you drive."

---

## Table of Contents

- [Architecture Overview](#architecture-overview)
- [Tech Stack](#tech-stack)
- [What's Done ✅](#whats-done-)
- [What's Partially Done ⚠️](#whats-partially-done-)
- [What's Missing / Not Done ❌](#whats-missing--not-done-)
- [How to Run](#how-to-run)
- [Project Structure](#project-structure)

---

## Architecture Overview

The solution contains **two .NET 9.0 projects** that are complementary:

### 1. `ParkJomV2/` — Main Production API Skeleton

The intended production backend. It has a **complete SQL Server database schema** (16 models + EF Core migrations) but **zero API endpoints**.

| Layer | Status |
|-------|--------|
| Database Schema (EF Core) | ✅ All 16 tables migrated |
| API Controllers | ❌ Only `UserController` exists — empty |
| Authentication | ❌ None configured |
| Frontend | ❌ None |

### 2. `ParkJomV2.Web` — Working Web API

A **functional web API** (in `src/ParkJomV2.Web/`) with a complete React SPA frontend (in `frontend/`):
- `src/ParkJomV2.Web/` — lightweight .NET 9 API (auth + nearby search)
- `frontend/` — complete React SPA (3 dashboards + landing page)

| Layer | Status |
|-------|--------|
| ASP.NET Web API (`src/ParkJomV2.Web/`) | ✅ Auth + Nearby Search API |
| Auth (Google OAuth + JWT) | ✅ Complete flow |
| React SPA (`frontend/`) | ✅ 3 dashboards + Landing Page |
| Database | ⚠️ In-memory mock data only |

**The intended merge path:** The `ParkJomV2.Web` API's in-memory stores are marked with TODOs to be replaced by `ParkJomV2`'s EF Core `DbContext`.

---

## Tech Stack

### Backend

| Technology | ParkJomV2 (DB Schema) | ParkJomV2.Web (API) |
|-----------|------------------------|----------------------|
| **Framework** | .NET 9.0 Web API | .NET 9.0 Web API |
| **Database** | SQL Server (EF Core) | In-memory (mock) |
| **Auth** | — | Google OAuth + JWT Bearer |
| **ORM** | Entity Framework Core 9 | — |
| **Port** | 5276 (HTTP) / 7250 (HTTPS) | 5176 (HTTP) / 7227 (HTTPS) |

### Frontend (shared)

| Category | Libraries |
|----------|-----------|
| **Core** | React 19, TypeScript 5.8, Vite 6 |
| **Styling** | TailwindCSS 4 |
| **Routing** | React Router 7 |
| **Maps** | Leaflet + react-leaflet |
| **Animation** | Motion (Framer Motion) |
| **Charts** | Recharts |
| **Auth** | @react-oauth/google |
| **Deploy** | Firebase Hosting |

---

## What's Done ✅

### Database Schema (ParkJomV2)

16 fully modeled entities with EF Core relationships and constraints:

| Model | Purpose |
|-------|---------|
| **User** | Full user profile with auth fields (RefreshToken, EmailVerifiedAt, LastLoginAt) |
| **Property** | Condominium/apartment with GPS coords + nearest transit station |
| **ParkingSpot** | Individual bays linked to property, owner, IoT device, pricing |
| **Booking** | Full booking lifecycle with unique reference, status tracking |
| **Vehicle** | User vehicles with number plate |
| **Wallet** | 1:1 per user with balance + on-hold tracking |
| **Transaction** | Top-up / Payment / Refund / Withdrawal ledger |
| **IoTDevice** | ESP32 serial, firmware, heartbeat tracking |
| **IoTStatusLog** | Device telemetry history |
| **AccessLog** | Booking access audit trail |
| **ParkingSpotImage** | Multi-image support with display order |
| **MediaFile** | Cloudinary-compatible media storage |
| **ParkingVerificationRequest** | Document-based property verification workflow |
| **VerificationDocument** | SPA/Utility Bill/IC uploads |
| **Review** | 1-5 star rating with owner reply |
| **Favorite** | Saved spots |

11 enums covering all domain states.

### Frontend (ParkJomV2.Web)

- **Landing Page** — Apple-style marketing hero, how-it-works, features, stats, FAQ accordion, footer
- **Login** — Google OAuth sign-in
- **Commuter Dashboard** — 5-tab mobile-first UI: Home, Active Booking, Wallet, Profile, Map
  - Interactive Leaflet map with GTFS rail lines overlay
  - Station search + Haversine distance filtering
  - Active booking countdown timer, GPS zone verification
  - IoT bollard control (QR scan, raise/lower animation)
  - Wallet top-up, vehicle management, booking history
  - Parking detail page with OSRM walking distance calculator
- **Owner Dashboard** — Earnings overview, IoT device monitor
  - Property onboarding with Nominatim geocoding + nearest station auto-detect
  - Availability scheduler (per-day-of-week)
  - Settings panel (bank account, auto-payout)
  - Support tickets with dispute timeline
- **Admin Dashboard** — Platform stats, listing governance, IoT health monitor
  - Financial settlement, overstay enforcement
  - Support/dispute resolution, system audit log
  - System configuration (commission %, grace period)
- **Nearby API** — Haversine-based with 14 simulated spots across 4 LRT/MRT stations

### Project Configuration

- Firebase hosting config (`.firebaserc`, `firebase.json`)
- Vite build pipeline (outputs to `src/ParkJomV2.Web/wwwroot/`)
- Service worker for PWA support

---

## What's Partially Done ⚠️

| Item | Detail |
|------|--------|
| **ParkJomV2 Controllers** (`ParkJomV2/`) | Only `UserController.cs` exists — class body is completely empty |
| **Auth in ParkJomV2** | Database schema has `RefreshToken`, `PasswordHash`, `EmailVerifiedAt` fields but no auth endpoints or middleware |
| **ParkJomV2.Web Auth** (`src/ParkJomV2.Web/`) | Uses in-memory `Dictionary<string, ...>` for users — marked `TODO: replace with DbContext.Users` |
| **Nearby API** | Uses hardcoded anonymous objects — not querying a real database |
| **JWT Secret Keys** | Hardcoded in source code (`ParkJom_SuperSecret_Key_2026...`) — needs User Secrets / Key Vault |
| **Notification Model** | Commented out in `ApplicationDbContext.cs` |
| **READMEs** | Both `ParkJomV2/README.md` and root `README.md` were empty — this file now addresses that |
| **Google GenAI API Key** (`src/ParkJomV2.Web/`) | `GoogleGenAI:ApiKey` is hardcoded in `Program.cs` with a fallback — not in `appsettings.json` |
| **Phone Number** | Placeholder `+60 3-XXXX XXXX` on landing page |
| **GTFS Data** | Conversion script (`convert-gtfs.cjs`) exists but isn't integrated into any pipeline |

---

## What's Missing / Not Done ❌

### API Layer (ParkJomV2)

- **All CRUD endpoints** — Users, Properties, ParkingSpots, Bookings, Vehicles, Wallets, Transactions, IoT devices, Reviews, Favorites — **none implemented**
- **Authentication / Authorization** — No login, register, JWT middleware, role-based access
- **Swagger / OpenAPI** — No API documentation endpoint
- **CORS configuration**
- **Structured logging** (Serilog, Application Insights)

### Production Features

| Feature | Status |
|---------|--------|
| Real IoT device integration (ESP32 firmware, MQTT/WebSocket) | ❌ |
| Payment gateway (FPX, credit/debit card processing) | ❌ |
| Real-time updates (SignalR for live bollard/booking state) | ❌ |
| Push notifications (FCM / APNs) | ❌ |
| Email service (verification, receipts, reminders) | ❌ |
| SMS / WhatsApp notifications | ❌ |
| Rate limiting / API security | ❌ |
| Docker / containerization | ❌ |
| CI/CD pipeline | ❌ |
| Unit / integration tests (both projects) | ❌ |
| Admin user management API | ❌ |
| GTFS real-time schedule integration | ❌ |
| Multi-language support (BM, CN, Tamil) | ❌ |

### Other

- **No seeding data** for ParkJomV2's database
- **No database migration** beyond `InitialCreate`
- **No error handling middleware** in ParkJomV2

---

## How to Run

### Prerequisites

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Node.js 20+](https://nodejs.org/)
- SQL Server (for ParkJomV2) or skip (Testing uses in-memory)

### ParkJomV2.Web (Web API + Frontend)

```bash
# 1. Start the .NET Web API
cd src/ParkJomV2.Web
dotnet run

# 2. In a separate terminal, start the frontend dev server
cd frontend
npm install
npm run dev
```

The API runs on `http://localhost:5176` and the frontend on `http://localhost:3000`.

### ParkJomV2 Main API

```bash
cd ParkJomV2

# Update connection string in appsettings.json, then apply migration
dotnet ef database update

dotnet run
```

The API runs on `http://localhost:5276`.

### Build Frontend for Production

```bash
cd frontend
npm run build
# Output goes to src/ParkJomV2.Web/wwwroot/
# The .NET server will serve it as static files
```

---

## Project Structure

```
ParkJomV2/
├── .gitignore
├── ParkJomV2.sln
├── README.md                        ← You are here
│
├── ParkJomV2/                       # Production API Skeleton
│   ├── Program.cs                   # Minimal — DbContext + controllers only
│   ├── appsettings.json             # SQL Server connection string
│   ├── Controllers/
│   │   └── UserController.cs        # Empty (no endpoints)
│   ├── Data/
│   │   └── ApplicationDbContext.cs  # EF Core — 16 DbSets
│   ├── Models/                      # 16 entity models + 11 enums
│   ├── Migrations/                  # InitialCreate — full schema
│   └── Properties/launchSettings.json
│
├── data/                             # Shared data files
│   ├── mrt_lrt_stations.json         # Station GeoJSON
│   ├── kl_rail_lines.json            # GTFS rail lines
│   └── kl_rail_stops.json            # GTFS rail stops
│
├── frontend/                         # React SPA (Vite + TailwindCSS)
│   ├── package.json
│   ├── vite.config.ts
│   ├── tsconfig.json
│   ├── index.html
│   ├── public/                       # Static assets (images, icons, data, sw.js)
│   └── src/
│       ├── main.tsx
│       ├── App.tsx
│       ├── index.css
│       ├── declarations.d.ts
│       ├── contexts/AuthContext.tsx
│       ├── components/               # LandingPage, GoogleLoginButton, DashboardHeader, UI
│       └── dashboards/
│           ├── commuter/             # CommuterDashboard, ParkingDetail, CommuterMap
│           ├── owner/                # OwnerDashboard, PropertyOnboarding, Scheduler, etc.
│           └── admin/                # AdminDashboard, IoT monitor, Settlement, etc.
│
├── src/                              # Backend projects
│   └── ParkJomV2.Web/                # .NET 9 Web API (Auth + Nearby Search)
│       ├── ParkJomV2.Web.csproj
│       ├── Program.cs                # Full middleware: JWT, CORS, static files
│       ├── appsettings.json
│       ├── Properties/launchSettings.json
│       ├── Controllers/
│       │   └── AuthController.cs     # Google OAuth → JWT
│       ├── Helpers/
│       │   └── JwtHelper.cs          # JWT generation
│       └── Models/
│           └── GoogleLoginRequestDto.cs
│
├── ParkJomV2/                        # DB Schema project (EF Core)
│   ├── Program.cs                    # DbContext + controllers only
│   ├── Controllers/UserController.cs # Empty (no endpoints)
│   ├── Data/ApplicationDbContext.cs  # 16 DbSets
│   ├── Models/                       # 16 entity models + 11 enums
│   └── Migrations/                   # InitialCreate — full schema
```

---

*Built with .NET 9, React 19, and ❤️ for Klang Valley commuters.*

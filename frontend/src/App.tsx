import { Routes, Route, Navigate, useSearchParams } from 'react-router-dom';
import { motion } from 'motion/react';
import { useAuth } from './contexts/AuthContext';
import GoogleLoginButton from './components/GoogleLoginButton';
import LandingPage from './components/LandingPage';
import SplashScreen from './components/ui/SplashScreen';
import AdminDashboard from './dashboards/admin/AdminDashboard';
import OwnerDashboard from './dashboards/owner/OwnerDashboard';
import CommuterDashboard from './dashboards/commuter/CommuterDashboard';
import ParkingDetail from './dashboards/commuter/components/ParkingDetail';

export default function App() {
  const { isLoggedIn, loading, user } = useAuth();

  if (loading) {
    return <SplashScreen />;
  }

  if (!isLoggedIn) {
    return (
      <div className="font-sans text-[#1d1d1f] page-shell">
        <Routes>
          <Route path="/" element={<LandingPage />} />
          <Route path="/login" element={<LoginPage />} />
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </div>
    );
  }

  const rolePath =
    user?.role === 'Admin' ? '/admin' : user?.role === 'Owner' ? '/owner' : '/commuter';

  return (
    <div className="page-shell">
      <Routes>
        <Route path="/login" element={<LoginRedirect />} />
        {/* Redirect / to the user's actual role */}
        <Route path="/" element={<Navigate to={rolePath} replace />} />
        {/* Role-guarded routes */}
        <Route path="/admin" element={<RequireRole role="Admin"><AdminDashboard /></RequireRole>} />
        <Route path="/owner" element={<RequireRole role="Owner"><OwnerDashboard /></RequireRole>} />
        <Route path="/commuter" element={<RequireRole role="Commuter"><CommuterDashboard /></RequireRole>} />
        <Route path="/commuter/parking/:id" element={<RequireRole role="Commuter"><ParkingDetail /></RequireRole>} />
        <Route path="*" element={<Navigate to={rolePath} replace />} />
      </Routes>
    </div>
  );
}

/** Blocks access if the logged-in user doesn't match the required role. */
function RequireRole({ role, children }: { role: string; children: React.ReactNode }) {
  const { user } = useAuth();
  if (!user || user.role !== role) {
    const redirectTo =
      user?.role === 'Admin' ? '/admin' : user?.role === 'Owner' ? '/owner' : '/commuter';
    return <Navigate to={redirectTo} replace />;
  }
  return <>{children}</>;
}

function LoginPage() {
  return (
    <div className="min-h-screen bg-[#f5f5f7] flex items-center justify-center px-4">
      <motion.div
        initial={{ opacity: 0, y: 24, scale: 0.96 }}
        animate={{ opacity: 1, y: 0, scale: 1 }}
        transition={{ duration: 0.45, ease: [0.32, 0.72, 0, 1] }}
        className="bg-white rounded-[28px] border border-black/[0.06] shadow-[0_12px_40px_rgba(0,0,0,0.08)] p-8 max-w-sm w-full space-y-6 text-center"
      >
        <div className="w-16 h-16 rounded-[20px] bg-[#007AFF] flex items-center justify-center font-bold text-white text-2xl mx-auto shadow-lg">
          PJ
        </div>
        <div>
          <h1 className="text-2xl font-bold text-[#1d1d1f] tracking-[-0.02em]">Welcome to ParkJom</h1>
          <p className="text-[13px] text-[#6e6e73] mt-2 leading-relaxed">
            Malaysia&apos;s peer-to-peer transit parking platform
          </p>
        </div>
        <GoogleLoginButton />

        <a href="/" className="block text-[13px] text-[#007AFF] font-medium hover:underline transition">
          ← Back to Home
        </a>
      </motion.div>
    </div>
  );
}

function LoginRedirect() {
  const { user } = useAuth();

  // Role is ALWAYS from the database — URL params cannot override it
  const rolePath =
    user?.role === 'Admin' ? '/admin' : user?.role === 'Owner' ? '/owner' : '/commuter';

  return <Navigate to={rolePath} replace />;
}

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
  const { isLoggedIn, loading, setUser } = useAuth();

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

  return (
    <div className="page-shell">
      <Routes>
        <Route path="/login" element={<LoginRedirect />} />
        <Route path="/" element={<Navigate to="/commuter" replace />} />
        <Route path="/admin" element={<AdminDashboard />} />
        <Route path="/owner" element={<OwnerDashboard />} />
        <Route path="/commuter" element={<CommuterDashboard />} />
        <Route path="/commuter/parking/:id" element={<ParkingDetail />} />
      </Routes>
    </div>
  );
}

function LoginPage() {
  const { setUser } = useAuth();

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

        <div className="pt-3 border-t border-black/[0.06] space-y-3">
          <p className="text-[10px] text-[#8e8e93] uppercase tracking-wider font-semibold">Quick demo access</p>
          <div className="flex gap-2">
            {(['Commuter', 'Owner', 'Admin'] as const).map((role) => (
              <motion.button
                key={role}
                whileTap={{ scale: 0.96 }}
                onClick={() =>
                  setUser({
                    userId: 'demo-' + role.toLowerCase(),
                    email: `${role.toLowerCase()}@demo.parkjom`,
                    name: `Demo ${role}`,
                    picture: '',
                    role,
                    token: 'demo-token',
                  })
                }
                className="flex-1 py-2.5 bg-[#f5f5f7] hover:bg-[#ebebed] text-xs font-semibold text-[#1d1d1f] rounded-xl border border-black/[0.06] transition-colors"
              >
                {role}
              </motion.button>
            ))}
          </div>
        </div>

        <a href="/" className="block text-[13px] text-[#007AFF] font-medium hover:underline transition">
          ← Back to Home
        </a>
      </motion.div>
    </div>
  );
}

function LoginRedirect() {
  const { user } = useAuth();
  const [searchParams] = useSearchParams();
  const redirectParam = searchParams.get('redirect');

  if (redirectParam) {
    return <Navigate to={redirectParam} replace />;
  }

  const rolePath =
    user?.role === 'Admin' ? '/admin' : user?.role === 'Owner' ? '/owner' : '/commuter';
  return <Navigate to={rolePath} replace />;
}

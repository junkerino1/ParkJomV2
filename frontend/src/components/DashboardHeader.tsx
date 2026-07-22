import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { LogOut, Menu, Repeat } from 'lucide-react';
import { useAuth } from '../contexts/AuthContext';

export type DashboardRole = 'commuter' | 'owner' | 'admin';

interface DashboardHeaderProps {
  role: DashboardRole;
  onMenuClick?: () => void;
  showMenuButton?: boolean;
  /** Extra controls rendered before the user / sign-out area (e.g. notifications) */
  actions?: React.ReactNode;
  /** Optional status line shown on sm+ screens (admin: system status, owner: location) */
  statusText?: string;
  /** Optional badge next to title (owner: Active) */
  badge?: { label: string; variant?: 'success' | 'warning' | 'info' };
}

const ROLE_META: Record<
  DashboardRole,
  { portal: string; accent: string; homePath: string }
> = {
  commuter: { portal: 'Transit Parking', accent: '#007AFF', homePath: '/commuter' },
  owner: { portal: 'Owner Portal', accent: '#007AFF', homePath: '/owner' },
  admin: { portal: 'Admin Console', accent: '#0f1115', homePath: '/admin' },
};

const BADGE_STYLES = {
  success: 'bg-[#f0fdf4] text-[#16a34a]',
  warning: 'bg-[#fefce8] text-[#a16207]',
  info: 'bg-[#e8f0fe] text-[#007AFF]',
};

export default function DashboardHeader({
  role,
  onMenuClick,
  showMenuButton = false,
  actions,
  statusText,
  badge,
}: DashboardHeaderProps) {
  const { user, setUser, logout } = useAuth();
  const navigate = useNavigate();
  const meta = ROLE_META[role];
  const [showRoleMenu, setShowRoleMenu] = useState(false);

  const otherRoles: { label: string; role: DashboardRole; path: string; icon: string }[] = [];
  if (role !== 'commuter') otherRoles.push({ label: 'Commuter', role: 'commuter', path: '/commuter', icon: '🚗' });
  if (role !== 'owner') otherRoles.push({ label: 'Owner', role: 'owner', path: '/owner', icon: '🏠' });

  const switchRole = (newRole: DashboardRole, path: string) => {
    setShowRoleMenu(false);
    if (user) {
      setUser({ ...user, role: newRole === 'owner' ? 'Owner' : newRole === 'admin' ? 'Admin' : 'Commuter' });
    }
    navigate(path);
  };

  const handleSignOut = () => {
    logout();
    navigate('/');
  };

  const handleBrandClick = () => {
    navigate(meta.homePath);
  };

  const displayName = user ? `${user.firstName} ${user.lastName}`.trim() || user.email : '';
  const initials = (user?.firstName?.charAt(0) || user?.email?.charAt(0))?.toUpperCase() ?? '?';

  return (
    <header
      className="sticky top-0 z-50 shrink-0 glass-bar"
      style={{ paddingTop: 'env(safe-area-inset-top, 0px)' }}
    >
      <div className="max-w-screen-2xl mx-auto h-14 px-4 sm:px-6 lg:px-8 flex items-center justify-between gap-3">
        {/* Left: menu + brand */}
        <div className="flex items-center gap-2.5 min-w-0">
          {showMenuButton && onMenuClick && (
            <button
              type="button"
              onClick={onMenuClick}
              aria-label="Open menu"
              className="lg:hidden -ml-1 p-2 rounded-xl text-[#5f6368] hover:text-[#111] hover:bg-black/[0.04] active:bg-black/[0.06] transition-colors"
            >
              <Menu size={20} strokeWidth={2} />
            </button>
          )}

          <button
            type="button"
            onClick={handleBrandClick}
            className="flex items-center gap-2.5 min-w-0 group"
          >
            <div
              className="w-8 h-8 rounded-[10px] flex items-center justify-center font-extrabold text-white text-[13px] shadow-sm shrink-0 transition-transform group-active:scale-95"
              style={{ backgroundColor: role === 'admin' ? '#0f1115' : '#007AFF' }}
            >
              PJ
            </div>
            <div className="min-w-0 text-left">
              <div className="flex items-center gap-2">
                <span className="font-semibold text-[15px] text-[#111] tracking-[-0.02em] truncate">
                  ParkJom
                </span>
                {badge && (
                  <span
                    className={`hidden sm:inline text-[10px] font-semibold px-2 py-0.5 rounded-full uppercase tracking-wider shrink-0 ${
                      BADGE_STYLES[badge.variant ?? 'success']
                    }`}
                  >
                    {badge.label}
                  </span>
                )}
              </div>
              <p className="text-[11px] text-[#6e6e73] font-medium leading-none mt-0.5 truncate hidden sm:block">
                {meta.portal}
              </p>
            </div>
          </button>
        </div>

        {/* Center status — desktop only */}
        {statusText && (
          <div className="hidden md:flex items-center gap-2 text-[12px] font-medium text-[#5f6368] mx-4">
            <span className="w-1.5 h-1.5 rounded-full bg-[#34c759] shrink-0" />
            <span className="truncate">{statusText}</span>
          </div>
        )}

        {/* Right: actions + user + sign out */}
        <div className="flex items-center gap-1 sm:gap-2 shrink-0">
          {actions}

          {user && (
            <div className="hidden sm:flex items-center gap-2 pl-1">
              {user.picture ? (
                <img
                  src={user.picture}
                  alt=""
                  className="w-7 h-7 rounded-full object-cover ring-1 ring-black/[0.06]"
                />
              ) : (
                <span
                  className="w-7 h-7 rounded-full flex items-center justify-center text-[11px] font-bold ring-1 ring-black/[0.06]"
                  style={{
                    backgroundColor: '#e8f0fe',
                    color: '#007AFF',
                  }}
                >
                  {initials}
                </span>
              )}
              <span className="hidden lg:inline text-[13px] font-medium text-[#333] max-w-[120px] truncate">
                {displayName}
              </span>
            </div>
          )}

          {/* Role switcher — hidden on smallest screens */}
          {otherRoles.length > 0 && (
            <div className="relative hidden sm:block">
              <button
                type="button"
                onClick={() => setShowRoleMenu(!showRoleMenu)}
                aria-label="Switch role"
                className="flex items-center gap-1.5 px-2.5 py-2 rounded-xl text-[12px] font-medium
                  text-[#007AFF] hover:bg-[#e8f0fe] active:bg-[#d0e3fd] transition-colors"
              >
                <Repeat size={14} strokeWidth={2} />
                <span className="hidden lg:inline">Switch to {otherRoles[0]?.label}</span>
              </button>

              {showRoleMenu && (
                <>
                  <div className="fixed inset-0 z-10" onClick={() => setShowRoleMenu(false)} />
                  <div className="absolute right-0 top-full mt-1 z-20 bg-white rounded-xl border border-[#e8eaed]
                    shadow-[0_8px_28px_rgba(0,0,0,0.12)] py-1.5 min-w-[180px] overflow-hidden">
                    {otherRoles.map(({ label, role: r, path, icon }) => (
                      <button
                        key={r}
                        onClick={() => switchRole(r, path)}
                        className="w-full flex items-center gap-2.5 px-4 py-2.5 text-[13px] font-medium
                          text-[#1d1d1f] hover:bg-[#f5f5f7] transition-colors text-left"
                      >
                        <span className="text-[15px]">{icon}</span>
                        <div>
                          <span>{label}</span>
                          <span className="block text-[10px] text-[#8e8e93] font-normal">Switch portal</span>
                        </div>
                      </button>
                    ))}
                  </div>
                </>
              )}
            </div>
          )}

          <div className="hidden sm:block w-px h-5 bg-[#e8eaed] mx-0.5" />

          {/* Sign out — always visible; icon-only on mobile, label on sm+ */}
          <button
            type="button"
            onClick={handleSignOut}
            aria-label="Sign out"
            className="flex items-center gap-1.5 px-2.5 sm:px-3 py-2 rounded-xl text-[13px] font-medium text-[#5f6368] hover:text-[#111] hover:bg-black/[0.04] active:bg-black/[0.06] transition-colors"
          >
            <LogOut size={16} strokeWidth={2} className="shrink-0" />
            <span className="hidden sm:inline">Sign out</span>
          </button>
        </div>
      </div>
    </header>
  );
}

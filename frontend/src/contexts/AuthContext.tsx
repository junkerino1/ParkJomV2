import { createContext, useContext, useState, useEffect, type ReactNode } from 'react';

// ---- Types ----
interface AuthUser {
  userId: string;
  email: string;
  name: string;
  picture: string;
  role: string;
  token: string; // Backend JWT
}

interface AuthContextValue {
  user: AuthUser | null;
  setUser: (u: AuthUser | null) => void;
  isLoggedIn: boolean;
  loading: boolean;
  logout: () => void;
}

const AuthContext = createContext<AuthContextValue>({
  user: null,
  setUser: () => {},
  isLoggedIn: false,
  loading: true,
  logout: () => {},
});

// ---- Provider ----
export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<AuthUser | null>(null);
  const [loading, setLoading] = useState(true);

  // Restore login state from localStorage on page load (sync to avoid flicker)
  useEffect(() => {
    const saved = localStorage.getItem('parkjom_user');
    if (saved) {
      try {
        setUser(JSON.parse(saved));
      } catch {
        localStorage.removeItem('parkjom_user');
      }
    }
    setLoading(false);
  }, []);

  // Sync user state to localStorage when it changes
  useEffect(() => {
    if (user) {
      localStorage.setItem('parkjom_user', JSON.stringify(user));
    } else {
      localStorage.removeItem('parkjom_user');
    }
  }, [user]);

  const logout = () => {
    setUser(null);
  };

  return (
    <AuthContext.Provider value={{ user, setUser, isLoggedIn: !!user, loading, logout }}>
      {children}
    </AuthContext.Provider>
  );
}

// ---- Hook ----
export const useAuth = () => useContext(AuthContext);

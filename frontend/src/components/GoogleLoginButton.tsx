import { GoogleLogin, type CredentialResponse } from '@react-oauth/google';
import { useAuth } from '../contexts/AuthContext';

// Backend API URL (Vite proxy in dev, VITE_API_BASE env or fallback in production)
const API_BASE = import.meta.env.VITE_API_BASE || '/api';

export default function GoogleLoginButton() {
  const { setUser } = useAuth();

  // ---- Login success: send Google ID Token to backend for verification ----
  const handleGoogleSuccess = async (credentialResponse: CredentialResponse) => {
    const { credential } = credentialResponse; // ID Token from Google

    if (!credential) {
      console.error('No credential received from Google');
      return;
    }

    try {
      // Send ID Token to ASP.NET backend for validation & user creation/lookup
      const res = await fetch(`${API_BASE}/auth/google-login`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ idToken: credential }),
      });

      if (!res.ok) {
        const errText = await res.text();
        throw new Error(`Backend login failed: ${errText}`);
      }

      // Backend returns custom JWT + user info
      const data = await res.json();

      // Decode user info from backend JWT (simple payload decode)
      const payloadBase64 = data.token.split('.')[1];
      const payloadJson = JSON.parse(atob(payloadBase64));

      const user = {
        userId: payloadJson.sub,
        email: payloadJson.email,
        name: payloadJson.name,
        picture: payloadJson.picture ?? '',
        role: payloadJson.role ?? 'Commuter',
        token: data.token,
      };

      // Store in global state + localStorage
      setUser(user);
      console.log('✅ Google login success:', user.name, '| Role:', user.role);
    } catch (err: any) {
      console.error('❌ Google login failed:', err.message);
      // If backend is not deployed, prompt user to use Demo login
      if (err.message?.includes('Unexpected token') || err.message?.includes('Failed to fetch')) {
        alert('Backend server not available. Please use the "Quick Demo Access" buttons below to explore the app.');
      } else {
        alert('Login failed. Please try again.');
      }
    }
  };

  // ---- Login failure handler ----
  const handleGoogleError = () => {
    console.error('Google Sign-In encountered an error');
    alert('Google login failed. Please try again.');
  };

  return (
    <div className="flex flex-col items-center gap-3">
      <GoogleLogin
        onSuccess={handleGoogleSuccess}
        onError={handleGoogleError}
        theme="outline"
        size="large"
        shape="pill"
        text="signin_with"
        locale="en"
      />
      <p className="text-[11px] text-[#8e8e93]">
        Use your Google account to sign in to ParkJom
      </p>
    </div>
  );
}

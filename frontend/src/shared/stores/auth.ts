import { create } from 'zustand';
import { persist } from 'zustand/middleware';

interface AuthState {
  accessToken: string | null;
  refreshToken: string | null;
  username: string | null;
  setSession: (session: { accessToken: string; refreshToken: string; username: string }) => void;
  logout: () => void;
}

export const useAuthStore = create<AuthState>()(
  persist(
    (set) => ({
      accessToken: null,
      refreshToken: null,
      username: null,
      setSession: ({ accessToken, refreshToken, username }) =>
        set({ accessToken, refreshToken, username }),
      logout: () => set({ accessToken: null, refreshToken: null, username: null })
    }),
    { name: 'finance.auth' }
  )
);

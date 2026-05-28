import { create } from 'zustand';
import { persist } from 'zustand/middleware';

interface LayoutState {
  isCompact: boolean;
  density: 'compact' | 'standard';
  toggleDensity: () => void;
}

export const useLayoutStore = create<LayoutState>()(
  persist(
    (set, get) => ({
      isCompact: false,
      density: 'standard',
      toggleDensity: () => {
        const next = !get().isCompact;
        set({ isCompact: next, density: next ? 'compact' : 'standard' });
      }
    }),
    { name: 'finance.layout' }
  )
);

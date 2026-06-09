import '@testing-library/jest-dom/vitest';
import { afterEach, beforeEach, vi } from 'vitest';
import { cleanup } from '@testing-library/react';
import i18n from '@/shared/i18n/i18n';
import { useAuthStore } from '@/shared/stores/auth';

// jsdom does not implement matchMedia or ResizeObserver, both of which MUI (and the
// x-data-grid) touch during render. Provide inert stubs so components mount cleanly.
if (!window.matchMedia) {
  window.matchMedia = vi.fn().mockImplementation((query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addListener: vi.fn(),
    removeListener: vi.fn(),
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
    dispatchEvent: vi.fn()
  }));
}

class ResizeObserverStub {
  observe() {}
  unobserve() {}
  disconnect() {}
}
window.ResizeObserver = window.ResizeObserver ?? (ResizeObserverStub as typeof ResizeObserver);

beforeEach(() => {
  // Deterministic locale + clean auth state for every test.
  void i18n.changeLanguage('en');
  useAuthStore.setState({ accessToken: null, refreshToken: null, username: null });
});

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

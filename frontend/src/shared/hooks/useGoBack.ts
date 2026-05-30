import { useCallback } from 'react';
import { useNavigate, type To } from 'react-router-dom';

interface UseGoBackOptions {
  /** Where to land when there is no in-app history to go back to. */
  fallback: To;
}

interface UseGoBack {
  /** Navigates to the previous in-app entry, or to the fallback route if none exists. */
  goBack: () => void;
}

/**
 * Back-navigation helper. Prefers `navigate(-1)` when the SPA owns the previous
 * history entry; otherwise routes to the supplied fallback (typically the listing).
 * Detail / create / edit views MUST use this instead of hard-coding a destination.
 */
export function useGoBack({ fallback }: UseGoBackOptions): UseGoBack {
  const navigate = useNavigate();

  const goBack = useCallback(() => {
    const hasInAppHistory = window.history.state?.idx > 0;
    if (hasInAppHistory) {
      navigate(-1);
      return;
    }
    navigate(fallback, { replace: true });
  }, [navigate, fallback]);

  return { goBack };
}

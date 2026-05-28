import { Navigate, useLocation } from 'react-router-dom';
import { useAuthStore } from '@/shared/stores/auth';

interface Props {
  children: React.ReactNode;
}

export function RequireAuth({ children }: Props) {
  const token = useAuthStore((s) => s.accessToken);
  const location = useLocation();

  if (!token) {
    return <Navigate to="/login" replace state={{ from: location }} />;
  }
  return <>{children}</>;
}

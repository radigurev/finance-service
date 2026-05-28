import axios from 'axios';
import { v4 as uuidv4 } from 'uuid';
import { useAuthStore } from '@/shared/stores/auth';

const baseURL = import.meta.env.VITE_API_BASE_URL ?? '/api/v1';

export const api = axios.create({
  baseURL,
  timeout: 15_000
});

api.interceptors.request.use((config) => {
  config.headers = config.headers ?? {};
  config.headers['X-Correlation-ID'] = uuidv4();

  const token = useAuthStore.getState().accessToken;
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

api.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response?.status === 401) {
      useAuthStore.getState().logout();
    }
    return Promise.reject(error);
  }
);

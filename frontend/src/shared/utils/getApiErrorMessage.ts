import { AxiosError } from 'axios';
import type { TFunction } from 'i18next';

interface ProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
  errors?: Record<string, string[]>;
}

/**
 * Maps an Axios error (RFC 7807 ProblemDetails) into a translated, user-facing message.
 * Looks up `errors.<CODE>` in i18n first, then falls back to a generic message.
 */
export function getApiErrorMessage(err: unknown, t: TFunction): string {
  if (err instanceof AxiosError && err.response?.data) {
    const problem = err.response.data as ProblemDetails;

    if (problem.errors) {
      const codes = Object.values(problem.errors).flat();
      if (codes.length > 0) {
        return codes.map((code) => t(`errors.${code}`, { defaultValue: code })).join('; ');
      }
    }

    if (problem.title) {
      return t(`errors.${problem.title}`, { defaultValue: problem.detail ?? problem.title });
    }
  }

  return t('errors.GENERIC_ERROR');
}

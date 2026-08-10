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

/**
 * Reads the machine error CODE out of an Axios/ProblemDetails failure — the SCREAMING_SNAKE_CASE
 * `title` the backend stamps per SDD-INFRA-001. Callers use it only to choose the PRESENTATION of a
 * failure (e.g. `PAYMENT_POSTING_PENDING` is a normal transient state and must read as progress, not
 * as a destructive error — SDD-UI-FIN-002 §2.7). The user-facing text still comes from
 * {@link getApiErrorMessage}; the raw code MUST NOT be rendered.
 */
export function getApiErrorCode(err: unknown): string | undefined {
  if (err instanceof AxiosError && err.response?.data) {
    const problem = err.response.data as ProblemDetails;
    if (problem.title) {
      return problem.title;
    }
  }
  return undefined;
}

/** True when the failure is an HTTP 403 — the missing-permission case (SDD-UI-FIN-002 §2.17). */
export function isForbiddenError(err: unknown): boolean {
  return err instanceof AxiosError && err.response?.status === 403;
}

import { describe, it, expect } from 'vitest';
import { AxiosError } from 'axios';
import i18n from '@/shared/i18n/i18n';
import { getApiErrorMessage } from './getApiErrorMessage';

const t = i18n.getFixedT('en');

function axiosErrorWith(data: unknown, status = 400): AxiosError {
  return new AxiosError('request failed', undefined, undefined, undefined, {
    status,
    data
  } as never);
}

describe('getApiErrorMessage', () => {
  it('translates a single error code from the ProblemDetails errors dictionary', () => {
    const err = axiosErrorWith({ errors: { field: ['CONCURRENT_MODIFICATION'] } }, 409);

    const message = getApiErrorMessage(err, t);

    expect(message).toBe(t('errors.CONCURRENT_MODIFICATION'));
    expect(message).not.toBe('CONCURRENT_MODIFICATION');
  });

  it('joins multiple error codes with a semicolon', () => {
    const err = axiosErrorWith({
      errors: { a: ['DUPLICATE_ACCOUNT_CODE'], b: ['INVALID_PARENT_ACCOUNT'] }
    });

    const message = getApiErrorMessage(err, t);

    expect(message).toContain(t('errors.DUPLICATE_ACCOUNT_CODE'));
    expect(message).toContain(t('errors.INVALID_PARENT_ACCOUNT'));
    expect(message).toContain('; ');
  });

  it('falls back to the unmapped code itself when no translation exists', () => {
    const err = axiosErrorWith({ errors: { x: ['SOME_UNMAPPED_CODE'] } });

    expect(getApiErrorMessage(err, t)).toBe('SOME_UNMAPPED_CODE');
  });

  it('uses the title (translated) when there is no errors dictionary', () => {
    const err = axiosErrorWith({ title: 'GENERIC_ERROR', detail: 'dev detail' }, 500);

    expect(getApiErrorMessage(err, t)).toBe(t('errors.GENERIC_ERROR'));
  });

  it('falls back to detail when the title has no translation', () => {
    const err = axiosErrorWith({ title: 'UNTRANSLATED_TITLE', detail: 'human readable detail' });

    expect(getApiErrorMessage(err, t)).toBe('human readable detail');
  });

  it('returns the generic message for a non-Axios error', () => {
    expect(getApiErrorMessage(new Error('boom'), t)).toBe(t('errors.GENERIC_ERROR'));
  });

  it('returns the generic message for an Axios error without a response body', () => {
    expect(getApiErrorMessage(new AxiosError('network down'), t)).toBe(t('errors.GENERIC_ERROR'));
  });
});

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import { useDebouncedValue } from './useDebouncedValue';

describe('useDebouncedValue (ui-validate D8)', () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('holds the initial value immediately', () => {
    const { result } = renderHook(() => useDebouncedValue('30', 300));

    expect(result.current).toBe('30');
  });

  it('publishes only the LAST value of a burst, once', () => {
    const { result, rerender } = renderHook(({ value }) => useDebouncedValue(value, 300), {
      initialProps: { value: '' }
    });

    for (const value of ['3', '30', '30,', '30, 6', '30, 60', '30, 60, 9', '30, 60, 90']) {
      rerender({ value });
      act(() => {
        vi.advanceTimersByTime(50);
      });
      // Every intermediate value is still pending — nothing keyed on this may have changed yet.
      expect(result.current).toBe('');
    }

    act(() => {
      vi.advanceTimersByTime(300);
    });

    expect(result.current).toBe('30, 60, 90');
  });

  it('drops the pending update when the hook unmounts', () => {
    const { rerender, unmount } = renderHook(({ value }) => useDebouncedValue(value, 300), {
      initialProps: { value: 'a' }
    });

    rerender({ value: 'b' });
    unmount();

    // No state update after unmount — advancing the clock must not throw or warn.
    expect(() =>
      act(() => {
        vi.advanceTimersByTime(1000);
      })
    ).not.toThrow();
  });
});

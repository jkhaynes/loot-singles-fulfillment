import { vi } from 'vitest'

/**
 * jsdom does not implement `window.matchMedia`, so any component choosing a layout by viewport
 * width needs one supplied (016-mobile-picking T003).
 *
 * The default view follows screen size rather than device type (spec FR-008, research R5), so
 * these helpers drive width directly.
 */

/** Width at or below which the focused view is the default. Keep in step with the component. */
export const PHONE_MAX_WIDTH = 767

export interface MatchMediaController {
  /** Change the viewport width and notify every registered listener. */
  setWidth(width: number): void
  restore(): void
}

/**
 * Installs a `matchMedia` that answers `(max-width: Npx)` queries against a settable width and
 * notifies listeners on change, so a test can resize mid-render the way a real device rotating
 * or a desktop window narrowing would.
 */
export function installMatchMedia(initialWidth: number): MatchMediaController {
  let width = initialWidth
  const listeners = new Set<(event: MediaQueryListEvent) => void>()

  function matches(query: string): boolean {
    const max = /max-width:\s*(\d+)px/.exec(query)
    if (max) return width <= Number(max[1])

    const min = /min-width:\s*(\d+)px/.exec(query)
    if (min) return width >= Number(min[1])

    return false
  }

  const original = window.matchMedia

  window.matchMedia = vi.fn().mockImplementation((query: string) => {
    const list = {
      matches: matches(query),
      media: query,
      onchange: null,
      addEventListener: (_: string, listener: (event: MediaQueryListEvent) => void) => {
        listeners.add(listener)
      },
      removeEventListener: (_: string, listener: (event: MediaQueryListEvent) => void) => {
        listeners.delete(listener)
      },
      // Deprecated API, still called by some libraries.
      addListener: (listener: (event: MediaQueryListEvent) => void) => listeners.add(listener),
      removeListener: (listener: (event: MediaQueryListEvent) => void) =>
        listeners.delete(listener),
      dispatchEvent: () => false,
    }

    return list as unknown as MediaQueryList
  }) as unknown as typeof window.matchMedia

  return {
    setWidth(next: number) {
      width = next
      for (const listener of listeners) {
        listener({ matches: matches(`(max-width: ${PHONE_MAX_WIDTH}px)`) } as MediaQueryListEvent)
      }
    },
    restore() {
      window.matchMedia = original
      listeners.clear()
    },
  }
}

/**
 * Makes every `localStorage` access throw, standing in for private browsing or blocked site
 * data. The view must still render (spec FR-010, research R4) — a preference that fails to
 * persist is a nuisance, a picking screen that fails to appear is not.
 */
export function breakLocalStorage(): () => void {
  const original = Object.getOwnPropertyDescriptor(window, 'localStorage')

  Object.defineProperty(window, 'localStorage', {
    configurable: true,
    get() {
      throw new DOMException('localStorage is not available', 'SecurityError')
    },
  })

  return () => {
    if (original) Object.defineProperty(window, 'localStorage', original)
  }
}

import { useCallback, useEffect, useState } from 'react'

/**
 * Which picking view to show (016-mobile-picking, PRD §8).
 *
 * The default follows screen size rather than device type — a desktop window narrowed to a
 * phone's width is a phone-shaped screen, and user-agent sniffing answers a different question.
 *
 * A deliberate choice is remembered **per device** (spec FR-010, Product Owner decision
 * 2026-09-20). `localStorage` is per-origin and per-browser, which is exactly that, and it needs
 * no schema or endpoint. Note this refines PRD §8's "persist for that employee" wording; §8 is
 * due a matching correction.
 */

export type PickingView = 'focused' | 'list'

/** Width at or below which the focused view is the default. */
export const PHONE_MAX_WIDTH = 767

const PHONE_QUERY = `(max-width: ${PHONE_MAX_WIDTH}px)`

export const VIEW_PREFERENCE_STORAGE_KEY = 'loot.picking.view'

function isPickingView(value: unknown): value is PickingView {
  return value === 'focused' || value === 'list'
}

/**
 * Storage can be absent or throw — private browsing, blocked site data, some embedded webviews.
 * Every access is guarded, and any failure is treated as "no preference stored" so the picking
 * screen still renders.
 */
function readStoredView(): PickingView | null {
  try {
    const stored = window.localStorage.getItem(VIEW_PREFERENCE_STORAGE_KEY)
    return isPickingView(stored) ? stored : null
  } catch {
    return null
  }
}

function writeStoredView(view: PickingView): void {
  try {
    window.localStorage.setItem(VIEW_PREFERENCE_STORAGE_KEY, view)
  } catch {
    // The choice still applies for this session; only persistence is lost.
  }
}

function matchesPhone(): boolean {
  try {
    return window.matchMedia(PHONE_QUERY).matches
  } catch {
    return false
  }
}

export interface ViewPreference {
  view: PickingView
  choose: (view: PickingView) => void
}

export function useViewPreference(): ViewPreference {
  const [chosen, setChosen] = useState<PickingView | null>(() => readStoredView())
  const [isPhone, setIsPhone] = useState<boolean>(() => matchesPhone())

  useEffect(() => {
    let list: MediaQueryList
    try {
      list = window.matchMedia(PHONE_QUERY)
    } catch {
      return
    }

    // Keep following the viewport while no deliberate choice has been made, so narrowing a
    // desktop window gives the view that fits.
    const onChange = () => setIsPhone(matchesPhone())
    list.addEventListener('change', onChange)
    return () => list.removeEventListener('change', onChange)
  }, [])

  const choose = useCallback((view: PickingView) => {
    setChosen(view)
    writeStoredView(view)
  }, [])

  return { view: chosen ?? (isPhone ? 'focused' : 'list'), choose }
}

import { useEffect, useState } from 'react'

/**
 * Whether the screen is phone-sized (016-mobile-picking, PRD §8).
 *
 * A phone gets the card-at-a-time view, a desktop gets the whole order, and neither offers the
 * other. An earlier build let the employee switch and remembered the choice; the Product Owner
 * removed it on 2026-09-21 — nobody asked for it, and the control it occupied is now the way
 * out to the dashboard, which pickers did ask for.
 *
 * Screen size rather than device type: a desktop window narrowed to a phone's width is a
 * phone-shaped screen, and user-agent sniffing answers a different question.
 */

export const PHONE_MAX_WIDTH = 767

const PHONE_QUERY = `(max-width: ${PHONE_MAX_WIDTH}px)`

function matches(): boolean {
  try {
    return window.matchMedia(PHONE_QUERY).matches
  } catch {
    // No matchMedia (jsdom, an unusual webview): fall back to the whole order, which works at
    // any width, rather than to a view built for a screen we cannot measure.
    return false
  }
}

export function useIsPhone(): boolean {
  const [isPhone, setIsPhone] = useState(matches)

  useEffect(() => {
    let list: MediaQueryList
    try {
      list = window.matchMedia(PHONE_QUERY)
    } catch {
      return
    }

    const onChange = () => setIsPhone(matches())
    list.addEventListener('change', onChange)
    return () => list.removeEventListener('change', onChange)
  }, [])

  return isPhone
}

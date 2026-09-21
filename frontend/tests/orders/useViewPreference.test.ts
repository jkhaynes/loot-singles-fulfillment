import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import {
  VIEW_PREFERENCE_STORAGE_KEY,
  useViewPreference,
} from '../../src/features/orders/useViewPreference'
import { breakLocalStorage, installMatchMedia } from '../support/matchMedia'

const PHONE = 390
const DESKTOP = 1280

let restoreMatchMedia: (() => void) | null = null
let restoreStorage: (() => void) | null = null

beforeEach(() => {
  window.localStorage.clear()
})

afterEach(() => {
  restoreMatchMedia?.()
  restoreStorage?.()
  restoreMatchMedia = null
  restoreStorage = null
})

function renderAt(width: number) {
  const controller = installMatchMedia(width)
  restoreMatchMedia = controller.restore
  return { controller, ...renderHook(() => useViewPreference()) }
}

describe('useViewPreference — size-based default (T018)', () => {
  it('defaults to the focused view on a phone-sized screen', () => {
    const { result } = renderAt(PHONE)

    expect(result.current.view).toBe('focused')
  })

  it('defaults to the list view on a desktop-sized screen', () => {
    const { result } = renderAt(DESKTOP)

    expect(result.current.view).toBe('list')
  })

  it('does not treat the default as a stored choice', () => {
    renderAt(PHONE)

    // Arriving on a phone must not silently pin the picker to the focused view everywhere.
    expect(window.localStorage.getItem(VIEW_PREFERENCE_STORAGE_KEY)).toBeNull()
  })
})

describe('useViewPreference — a deliberate choice (T018)', () => {
  it('overrides the size-based default', () => {
    const { result } = renderAt(PHONE)

    act(() => result.current.choose('list'))

    expect(result.current.view).toBe('list')
  })

  it('persists the choice for the next order', () => {
    const { result, unmount } = renderAt(PHONE)
    act(() => result.current.choose('list'))
    unmount()
    restoreMatchMedia?.()

    const second = renderAt(PHONE)

    expect(second.result.current.view).toBe('list')
  })

  it('keeps a stored choice when the viewport changes', () => {
    const { result, controller } = renderAt(PHONE)
    act(() => result.current.choose('list'))

    act(() => controller.setWidth(DESKTOP))

    expect(result.current.view).toBe('list')
  })

  it('follows the viewport while no choice has been made', () => {
    const { result, controller } = renderAt(DESKTOP)
    expect(result.current.view).toBe('list')

    act(() => controller.setWidth(PHONE))

    // No deliberate choice yet, so the default may still follow the screen.
    expect(result.current.view).toBe('focused')
  })
})

describe('useViewPreference — storage unavailable (T019)', () => {
  it('still renders using the size-based default when localStorage throws', () => {
    // Private browsing, blocked site data, some embedded webviews. A preference that fails to
    // persist is a nuisance; a picking screen that fails to appear is not (research R4).
    restoreStorage = breakLocalStorage()

    const { result } = renderAt(PHONE)

    expect(result.current.view).toBe('focused')
  })

  it('still switches view in the session when the choice cannot be saved', () => {
    restoreStorage = breakLocalStorage()
    const { result } = renderAt(PHONE)

    expect(() => act(() => result.current.choose('list'))).not.toThrow()

    expect(result.current.view).toBe('list')
  })

  it('ignores an unrecognised stored value rather than rendering nothing', () => {
    window.localStorage.setItem(VIEW_PREFERENCE_STORAGE_KEY, 'not-a-view')

    const { result } = renderAt(DESKTOP)

    expect(result.current.view).toBe('list')
  })
})

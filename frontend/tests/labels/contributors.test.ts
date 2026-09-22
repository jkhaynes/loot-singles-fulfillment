import { describe, expect, it } from 'vitest'
import { formatContributors } from '../../src/features/labels/contributors'

// FR-043. The label's stock is a fixed 3½ × 1⅛ inches, so the picker line cannot grow without
// bound. Two names cover the cases that actually occur — usually one picker, sometimes two after
// an order is released and re-claimed — and anything beyond that is counted rather than printed.
// The packing desk has no such constraint and shows everyone.
describe('formatContributors', () => {
  it('prints a single picker as themselves', () => {
    expect(formatContributors(['Jordan'])).toBe('Jordan')
  })

  it('prints two pickers in full', () => {
    expect(formatContributors(['Jordan', 'Sam'])).toBe('Jordan, Sam')
  })

  it('counts the remainder beyond two', () => {
    expect(formatContributors(['Jordan', 'Sam', 'Alex'])).toBe('Jordan, Sam +1')
    expect(formatContributors(['Jordan', 'Sam', 'Alex', 'Robin'])).toBe('Jordan, Sam +2')
  })

  it('returns an empty string when nobody is recorded', () => {
    // Not "Unknown": a label that names nobody is better than one that asserts a picker exists.
    expect(formatContributors([])).toBe('')
  })
})

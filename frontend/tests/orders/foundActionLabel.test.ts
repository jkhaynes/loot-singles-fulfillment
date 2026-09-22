import { describe, expect, it } from 'vitest'
import { foundActionLabel } from '../../src/features/orders/foundActionLabel'

// 018 FR-015. The found action says what the picker is claiming to have in hand. A single copy is
// "it", never "all 1".
describe('foundActionLabel', () => {
  it('names a single copy as "it"', () => {
    expect(foundActionLabel(1)).toBe('I found it')
  })

  it('states the count for more than one', () => {
    expect(foundActionLabel(2)).toBe('I found all 2')
    expect(foundActionLabel(4)).toBe('I found all 4')
  })
})

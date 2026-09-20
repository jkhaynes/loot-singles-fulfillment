import { beforeEach, describe, expect, it } from 'vitest'
import { computeProgress, groupOrderLines } from '../../src/features/orders/orderGrouping'
import {
  buildIssueLine,
  buildLine,
  buildMultiGameLines,
  resetLineIds,
} from '../support/orderBuilders'

beforeEach(() => {
  resetLineIds()
})

/** Every line id across every group, in presentation order. */
function groupedLineIds(lines: ReturnType<typeof buildLine>[]): number[] {
  return groupOrderLines(lines).flatMap((group) => group.lines.map((line) => line.id))
}

describe('groupOrderLines — totality (T004)', () => {
  it('returns no groups for an empty order', () => {
    expect(groupOrderLines([])).toEqual([])
  })

  it('keeps the single line of a one-line order', () => {
    const line = buildLine({ productName: 'Pikachu ex' })

    const groups = groupOrderLines([line])

    expect(groups).toHaveLength(1)
    expect(groups[0].lines).toEqual([line])
  })

  it('places every line in exactly one group', () => {
    const lines = buildMultiGameLines()

    const ids = groupedLineIds(lines)

    // A permutation of the input: nothing dropped, nothing duplicated. This is the invariant a
    // grouping bug would break, and the one that would lose a picker's work.
    expect([...ids].sort((a, b) => a - b)).toEqual(
      lines.map((line) => line.id).sort((a, b) => a - b),
    )
    expect(new Set(ids).size).toBe(lines.length)
  })

  it('is deterministic', () => {
    const lines = buildMultiGameLines()

    expect(groupedLineIds(lines)).toEqual(groupedLineIds(lines))
  })
})

describe('groupOrderLines — ordering (T005)', () => {
  it('orders games alphabetically by name', () => {
    const groups = groupOrderLines(buildMultiGameLines())

    // Magic before Pokemon, whatever order the import supplied them in.
    expect(groups.map((group) => group.game)).toEqual(['Magic', 'Magic', 'Pokemon', 'Pokemon'])
  })

  it('orders sets alphabetically within a game', () => {
    const groups = groupOrderLines(buildMultiGameLines())

    expect(groups.map((group) => group.setName)).toEqual([
      'Aetherdrift',
      'Bloomburrow',
      'Black Bolt',
      'Surging Sparks',
    ])
  })

  it('keeps every line of a set contiguous', () => {
    const groups = groupOrderLines(buildMultiGameLines())
    const surgingSparks = groups.find((group) => group.setName === 'Surging Sparks')

    expect(surgingSparks?.lines.map((line) => line.productName)).toEqual([
      'Pikachu ex',
      'Latias ex',
    ])
  })

  it('preserves the relative order of lines within a set', () => {
    const lines = [
      buildLine({ set: 'Base Set', productName: 'Third' }),
      buildLine({ set: 'Base Set', productName: 'First' }),
      buildLine({ set: 'Base Set', productName: 'Second' }),
    ]

    const groups = groupOrderLines(lines)

    // Sets are reordered; lines inside a set are not.
    expect(groups[0].lines.map((line) => line.productName)).toEqual(['Third', 'First', 'Second'])
  })

  it('orders set names that differ only by case deterministically', () => {
    // Imported data is not case-normalised, so this must be total and repeatable rather than
    // depending on sort stability.
    const lines = [
      buildLine({ set: 'base set', productName: 'Lowercase' }),
      buildLine({ set: 'Base Set', productName: 'Titlecase' }),
      buildLine({ set: 'Alpha', productName: 'Earlier' }),
    ]

    const first = groupOrderLines(lines).map((group) => group.setName)
    const second = groupOrderLines(lines).map((group) => group.setName)

    expect(first).toEqual(second)
    expect(first[0]).toBe('Alpha')
    expect(first.slice(1).sort()).toEqual(['Base Set', 'base set'])
  })

  it('reports per-group product and physical card counts', () => {
    const groups = groupOrderLines(buildMultiGameLines())
    const blackBolt = groups.find((group) => group.setName === 'Black Bolt')

    expect(blackBolt?.productCount).toBe(1)
    // One product line, three physical cards.
    expect(blackBolt?.cardCount).toBe(3)
  })
})

describe('groupOrderLines — same set name in different games (T006)', () => {
  it('does not merge sets that share a name across games', () => {
    const lines = [
      buildLine({ productLine: 'Magic', set: 'Promo', productName: 'Magic Promo' }),
      buildLine({ productLine: 'Pokemon', set: 'Promo', productName: 'Pokemon Promo' }),
    ]

    const groups = groupOrderLines(lines)

    // Two boxes in two sections of the shop, not one box.
    expect(groups).toHaveLength(2)
    expect(groups.map((group) => group.game)).toEqual(['Magic', 'Pokemon'])
    expect(groups.every((group) => group.lines.length === 1)).toBe(true)
  })
})

describe('groupOrderLines — missing set values (T007)', () => {
  it.each([
    ['empty', ''],
    ['whitespace only', '   '],
  ])('keeps a line whose set is %s', (_label, set) => {
    const lines = [
      buildLine({ productLine: 'Pokemon', set: 'Base Set', productName: 'Pikachu' }),
      buildLine({ productLine: 'Pokemon', set, productName: 'Mystery Promo' }),
    ]

    const groups = groupOrderLines(lines)

    const allLines = groups.flatMap((group) => group.lines)
    expect(allLines).toHaveLength(2)
    expect(allLines.map((line) => line.productName)).toContain('Mystery Promo')
  })

  it('marks a group with no recorded set', () => {
    const groups = groupOrderLines([buildLine({ set: '' })])

    expect(groups[0].isSetRecorded).toBe(false)
  })

  it('sorts a group with no recorded set last within its game', () => {
    const lines = [
      buildLine({ productLine: 'Pokemon', set: '', productName: 'Mystery Promo' }),
      buildLine({ productLine: 'Pokemon', set: 'Zephyr', productName: 'Late Alphabetically' }),
      buildLine({ productLine: 'Pokemon', set: 'Base Set', productName: 'Pikachu' }),
    ]

    const groups = groupOrderLines(lines)

    // Last within Pokemon even though "" would sort first as a plain string.
    expect(groups.map((group) => group.isSetRecorded)).toEqual([true, true, false])
  })

  it('keeps unrecorded groups inside their own game, not at the end of the order', () => {
    const lines = [
      buildLine({ productLine: 'Magic', set: '', productName: 'Magic Unknown' }),
      buildLine({ productLine: 'Pokemon', set: 'Base Set', productName: 'Pikachu' }),
    ]

    const groups = groupOrderLines(lines)

    // The unrecorded Magic group belongs with Magic, before Pokemon begins.
    expect(groups.map((group) => group.game)).toEqual(['Magic', 'Pokemon'])
  })
})

describe('computeProgress (T008)', () => {
  it('reports nothing resolved for a fresh order', () => {
    const groups = groupOrderLines(buildMultiGameLines())

    const progress = computeProgress(groups, null)

    expect(progress.resolvedProducts).toBe(0)
    expect(progress.totalProducts).toBe(5)
    expect(progress.accountedCards).toBe(0)
    // Five lines, seven physical cards — the line of three is why these differ.
    expect(progress.totalCards).toBe(7)
  })

  it('counts physical cards rather than product lines', () => {
    const lines = [buildLine({ quantity: 3, pickOutcome: 'picked' }), buildLine({ quantity: 1 })]

    const progress = computeProgress(groupOrderLines(lines), null)

    expect(progress.resolvedProducts).toBe(1)
    expect(progress.accountedCards).toBe(3)
    expect(progress.totalCards).toBe(4)
  })

  it('counts a line carrying an issue as accounted for', () => {
    // "Accounted for" means resolved, not successfully found — a picker who cannot find a card
    // has still dealt with it.
    const lines = [buildIssueLine({ quantity: 2 }), buildLine({ quantity: 1 })]

    const progress = computeProgress(groupOrderLines(lines), null)

    expect(progress.resolvedProducts).toBe(1)
    expect(progress.accountedCards).toBe(2)
  })

  it('reports everything resolved once every line has an outcome', () => {
    const lines = [
      buildLine({ quantity: 2, pickOutcome: 'picked' }),
      buildIssueLine({ quantity: 1 }),
    ]

    const progress = computeProgress(groupOrderLines(lines), null)

    expect(progress.resolvedProducts).toBe(progress.totalProducts)
    expect(progress.accountedCards).toBe(progress.totalCards)
  })

  it('reports position within the current set, not within the order', () => {
    const lines = buildMultiGameLines()
    const groups = groupOrderLines(lines)
    const surgingSparks = groups.find((group) => group.setName === 'Surging Sparks')!
    const secondLineOfThatSet = surgingSparks.lines[1]

    const progress = computeProgress(groups, secondLineOfThatSet.id)

    expect(progress.currentSetName).toBe('Surging Sparks')
    expect(progress.currentSetPosition).toBe(2)
    expect(progress.currentSetSize).toBe(2)
  })
})

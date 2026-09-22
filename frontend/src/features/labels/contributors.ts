/**
 * How the label names who picked an order (FR-043).
 *
 * The label's stock is a fixed size, so this line cannot grow without bound. Two names cover what
 * actually happens — usually one picker, occasionally two after an order is released and
 * re-claimed — and the rest becomes a count. The packing desk, which has no size constraint,
 * shows every contributor instead of calling this.
 */
const NAMES_ON_LABEL = 2

export function formatContributors(displayNames: readonly string[]): string {
  const shown = displayNames.slice(0, NAMES_ON_LABEL).join(', ')
  const remainder = displayNames.length - NAMES_ON_LABEL

  return remainder > 0 ? `${shown} +${remainder}` : shown
}

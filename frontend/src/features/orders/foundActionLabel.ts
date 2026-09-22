/**
 * The found action on a reported product (018 FR-015): what the picker claims to have in hand. A
 * single copy is "it", never "all 1".
 */
export function foundActionLabel(quantity: number): string {
  return quantity === 1 ? 'I found it' : `I found all ${quantity}`
}

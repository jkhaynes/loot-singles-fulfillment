import type {
  OrderDetail,
  OrderLineDetail,
  PickOutcome,
  PickingIssueDetail,
} from '../../src/features/orders/ordersApi'

/**
 * Builders for order test data (016-mobile-picking T001).
 *
 * Grouping, progress and the unresolved-set guard all key off game, set, quantity and pick
 * outcome, so tests need to vary those four freely — including the awkward cases a real import
 * produces, such as a blank set or a line of three copies.
 */

let nextLineId = 1

/** Resets line ids so a test asserting on specific ids is not order-dependent. */
export function resetLineIds(): void {
  nextLineId = 1
}

export interface LineOptions {
  id?: number
  productName?: string
  /** The game, as imported. Blank and whitespace values are legal here on purpose. */
  productLine?: string
  /** The set, as imported. Blank and whitespace values are legal here on purpose. */
  set?: string
  collectorNumber?: string
  rarity?: string | null
  variant?: string | null
  condition?: string
  /** Physical cards on this line. Greater than one is the high-risk case (PRD §15). */
  quantity?: number
  imageUrl?: string | null
  pickOutcome?: PickOutcome | null
  currentIssue?: PickingIssueDetail | null
}

export function buildLine(options: LineOptions = {}): OrderLineDetail {
  const id = options.id ?? nextLineId++

  return {
    id,
    pickOutcome: options.pickOutcome ?? null,
    currentIssue: options.currentIssue ?? null,
    productName: options.productName ?? `Product ${id}`,
    productLine: options.productLine ?? 'Pokemon',
    set: options.set ?? 'Base Set',
    collectorNumber: options.collectorNumber ?? `#${id}`,
    rarity: options.rarity ?? null,
    variant: options.variant ?? null,
    condition: options.condition ?? 'Near Mint',
    quantity: options.quantity ?? 1,
    imageUrl: options.imageUrl ?? null,
  }
}

/** A line carrying a reported issue — resolved, but not successfully picked. */
export function buildIssueLine(options: LineOptions = {}): OrderLineDetail {
  return buildLine({
    ...options,
    pickOutcome: 'hasIssue',
    currentIssue: options.currentIssue ?? {
      issueType: 'cardNotFound',
      requiredQuantity: options.quantity ?? 1,
      foundQuantity: 0,
      note: null,
      reportedByEmployeeName: 'Sam',
      reportedAt: '2026-09-20T14:31:00Z',
    },
  })
}

export interface OrderOptions {
  orderId?: number
  tcgplayerOrderId?: string
  status?: string
  lines?: OrderLineDetail[]
  claimedByEmployeeId?: number | null
  claimedByEmployeeName?: string | null
}

export function buildOrder(options: OrderOptions = {}): OrderDetail {
  return {
    orderId: options.orderId ?? 1,
    tcgplayerOrderId: options.tcgplayerOrderId ?? 'F8433182-7B9B3B-BC75E',
    status: options.status ?? 'inProgress',
    lines: options.lines ?? [buildLine()],
    claimedByEmployeeId:
      options.claimedByEmployeeId === undefined ? 1 : options.claimedByEmployeeId,
    claimedByEmployeeName:
      options.claimedByEmployeeName === undefined ? 'Sam' : options.claimedByEmployeeName,
  }
}

/** An order nobody holds. */
export function buildUnclaimedOrder(options: OrderOptions = {}): OrderDetail {
  return buildOrder({
    status: 'ready',
    ...options,
    claimedByEmployeeId: null,
    claimedByEmployeeName: null,
  })
}

/**
 * Lines spanning two games and two sets per game, deliberately supplied in an order that
 * matches neither the expected game order nor the expected set order — so a test that passes
 * against this fixture cannot be passing by accident of input order.
 */
export function buildMultiGameLines(): OrderLineDetail[] {
  return [
    buildLine({ productLine: 'Pokemon', set: 'Surging Sparks', productName: 'Pikachu ex' }),
    buildLine({ productLine: 'Magic', set: 'Bloomburrow', productName: 'Mabel' }),
    buildLine({
      productLine: 'Pokemon',
      set: 'Black Bolt',
      productName: 'Genesect ex',
      quantity: 3,
    }),
    buildLine({ productLine: 'Magic', set: 'Aetherdrift', productName: 'Hare Apparent' }),
    buildLine({ productLine: 'Pokemon', set: 'Surging Sparks', productName: 'Latias ex' }),
  ]
}

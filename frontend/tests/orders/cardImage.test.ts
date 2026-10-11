import { describe, expect, it } from 'vitest'
import { cardImageSources } from '../../src/features/orders/cardImage'

const cdn = 'https://tcgplayer-cdn.tcgplayer.com/product'

describe('cardImageSources', () => {
  it.each([
    [`${cdn}/123456_75w.jpg`, 'jpg'],
    [`${cdn}/123456_200w.jpg`, 'jpg'],
    [`${cdn}/123456_400w.jpeg`, 'jpeg'],
  ])('asks TCGplayer for the larger renditions of %s', (url, ext) => {
    expect(cardImageSources(url)).toEqual({
      src: `${cdn}/123456_400w.${ext}`,
      srcSet: `${cdn}/123456_400w.${ext} 400w, ${cdn}/123456_in_1000x1000.${ext} 1000w`,
    })
  })

  it('leaves a Scryfall image unchanged', () => {
    const url = 'https://cards.scryfall.io/large/front/a/b/ab12.jpg?1700000000'

    expect(cardImageSources(url)).toEqual({ src: url })
  })

  it('leaves a TCGplayer image without a width suffix unchanged', () => {
    const url = `${cdn}/123456_in_1000x1000.jpg`

    expect(cardImageSources(url)).toEqual({ src: url })
  })

  // Only .jpg was checked by hand to have the larger renditions (R31 fix round 1).
  it.each([[`${cdn}/123456_400w.png`], [`${cdn}/123456_75w.webp`]])(
    'leaves an unverified extension unchanged: %s',
    (url) => {
      expect(cardImageSources(url)).toEqual({ src: url })
    },
  )

  it.each([
    ['https://tcgplayer-cdn.tcgplayer.com.evil.example/product/123456_75w.jpg'],
    ['https://evil.example/tcgplayer-cdn.tcgplayer.com/product/123456_75w.jpg'],
    ['http://tcgplayer-cdn.tcgplayer.com/product/123456_75w.jpg'],
  ])('never rewrites a lookalike: %s', (url) => {
    expect(cardImageSources(url)).toEqual({ src: url })
  })
})

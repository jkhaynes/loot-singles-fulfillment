/**
 * The sources to display for a line's card image (ruling R31).
 *
 * Lines imported from the TCGplayer API store TCGplayer's 75px thumbnail. TCGplayer serves the same
 * product image at larger sizes from the same CDN path, so the thumbnail is swapped for those at
 * display time — the same TCGplayer image, which keeps the API agreement and fixes lines already
 * imported. Any other URL (Scryfall, TCGdex, Lorcast, or a TCGplayer shape not recognised here) is
 * shown exactly as stored.
 */
const tcgplayerThumbnail =
  /^(https:\/\/tcgplayer-cdn\.tcgplayer\.com\/product\/\d+)_\d+w\.(jpe?g|png|webp)$/i

export interface CardImageSources {
  src: string
  srcSet?: string
}

export function cardImageSources(imageUrl: string): CardImageSources {
  const match = tcgplayerThumbnail.exec(imageUrl)
  if (match === null) {
    return { src: imageUrl }
  }

  const [, base, extension] = match
  const medium = `${base}_400w.${extension}`
  const large = `${base}_in_1000x1000.${extension}`
  return { src: medium, srcSet: `${medium} 400w, ${large} 1000w` }
}

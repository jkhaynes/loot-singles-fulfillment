import { useState } from 'react'
import type { ReactNode } from 'react'
import { cardImageSources } from './cardImage'

interface LineCardImageProps {
  imageUrl: string
  alt: string
  className: string
  sizes: string
  /** What to show when no source loads: the surface's existing no-image state. */
  placeholder: ReactNode
}

/**
 * A line's card image, with the larger TCGplayer renditions first (R31). A browser does not fall
 * back from a failed srcset candidate on its own, so a failure retries the stored URL alone, and
 * a second failure shows the no-image state rather than a broken-image icon.
 */
export function LineCardImage({
  imageUrl,
  alt,
  className,
  sizes,
  placeholder,
}: LineCardImageProps) {
  // Failures are counted per URL, so moving to another card starts again from the top.
  const [failed, setFailed] = useState({ url: imageUrl, count: 0 })
  const failures = failed.url === imageUrl ? failed.count : 0

  const sources = cardImageSources(imageUrl)
  const attempts = sources.srcSet === undefined ? [sources] : [sources, { src: imageUrl }]
  const attempt = attempts[failures]
  if (attempt === undefined) {
    return placeholder
  }

  return (
    <img
      // A new element per attempt, so the browser drops the failed srcset entirely.
      key={failures}
      className={className}
      {...attempt}
      sizes={sizes}
      alt={alt}
      onError={() => setFailed({ url: imageUrl, count: failures + 1 })}
    />
  )
}

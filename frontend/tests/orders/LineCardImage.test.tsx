import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { LineCardImage } from '../../src/features/orders/LineCardImage'

const thumbnail = 'https://tcgplayer-cdn.tcgplayer.com/product/900001_75w.jpg'
const placeholder = <p>No image</p>

function renderImage(imageUrl: string) {
  return render(
    <LineCardImage
      className="some-image"
      imageUrl={imageUrl}
      alt="Api Card"
      sizes="150px"
      placeholder={placeholder}
    />,
  )
}

describe('LineCardImage', () => {
  it('shows the larger TCGplayer rendition first', () => {
    renderImage(thumbnail)

    const image = screen.getByRole('img', { name: 'Api Card' })
    expect(image).toHaveAttribute(
      'src',
      'https://tcgplayer-cdn.tcgplayer.com/product/900001_400w.jpg',
    )
    expect(image).toHaveAttribute('srcset')
    expect(image).toHaveAttribute('sizes', '150px')
    expect(image).toHaveClass('some-image')
  })

  it('falls back to the stored URL, with no srcset, when the larger rendition fails', () => {
    renderImage(thumbnail)

    fireEvent.error(screen.getByRole('img', { name: 'Api Card' }))

    const image = screen.getByRole('img', { name: 'Api Card' })
    expect(image).toHaveAttribute('src', thumbnail)
    expect(image).not.toHaveAttribute('srcset')
    expect(image).toHaveClass('some-image')
  })

  it('shows the no-image placeholder when the stored URL fails too', () => {
    renderImage(thumbnail)

    fireEvent.error(screen.getByRole('img', { name: 'Api Card' }))
    fireEvent.error(screen.getByRole('img', { name: 'Api Card' }))

    expect(screen.queryByRole('img')).not.toBeInTheDocument()
    expect(screen.getByText('No image')).toBeInTheDocument()
  })

  it('shows the placeholder at once when an unrewritten URL fails', () => {
    renderImage('https://cards.scryfall.io/large/front/a/b/ab12.jpg')

    fireEvent.error(screen.getByRole('img', { name: 'Api Card' }))

    expect(screen.queryByRole('img')).not.toBeInTheDocument()
    expect(screen.getByText('No image')).toBeInTheDocument()
  })

  it('starts again for a different card', () => {
    const { rerender } = renderImage(thumbnail)
    fireEvent.error(screen.getByRole('img', { name: 'Api Card' }))
    fireEvent.error(screen.getByRole('img', { name: 'Api Card' }))

    const next = 'https://tcgplayer-cdn.tcgplayer.com/product/900002_75w.jpg'
    rerender(
      <LineCardImage
        className="some-image"
        imageUrl={next}
        alt="Api Card"
        sizes="150px"
        placeholder={placeholder}
      />,
    )

    expect(screen.getByRole('img', { name: 'Api Card' })).toHaveAttribute(
      'src',
      'https://tcgplayer-cdn.tcgplayer.com/product/900002_400w.jpg',
    )
  })
})

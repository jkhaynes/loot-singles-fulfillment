import type { SVGProps } from 'react'

type IconProps = SVGProps<SVGSVGElement>

const shared = {
  viewBox: '0 0 24 24',
  fill: 'none',
  stroke: 'currentColor',
  strokeWidth: 2,
  strokeLinecap: 'round' as const,
  strokeLinejoin: 'round' as const,
  'aria-hidden': true,
}

export function ShoppingBagIcon(props: IconProps) {
  return (
    <svg {...shared} {...props}>
      <path d="M6 8h12l-1 12H7L6 8Z" />
      <path d="M9 8V6a3 3 0 0 1 6 0v2" />
    </svg>
  )
}

export function ClockIcon(props: IconProps) {
  return (
    <svg {...shared} {...props}>
      <circle cx="12" cy="12" r="9" />
      <path d="M12 7v5l3 3" />
    </svg>
  )
}

export function AlertTriangleIcon(props: IconProps) {
  return (
    <svg {...shared} {...props}>
      <path d="M12 3 2 20h20L12 3Z" />
      <path d="M12 10v4" />
      <path d="M12 17h.01" />
    </svg>
  )
}

export function CheckCircleIcon(props: IconProps) {
  return (
    <svg {...shared} {...props}>
      <circle cx="12" cy="12" r="9" />
      <path d="m8 12 3 3 5-6" />
    </svg>
  )
}

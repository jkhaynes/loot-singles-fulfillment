import type { ReactNode } from 'react'
import { Navigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'

export function RequireManagerAdmin({ children }: { children: ReactNode }) {
  const { employee, isLoading } = useAuth()

  if (isLoading) {
    return null
  }

  if (employee?.role !== 'ManagerAdmin') {
    return <Navigate to="/" replace />
  }

  return <>{children}</>
}

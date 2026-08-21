import { createContext, useContext, useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import { me } from './authApi'
import type { AuthenticatedEmployee } from './authApi'

interface AuthContextValue {
  employee: AuthenticatedEmployee | null
  isLoading: boolean
  login: (employee: AuthenticatedEmployee) => void
}

const AuthContext = createContext<AuthContextValue | undefined>(undefined)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [employee, setEmployee] = useState<AuthenticatedEmployee | null>(null)
  const [isLoading, setIsLoading] = useState(true)

  useEffect(() => {
    let cancelled = false

    me()
      .then((result) => {
        if (!cancelled) {
          setEmployee(result)
        }
      })
      .finally(() => {
        if (!cancelled) {
          setIsLoading(false)
        }
      })

    return () => {
      cancelled = true
    }
  }, [])

  function login(loggedInEmployee: AuthenticatedEmployee) {
    setEmployee(loggedInEmployee)
  }

  return <AuthContext.Provider value={{ employee, isLoading, login }}>{children}</AuthContext.Provider>
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext)
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider')
  }
  return context
}

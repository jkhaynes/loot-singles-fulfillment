import { useState } from 'react'
import type { FormEvent } from 'react'
import { login, AuthApiError } from './authApi'
import type { AuthenticatedEmployee } from './authApi'

export interface LoginPageProps {
  onLoginSuccess: (employee: AuthenticatedEmployee) => void
}

export function LoginPage({ onLoginSuccess }: LoginPageProps) {
  const [username, setUsername] = useState('')
  const [pin, setPin] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setError(null)
    setIsSubmitting(true)

    try {
      const employee = await login(username, pin)
      onLoginSuccess(employee)
    } catch (caught) {
      setError(caught instanceof AuthApiError ? caught.message : 'Something went wrong. Please try again.')
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <form onSubmit={handleSubmit}>
      <h1>Log in</h1>
      <div>
        <label htmlFor="username">Username</label>
        <input
          id="username"
          name="username"
          type="text"
          autoComplete="username"
          value={username}
          onChange={(event) => setUsername(event.target.value)}
          required
        />
      </div>
      <div>
        <label htmlFor="pin">PIN</label>
        <input
          id="pin"
          name="pin"
          type="password"
          inputMode="numeric"
          pattern="\d{4}"
          maxLength={4}
          autoComplete="current-password"
          value={pin}
          onChange={(event) => setPin(event.target.value)}
          required
        />
      </div>
      {error && <p role="alert">{error}</p>}
      <button type="submit" disabled={isSubmitting}>
        Log in
      </button>
    </form>
  )
}

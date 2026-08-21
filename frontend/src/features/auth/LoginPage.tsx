import { useState } from 'react'
import type { FormEvent } from 'react'
import { login, AuthApiError } from './authApi'
import type { AuthenticatedEmployee } from './authApi'
import lootLogo from '../../assets/lcs-logo.png'
import './LoginPage.css'

export interface LoginPageProps {
  onLoginSuccess: (employee: AuthenticatedEmployee) => void
}

type LoginErrorVariant = 'generic' | 'locked'

interface LoginError {
  message: string
  variant: LoginErrorVariant
}

const invalidCredentialsMessage = 'Username or PIN is incorrect.'
const accountLockedMessage = 'This account is locked. Ask a Manager/Admin to unlock it.'

export function LoginPage({ onLoginSuccess }: LoginPageProps) {
  const [username, setUsername] = useState('')
  const [pin, setPin] = useState('')
  const [error, setError] = useState<LoginError | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setError(null)
    setIsSubmitting(true)

    try {
      const employee = await login(username, pin)
      onLoginSuccess(employee)
    } catch (caught) {
      if (caught instanceof AuthApiError && caught.code === 'account_locked') {
        setError({ message: accountLockedMessage, variant: 'locked' })
      } else if (caught instanceof AuthApiError && caught.code === 'invalid_credentials') {
        setError({ message: invalidCredentialsMessage, variant: 'generic' })
      } else {
        setError({ message: 'Something went wrong. Please try again.', variant: 'generic' })
      }
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <div className="login-page">
      <form className="login-card" onSubmit={handleSubmit}>
        <div className="login-title">
          <img src={lootLogo} alt="" className="login-title__icon" />
          <div className="login-title__text">
            <p className="login-title__wordmark">
              Loot <span className="login-title__accent">Singles</span>
            </p>
            <p className="login-title__subtitle">Fulfillment</p>
          </div>
        </div>
        <h2>Log in</h2>
        <div className="login-field">
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
        <div className="login-field">
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
        {error && (
          <p role="alert" data-variant={error.variant} className="login-error">
            {error.message}
          </p>
        )}
        <button type="submit" className="login-submit" disabled={isSubmitting}>
          Log in
        </button>
      </form>
    </div>
  )
}

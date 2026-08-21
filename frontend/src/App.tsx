import { LoginPage } from './features/auth/LoginPage'
import { useAuth } from './features/auth/AuthContext'
import './App.css'

function App() {
  const { employee, isLoading, login, logout } = useAuth()

  if (isLoading) {
    return null
  }

  if (!employee) {
    return <LoginPage onLoginSuccess={login} />
  }

  return (
    <div>
      <p>
        Logged in as {employee.displayName} ({employee.role})
      </p>
      <button type="button" onClick={logout}>Log out</button>
    </div>
  )
}

export default App

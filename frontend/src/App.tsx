import { LoginPage } from './features/auth/LoginPage'
import { useAuth } from './features/auth/AuthContext'
import './App.css'

function App() {
  const { employee, isLoading, login } = useAuth()

  if (isLoading) {
    return null
  }

  if (!employee) {
    return <LoginPage onLoginSuccess={login} />
  }

  return (
    <p>
      Logged in as {employee.displayName} ({employee.role})
    </p>
  )
}

export default App

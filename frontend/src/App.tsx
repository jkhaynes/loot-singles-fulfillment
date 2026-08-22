import { LoginPage } from './features/auth/LoginPage'
import { useAuth } from './features/auth/AuthContext'
import { DashboardPage } from './features/dashboard/DashboardPage'
import './App.css'

function App() {
  const { employee, isLoading, login, logout } = useAuth()

  if (isLoading) {
    return null
  }

  if (!employee) {
    return <LoginPage onLoginSuccess={login} />
  }

  return <DashboardPage employee={employee} onLogout={logout} />
}

export default App

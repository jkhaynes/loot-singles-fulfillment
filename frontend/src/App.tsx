import { Route, Routes } from 'react-router-dom'
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

  return (
    <Routes>
      <Route path="/" element={<DashboardPage employee={employee} onLogout={logout} />} />
    </Routes>
  )
}

export default App

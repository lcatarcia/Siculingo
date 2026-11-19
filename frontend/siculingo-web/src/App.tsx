import { lazy, Suspense } from 'react'
import AccountApp from './features/accounts/AccountApp'

const FoundationPage = import.meta.env.DEV
  ? lazy(() => import('./app/FoundationPage'))
  : null

export default function App() {
  if (FoundationPage && window.location.pathname === '/foundation') {
    return <Suspense fallback={<p>Caricamento stato servizi…</p>}><FoundationPage /></Suspense>
  }
  return <AccountApp />
}

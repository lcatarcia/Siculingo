import { lazy, Suspense } from 'react'
import AccountApp from './features/accounts/AccountApp'

const VisualPrototype = import.meta.env.DEV
  ? lazy(() => import('./features/visual-prototype/VisualPrototype'))
  : null
const FoundationPage = import.meta.env.DEV
  ? lazy(() => import('./app/FoundationPage'))
  : null

export default function App() {
  if (VisualPrototype && window.location.pathname.startsWith('/prototype')) {
    return <Suspense fallback={<p>Caricamento anteprima grafica…</p>}><VisualPrototype /></Suspense>
  }
  if (FoundationPage && window.location.pathname === '/foundation') {
    return <Suspense fallback={<p>Caricamento stato servizi…</p>}><FoundationPage /></Suspense>
  }
  return <AccountApp />
}

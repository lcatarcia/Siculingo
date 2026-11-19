import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { ApiError, currentAccount, errorMessage, login, logout, recover, register, type Account, type Recovery } from '../../shared/api/accounts'
import AdminConsolePage from '../administration/AdminConsolePage'
import AchievementsPage from '../gamification/AchievementsPage'
import ProfileDashboardPage from '../gamification/ProfileDashboardPage'
import LearningPathPage from '../learning/LearningPathPage'
import FriendsPage from '../social/FriendsPage'
import LeaderboardPage from '../social/LeaderboardPage'
import AccountForm, { type AccountMode } from './AccountForm'
import LegalNoticeDialog from './LegalNoticeDialog'
import RecoveryCard from './RecoveryCard'
import SettingsPage from './SettingsPage'
import './accounts.css'

interface BeforeInstallPromptEvent extends Event {
  prompt: () => Promise<void>
  userChoice: Promise<{ outcome: 'accepted' | 'dismissed' }>
}

type Session = { kind: 'loading' } | { kind: 'guest' } | { kind: 'authenticated'; account: Account } | { kind: 'error'; message: string }
type PendingCode = { result: Recovery; recovered: boolean }
type AppScreen = 'path' | 'leaderboard' | 'friends' | 'trophies' | 'profile' | 'settings' | 'admin'

export default function AccountApp() {
  const [session, setSession] = useState<Session>({ kind: 'loading' })
  const [mode, setMode] = useState<AccountMode>('login')
  const [pendingCode, setPendingCode] = useState<PendingCode | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [revision, setRevision] = useState(0)
  const [screen, setScreen] = useState<AppScreen>('path')
  const [runnerActive, setRunnerActive] = useState(false)
  const [legalOpen, setLegalOpen] = useState(false)
  const [isOnline, setIsOnline] = useState(() => (typeof navigator !== 'undefined' ? navigator.onLine : true))
  const [installPrompt, setInstallPrompt] = useState<BeforeInstallPromptEvent | null>(null)

  useEffect(() => {
    const onOnline = () => setIsOnline(true)
    const onOffline = () => setIsOnline(false)
    const onBeforeInstallPrompt = (event: Event) => {
      event.preventDefault()
      setInstallPrompt(event as BeforeInstallPromptEvent)
    }
    const onAppInstalled = () => setInstallPrompt(null)

    window.addEventListener('online', onOnline)
    window.addEventListener('offline', onOffline)
    window.addEventListener('beforeinstallprompt', onBeforeInstallPrompt)
    window.addEventListener('appinstalled', onAppInstalled)
    return () => {
      window.removeEventListener('online', onOnline)
      window.removeEventListener('offline', onOffline)
      window.removeEventListener('beforeinstallprompt', onBeforeInstallPrompt)
      window.removeEventListener('appinstalled', onAppInstalled)
    }
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    currentAccount(controller.signal).then(account => {
      if (!controller.signal.aborted) setSession(account ? { kind: 'authenticated', account } : { kind: 'guest' })
    }).catch((error: unknown) => {
      if (!controller.signal.aborted) setSession({ kind: 'error', message: errorMessage(error) })
    })
    return () => controller.abort()
  }, [revision])

  const expired = useCallback(() => {
    setScreen('path')
    setRunnerActive(false)
    setSession({ kind: 'guest' })
    setNotice('La sessione è scaduta o è stata revocata. Accedi di nuovo.')
    setMode('login')
  }, [])

  function passwordChanged() {
    setScreen('path')
    setRunnerActive(false)
    setSession({ kind: 'guest' })
    setMode('login')
    setError(null)
    setNotice('Password aggiornata. Gli accessi precedenti sono stati revocati: accedi con la nuova password.')
  }

  function accountDeleted() {
    setScreen('path')
    setRunnerActive(false)
    setSession({ kind: 'guest' })
    setMode('login')
    setError(null)
    setNotice('Il tuo account e i relativi dati personali sono stati eliminati in modo definitivo.')
  }

  async function triggerInstall() {
    if (!installPrompt) return
    await installPrompt.prompt()
    await installPrompt.userChoice.catch(() => null)
    setInstallPrompt(null)
  }

  function changeMode(next: AccountMode) {
    setMode(next)
    setError(null)
    setNotice(null)
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    const credentials = { username: String(form.get('username') ?? ''), password: String(form.get('password') ?? '') }
    setBusy(true)
    setError(null)
    setNotice(null)
    try {
      if (mode === 'login') {
        setSession({ kind: 'authenticated', account: await login(credentials) })
        setScreen('path')
      }
      else {
        const result = mode === 'register'
          ? await register({
              ...credentials,
              acceptTerms: form.get('acceptTerms') === 'true',
            })
          : await recover({ username: credentials.username, newPassword: credentials.password, recoveryCode: String(form.get('recoveryCode') ?? '') })
        setPendingCode({ result, recovered: mode === 'recover' })
      }
    } catch (error: unknown) {
      setError(errorMessage(error))
    } finally { setBusy(false) }
  }

  function savedCode() {
    if (!pendingCode) return
    setSession(pendingCode.recovered ? { kind: 'guest' } : { kind: 'authenticated', account: pendingCode.result.account })
    if (pendingCode.recovered) {
      setMode('login')
      setNotice('Password aggiornata. Accedi con la nuova password.')
    }
    setPendingCode(null)
  }

  async function signOut(allDevices: boolean) {
    setBusy(true)
    setError(null)
    try {
      await logout(allDevices)
      setSession({ kind: 'guest' })
      setScreen('path')
      setRunnerActive(false)
      setMode('login')
      setNotice(allDevices ? 'Accessi revocati su tutti i dispositivi.' : 'Sei uscito da questo browser.')
    } catch (error: unknown) {
      if (error instanceof ApiError && error.status === 401) expired()
      else setError(errorMessage(error))
    } finally { setBusy(false) }
  }

  return <div className="account-app">
    <a className="skip-to-content" href="#main-content">Salta al contenuto principale</a>
    {!isOnline && (
      <div className="offline-banner" role="status" aria-live="polite">
        <strong>Connessione assente.</strong> Siculingo richiede una connessione attiva per svolgere le lezioni e sincronizzare i progressi.
      </div>
    )}
    <header className="account-topbar">
      <a href="/" aria-label="Siculingo, pagina iniziale"><img src="/assets/brand/siculingo-logo.png" alt="Siculingo" width="172" height="64" /></a>
      {session.kind === 'authenticated' && !pendingCode && !runnerActive ? (
        <nav className="account-primary-nav" aria-label="Sezioni principali">
          <button
            type="button"
            className={`account-nav-tab ${screen === 'path' ? 'active' : ''}`}
            aria-current={screen === 'path' ? 'page' : undefined}
            onClick={() => setScreen('path')}
          >
            <img src="/assets/icons/home.png" alt="" width="22" height="22" />
            <span>Impara</span>
          </button>
          <button
            type="button"
            className={`account-nav-tab ${screen === 'leaderboard' ? 'active' : ''}`}
            aria-current={screen === 'leaderboard' ? 'page' : undefined}
            onClick={() => setScreen('leaderboard')}
          >
            <img src="/assets/badges/shield.png" alt="" width="22" height="22" />
            <span>Classifica</span>
          </button>
          <button
            type="button"
            className={`account-nav-tab ${screen === 'friends' ? 'active' : ''}`}
            aria-current={screen === 'friends' ? 'page' : undefined}
            onClick={() => setScreen('friends')}
          >
            <img src="/assets/icons/friends.png" alt="" width="22" height="22" />
            <span>Amici</span>
          </button>
          <button
            type="button"
            className={`account-nav-tab ${screen === 'trophies' ? 'active' : ''}`}
            aria-current={screen === 'trophies' ? 'page' : undefined}
            onClick={() => setScreen('trophies')}
          >
            <img src="/assets/icons/trophy.png" alt="" width="22" height="22" />
            <span>Trofei</span>
          </button>
          <button
            type="button"
            className={`account-nav-tab ${screen === 'profile' || screen === 'settings' ? 'active' : ''}`}
            aria-current={screen === 'profile' || screen === 'settings' ? 'page' : undefined}
            onClick={() => setScreen('profile')}
          >
            <img src="/assets/icons/goal.png" alt="" width="22" height="22" />
            <span>Profilo</span>
          </button>
          {(session.account.roles.includes('Editor') || session.account.roles.includes('Admin')) && (
            <button
              type="button"
              className={`account-nav-tab ${screen === 'admin' ? 'active' : ''}`}
              aria-current={screen === 'admin' ? 'page' : undefined}
              onClick={() => setScreen('admin')}
            >
              <img src="/assets/icons/book.png" alt="" width="22" height="22" />
              <span>{session.account.roles.includes('Admin') ? 'Admin' : 'Redazione'}</span>
            </button>
          )}
        </nav>
      ) : (
        <span className="account-local-label">SVILUPPO LOCALE</span>
      )}
      {session.kind === 'authenticated' && !pendingCode && <details className="account-menu">
        <summary><span aria-hidden="true">{Array.from(session.account.displayName)[0]?.toUpperCase()}</span>{session.account.displayName}</summary>
        <div><p>Accesso: 30 giorni dal login, senza rinnovo automatico.</p><button disabled={busy} onClick={event => { setScreen('profile'); event.currentTarget.closest('details')?.removeAttribute('open') }}>Il tuo profilo</button><button disabled={busy} onClick={event => { setScreen(screen === 'settings' ? 'path' : 'settings'); event.currentTarget.closest('details')?.removeAttribute('open') }}>{screen === 'settings' ? 'Torna a Impara' : 'Impostazioni account'}</button><button disabled={busy} onClick={() => void signOut(false)}>Esci da questo browser</button><button disabled={busy} onClick={() => void signOut(true)}>Esci da tutti i dispositivi</button></div>
      </details>}
      {import.meta.env.DEV && session.kind !== 'authenticated' && <a className="account-dev-link" href="/foundation">Stato servizi</a>}
    </header>
    {error && session.kind === 'authenticated' && <div className="account-error account-global-error" role="alert">{error}</div>}
    {session.kind === 'loading' && <main id="main-content" tabIndex={-1} className="account-status" role="status">Un momento, ritroviamo il tuo account…</main>}
    {session.kind === 'error' && <main id="main-content" tabIndex={-1} className="account-status"><h1>Non riusciamo a collegarci.</h1><p role="alert">{session.message}</p><button className="account-secondary" onClick={() => { setSession({ kind: 'loading' }); setRevision(value => value + 1) }}>Riprova</button></main>}
    {session.kind === 'authenticated' && !pendingCode && <main id="main-content" tabIndex={-1}>
      {screen === 'path' && (
        <LearningPathPage
          account={session.account}
          onExpired={expired}
          onOpenTrophies={() => setScreen('trophies')}
          onOpenProfile={() => setScreen('profile')}
          onRunnerActiveChange={setRunnerActive}
        />
      )}
      {screen === 'leaderboard' && (
        <LeaderboardPage
          onExpired={expired}
          onOpenSettings={() => setScreen('settings')}
          onOpenFriends={() => setScreen('friends')}
          onGoLearn={() => setScreen('path')}
        />
      )}
      {screen === 'friends' && (
        <FriendsPage
          onExpired={expired}
          onOpenSettings={() => setScreen('settings')}
        />
      )}
      {screen === 'trophies' && (
        <AchievementsPage onExpired={expired} onGoLearn={() => setScreen('path')} />
      )}
      {screen === 'profile' && (
        <ProfileDashboardPage
          account={session.account}
          onExpired={expired}
          onOpenSettings={() => setScreen('settings')}
          onOpenTrophies={() => setScreen('trophies')}
        />
      )}
      {screen === 'settings' && (
        <SettingsPage
          account={session.account}
          busy={busy}
          onBusyChange={setBusy}
          onUpdated={account => setSession({ kind: 'authenticated', account })}
          onPasswordChanged={passwordChanged}
          onAccountDeleted={accountDeleted}
          onOpenLegal={() => setLegalOpen(true)}
          onExpired={expired}
          onBack={() => setScreen('profile')}
        />
      )}
      {screen === 'admin' && (session.account.roles.includes('Editor') || session.account.roles.includes('Admin')) && (
        <AdminConsolePage account={session.account} onExpired={expired} />
      )}
    </main>}
    {(session.kind === 'guest' || pendingCode) && <main id="main-content" tabIndex={-1} className="account-welcome">
      <section className="account-story">
        <span className="account-eyebrow">ITALIANO → CATANESE</span><h1>Il tuo viaggio<br /><em>parte da qui.</em></h1>
        <p>Una parola, un sorriso, una chiacchiera in più.<br />Il catanese si impara a piccoli passi.</p>
        <img src="/assets/mascot/ficodi-front.png" alt="Ficodì ti dà il benvenuto" width="168" height="192" decoding="async" />
        <span className="account-story-note">FATTI ACCOMPAGNARE DA FICODÌ</span>
      </section>
      {pendingCode ? <RecoveryCard code={pendingCode.result.recoveryCode} recovered={pendingCode.recovered} onContinue={savedCode} /> : <AccountForm key={mode} mode={mode} busy={busy} error={error} notice={notice} onMode={changeMode} onSubmit={event => void submit(event)} onOpenLegal={() => setLegalOpen(true)} />}
    </main>}
    <footer className="account-footer">
      <span>Siculingo · Beta sperimentale in preparazione locale · Cinque lezioni pilota approvate.</span>
      <div className="account-footer-links">
        <button type="button" className="footer-link-btn" onClick={() => setLegalOpen(true)}>
          Informativa privacy e termini beta
        </button>
        {installPrompt && (
          <button type="button" className="footer-link-btn" onClick={() => void triggerInstall()}>
            Installa l’app sul dispositivo
          </button>
        )}
      </div>
    </footer>
    {legalOpen && <LegalNoticeDialog onClose={() => setLegalOpen(false)} />}
  </div>
}

import { useEffect, useState, type FormEvent } from 'react'
import {
  ApiError,
  changePassword,
  deleteAccount,
  errorMessage,
  socialPrivacy,
  updateProfile,
  updateSocialPrivacy,
  type Account,
  type PrivacySettings,
} from '../../shared/api/accounts'

export default function SettingsPage({
  account,
  busy,
  onBusyChange,
  onUpdated,
  onPasswordChanged,
  onAccountDeleted,
  onOpenLegal,
  onExpired,
  onBack,
}: {
  account: Account
  busy: boolean
  onBusyChange: (busy: boolean) => void
  onUpdated: (account: Account) => void
  onPasswordChanged: () => void
  onAccountDeleted: () => void
  onOpenLegal: () => void
  onExpired: () => void
  onBack: () => void
}) {
  const [displayName, setDisplayName] = useState(account.displayName)
  const [saving, setSaving] = useState<'profile' | 'password' | 'privacy' | 'delete' | null>(null)
  const [profileError, setProfileError] = useState<string | null>(null)
  const [passwordError, setPasswordError] = useState<string | null>(null)
  const [deleteError, setDeleteError] = useState<string | null>(null)
  const [confirmPermanentDelete, setConfirmPermanentDelete] = useState(false)
  const [saved, setSaved] = useState(false)
  const [privacy, setPrivacy] = useState<PrivacySettings>({
    profileVisibility: 'public',
    allowFriendRequests: true,
    showInLeaderboard: true,
  })
  const [privacyLoaded, setPrivacyLoaded] = useState(false)
  const [privacyError, setPrivacyError] = useState<string | null>(null)
  const [privacySaved, setPrivacySaved] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    socialPrivacy(controller.signal)
      .then(data => {
        if (!controller.signal.aborted) {
          setPrivacy(data)
          setPrivacyLoaded(true)
        }
      })
      .catch((err: unknown) => {
        if (controller.signal.aborted) return
        if (err instanceof ApiError && err.status === 401) onExpired()
        else setPrivacyError(errorMessage(err))
      })
    return () => controller.abort()
  }, [onExpired])

  async function savePrivacy(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setSaving('privacy')
    onBusyChange(true)
    setPrivacySaved(false)
    setPrivacyError(null)
    try {
      const updated = await updateSocialPrivacy({
        profileVisibility: privacy.profileVisibility,
        allowFriendRequests: privacy.profileVisibility === 'private' ? false : privacy.allowFriendRequests,
        showInLeaderboard: privacy.showInLeaderboard,
      })
      setPrivacy(updated)
      setPrivacySaved(true)
    } catch (error: unknown) {
      if (error instanceof ApiError && error.status === 401) onExpired()
      else setPrivacyError(errorMessage(error))
    } finally {
      setSaving(null)
      onBusyChange(false)
    }
  }

  async function saveProfile(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setSaving('profile')
    onBusyChange(true)
    setSaved(false)
    setProfileError(null)
    try {
      const updated = await updateProfile(displayName)
      onUpdated(updated)
      setDisplayName(updated.displayName)
      setSaved(true)
    } catch (error: unknown) {
      if (error instanceof ApiError && error.status === 401) onExpired()
      else setProfileError(errorMessage(error))
    } finally { setSaving(null); onBusyChange(false) }
  }

  async function savePassword(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = event.currentTarget
    const values = new FormData(form)
    const currentPassword = String(values.get('currentPassword') ?? '')
    const newPassword = String(values.get('newPassword') ?? '')
    setPasswordError(null)
    if (newPassword !== String(values.get('confirmPassword') ?? '')) {
      setPasswordError('Le due nuove password non coincidono.')
      return
    }
    setSaving('password')
    onBusyChange(true)
    try {
      await changePassword({ currentPassword, newPassword })
      form.reset()
      onPasswordChanged()
    } catch (error: unknown) {
      if (error instanceof ApiError && error.status === 401) onExpired()
      else setPasswordError(errorMessage(error))
    } finally { setSaving(null); onBusyChange(false) }
  }

  async function handleDeleteAccount(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = event.currentTarget
    const values = new FormData(form)
    const currentPassword = String(values.get('deleteCurrentPassword') ?? '')
    setDeleteError(null)
    if (!confirmPermanentDelete) {
      setDeleteError('Conferma la spunta di eliminazione definitiva prima di procedere.')
      return
    }
    setSaving('delete')
    onBusyChange(true)
    try {
      await deleteAccount(currentPassword)
      form.reset()
      onAccountDeleted()
    } catch (error: unknown) {
      if (error instanceof ApiError && error.status === 401) onExpired()
      else setDeleteError(errorMessage(error))
    } finally {
      setSaving(null)
      onBusyChange(false)
    }
  }

  return <div className="settings-page">
    <button className="account-secondary" onClick={onBack} disabled={busy}>← Torna al percorso</button>
    <header className="learning-heading"><span className="account-eyebrow">IL TUO ACCOUNT</span><h1>Come ti va di apparire?</h1><p>Un nome per salutarti. Una password per tenere al sicuro il tuo viaggio.</p></header>
    <section className="account-form-card" aria-labelledby="profile-title">
      <h2 id="profile-title">Il tuo profilo</h2>
      <p>Per accedere usa sempre <strong className="settings-username">{account.username}</strong>. Il nome visualizzato non cambia lo username e non deve essere unico.</p>
      <form onSubmit={event => void saveProfile(event)}>
        <label htmlFor="settings-name">Nome visualizzato</label>
        <input id="settings-name" name="displayName" autoComplete="nickname" value={displayName} onChange={event => { setDisplayName(event.target.value); setSaved(false) }} maxLength={40} disabled={busy} aria-describedby="settings-name-help" />
        <small id="settings-name-help">Massimo 40 caratteri. Lascia vuoto per usare il tuo username.</small>
        {profileError && <p className="account-error" role="alert">{profileError}</p>}
        {saved && <p className="account-notice" role="status">Nome salvato su tutti i tuoi dispositivi.</p>}
        <button className="account-primary" disabled={busy}>{saving === 'profile' ? 'Salvataggio…' : 'SALVA IL NOME'}</button>
      </form>
    </section>
    <section className="account-form-card" aria-labelledby="privacy-title">
      <h2 id="privacy-title">Privacy, amici e classifiche</h2>
      <p>Scegli chi può trovare e consultare il tuo profilo, inviarti richieste di amicizia e vedere i tuoi XP nelle leghe settimanali.</p>
      {!privacyLoaded && !privacyError ? (
        <p role="status">Caricamento impostazioni privacy…</p>
      ) : (
        <form onSubmit={event => void savePrivacy(event)}>
          <label htmlFor="settings-visibility">Visibilità del profilo</label>
          <select
            id="settings-visibility"
            className="settings-privacy-select"
            value={privacy.profileVisibility}
            disabled={busy}
            onChange={event => {
              const nextVis = event.target.value
              setPrivacy(prev => ({
                ...prev,
                profileVisibility: nextVis,
                allowFriendRequests: nextVis === 'private' ? false : prev.allowFriendRequests,
              }))
              setPrivacySaved(false)
            }}
          >
            <option value="public">Pubblico (ricercabile e visibile nella comunità)</option>
            <option value="friends">Solo amici (ricercabile, dettagli visibili solo agli amici)</option>
            <option value="private">Privato (nascosto dalla ricerca e non consultabile)</option>
          </select>

          <label className="account-save-check">
            <input
              type="checkbox"
              checked={privacy.profileVisibility === 'private' ? false : privacy.allowFriendRequests}
              disabled={busy || privacy.profileVisibility === 'private'}
              onChange={event => {
                setPrivacy(prev => ({ ...prev, allowFriendRequests: event.target.checked }))
                setPrivacySaved(false)
              }}
            />
            <span>
              Consenti di ricevere nuove richieste di amicizia
              {privacy.profileVisibility === 'private' ? ' (disattivato automaticamente con profilo privato)' : ''}
            </span>
          </label>

          <label className="account-save-check">
            <input
              type="checkbox"
              checked={privacy.showInLeaderboard}
              disabled={busy}
              onChange={event => {
                setPrivacy(prev => ({ ...prev, showInLeaderboard: event.target.checked }))
                setPrivacySaved(false)
              }}
            />
            <span>Partecipa alle classifiche settimanali e alle leghe Bronzo / Argento / Oro</span>
          </label>

          {privacyError && <p className="account-error" role="alert">{privacyError}</p>}
          {privacySaved && <p className="account-notice" role="status">Preferenze privacy aggiornate.</p>}
          <button className="account-primary" disabled={busy}>
            {saving === 'privacy' ? 'Salvataggio…' : 'SALVA IMPOSTAZIONI PRIVACY'}
          </button>
        </form>
      )}
    </section>
    <section className="account-form-card" aria-labelledby="password-title">
      <h2 id="password-title">Cambia password</h2>
      <p>Il cambio password chiude gli accessi su tutti i dispositivi, compreso questo. Poi accedi con la nuova password. Il tuo codice di recupero resta valido.</p>
      <form onSubmit={event => void savePassword(event)}>
        <input type="text" name="username" value={account.username} autoComplete="username" hidden readOnly />
        <label htmlFor="settings-current-password">Password attuale</label>
        <input id="settings-current-password" type="password" name="currentPassword" autoComplete="current-password" maxLength={128} required disabled={busy} />
        <label htmlFor="settings-new-password">Nuova password</label>
        <input id="settings-new-password" type="password" name="newPassword" autoComplete="new-password" minLength={12} maxLength={128} required disabled={busy} aria-describedby="settings-password-help" />
        <small id="settings-password-help">Da 12 a 128 caratteri e diversa da quella attuale. Anche una frase va bene.</small>
        <label htmlFor="settings-confirm-password">Ripeti la nuova password</label>
        <input id="settings-confirm-password" type="password" name="confirmPassword" autoComplete="new-password" minLength={12} maxLength={128} required disabled={busy} />
        {passwordError && <p className="account-error" role="alert">{passwordError}</p>}
        <button className="account-primary" disabled={busy}>{saving === 'password' ? 'Aggiornamento…' : 'CAMBIA PASSWORD ED ESCI'}</button>
      </form>
    </section>

    <section className="account-form-card danger-card" aria-labelledby="delete-account-title">
      <h2 id="delete-account-title">Trasparenza ed eliminazione account</h2>
      <p>
        Puoi consultare in qualsiasi momento l’{' '}
        <button type="button" className="inline-legal-link" onClick={onOpenLegal}>
          Informativa privacy e i Termini della beta
        </button>
        . Se desideri chiudere l’account, la cancellazione è immediata e definitiva: vengono rimossi credenziali, sessioni,
        lezioni completate, XP, trofei, cuori, missioni, classifiche e amicizie, mentre eventuali segnalazioni didattiche storiche
        vengono anonimizzate.
      </p>
      <form onSubmit={event => void handleDeleteAccount(event)}>
        <input type="text" name="username" value={account.username} autoComplete="username" hidden readOnly />
        <label className="account-save-check">
          <input
            type="checkbox"
            checked={confirmPermanentDelete}
            disabled={busy}
            onChange={event => {
              setConfirmPermanentDelete(event.target.checked)
              setDeleteError(null)
            }}
          />
          <span>Comprendo che l’eliminazione dell’account @{account.username} è immediata e irreversibile.</span>
        </label>
        <label htmlFor="settings-delete-password">Password attuale per confermare</label>
        <input
          id="settings-delete-password"
          type="password"
          name="deleteCurrentPassword"
          autoComplete="current-password"
          maxLength={128}
          required
          disabled={busy}
        />
        {deleteError && (
          <p className="account-error" role="alert">
            {deleteError}
          </p>
        )}
        <button
          type="submit"
          className="account-danger-btn"
          disabled={busy || !confirmPermanentDelete}
        >
          {saving === 'delete' ? 'Eliminazione in corso…' : 'ELIMINA DEFINITIVAMENTE L’ACCOUNT'}
        </button>
      </form>
    </section>
  </div>
}

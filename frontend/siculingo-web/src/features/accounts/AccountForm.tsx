import type { FormEvent } from 'react'

export type AccountMode = 'login' | 'register' | 'recover'

export default function AccountForm({ mode, busy, error, notice, onMode, onSubmit, onOpenLegal }: {
  mode: AccountMode
  busy: boolean
  error: string | null
  notice: string | null
  onMode: (mode: AccountMode) => void
  onSubmit: (event: FormEvent<HTMLFormElement>) => void
  onOpenLegal: () => void
}) {
  return <section className="account-form-card" aria-labelledby="account-form-title">
    {mode !== 'recover' && <div className="account-switch">
      <button type="button" aria-pressed={mode === 'login'} disabled={busy} onClick={() => onMode('login')}>Accedi</button>
      <button type="button" aria-pressed={mode === 'register'} disabled={busy} onClick={() => onMode('register')}>Crea account</button>
    </div>}
    <h2 id="account-form-title">{mode === 'login' ? 'Bentornato!' : mode === 'register' ? 'Il primo passo è tuo.' : 'Ritroviamo il tuo account.'}</h2>
    <p>{mode === 'login' ? 'Entra con il tuo nome utente e la password.' : mode === 'register' ? 'Bastano un nome utente e una password. Nessuna email.' : 'Servono nome utente e codice di recupero. Il vecchio codice potrà essere usato una sola volta.'}</p>
    {notice && <div className="account-notice" role="status">{notice}</div>}
    <form onSubmit={onSubmit}>
      <label htmlFor="account-username">Nome utente</label>
      <input id="account-username" name="username" autoComplete="username" autoCapitalize="none" spellCheck={false} minLength={3} maxLength={24} pattern="[A-Za-z0-9_]{3,24}" required disabled={busy} aria-describedby={mode === 'register' ? 'username-help' : undefined} />
      {mode === 'register' && <small id="username-help">3–24 caratteri: lettere senza accenti, numeri e underscore.</small>}
      {mode === 'recover' && <>
        <label htmlFor="account-code">Codice di recupero</label>
        <input id="account-code" name="recoveryCode" autoComplete="off" autoCapitalize="none" spellCheck={false} maxLength={128} required disabled={busy} />
      </>}
      <label htmlFor="account-password">{mode === 'recover' ? 'Nuova password' : 'Password'}</label>
      <input id="account-password" name="password" type="password" autoComplete={mode === 'login' ? 'current-password' : 'new-password'} minLength={mode === 'login' ? 1 : 12} maxLength={128} required disabled={busy} aria-describedby={mode !== 'login' ? 'password-help' : undefined} />
      {mode !== 'login' && <small id="password-help">Da 12 a 128 caratteri. Anche una frase va bene.</small>}
      {mode === 'register' && (
        <>
          <div className="account-recovery-warning"><strong>Il codice è la tua chiave di riserva.</strong> Alla registrazione ti mostreremo un codice da conservare. Senza password e codice non potrai recuperare autonomamente l’account.</div>
          <label className="account-save-check">
            <input type="checkbox" name="acceptTerms" value="true" required disabled={busy} />
            <span>
              Dichiaro di aver preso visione dell’{' '}
              <button type="button" className="inline-legal-link" onClick={onOpenLegal}>
                Informativa privacy e dei Termini della beta
              </button>
              .
            </span>
          </label>
        </>
      )}
      {error && <div className="account-error" role="alert">{error}</div>}
      <button className="account-primary" type="submit" disabled={busy}>{busy ? 'Un momento…' : mode === 'login' ? 'ENTRA' : mode === 'register' ? 'INIZIA IL VIAGGIO' : 'IMPOSTA NUOVA PASSWORD'}</button>
    </form>
    <button className="account-text-button" type="button" disabled={busy} onClick={() => onMode(mode === 'recover' ? 'login' : 'recover')}>{mode === 'recover' ? 'Torna all’accesso' : 'Hai dimenticato la password?'}</button>
    {mode === 'recover' && <p className="account-help">Il recupero chiude tutti gli accessi precedenti e genera un nuovo codice. Non inviamo email.</p>}
  </section>
}

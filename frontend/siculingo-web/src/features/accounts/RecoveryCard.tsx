import { useState } from 'react'

export default function RecoveryCard({ code, recovered, onContinue }: {
  code: string
  recovered: boolean
  onContinue: () => void
}) {
  const [saved, setSaved] = useState(false)
  const [copyMessage, setCopyMessage] = useState<string | null>(null)

  async function copy() {
    try {
      await navigator.clipboard.writeText(code)
      setCopyMessage('Codice copiato. Conservalo in un luogo sicuro.')
    } catch {
      setCopyMessage('Copia automatica non disponibile. Seleziona il codice e copialo manualmente.')
    }
  }

  return <section className="account-form-card recovery-card" aria-labelledby="recovery-title">
    <span className="account-eyebrow">{recovered ? 'PASSWORD AGGIORNATA' : 'ACCOUNT CREATO'}</span>
    <h2 id="recovery-title">Custodisci la tua chiave.</h2>
    <p>Questo codice viene mostrato solo ora. Salvalo prima di continuare o ricaricare la pagina. Chi lo possiede, insieme al tuo nome utente, può reimpostare la password.</p>
    <code className="account-secret">{code}</code>
    <button className="account-secondary" onClick={copy}>Copia il codice</button>
    {copyMessage && <p role="status">{copyMessage}</p>}
    <div className="account-recovery-warning">{recovered ? 'Il vecchio codice e gli accessi precedenti non sono più validi.' : 'Non usare la stessa password di altri servizi.'} Senza password e codice non è possibile il recupero autonomo.</div>
    <label className="account-save-check"><input type="checkbox" checked={saved} onChange={event => setSaved(event.target.checked)} />Ho salvato il codice in un luogo sicuro.</label>
    <button className="account-primary" disabled={!saved} onClick={onContinue}>{recovered ? 'VAI ALL’ACCESSO' : 'VAI AL TUO PERCORSO'}</button>
  </section>
}

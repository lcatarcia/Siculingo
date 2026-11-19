import { useEffect, useRef, useState } from 'react'
import { errorMessage, legalNotice, type LegalNotice } from '../../shared/api/accounts'

export default function LegalNoticeDialog({ onClose }: { onClose: () => void }) {
  const [data, setData] = useState<LegalNotice | null>(null)
  const [error, setError] = useState<string | null>(null)
  const closeBtnRef = useRef<HTMLButtonElement | null>(null)

  useEffect(() => {
    closeBtnRef.current?.focus()
    const controller = new AbortController()
    legalNotice(controller.signal)
      .then(result => {
        if (!controller.signal.aborted) setData(result)
      })
      .catch((err: unknown) => {
        if (!controller.signal.aborted) setError(errorMessage(err))
      })

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault()
        onClose()
      }
    }
    window.addEventListener('keydown', onKeyDown)
    return () => {
      controller.abort()
      window.removeEventListener('keydown', onKeyDown)
    }
  }, [onClose])

  return (
    <div
      className="legal-modal-backdrop"
      role="presentation"
      onClick={event => {
        if (event.target === event.currentTarget) onClose()
      }}
    >
      <div
        className="legal-modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby="legal-dialog-title"
      >
        <div className="legal-modal-header">
          <div>
            <span className="account-eyebrow">TRASPARENZA E DATI · BETA SPERIMENTALE</span>
            <h2 id="legal-dialog-title">Informativa privacy e termini d’uso</h2>
          </div>
          <button
            ref={closeBtnRef}
            type="button"
            className="account-secondary legal-close-btn"
            onClick={onClose}
          >
            Chiudi
          </button>
        </div>

        {!data && !error && <p role="status">Caricamento informativa…</p>}
        {error && (
          <p className="account-error" role="alert">
            {error}
          </p>
        )}

        {data && (
          <div className="legal-modal-body">
            <div className="legal-meta-box">
              <p>
                <strong>Versione documento:</strong> {data.termsVersion} ·{' '}
                <strong>Ultimo aggiornamento:</strong> {data.lastUpdatedIsoDate}
              </p>
              <p>
                <strong>Titolare / riferimento:</strong> {data.controllerName}
              </p>
              <p>
                <strong>Contatto e segnalazioni:</strong> {data.contactChannel}
              </p>
            </div>

            <section aria-labelledby="legal-privacy-heading" className="legal-section-group">
              <h3 id="legal-privacy-heading">1. Informativa sul trattamento dei dati</h3>
              {data.privacySections.map(section => (
                <article key={section.title} className="legal-article">
                  <h4>{section.title}</h4>
                  <p>{section.body}</p>
                </article>
              ))}
            </section>

            <section aria-labelledby="legal-terms-heading" className="legal-section-group">
              <h3 id="legal-terms-heading">2. Termini della beta sperimentale</h3>
              {data.termsSections.map(section => (
                <article key={section.title} className="legal-article">
                  <h4>{section.title}</h4>
                  <p>{section.body}</p>
                </article>
              ))}
            </section>
          </div>
        )}
      </div>
    </div>
  )
}

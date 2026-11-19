import { useEffect, useState } from 'react'
import { getReadiness, getSystemStatus } from '../shared/api/system'
import './foundation.css'

type Check = { phase: 'loading' | 'ready' | 'error'; detail: string }

const loading: Check = { phase: 'loading', detail: 'Verifica in corso…' }

function CheckCard({ title, state, endpoint }: { title: string; state: Check; endpoint: string }) {
  return <section className={`foundation-check ${state.phase}`}>
    <span className="foundation-check-icon" aria-hidden="true">{state.phase === 'ready' ? '✓' : state.phase === 'error' ? '!' : '·'}</span>
    <div><h2>{title}</h2><p>{state.detail}</p><code>{endpoint}</code></div>
    <span className="foundation-check-label">{state.phase === 'ready' ? 'COLLEGATO' : state.phase === 'error' ? 'DA VERIFICARE' : 'ATTENDI'}</span>
  </section>
}

export default function FoundationPage() {
  const [api, setApi] = useState<Check>(loading)
  const [database, setDatabase] = useState<Check>(loading)
  const [revision, setRevision] = useState(0)
  const busy = api.phase === 'loading' || database.phase === 'loading'

  useEffect(() => {
    const controller = new AbortController()
    async function load() {
      const [service, ready] = await Promise.allSettled([
        getSystemStatus(controller.signal),
        getReadiness(controller.signal),
      ])
      if (controller.signal.aborted) return
      setApi(service.status === 'fulfilled'
        ? { phase: 'ready', detail: `${service.value.service} · API ${service.value.apiVersion}` }
        : { phase: 'error', detail: service.reason instanceof Error ? service.reason.message : 'Connessione API fallita.' })
      setDatabase(ready.status === 'fulfilled'
        ? { phase: 'ready', detail: 'Connessione verificata e migrazioni aggiornate.' }
        : { phase: 'error', detail: ready.reason instanceof Error ? ready.reason.message : 'Verifica del database fallita.' })
    }
    void load()
    return () => controller.abort()
  }, [revision])

  function retry() {
    setApi(loading)
    setDatabase(loading)
    setRevision(value => value + 1)
  }

  return <main className="foundation-page">
    <header className="foundation-header">
      <img src="/assets/brand/siculingo-logo.png" alt="Siculingo" width="172" height="64" />
      <span>SVILUPPO LOCALE · NON È ANCORA LA BETA</span>
    </header>
    <div className="foundation-content">
      <span className="foundation-eyebrow">TAPPA 2 · FONDAZIONI TECNICHE</span>
      <h1>Le radici ci sono.<br /><span>Ora si cresce.</span></h1>
      <p className="foundation-intro">Questa pagina parla con il backend reale. Nessun account o progresso viene simulato: stiamo preparando la base per le prossime lezioni.</p>
      <div className="foundation-checks" aria-live="polite">
        <CheckCard title="Backend ASP.NET Core 10" state={api} endpoint="/api/v1/system/status" />
        <CheckCard title="SQL Server · Entity Framework Core 10" state={database} endpoint="/health/ready" />
      </div>
      <button className="foundation-refresh" onClick={retry} disabled={busy}>{busy ? 'Collegamento in corso…' : 'Verifica di nuovo'}</button>
      {import.meta.env.DEV && <section className="foundation-preview">
        <img src="/assets/mascot/ficodi-front.png" alt="" width="112" height="128" />
        <div><span className="foundation-eyebrow">DIREZIONE SCELTA · A, IL SENTIERO</span><h2>Uno sguardo al viaggio.</h2><p>Rivedi le quattro schermate guida. La preview grafica resta separata dai dati reali.</p><a href="/prototype?screen=percorso&variant=A">Apri l’anteprima grafica <span aria-hidden="true">→</span></a></div>
      </section>}
      <footer><a href="/">Torna all’app e agli account reali</a>. Lezioni e progresso arrivano nelle prossime tappe. Nessuna risorsa Azure è stata creata.</footer>
    </div>
  </main>
}

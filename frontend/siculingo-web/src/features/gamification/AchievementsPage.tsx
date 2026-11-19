import { useEffect, useState } from 'react'
import { ApiError, errorMessage, userAchievements, type AchievementsCatalog } from '../../shared/api/accounts'

type CatalogState =
  | { kind: 'loading' }
  | { kind: 'ready'; catalog: AchievementsCatalog }
  | { kind: 'error'; message: string }

function toNumber(value: number | string): number {
  return typeof value === 'number' ? value : Number(value)
}

const CATEGORIES: Array<{ id: string; label: string }> = [
  { id: 'tutti', label: 'Tutti' },
  { id: 'apprendimento', label: 'Apprendimento' },
  { id: 'costanza', label: 'Costanza' },
  { id: 'precisione', label: 'Precisione' },
  { id: 'esplorazione', label: 'Esplorazione' },
]

function formatRarityLabel(rarity: string): string {
  switch (rarity) {
    case 'raro':
      return 'Raro'
    case 'epico':
      return 'Epico'
    case 'leggendario':
      return 'Leggendario'
    default:
      return 'Comune'
  }
}

export default function AchievementsPage({
  onExpired,
  onGoLearn,
}: {
  onExpired: () => void
  onGoLearn: () => void
}) {
  const [state, setState] = useState<CatalogState>({ kind: 'loading' })
  const [category, setCategory] = useState('tutti')
  const [revision, setRevision] = useState(0)

  useEffect(() => {
    const controller = new AbortController()
    userAchievements(controller.signal)
      .then(catalog => {
        if (!controller.signal.aborted) setState({ kind: 'ready', catalog })
      })
      .catch((err: unknown) => {
        if (controller.signal.aborted) return
        if (err instanceof ApiError && err.status === 401) onExpired()
        else setState({ kind: 'error', message: errorMessage(err) })
      })
    return () => controller.abort()
  }, [revision, onExpired])

  const filtered =
    state.kind === 'ready'
      ? state.catalog.achievements.filter(item => category === 'tutti' || item.category === category)
      : []

  return (
    <div className="learning-page">
      <header className="learning-heading trophy-hero">
        <div>
          <span className="account-eyebrow">BACHECA DI FICODÌ</span>
          <h1>I tuoi trofei</h1>
          <p>Ogni traguardo racconta la tua costanza, la precisione e le tappe esplorate sul sentiero.</p>
        </div>
        {state.kind === 'ready' && (
          <div className="trophy-counter-badge">
            <img src="/assets/icons/trophy.png" alt="" width="38" height="38" />
            <div>
              <strong>
                {toNumber(state.catalog.unlockedCount)} / {toNumber(state.catalog.totalCount)}
              </strong>
              <span>sbloccati</span>
            </div>
          </div>
        )}
      </header>

      <div className="trophy-filters" role="tablist" aria-label="Filtra trofei per categoria">
        {CATEGORIES.map(tab => (
          <button
            key={tab.id}
            type="button"
            role="tab"
            aria-selected={category === tab.id}
            className={`trophy-filter-pill ${category === tab.id ? 'active' : ''}`}
            onClick={() => setCategory(tab.id)}
          >
            {tab.label}
          </button>
        ))}
      </div>

      {state.kind === 'loading' && (
        <section className="learning-panel">
          <p role="status">Caricamento dei trofei…</p>
        </section>
      )}

      {state.kind === 'error' && (
        <div className="account-error" role="alert">
          <p>{state.message}</p>
          <button
            type="button"
            className="account-secondary"
            onClick={() => {
              setState({ kind: 'loading' })
              setRevision(v => v + 1)
            }}
          >
            Riprova
          </button>
        </div>
      )}

      {state.kind === 'ready' && (
        <section className="trophy-grid" aria-live="polite">
          {filtered.map(item => {
            const current = toNumber(item.currentProgress)
            const target = toNumber(item.targetProgress)
            const percent = toNumber(item.progressPercent)
            return (
              <article
                key={item.code}
                className={`trophy-card ${item.isUnlocked ? 'unlocked' : 'locked'} rarity-${item.rarity}`}
              >
                <div className="trophy-card-icon">
                  <img src={item.iconPath} alt="" width="44" height="44" />
                </div>
                <div className="trophy-card-body">
                  <div className="trophy-card-meta">
                    <span className={`trophy-rarity-badge rarity-${item.rarity}`}>{formatRarityLabel(item.rarity)}</span>
                    <span className="trophy-status-tag">
                      {item.isUnlocked ? 'Sbloccato' : `${current} / ${target}`}
                    </span>
                  </div>
                  <h2>{item.name}</h2>
                  <p>{item.description}</p>
                  <div
                    className="trophy-progress-bar"
                    role="progressbar"
                    aria-label={`Progresso ${item.name}`}
                    aria-valuemin={0}
                    aria-valuemax={target}
                    aria-valuenow={current}
                  >
                    <span style={{ width: `${percent}%` }} />
                  </div>
                  {item.isUnlocked && item.unlockedAtUtc && (
                    <small className="trophy-date">
                      Ottenuto il {new Date(item.unlockedAtUtc).toLocaleDateString('it-IT')}
                    </small>
                  )}
                </div>
              </article>
            )
          })}
        </section>
      )}

      <div className="trophy-cta">
        <button type="button" className="account-secondary" onClick={onGoLearn}>
          Torna al sentiero per sbloccare nuovi trofei
        </button>
      </div>
    </div>
  )
}

import { useEffect, useState } from 'react'
import {
  ApiError,
  errorMessage,
  gamificationSummary,
  type Account,
  type GamificationSummary,
} from '../../shared/api/accounts'

type SummaryState =
  | { kind: 'loading' }
  | { kind: 'ready'; summary: GamificationSummary }
  | { kind: 'error'; message: string }

function toNumber(value: number | string | null | undefined): number {
  if (typeof value === 'number') return value
  if (typeof value === 'string') return Number(value)
  return 0
}

export default function ProfileDashboardPage({
  account,
  onExpired,
  onOpenSettings,
  onOpenTrophies,
}: {
  account: Account
  onExpired: () => void
  onOpenSettings: () => void
  onOpenTrophies: () => void
}) {
  const [state, setState] = useState<SummaryState>({ kind: 'loading' })
  const [revision, setRevision] = useState(0)

  useEffect(() => {
    const controller = new AbortController()
    gamificationSummary(controller.signal)
      .then(summary => {
        if (!controller.signal.aborted) setState({ kind: 'ready', summary })
      })
      .catch((err: unknown) => {
        if (controller.signal.aborted) return
        if (err instanceof ApiError && err.status === 401) onExpired()
        else setState({ kind: 'error', message: errorMessage(err) })
      })
    return () => controller.abort()
  }, [revision, onExpired])

  return (
    <div className="learning-page">
      <header className="profile-hero-card">
        <div className="profile-avatar-wrap">
          <img src="/assets/mascot/ficodi-proud.png" alt="Ficodì sul tuo profilo" width="84" height="84" />
        </div>
        <div className="profile-identity">
          <span className="account-eyebrow">PROFILO STUDENTE · @{account.username}</span>
          <h1>{account.displayName}</h1>
          {state.kind === 'ready' && (
            <p>
              Livello {toNumber(state.summary.currentLevel)} · <strong>{state.summary.levelTitle}</strong> · Fuso{' '}
              <code>{state.summary.timeZoneId}</code>
            </p>
          )}
        </div>
        <button type="button" className="account-secondary" onClick={onOpenSettings}>
          Impostazioni account
        </button>
      </header>

      {state.kind === 'loading' && (
        <section className="learning-panel">
          <p role="status">Caricamento delle statistiche personali…</p>
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
        <>
          <section className="profile-stats-grid" aria-label="Statistiche principali">
            <article className="profile-stat-tile">
              <img src="/assets/badges/xp.png" alt="" width="34" height="34" />
              <span>XP TOTALI</span>
              <strong>{toNumber(state.summary.totalXp)} XP</strong>
              <small>Settimana in corso: +{toNumber(state.summary.weeklyXp)} XP</small>
            </article>

            <article className="profile-stat-tile">
              <img src="/assets/icons/flame.png" alt="" width="34" height="34" />
              <span>SERIE ATTUALE</span>
              <strong>
                {toNumber(state.summary.currentStreak)}{' '}
                {toNumber(state.summary.currentStreak) === 1 ? 'giorno' : 'giorni'}
              </strong>
              <small>Record personale: {toNumber(state.summary.bestStreak)} gg</small>
            </article>

            <article className="profile-stat-tile">
              <img src="/assets/icons/book.png" alt="" width="34" height="34" />
              <span>TAPPE SUPERATE</span>
              <strong>{toNumber(state.summary.uniqueLessonsCompleted)}</strong>
              <small>
                Sessioni completate: {toNumber(state.summary.totalLessonCompletions)} · Perfette:{' '}
                {toNumber(state.summary.perfectLessonsCount)}
              </small>
            </article>

            <article className="profile-stat-tile">
              <img src="/assets/icons/goal.png" alt="" width="34" height="34" />
              <span>SERIE DI RISPOSTE ESATTE</span>
              <strong>{toNumber(state.summary.bestConsecutiveCorrectAnswers)}</strong>
              <small>
                Ripassi premiati oggi: {toNumber(state.summary.rewardedReviewsToday)} /{' '}
                {toNumber(state.summary.dailyRewardedReviewCap)}
              </small>
            </article>
          </section>

          <section className="learning-panel profile-ledger-panel">
            <div className="learning-unit-header">
              <div>
                <h2>Ultimi accrediti XP</h2>
                <p className="learning-stage-note">
                  Registro verificato lato server: ogni completamento accredita i punti una sola volta.
                </p>
              </div>
              <button type="button" className="account-text-button" onClick={onOpenTrophies}>
                Trofei ({toNumber(state.summary.unlockedAchievementsCount)}/{toNumber(state.summary.totalAchievementsCount)}) →
              </button>
            </div>

            {state.summary.recentXpEvents.length === 0 ? (
              <p className="learning-stage-note">
                Completa la tua prima lezione sul sentiero per inaugurare il diario dei punti esperienza!
              </p>
            ) : (
              <ul className="profile-ledger-list">
                {state.summary.recentXpEvents.map(entry => (
                  <li key={entry.id}>
                    <div>
                      <strong>{entry.label}</strong>
                      <span>Giornata {entry.localActivityDate}</span>
                    </div>
                    <span className="profile-ledger-xp">+{toNumber(entry.amount)} XP</span>
                  </li>
                ))}
              </ul>
            )}
          </section>
        </>
      )}
    </div>
  )
}

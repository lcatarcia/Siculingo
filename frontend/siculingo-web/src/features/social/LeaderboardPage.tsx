import { useEffect, useState } from 'react'
import {
  ApiError,
  errorMessage,
  weeklyLeaderboard,
  type LeaderboardData,
  type LeaderboardEntry,
} from '../../shared/api/accounts'
import PublicProfileModal from './PublicProfileModal'

function toNumber(value: number | string | null | undefined): number {
  if (typeof value === 'number') return value
  if (typeof value === 'string') return Number(value)
  return 0
}

function zoneBadgeLabel(zone: string, tierCode: string): string | null {
  if (zone === 'promotion') {
    return tierCode === 'bronze' ? 'Promozione in Argento' : 'Promozione in Oro'
  }
  if (zone === 'demotion') {
    return tierCode === 'gold' ? 'Rischio discesa in Argento' : 'Rischio discesa in Bronzo'
  }
  return null
}

export default function LeaderboardPage({
  onExpired,
  onOpenSettings,
  onOpenFriends,
  onGoLearn,
}: {
  onExpired: () => void
  onOpenSettings: () => void
  onOpenFriends: () => void
  onGoLearn: () => void
}) {
  const [scope, setScope] = useState<'league' | 'friends'>('league')
  const [state, setState] = useState<
    | { kind: 'loading' }
    | { kind: 'ready'; data: LeaderboardData }
    | { kind: 'error'; message: string }
  >({ kind: 'loading' })
  const [revision, setRevision] = useState(0)
  const [selectedUsername, setSelectedUsername] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    weeklyLeaderboard(scope, controller.signal)
      .then(data => {
        if (!controller.signal.aborted) setState({ kind: 'ready', data })
      })
      .catch((err: unknown) => {
        if (controller.signal.aborted) return
        if (err instanceof ApiError && err.status === 401) onExpired()
        else setState({ kind: 'error', message: errorMessage(err) })
      })
    return () => controller.abort()
  }, [scope, revision, onExpired])

  function switchScope(nextScope: 'league' | 'friends') {
    if (nextScope === scope) return
    setScope(nextScope)
    setState({ kind: 'loading' })
  }

  function reloadLeaderboard() {
    setState({ kind: 'loading' })
    setRevision(val => val + 1)
  }

  function renderRow(entry: LeaderboardEntry, tierCode: string, isLeagueScope: boolean) {
    const rank = toNumber(entry.rank)
    const weeklyXp = toNumber(entry.weeklyXp)
    const zoneTag = isLeagueScope ? zoneBadgeLabel(entry.zone, tierCode) : null

    return (
      <li
        key={entry.userId}
        className={`leaderboard-row ${entry.isCurrentUser ? 'self' : ''} zone-${entry.zone}`}
      >
        <span className={`leaderboard-rank rank-${rank <= 3 ? rank : 'std'}`}>{rank}</span>
        <div className="leaderboard-identity">
          <div className="leaderboard-name-line">
            {entry.canViewProfile && !entry.isCurrentUser ? (
              <button
                type="button"
                className="leaderboard-profile-link"
                onClick={() => setSelectedUsername(entry.username)}
              >
                {entry.displayName}
              </button>
            ) : (
              <strong>{entry.displayName}</strong>
            )}
            {entry.isCurrentUser && <span className="social-mini-chip self">Tu</span>}
            {entry.isFriend && !entry.isCurrentUser && <span className="social-mini-chip friend">Amico</span>}
          </div>
          <small>
            @{entry.username} · Livello {toNumber(entry.currentLevel)} ({entry.levelTitle})
            {zoneTag ? ` · ${zoneTag}` : ''}
          </small>
        </div>
        <strong className="leaderboard-xp">{weeklyXp} XP</strong>
      </li>
    )
  }

  return (
    <div className="learning-page">
      <header className="learning-heading">
        <span className="account-eyebrow">SFIDA SETTIMANALE · EUROPE/ROME</span>
        <h1>Classifiche e leghe</h1>
        <p>
          Ogni lunedì a mezzanotte si chiude la settimana: i primi 10 con XP &gt; 0 salgono di lega, gli ultimi 5
          scendono di un gradino (mai sotto Bronzo).
        </p>
      </header>

      <div className="trophy-filters" role="tablist" aria-label="Ambito classifica">
        <button
          type="button"
          role="tab"
          aria-selected={scope === 'league'}
          className={`trophy-filter-pill ${scope === 'league' ? 'active' : ''}`}
          onClick={() => switchScope('league')}
        >
          La tua lega
        </button>
        <button
          type="button"
          role="tab"
          aria-selected={scope === 'friends'}
          className={`trophy-filter-pill ${scope === 'friends' ? 'active' : ''}`}
          onClick={() => switchScope('friends')}
        >
          Tra amici
        </button>
      </div>

      {state.kind === 'loading' && (
        <section className="learning-panel">
          <p role="status">Aggiornamento della classifica settimanale…</p>
        </section>
      )}

      {state.kind === 'error' && (
        <div className="account-error" role="alert">
          <p>{state.message}</p>
          <button
            type="button"
            className="account-secondary"
            onClick={reloadLeaderboard}
          >
            Riprova
          </button>
        </div>
      )}

      {state.kind === 'ready' && (
        <>
          <section className={`league-banner-card tier-${state.data.currentTierCode}`}>
            <img src="/assets/badges/shield.png" alt="" width="58" height="58" />
            <div>
              <span className="account-eyebrow">
                SETTIMANA {state.data.weekStartDateLocal} → {state.data.weekEndDateLocal}
              </span>
              <h2>Lega {state.data.currentTierName}</h2>
              <p>
                {scope === 'league'
                  ? `${toNumber(state.data.totalParticipants)} partecipanti attivi in Lega ${state.data.currentTierName}. Top ${toNumber(state.data.promotionSlots)} promossi · Ultimi ${toNumber(state.data.demotionSlots)} retrocessi.`
                  : 'Confronto amichevole sugli XP guadagnati da lunedì a domenica con i tuoi amici confermati.'}
              </p>
            </div>
            <div className="league-tier-steps" aria-label="Livelli delle leghe">
              <span className={state.data.currentTierCode === 'bronze' ? 'current' : ''}>Bronzo</span>
              <span className={state.data.currentTierCode === 'silver' ? 'current' : ''}>Argento</span>
              <span className={state.data.currentTierCode === 'gold' ? 'current' : ''}>Oro</span>
            </div>
          </section>

          {state.data.lastSettledWeekStartLocal &&
            (state.data.lastWeekOutcome === 'promoted' || state.data.lastWeekOutcome === 'demoted') && (
              <aside className="account-notice" role="status">
                {state.data.lastWeekOutcome === 'promoted'
                  ? `Complimenti! Con i risultati della settimana del ${state.data.lastSettledWeekStartLocal} hai conquistato la promozione in Lega ${state.data.currentTierName}.`
                  : `Settimana del ${state.data.lastSettledWeekStartLocal} conclusa: riparti dalla Lega ${state.data.currentTierName} pronto a risalire!`}
              </aside>
            )}

          {!state.data.isParticipatingInLeaderboard && (
            <aside className="learning-resume-banner" aria-label="Partecipazione classifica disattivata">
              <img src="/assets/mascot/ficodi-study.png" alt="" width="48" height="56" />
              <div>
                <span className="account-eyebrow">PRIVACY CLASSIFICA ATTIVA</span>
                <strong>Il tuo profilo è nascosto dalle classifiche pubbliche</strong>
                <p>
                  Puoi riattivare in qualsiasi momento la partecipazione alle leghe settimanali dalle impostazioni
                  privacy.
                </p>
              </div>
              <button type="button" className="account-secondary" onClick={onOpenSettings}>
                Impostazioni privacy
              </button>
            </aside>
          )}

          <section className="learning-panel leaderboard-panel">
            {state.data.entries.length === 0 ? (
              <div className="learning-empty">
                <img src="/assets/mascot/ficodi-proud.png" alt="" width="96" height="110" />
                <h2>Nessun partecipante in questa vista</h2>
                <p>
                  {scope === 'friends'
                    ? 'Aggiungi i tuoi primi amici per confrontare gli XP settimanali insieme!'
                    : 'Completa una lezione per inaugurare la classifica di questa settimana.'}
                </p>
                <button
                  type="button"
                  className="account-primary admin-inline-primary"
                  onClick={scope === 'friends' ? onOpenFriends : onGoLearn}
                >
                  {scope === 'friends' ? 'Cerca amici' : 'Vai alle lezioni'}
                </button>
              </div>
            ) : (
              <>
                <ol className="leaderboard-list">
                  {state.data.entries.map(entry =>
                    renderRow(entry, state.data.currentTierCode, scope === 'league'),
                  )}
                </ol>

                {state.data.currentUserEntry &&
                  !state.data.entries.some(item => item.userId === state.data.currentUserEntry?.userId) && (
                    <div className="leaderboard-outside-top">
                      <span className="account-eyebrow">LA TUA POSIZIONE ATTUALE</span>
                      <ol className="leaderboard-list">
                        {renderRow(
                          state.data.currentUserEntry,
                          state.data.currentTierCode,
                          scope === 'league',
                        )}
                      </ol>
                    </div>
                  )}
              </>
            )}
          </section>
        </>
      )}

      {selectedUsername && (
        <PublicProfileModal
          key={selectedUsername}
          username={selectedUsername}
          onClose={() => setSelectedUsername(null)}
          onExpired={onExpired}
          onSocialMutated={reloadLeaderboard}
        />
      )}
    </div>
  )
}

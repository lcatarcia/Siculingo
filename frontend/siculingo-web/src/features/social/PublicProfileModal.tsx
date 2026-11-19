import { useEffect, useState } from 'react'
import {
  acceptFriendRequest,
  ApiError,
  errorMessage,
  publicProfile,
  sendFriendRequest,
  type PublicProfile,
} from '../../shared/api/accounts'

function toNumber(value: number | string | null | undefined): number {
  if (typeof value === 'number') return value
  if (typeof value === 'string') return Number(value)
  return 0
}

export default function PublicProfileModal({
  username,
  onClose,
  onExpired,
  onSocialMutated,
}: {
  username: string
  onClose: () => void
  onExpired: () => void
  onSocialMutated?: () => void
}) {
  const [state, setState] = useState<
    | { kind: 'loading' }
    | { kind: 'ready'; profile: PublicProfile }
    | { kind: 'error'; message: string }
  >({ kind: 'loading' })
  const [busy, setBusy] = useState(false)
  const [actionNotice, setActionNotice] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    publicProfile(username, controller.signal)
      .then(profile => {
        if (!controller.signal.aborted) setState({ kind: 'ready', profile })
      })
      .catch((err: unknown) => {
        if (controller.signal.aborted) return
        if (err instanceof ApiError && err.status === 401) onExpired()
        else setState({ kind: 'error', message: errorMessage(err) })
      })
    return () => controller.abort()
  }, [username, onExpired])

  useEffect(() => {
    function onKey(event: KeyboardEvent) {
      if (event.key === 'Escape' && !busy) onClose()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose, busy])

  async function handleFriendAction() {
    if (state.kind !== 'ready') return
    const profile = state.profile
    setBusy(true)
    setActionNotice(null)
    try {
      if (profile.relationshipStatus === 'incoming_pending' && profile.friendshipId) {
        await acceptFriendRequest(profile.friendshipId)
        setActionNotice('Richiesta accettata: ora siete amici!')
      } else {
        const res = await sendFriendRequest(profile.userId)
        setActionNotice(
          res.autoAccepted
            ? 'Richiesta reciproca accettata in automatico: ora siete amici!'
            : 'Richiesta di amicizia inviata.',
        )
      }
      const refreshed = await publicProfile(username)
      setState({ kind: 'ready', profile: refreshed })
      onSocialMutated?.()
    } catch (err: unknown) {
      if (err instanceof ApiError && err.status === 401) onExpired()
      else setActionNotice(errorMessage(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div
      className="lesson-modal-backdrop"
      onClick={event => {
        if (event.target === event.currentTarget && !busy) onClose()
      }}
    >
      <div
        className="lesson-modal social-profile-modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby="public-profile-heading"
      >
        {state.kind === 'loading' && <p role="status">Caricamento profilo di @{username}…</p>}

        {state.kind === 'error' && (
          <>
            <h2 id="public-profile-heading">Profilo non visibile</h2>
            <p>{state.message}</p>
            <button type="button" className="account-secondary" onClick={onClose}>
              Chiudi
            </button>
          </>
        )}

        {state.kind === 'ready' && (
          <>
            <div className="social-profile-header">
              <img src="/assets/mascot/ficodi-happy.png" alt="" width="64" height="74" />
              <div>
                <span className={`league-tier-pill tier-${state.profile.leagueTierCode}`}>
                  Lega {state.profile.leagueTierName}
                </span>
                <h2 id="public-profile-heading">{state.profile.displayName}</h2>
                <p className="social-username-tag">
                  @{state.profile.username} · Livello {toNumber(state.profile.currentLevel)} ({state.profile.levelTitle})
                </p>
              </div>
            </div>

            <div className="social-profile-metrics">
              <div>
                <span>XP TOTALI</span>
                <strong>{toNumber(state.profile.totalXp)}</strong>
              </div>
              <div>
                <span>XP SETTIMANA</span>
                <strong>
                  {state.profile.weeklyXp === null ? 'Riservati' : `+${toNumber(state.profile.weeklyXp)}`}
                </strong>
              </div>
              <div>
                <span>SERIE ATTIVA</span>
                <strong>{toNumber(state.profile.currentStreak)} gg</strong>
              </div>
              <div>
                <span>LEZIONI</span>
                <strong>{toNumber(state.profile.uniqueLessonsCompleted)}</strong>
              </div>
            </div>

            {state.profile.recentAchievements.length > 0 && (
              <div className="social-profile-trophies">
                <span className="account-eyebrow">
                  TROFEI SBLOCCATI ({toNumber(state.profile.unlockedAchievementsCount)})
                </span>
                <ul>
                  {state.profile.recentAchievements.map(badge => (
                    <li key={badge.code}>
                      <img src={badge.iconPath} alt="" width="26" height="26" />
                      <span>{badge.name}</span>
                    </li>
                  ))}
                </ul>
              </div>
            )}

            {actionNotice && (
              <p className="account-notice" role="status">
                {actionNotice}
              </p>
            )}

            <div className="lesson-modal-actions">
              {state.profile.relationshipStatus === 'friends' && (
                <span className="social-relation-chip friends">✓ Siete amici</span>
              )}
              {state.profile.relationshipStatus === 'outgoing_pending' && (
                <span className="social-relation-chip pending">Richiesta di amicizia in attesa</span>
              )}
              {state.profile.canRequestFriendship && (
                <button
                  type="button"
                  className="account-primary"
                  disabled={busy}
                  onClick={() => void handleFriendAction()}
                >
                  {state.profile.relationshipStatus === 'incoming_pending'
                    ? 'Accetta richiesta di amicizia'
                    : 'Aggiungi agli amici'}
                </button>
              )}
              <button type="button" className="account-secondary" disabled={busy} onClick={onClose}>
                Chiudi
              </button>
            </div>
          </>
        )}
      </div>
    </div>
  )
}

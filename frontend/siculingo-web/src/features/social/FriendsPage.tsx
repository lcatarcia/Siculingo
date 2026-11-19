import { useEffect, useState, type FormEvent } from 'react'
import {
  acceptFriendRequest,
  ApiError,
  blockSocialUser,
  errorMessage,
  rejectFriendRequest,
  removeFriend,
  searchSocialUsers,
  sendFriendRequest,
  socialOverview,
  unblockSocialUser,
  type SocialOverview,
  type UserSearchItem,
} from '../../shared/api/accounts'
import PublicProfileModal from './PublicProfileModal'

function toNumber(value: number | string | null | undefined): number {
  if (typeof value === 'number') return value
  if (typeof value === 'string') return Number(value)
  return 0
}

export default function FriendsPage({
  onExpired,
  onOpenSettings,
}: {
  onExpired: () => void
  onOpenSettings: () => void
}) {
  const [state, setState] = useState<
    | { kind: 'loading' }
    | { kind: 'ready'; overview: SocialOverview }
    | { kind: 'error'; message: string }
  >({ kind: 'loading' })
  const [revision, setRevision] = useState(0)
  const [searchQuery, setSearchQuery] = useState('')
  const [searchResults, setSearchResults] = useState<UserSearchItem[] | null>(null)
  const [searching, setSearching] = useState(false)
  const [busyAction, setBusyAction] = useState<string | null>(null)
  const [feedback, setFeedback] = useState<{ kind: 'notice' | 'error'; text: string } | null>(null)
  const [selectedUsername, setSelectedUsername] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    socialOverview(controller.signal)
      .then(overview => {
        if (!controller.signal.aborted) setState({ kind: 'ready', overview })
      })
      .catch((err: unknown) => {
        if (controller.signal.aborted) return
        if (err instanceof ApiError && err.status === 401) onExpired()
        else setState({ kind: 'error', message: errorMessage(err) })
      })
    return () => controller.abort()
  }, [revision, onExpired])

  async function refreshAll(preserveSearch = true) {
    setRevision(val => val + 1)
    if (preserveSearch && searchQuery.trim().length >= 2) {
      try {
        const result = await searchSocialUsers(searchQuery.trim())
        setSearchResults(result.users)
      } catch {
        // Keep current view if refresh search fails
      }
    }
  }

  async function handleSearch(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const trimmed = searchQuery.trim()
    if (trimmed.length < 2) {
      setFeedback({ kind: 'error', text: 'Inserisci almeno 2 caratteri per cercare un compagno di studio.' })
      return
    }
    setSearching(true)
    setFeedback(null)
    try {
      const response = await searchSocialUsers(trimmed)
      setSearchResults(response.users)
    } catch (err: unknown) {
      if (err instanceof ApiError && err.status === 401) onExpired()
      else setFeedback({ kind: 'error', text: errorMessage(err) })
    } finally {
      setSearching(false)
    }
  }

  async function runAction(key: string, work: () => Promise<string | null>) {
    setBusyAction(key)
    setFeedback(null)
    try {
      const message = await work()
      if (message) setFeedback({ kind: 'notice', text: message })
      await refreshAll()
    } catch (err: unknown) {
      if (err instanceof ApiError && err.status === 401) onExpired()
      else setFeedback({ kind: 'error', text: errorMessage(err) })
    } finally {
      setBusyAction(null)
    }
  }

  return (
    <div className="learning-page">
      <header className="learning-heading social-heading-row">
        <div>
          <span className="account-eyebrow">COMUNITÀ LEGGERA · SENZA CHAT</span>
          <h1>I tuoi amici</h1>
          <p>Aggiungi compagni di percorso, segui serie e leghe e gestisci in ogni momento privacy e blocchi.</p>
        </div>
        <button type="button" className="account-secondary" onClick={onOpenSettings}>
          Impostazioni privacy
        </button>
      </header>

      {feedback && (
        <div className={feedback.kind === 'error' ? 'account-error' : 'account-notice'} role="status">
          {feedback.text}
        </div>
      )}

      <section className="learning-panel social-section-panel" aria-labelledby="friend-search-heading">
        <h2 id="friend-search-heading">Cerca compagni di studio</h2>
        <form className="social-search-form" onSubmit={event => void handleSearch(event)}>
          <input
            type="search"
            value={searchQuery}
            onChange={event => setSearchQuery(event.target.value)}
            placeholder="Cerca per username o nome visualizzato…"
            maxLength={40}
            aria-label="Username o nome visualizzato da cercare"
          />
          <button type="submit" className="account-primary admin-inline-primary" disabled={searching}>
            {searching ? 'Ricerca…' : 'Cerca'}
          </button>
          {searchResults !== null && (
            <button
              type="button"
              className="account-secondary"
              onClick={() => {
                setSearchResults(null)
                setSearchQuery('')
              }}
            >
              Pulisci
            </button>
          )}
        </form>

        {searchResults !== null && (
          <div className="social-search-results">
            {searchResults.length === 0 ? (
              <p className="admin-muted-copy">
                Nessun profilo pubblico trovato per «{searchQuery.trim()}». Gli account privati o bloccati non vengono
                mostrati.
              </p>
            ) : (
              <ul className="social-user-list">
                {searchResults.map(user => (
                  <li key={user.userId} className="social-user-card">
                    <div>
                      <div className="leaderboard-name-line">
                        {user.canViewProfile ? (
                          <button
                            type="button"
                            className="leaderboard-profile-link"
                            onClick={() => setSelectedUsername(user.username)}
                          >
                            {user.displayName}
                          </button>
                        ) : (
                          <strong>{user.displayName}</strong>
                        )}
                        {user.relationshipStatus === 'friends' && (
                          <span className="social-mini-chip friend">Amico</span>
                        )}
                        {user.relationshipStatus === 'outgoing_pending' && (
                          <span className="social-mini-chip">In attesa</span>
                        )}
                      </div>
                      <small>
                        @{user.username} · Livello {toNumber(user.currentLevel)} ({user.levelTitle})
                      </small>
                    </div>
                    <div className="admin-action-row">
                      {user.canViewProfile && (
                        <button
                          type="button"
                          className="account-secondary admin-mini-btn"
                          onClick={() => setSelectedUsername(user.username)}
                        >
                          Profilo
                        </button>
                      )}
                      {user.canRequestFriendship && (
                        <button
                          type="button"
                          className="account-primary admin-inline-primary admin-mini-btn"
                          disabled={busyAction !== null}
                          onClick={() =>
                            void runAction(`req-${user.userId}`, async () => {
                              const res = await sendFriendRequest(user.userId)
                              return res.autoAccepted
                                ? `Richiesta reciproca accettata: tu e ${user.displayName} siete amici!`
                                : `Richiesta inviata a ${user.displayName}.`
                            })
                          }
                        >
                          {user.relationshipStatus === 'incoming_pending' ? 'Accetta' : 'Aggiungi'}
                        </button>
                      )}
                      <button
                        type="button"
                        className="account-text-button social-inline-danger"
                        disabled={busyAction !== null}
                        onClick={() =>
                          void runAction(`block-${user.userId}`, async () => {
                            await blockSocialUser(user.userId)
                            return `Hai bloccato @${user.username}.`
                          })
                        }
                      >
                        Blocca
                      </button>
                    </div>
                  </li>
                ))}
              </ul>
            )}
          </div>
        )}
      </section>

      {state.kind === 'loading' && (
        <section className="learning-panel">
          <p role="status">Caricamento amici e richieste…</p>
        </section>
      )}

      {state.kind === 'error' && (
        <div className="account-error" role="alert">
          <p>{state.message}</p>
          <button type="button" className="account-secondary" onClick={() => setRevision(val => val + 1)}>
            Riprova
          </button>
        </div>
      )}

      {state.kind === 'ready' && (
        <>
          {(state.overview.incomingRequests.length > 0 || state.overview.outgoingRequests.length > 0) && (
            <section className="learning-panel social-section-panel" aria-labelledby="requests-heading">
              <h2 id="requests-heading">
                Richieste di amicizia ({state.overview.incomingRequests.length} in arrivo ·{' '}
                {state.overview.outgoingRequests.length} inviate)
              </h2>

              {state.overview.incomingRequests.length > 0 && (
                <ul className="social-user-list">
                  {state.overview.incomingRequests.map(req => (
                    <li key={req.friendshipId} className="social-user-card highlight-incoming">
                      <div>
                        <strong>{req.displayName}</strong>
                        <small>@{req.username} vuole aggiungerti agli amici</small>
                      </div>
                      <div className="admin-action-row">
                        <button
                          type="button"
                          className="account-primary admin-inline-primary admin-mini-btn"
                          disabled={busyAction !== null}
                          onClick={() =>
                            void runAction(`acc-${req.friendshipId}`, async () => {
                              await acceptFriendRequest(req.friendshipId)
                              return `Ora tu e ${req.displayName} siete amici!`
                            })
                          }
                        >
                          Accetta
                        </button>
                        <button
                          type="button"
                          className="account-secondary admin-mini-btn"
                          disabled={busyAction !== null}
                          onClick={() =>
                            void runAction(`rej-${req.friendshipId}`, async () => {
                              await rejectFriendRequest(req.friendshipId)
                              return `Richiesta di @${req.username} rifiutata.`
                            })
                          }
                        >
                          Rifiuta
                        </button>
                        <button
                          type="button"
                          className="account-text-button social-inline-danger"
                          disabled={busyAction !== null}
                          onClick={() =>
                            void runAction(`blk-${req.userId}`, async () => {
                              await blockSocialUser(req.userId)
                              return `Hai bloccato @${req.username}.`
                            })
                          }
                        >
                          Blocca
                        </button>
                      </div>
                    </li>
                  ))}
                </ul>
              )}

              {state.overview.outgoingRequests.length > 0 && (
                <ul className="social-user-list">
                  {state.overview.outgoingRequests.map(req => (
                    <li key={req.friendshipId} className="social-user-card">
                      <div>
                        <strong>{req.displayName}</strong>
                        <small>@{req.username} · Richiesta inviata in attesa di risposta</small>
                      </div>
                      <button
                        type="button"
                        className="account-secondary admin-mini-btn"
                        disabled={busyAction !== null}
                        onClick={() =>
                          void runAction(`cancel-${req.friendshipId}`, async () => {
                            await rejectFriendRequest(req.friendshipId)
                            return `Richiesta per @${req.username} annullata.`
                          })
                        }
                      >
                        Annulla richiesta
                      </button>
                    </li>
                  ))}
                </ul>
              )}
            </section>
          )}

          <section className="learning-panel social-section-panel" aria-labelledby="friends-list-heading">
            <div className="learning-unit-header">
              <h2 id="friends-list-heading">Amici confermati</h2>
              <span className="learning-unit-badge">{state.overview.friends.length} amici</span>
            </div>

            {state.overview.friends.length === 0 ? (
              <div className="learning-empty">
                <img src="/assets/mascot/ficodi-happy.png" alt="" width="96" height="110" />
                <h3>Ancora nessun amico in lista</h3>
                <p>Usa la casella qui sopra per cercare un compagno tramite username e seguire insieme i progressi.</p>
              </div>
            ) : (
              <ul className="social-user-list">
                {state.overview.friends.map(friend => (
                  <li key={friend.friendshipId} className="social-user-card">
                    <div>
                      <div className="leaderboard-name-line">
                        {friend.canViewProfile ? (
                          <button
                            type="button"
                            className="leaderboard-profile-link"
                            onClick={() => setSelectedUsername(friend.username)}
                          >
                            {friend.displayName}
                          </button>
                        ) : (
                          <strong>{friend.displayName}</strong>
                        )}
                        <span className={`league-tier-pill tier-${friend.leagueTierCode}`}>
                          Lega {friend.leagueTierName}
                        </span>
                      </div>
                      <small>
                        @{friend.username} · Livello {toNumber(friend.currentLevel)} ({friend.levelTitle}) · Serie{' '}
                        {toNumber(friend.currentStreak)} gg · Settimana +{toNumber(friend.weeklyXp)} XP
                      </small>
                    </div>
                    <div className="admin-action-row">
                      {friend.canViewProfile && (
                        <button
                          type="button"
                          className="account-secondary admin-mini-btn"
                          onClick={() => setSelectedUsername(friend.username)}
                        >
                          Profilo
                        </button>
                      )}
                      <button
                        type="button"
                        className="account-secondary admin-mini-btn"
                        disabled={busyAction !== null}
                        onClick={() =>
                          void runAction(`rm-${friend.userId}`, async () => {
                            await removeFriend(friend.userId)
                            return `@${friend.username} è stato rimosso dagli amici.`
                          })
                        }
                      >
                        Rimuovi
                      </button>
                      <button
                        type="button"
                        className="account-text-button social-inline-danger"
                        disabled={busyAction !== null}
                        onClick={() =>
                          void runAction(`blkf-${friend.userId}`, async () => {
                            await blockSocialUser(friend.userId)
                            return `Hai bloccato @${friend.username}.`
                          })
                        }
                      >
                        Blocca
                      </button>
                    </div>
                  </li>
                ))}
              </ul>
            )}
          </section>

          {state.overview.blockedUsers.length > 0 && (
            <section className="learning-panel social-section-panel" aria-labelledby="blocked-heading">
              <h2 id="blocked-heading">Utenti bloccati ({state.overview.blockedUsers.length})</h2>
              <p className="admin-muted-copy">
                Gli utenti bloccati non possono cercarti, vedere il tuo profilo né inviarti richieste di amicizia.
              </p>
              <ul className="social-user-list">
                {state.overview.blockedUsers.map(blocked => (
                  <li key={blocked.userId} className="social-user-card">
                    <div>
                      <strong>{blocked.displayName}</strong>
                      <small>@{blocked.username}</small>
                    </div>
                    <button
                      type="button"
                      className="account-secondary admin-mini-btn"
                      disabled={busyAction !== null}
                      onClick={() =>
                        void runAction(`unblk-${blocked.userId}`, async () => {
                          await unblockSocialUser(blocked.userId)
                          return `@${blocked.username} è stato sbloccato.`
                        })
                      }
                    >
                      Sblocca
                    </button>
                  </li>
                ))}
              </ul>
            </section>
          )}
        </>
      )}

      {selectedUsername && (
        <PublicProfileModal
          key={selectedUsername}
          username={selectedUsername}
          onClose={() => setSelectedUsername(null)}
          onExpired={onExpired}
          onSocialMutated={() => void refreshAll()}
        />
      )}
    </div>
  )
}

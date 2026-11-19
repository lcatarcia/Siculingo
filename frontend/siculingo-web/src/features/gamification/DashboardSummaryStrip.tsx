import type { GamificationSummary } from '../../shared/api/accounts'

function toNumber(value: number | string | null | undefined): number {
  if (typeof value === 'number') return value
  if (typeof value === 'string') return Number(value)
  return 0
}

function formatRegenHint(seconds: number | string | null | undefined): string {
  if (seconds === null || seconds === undefined) return 'Tutti pieni!'
  const totalSec = typeof seconds === 'number' ? seconds : Number(seconds)
  if (!Number.isFinite(totalSec) || totalSec <= 0) return 'Rigenerazione imminente'
  const hours = Math.floor(totalSec / 3600)
  const mins = Math.max(1, Math.ceil((totalSec % 3600) / 60))
  if (hours > 0) {
    return mins === 60 ? `+1 cuore tra ${hours + 1}h` : `+1 cuore tra ${hours}h ${mins}m`
  }
  return `+1 cuore tra ${mins} min`
}

export default function DashboardSummaryStrip({
  summary,
  onOpenTrophies,
  onOpenProfile,
  onStartPractice,
}: {
  summary: GamificationSummary
  onOpenTrophies: () => void
  onOpenProfile: () => void
  onStartPractice: () => void
}) {
  const totalXp = toNumber(summary.totalXp)
  const currentLevel = toNumber(summary.currentLevel)
  const progressPercent = toNumber(summary.levelProgressPercent)
  const xpNeeded = summary.xpNeededForNextLevel === null ? null : toNumber(summary.xpNeededForNextLevel)
  const currentStreak = toNumber(summary.currentStreak)
  const bestStreak = toNumber(summary.bestStreak)
  const todayXp = toNumber(summary.todayXp)
  const weeklyXp = toNumber(summary.weeklyXp)
  const unlockedTrophies = toNumber(summary.unlockedAchievementsCount)
  const totalTrophies = toNumber(summary.totalAchievementsCount)
  const currentHearts = toNumber(summary.hearts.currentHearts)
  const maxHearts = toNumber(summary.hearts.maxHearts)
  const heartsEnabled = summary.hearts.consumptionEnabled
  const missingHearts = heartsEnabled && currentHearts < maxHearts

  return (
    <section className="gamification-strip" aria-label="Riepilogo progresso">
      <div className="gamification-level-card">
        <div className="gamification-level-head">
          <div className="gamification-level-title">
            <img src="/assets/badges/xp.png" alt="" width="36" height="36" />
            <div>
              <span className="account-eyebrow">LIVELLO {currentLevel}</span>
              <strong>{summary.levelTitle}</strong>
            </div>
          </div>
          <button type="button" className="gamification-xp-pill" onClick={onOpenProfile}>
            {totalXp} XP totali
          </button>
        </div>

        <div
          className="gamification-xp-bar"
          role="progressbar"
          aria-label="Avanzamento verso il prossimo livello"
          aria-valuemin={0}
          aria-valuemax={100}
          aria-valuenow={progressPercent}
        >
          <span style={{ width: `${Math.max(progressPercent, totalXp > 0 ? 6 : 0)}%` }} />
        </div>

        <div className="gamification-level-foot">
          <span>
            {xpNeeded === null
              ? 'Livello massimo attuale raggiunto!'
              : `Mancano ${xpNeeded} XP al livello ${currentLevel + 1}`}
          </span>
          <span>Oggi +{todayXp} XP · Settimana +{weeklyXp} XP</span>
        </div>
      </div>

      <div className="gamification-stat-pills gamification-stat-pills-three">
        <button
          type="button"
          className={`gamification-stat-card ${currentHearts === 0 && heartsEnabled ? 'empty-hearts' : ''}`}
          onClick={missingHearts ? onStartPractice : onOpenProfile}
        >
          <img src="/assets/icons/heart.png" alt="" width="32" height="32" />
          <div>
            <span>CUORI</span>
            <strong>{heartsEnabled ? `${currentHearts} / ${maxHearts}` : 'Illimitati'}</strong>
            <small>
              {!heartsEnabled
                ? 'Consumo disattivato'
                : missingHearts
                  ? `${formatRegenHint(summary.hearts.secondsUntilNextHeart)} · Ripassa +1`
                  : 'Tutti carichi!'}
            </small>
          </div>
        </button>

        <button
          type="button"
          className={`gamification-stat-card ${summary.studiedToday ? 'active-streak' : ''}`}
          onClick={onOpenProfile}
        >
          <img src="/assets/icons/flame.png" alt="" width="34" height="34" />
          <div>
            <span>SERIE ATTIVA</span>
            <strong>
              {currentStreak} {currentStreak === 1 ? 'giorno' : 'giorni'}
            </strong>
            <small>{summary.studiedToday ? 'Mantenuta oggi!' : `Record: ${bestStreak} gg`}</small>
          </div>
        </button>

        <button type="button" className="gamification-stat-card" onClick={onOpenTrophies}>
          <img src="/assets/icons/trophy.png" alt="" width="34" height="34" />
          <div>
            <span>TROFEI</span>
            <strong>
              {unlockedTrophies} / {totalTrophies}
            </strong>
            <small>Apri bacheca →</small>
          </div>
        </button>
      </div>
    </section>
  )
}

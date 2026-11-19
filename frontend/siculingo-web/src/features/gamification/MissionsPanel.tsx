import type { MissionProgressItem, MissionsOverview } from '../../shared/api/accounts'

function toNumber(value: number | string | null | undefined): number {
  if (typeof value === 'number') return value
  if (typeof value === 'string') return Number(value)
  return 0
}

function MissionCard({ item }: { item: MissionProgressItem }) {
  const current = toNumber(item.currentValue)
  const target = Math.max(1, toNumber(item.targetValue))
  const reward = toNumber(item.rewardXp)
  const percent = Math.min(100, toNumber(item.progressPercent))

  return (
    <li className={`mission-item ${item.isCompleted ? 'completed' : ''}`}>
      <div className="mission-item-head">
        <div>
          <strong>{item.title}</strong>
          <p>{item.description}</p>
        </div>
        <span className={`mission-reward-pill ${item.isCompleted ? 'done' : ''}`}>
          {item.isCompleted ? `✓ +${reward} XP` : `+${reward} XP`}
        </span>
      </div>
      <div className="mission-progress-row">
        <div
          className="mission-progress-bar"
          role="progressbar"
          aria-label={`${item.title}: ${current} su ${target}`}
          aria-valuemin={0}
          aria-valuemax={target}
          aria-valuenow={current}
        >
          <span style={{ width: `${percent}%` }} />
        </div>
        <small>
          {current} / {target}
        </small>
      </div>
    </li>
  )
}

export default function MissionsPanel({ missions }: { missions: MissionsOverview }) {
  const completedDaily = missions.dailyMissions.filter(item => item.isCompleted).length
  const completedWeekly = missions.weeklyMissions.filter(item => item.isCompleted).length

  return (
    <section className="missions-panel" aria-label="Missioni del giorno e della settimana">
      <div className="missions-column">
        <header className="missions-col-head">
          <div className="missions-title-wrap">
            <img src="/assets/icons/goal.png" alt="" width="26" height="26" />
            <div>
              <span className="account-eyebrow">OGGI</span>
              <h2>Missioni giornaliere</h2>
            </div>
          </div>
          <span className="learning-unit-badge">
            {completedDaily} / {missions.dailyMissions.length}
          </span>
        </header>
        <ul className="missions-list">
          {missions.dailyMissions.map(item => (
            <MissionCard key={item.code} item={item} />
          ))}
        </ul>
      </div>

      <div className="missions-column">
        <header className="missions-col-head">
          <div className="missions-title-wrap">
            <img src="/assets/badges/star.png" alt="" width="26" height="26" />
            <div>
              <span className="account-eyebrow">SETTIMANA IN CORSO</span>
              <h2>Sfide settimanali</h2>
            </div>
          </div>
          <span className="learning-unit-badge">
            {completedWeekly} / {missions.weeklyMissions.length}
          </span>
        </header>
        <ul className="missions-list">
          {missions.weeklyMissions.map(item => (
            <MissionCard key={item.code} item={item} />
          ))}
        </ul>
      </div>
    </section>
  )
}

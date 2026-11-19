import { useEffect, useState } from 'react'
import {
  ApiError,
  errorMessage,
  gamificationSummary,
  getLessonAttempt,
  learningPath,
  startLessonAttempt,
  startPracticeSession,
  userMissions,
  type Account,
  type GamificationSummary,
  type LearningPath,
  type LessonAttempt,
  type MissionsOverview,
} from '../../shared/api/accounts'
import DashboardSummaryStrip from '../gamification/DashboardSummaryStrip'
import MissionsPanel from '../gamification/MissionsPanel'
import LessonRunnerPage from './LessonRunnerPage'

type PathState =
  | { kind: 'loading' }
  | { kind: 'ready'; path: LearningPath; summary: GamificationSummary; missions: MissionsOverview }
  | { kind: 'error'; message: string }

function toNumber(value: number | string): number {
  return typeof value === 'number' ? value : Number(value)
}

const ATTEMPT_QUERY_PARAM = 'attempt'

function readUrlAttemptId(): string | null {
  return new URLSearchParams(window.location.search).get(ATTEMPT_QUERY_PARAM)
}

function writeUrlAttemptId(attemptId: string | null) {
  const url = new URL(window.location.href)
  if (attemptId) url.searchParams.set(ATTEMPT_QUERY_PARAM, attemptId)
  else url.searchParams.delete(ATTEMPT_QUERY_PARAM)
  window.history.replaceState(null, '', url)
}

export default function LearningPathPage({
  account,
  onExpired,
  onOpenTrophies,
  onOpenProfile,
  onRunnerActiveChange,
}: {
  account: Account
  onExpired: () => void
  onOpenTrophies: () => void
  onOpenProfile: () => void
  onRunnerActiveChange?: (isActive: boolean) => void
}) {
  const [state, setState] = useState<PathState>({ kind: 'loading' })
  const [revision, setRevision] = useState(0)
  const [activeRunner, setActiveRunner] = useState<LessonAttempt | null>(null)
  const [launchingId, setLaunchingId] = useState<string | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  const [replacePrompt, setReplacePrompt] = useState<{
    targetLessonId: string | null
    targetTitle: string
    isPracticeSession: boolean
    activeTitle: string
    activeAttemptId: string
  } | null>(null)

  useEffect(() => {
    onRunnerActiveChange?.(activeRunner !== null)
  }, [activeRunner, onRunnerActiveChange])

  useEffect(() => {
    if (activeRunner) return
    const controller = new AbortController()
    const pendingAttemptId = readUrlAttemptId()

    async function load() {
      try {
        if (pendingAttemptId) {
          try {
            const restored = await getLessonAttempt(pendingAttemptId, controller.signal)
            if (!controller.signal.aborted && restored.status !== 'abandoned') {
              setActiveRunner(restored)
              return
            }
          } catch (err: unknown) {
            if (controller.signal.aborted) return
            if (err instanceof ApiError && err.status === 401) {
              onExpired()
              return
            }
            writeUrlAttemptId(null)
            setActionError(errorMessage(err))
          }
        }
        const [path, summary, missions] = await Promise.all([
          learningPath(controller.signal),
          gamificationSummary(controller.signal),
          userMissions(controller.signal),
        ])
        if (!controller.signal.aborted) {
          setState({ kind: 'ready', path, summary, missions })
        }
      } catch (error: unknown) {
        if (controller.signal.aborted) return
        if (error instanceof ApiError && error.status === 401) onExpired()
        else setState({ kind: 'error', message: errorMessage(error) })
      }
    }

    void load()
    return () => controller.abort()
  }, [revision, activeRunner, onExpired])

  function refreshPath() {
    setState({ kind: 'loading' })
    setRevision(value => value + 1)
  }

  async function openPractice(replaceActive = false) {
    if (
      state.kind === 'ready' &&
      !replaceActive &&
      state.path.activeAttempt &&
      !state.path.activeAttempt.isPractice
    ) {
      setReplacePrompt({
        targetLessonId: null,
        targetTitle: 'Sessione di ripasso',
        isPracticeSession: true,
        activeTitle: state.path.activeAttempt.lessonTitle,
        activeAttemptId: state.path.activeAttempt.attemptId,
      })
      return
    }

    setLaunchingId('practice')
    setActionError(null)
    setReplacePrompt(null)
    try {
      const attempt = await startPracticeSession(replaceActive)
      writeUrlAttemptId(attempt.attemptId)
      setActiveRunner(attempt)
    } catch (err: unknown) {
      if (err instanceof ApiError && err.status === 401) {
        onExpired()
      } else {
        setActionError(errorMessage(err))
        refreshPath()
      }
    } finally {
      setLaunchingId(null)
    }
  }

  async function openLesson(lessonId: string, lessonTitle: string, replaceActive = false) {
    if (state.kind === 'ready' && !replaceActive && state.path.activeAttempt && state.path.activeAttempt.lessonId !== lessonId) {
      setReplacePrompt({
        targetLessonId: lessonId,
        targetTitle: lessonTitle,
        isPracticeSession: false,
        activeTitle: state.path.activeAttempt.lessonTitle,
        activeAttemptId: state.path.activeAttempt.attemptId,
      })
      return
    }

    setLaunchingId(lessonId)
    setActionError(null)
    setReplacePrompt(null)
    try {
      const attempt = await startLessonAttempt(lessonId, replaceActive)
      writeUrlAttemptId(attempt.attemptId)
      setActiveRunner(attempt)
    } catch (err: unknown) {
      if (err instanceof ApiError && err.status === 401) {
        onExpired()
      } else {
        setActionError(errorMessage(err))
        refreshPath()
      }
    } finally {
      setLaunchingId(null)
    }
  }

  async function resumeById(attemptId: string) {
    setLaunchingId(attemptId)
    setActionError(null)
    setReplacePrompt(null)
    try {
      const attempt = await getLessonAttempt(attemptId)
      writeUrlAttemptId(attempt.attemptId)
      setActiveRunner(attempt)
    } catch (err: unknown) {
      if (err instanceof ApiError && err.status === 401) onExpired()
      else setActionError(errorMessage(err))
    } finally {
      setLaunchingId(null)
    }
  }

  if (activeRunner) {
    return (
      <div className="learning-page">
        <LessonRunnerPage
          key={activeRunner.attemptId}
          initialAttempt={activeRunner}
          onAttemptChanged={updated => setActiveRunner(updated)}
          onCloseToPath={() => {
            writeUrlAttemptId(null)
            setActiveRunner(null)
            refreshPath()
          }}
          onStartNextLesson={async nextLessonId => {
            const nextAttempt = await startLessonAttempt(nextLessonId, false)
            writeUrlAttemptId(nextAttempt.attemptId)
            setActiveRunner(nextAttempt)
          }}
          onStartPractice={async () => {
            const practiceAttempt = await startPracticeSession(false)
            writeUrlAttemptId(practiceAttempt.attemptId)
            setActiveRunner(practiceAttempt)
          }}
          onExpired={onExpired}
        />
      </div>
    )
  }

  const outOfHearts =
    state.kind === 'ready' &&
    state.summary.hearts.consumptionEnabled &&
    toNumber(state.summary.hearts.currentHearts) === 0

  return (
    <div className="learning-page">
      <header className="learning-heading learning-heading-split">
        <div>
          <span className="account-eyebrow">IL TUO SENTIERO · ITALIANO → CATANESE</span>
          <h1>Ciao, {account.displayName}!</h1>
          <p>Un piccolo passo alla volta, verso nuove chiacchiere.</p>
        </div>
        {state.kind === 'ready' && (
          <button
            type="button"
            className="practice-cta-pill"
            disabled={launchingId !== null}
            onClick={() => void openPractice(false)}
          >
            <img src="/assets/icons/practice.png" alt="" width="22" height="22" />
            <span>{launchingId === 'practice' ? 'Avvio ripasso…' : 'Ripasso libero (+1 ❤️)'}</span>
          </button>
        )}
      </header>

      {state.kind === 'ready' && (
        <DashboardSummaryStrip
          summary={state.summary}
          onOpenTrophies={onOpenTrophies}
          onOpenProfile={onOpenProfile}
          onStartPractice={() => void openPractice(false)}
        />
      )}

      {outOfHearts && (
        <aside className="hearts-empty-banner" role="status">
          <img src="/assets/icons/heart.png" alt="" width="40" height="40" />
          <div>
            <span className="account-eyebrow">0 CUORI DISPONIBILI</span>
            <strong>Fai un ripasso veloce per recuperare subito +1 cuore!</strong>
            <p>
              Le nuove lezioni del percorso richiedono almeno 1 cuore. I cuori si rigenerano anche da soli (+1 ogni 4
              ore), e il ripasso non consuma mai cuori.
            </p>
          </div>
          <button
            type="button"
            className="account-primary learning-resume-btn"
            disabled={launchingId !== null}
            onClick={() => void openPractice(false)}
          >
            {launchingId === 'practice' ? 'Avvio…' : 'Avvia ripasso (+1 ❤️)'}
          </button>
        </aside>
      )}

      {state.kind === 'ready' && <MissionsPanel missions={state.missions} />}

      {actionError && (
        <div className="account-error" role="alert">
          <p>{actionError}</p>
        </div>
      )}

      {state.kind === 'ready' && state.path.activeAttempt && (
        <aside className="learning-resume-banner" aria-label="Lezione in corso">
          <img src="/assets/mascot/ficodi-study.png" alt="" width="56" height="64" />
          <div>
            <span className="account-eyebrow">SESSIONE IN CORSO</span>
            <strong>«{state.path.activeAttempt.lessonTitle}»</strong>
            <p>
              Hai completato {toNumber(state.path.activeAttempt.answeredCount)} di{' '}
              {toNumber(state.path.activeAttempt.totalExercises)} esercizi. Riprendi da dove eri rimasto!
            </p>
          </div>
          <button
            type="button"
            className="account-primary learning-resume-btn"
            disabled={launchingId !== null}
            onClick={() => void resumeById(state.path.activeAttempt!.attemptId)}
          >
            Riprendi
          </button>
        </aside>
      )}

      {replacePrompt && (
        <div className="lesson-modal-backdrop">
          <div className="lesson-modal" role="dialog" aria-modal="true" aria-labelledby="replace-attempt-title">
            <h2 id="replace-attempt-title">Hai già una lezione aperta</h2>
            <p>
              Stai svolgendo «{replacePrompt.activeTitle}». Vuoi riprendere quella sessione oppure interromperla per
              iniziare «{replacePrompt.targetTitle}»?
            </p>
            <div className="lesson-modal-actions">
              <button
                type="button"
                className="account-primary"
                autoFocus
                disabled={launchingId !== null}
                onClick={() => void resumeById(replacePrompt.activeAttemptId)}
              >
                Riprendi «{replacePrompt.activeTitle}»
              </button>
              <button
                type="button"
                className="account-secondary"
                disabled={launchingId !== null}
                onClick={() =>
                  replacePrompt.isPracticeSession || !replacePrompt.targetLessonId
                    ? void openPractice(true)
                    : void openLesson(replacePrompt.targetLessonId, replacePrompt.targetTitle, true)
                }
              >
                Chiudi e inizia «{replacePrompt.targetTitle}»
              </button>
              <button
                type="button"
                className="account-text-button"
                disabled={launchingId !== null}
                onClick={() => setReplacePrompt(null)}
              >
                Annulla
              </button>
            </div>
          </div>
        </div>
      )}

      <section className="learning-panel" aria-live="polite">
        {state.kind === 'loading' && <p role="status">Caricamento del percorso…</p>}
        {state.kind === 'error' && (
          <div className="account-error" role="alert">
            <p>{state.message}</p>
            <button className="account-secondary" onClick={refreshPath}>
              Riprova
            </button>
          </div>
        )}
        {state.kind === 'ready' && state.path.courses.length === 0 && (
          <div className="learning-empty">
            <img src="/assets/mascot/ficodi-study.png" alt="Ficodì legge un libro" width="112" height="128" />
            <span className="account-eyebrow">LE COSE BELLE RICHIEDONO CURA</span>
            <h2>Stiamo preparando il tuo sentiero.</h2>
            <p>
              Il tuo account è pronto. Le lezioni compariranno qui quando i contenuti saranno revisionati e pubblicati:
              nessuna bozza viene mostrata come lezione disponibile.
            </p>
            <button className="account-secondary" onClick={refreshPath}>
              Aggiorna il percorso
            </button>
          </div>
        )}
        {state.kind === 'ready' &&
          state.path.courses.map(course => (
            <article className="learning-course" key={course.id}>
              <h2>{course.name}</h2>
              {course.sections.length === 0 && <p>Le sezioni non sono ancora pubblicate.</p>}
              {course.sections.map(section => (
                <section key={section.id}>
                  <h3>{section.title}</h3>
                  {section.units.length === 0 && <p>Le unità non sono ancora pubblicate.</p>}
                  {section.units.map(unit => {
                    const completedInUnit = unit.lessons.filter(item => item.status === 'completed').length
                    return (
                      <section className="learning-unit" key={unit.id}>
                        <div className="learning-unit-header">
                          <div>
                            <h4>{unit.title}</h4>
                            <p className="learning-stage-note">
                              Tocca la prossima tappa disponibile per iniziare o ripassare una lezione completata.
                            </p>
                          </div>
                          <span className="learning-unit-badge">
                            {completedInUnit} / {unit.lessons.length} completate
                          </span>
                        </div>
                        {unit.lessons.length === 0 && <p>Le lezioni non sono ancora pubblicate.</p>}
                        <ol className="learning-nodes">
                          {unit.lessons.map((lesson, index) => {
                            const isLocked = lesson.status === 'locked'
                            const isCompleted = lesson.status === 'completed'
                            const isInProgress = Boolean(lesson.inProgressAttemptId)
                            const isCurrent = lesson.status === 'available'
                            const statusLabel = isInProgress
                              ? 'In corso · riprendi'
                              : isCompleted
                                ? 'Completata · ripassa'
                                : isCurrent
                                  ? outOfHearts
                                    ? '0 cuori · fai un ripasso'
                                    : 'Pronta da iniziare'
                                  : 'Bloccata'
                            return (
                              <li key={lesson.id} className={`learning-step ${lesson.status} ${isInProgress ? 'in-progress' : ''}`}>
                                {(isCurrent || isInProgress) && (
                                  <span className="learning-start-pill">{isInProgress ? 'RIPRENDI QUI' : 'INIZIA QUI'}</span>
                                )}
                                <button
                                  type="button"
                                  className={`learning-node ${lesson.status} ${isInProgress ? 'in-progress' : ''}`}
                                  disabled={isLocked || launchingId !== null}
                                  aria-label={`${lesson.title}: ${statusLabel}`}
                                  onClick={() => void openLesson(lesson.id, lesson.title)}
                                >
                                  {isCompleted ? '✓' : isLocked ? '🔒' : index + 1}
                                </button>
                                <strong>{lesson.title}</strong>
                                <span>{launchingId === lesson.id ? 'Apertura…' : statusLabel}</span>
                              </li>
                            )
                          })}
                        </ol>
                      </section>
                    )
                  })}
                </section>
              ))}
            </article>
          ))}
      </section>
      {import.meta.env.DEV && (
        <aside className="learning-prototype-note">
          Vuoi rivedere la direzione grafica? <a href="/prototype?screen=percorso&variant=A">Apri la demo A — Il sentiero</a>. È
          separata dai tuoi dati e non salva progressi.
        </aside>
      )}
    </div>
  )
}

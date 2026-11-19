import { useEffect, useRef, useState, type FormEvent } from 'react'
import {
  abandonLessonAttempt,
  ApiError,
  completeLessonAttempt,
  errorMessage,
  getLessonAttempt,
  reportExerciseIssue,
  submitExerciseAnswer,
  type ExerciseSubmission,
  type LessonAttempt,
  type LessonCompletion,
  type StudentExercise,
  type SubmittedAnswer,
} from '../../shared/api/accounts'

function toNumber(value: number | string): number {
  return typeof value === 'number' ? value : Number(value)
}

type Props = {
  initialAttempt: LessonAttempt
  onAttemptChanged: (attempt: LessonAttempt) => void
  onCloseToPath: () => void
  onStartNextLesson: (lessonId: string) => Promise<void>
  onStartPractice: () => Promise<void>
  onExpired: () => void
}

export default function LessonRunnerPage({
  initialAttempt,
  onAttemptChanged,
  onCloseToPath,
  onStartNextLesson,
  onStartPractice,
  onExpired,
}: Props) {
  const [attempt, setAttempt] = useState<LessonAttempt>(initialAttempt)
  const [stepIndex, setStepIndex] = useState<number>(() => {
    const current = toNumber(initialAttempt.currentStepIndex)
    const total = toNumber(initialAttempt.totalExercises)
    return Math.min(current, Math.max(0, total - 1))
  })
  const [feedback, setFeedback] = useState<ExerciseSubmission | null>(null)
  const [completion, setCompletion] = useState<LessonCompletion | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [exitOpen, setExitOpen] = useState(false)
  const [reportOpen, setReportOpen] = useState(false)
  const [reportSentForExercise, setReportSentForExercise] = useState<string | null>(null)

  // Choice / FillBlank selection
  const [selectedOption, setSelectedOption] = useState<number | null>(null)
  // GuidedTranslation selected token indices in order
  const [selectedTokenIndices, setSelectedTokenIndices] = useState<number[]>([])
  // Matching state: active left item + assigned left->right map
  const [activeLeft, setActiveLeft] = useState<string | null>(null)
  const [matchedPairs, setMatchedPairs] = useState<Record<string, string>>({})

  const headingRef = useRef<HTMLHeadingElement>(null)
  const totalExercises = toNumber(attempt.totalExercises)
  const answeredCount = attempt.recordedAnswers.length
  const allAnswered = answeredCount >= totalExercises
  const currentExercise: StudentExercise | undefined = attempt.exercises[stepIndex]

  async function finalizeLesson(attemptId: string) {
    setBusy(true)
    setError(null)
    try {
      const summary = await completeLessonAttempt(attemptId)
      setCompletion(summary)
    } catch (err: unknown) {
      if (err instanceof ApiError && err.status === 401) onExpired()
      else setError(errorMessage(err))
    } finally {
      setBusy(false)
    }
  }

  useEffect(() => {
    if (!allAnswered || feedback || completion || attempt.status === 'abandoned') return
    let cancelled = false
    completeLessonAttempt(attempt.attemptId)
      .then(summary => {
        if (!cancelled) setCompletion(summary)
      })
      .catch((err: unknown) => {
        if (cancelled) return
        if (err instanceof ApiError && err.status === 401) onExpired()
        else setError(errorMessage(err))
      })
    return () => {
      cancelled = true
    }
  }, [allAnswered, feedback, completion, attempt.attemptId, attempt.status, onExpired])

  useEffect(() => {
    headingRef.current?.focus()
  }, [stepIndex, completion])

  useEffect(() => {
    if (!exitOpen) return
    function onKey(event: KeyboardEvent) {
      if (event.key === 'Escape' && !busy) setExitOpen(false)
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [exitOpen, busy])

  function resetExerciseInputs() {
    setSelectedOption(null)
    setSelectedTokenIndices([])
    setActiveLeft(null)
    setMatchedPairs({})
    setReportOpen(false)
  }

  async function handleSubmitReport(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!currentExercise) return
    const form = new FormData(event.currentTarget)
    setBusy(true)
    setError(null)
    try {
      await reportExerciseIssue({
        lessonId: attempt.lessonId,
        exerciseId: currentExercise.id,
        reasonCategory: String(form.get('reasonCategory') ?? 'variante'),
        message: String(form.get('message') ?? ''),
      })
      setReportOpen(false)
      setReportSentForExercise(currentExercise.id)
    } catch (err: unknown) {
      if (err instanceof ApiError && err.status === 401) onExpired()
      else setError(errorMessage(err))
    } finally {
      setBusy(false)
    }
  }

  function buildSubmission(exercise: StudentExercise): SubmittedAnswer | null {
    if ('options' in exercise && !('textWithGap' in exercise)) {
      if (selectedOption === null) return null
      return { kind: 'choice', selectedOptionIndex: selectedOption }
    }
    if ('textWithGap' in exercise) {
      if (selectedOption === null) return null
      return { kind: 'fillBlank', selectedOptionIndex: selectedOption }
    }
    if ('tokens' in exercise) {
      if (selectedTokenIndices.length === 0) return null
      return {
        kind: 'guidedTranslation',
        selectedTokens: selectedTokenIndices.map(index => exercise.tokens[index]),
      }
    }
    if ('leftItems' in exercise) {
      if (Object.keys(matchedPairs).length !== exercise.leftItems.length) return null
      return {
        kind: 'matching',
        pairs: exercise.leftItems.map(left => ({ left, right: matchedPairs[left] })),
      }
    }
    return null
  }

  async function resyncAttempt() {
    setBusy(true)
    setError(null)
    try {
      const fresh = await getLessonAttempt(attempt.attemptId)
      setAttempt(fresh)
      onAttemptChanged(fresh)
      const nextStep = toNumber(fresh.currentStepIndex)
      const total = toNumber(fresh.totalExercises)
      if (nextStep >= total) {
        await finalizeLesson(fresh.attemptId)
      } else {
        setStepIndex(nextStep)
        setFeedback(null)
        resetExerciseInputs()
      }
    } catch (err: unknown) {
      if (err instanceof ApiError && err.status === 401) onExpired()
      else setError(errorMessage(err))
    } finally {
      setBusy(false)
    }
  }

  async function handlePrimaryAction() {
    if (!currentExercise || busy) return
    if (feedback) {
      const nextAnswered = toNumber(feedback.answeredCount)
      if (feedback.isReadyToComplete || nextAnswered >= totalExercises) {
        await finalizeLesson(attempt.attemptId)
      } else {
        setFeedback(null)
        resetExerciseInputs()
        setStepIndex(nextAnswered)
      }
      return
    }

    const answerPayload = buildSubmission(currentExercise)
    if (!answerPayload) return

    setBusy(true)
    setError(null)
    try {
      const result = await submitExerciseAnswer(attempt.attemptId, currentExercise.id, answerPayload)
      setFeedback(result)
      const updatedRecorded = attempt.recordedAnswers.some(item => item.exerciseId === result.exerciseId)
        ? attempt.recordedAnswers
        : [
            ...attempt.recordedAnswers,
            {
              stepIndex: result.stepIndex,
              exerciseId: result.exerciseId,
              isCorrect: result.isCorrect,
              explanation: result.explanation,
              expectedDisplayAnswer: result.expectedDisplayAnswer,
            },
          ]
      const updatedAttempt: LessonAttempt = {
        ...attempt,
        currentStepIndex: result.answeredCount,
        recordedAnswers: updatedRecorded,
        hearts: result.hearts,
      }
      setAttempt(updatedAttempt)
      onAttemptChanged(updatedAttempt)
    } catch (err: unknown) {
      if (err instanceof ApiError && err.status === 401) {
        onExpired()
      } else if (err instanceof ApiError && err.status === 409) {
        await resyncAttempt()
      } else {
        setError(errorMessage(err))
      }
    } finally {
      setBusy(false)
    }
  }

  async function handleAbandonAndExit() {
    setBusy(true)
    setError(null)
    try {
      await abandonLessonAttempt(attempt.attemptId)
      setExitOpen(false)
      onCloseToPath()
    } catch (err: unknown) {
      if (err instanceof ApiError && err.status === 401) onExpired()
      else setError(errorMessage(err))
    } finally {
      setBusy(false)
    }
  }

  if (completion) {
    const correct = toNumber(completion.correctAnswers)
    const total = toNumber(completion.totalExercises)
    const accuracy = toNumber(completion.accuracyPercent)
    const rewards = completion.rewards
    const xpEarned = toNumber(rewards.xpEarned)
    const currentStreak = toNumber(rewards.currentStreak)
    const currentLevel = toNumber(rewards.currentLevel)
    const levelPercent = toNumber(rewards.levelProgressPercent)

    return (
      <section className="lesson-summary" aria-live="polite">
        <div className="lesson-summary-hero">
          <img
            src={rewards.leveledUp ? '/assets/mascot/ficodi-victory.png' : '/assets/mascot/ficodi-front.png'}
            alt="Ficodì festeggia la lezione completata"
            width="126"
            height="144"
          />
          <span className="account-eyebrow">
            {rewards.leveledUp
              ? `NUOVO LIVELLO · ${rewards.levelTitle.toUpperCase()}`
              : completion.isPractice
                ? 'SESSIONE DI RIPASSO COMPLETATA'
                : 'LEZIONE COMPLETATA'}
          </span>
          <h1 ref={headingRef} tabIndex={-1}>
            Ottimo passo su «{completion.lessonTitle}»!
          </h1>
          <p>
            Il tuo progresso è salvato sul profilo.{' '}
            {rewards.practiceHeartRestored && (
              <strong>Hai recuperato +1 cuore ({toNumber(rewards.hearts.currentHearts)}/{toNumber(rewards.hearts.maxHearts)})! </strong>
            )}
            {completion.nextUnlockedLessonTitle
              ? `Hai sbloccato «${completion.nextUnlockedLessonTitle}».`
              : completion.isPractice
                ? 'Il ripasso rafforza la memoria e protegge sempre i tuoi cuori.'
                : 'Hai completato tutte le tappe pubblicate finora in questo percorso.'}
          </p>
        </div>

        <div className="lesson-summary-metrics lesson-summary-metrics-four">
          <div className="lesson-metric-card highlight-xp">
            <img src="/assets/badges/xp.png" alt="" width="34" height="34" />
            <span>XP GUADAGNATI</span>
            <strong>+{xpEarned} XP</strong>
          </div>
          <div className="lesson-metric-card">
            <img src="/assets/icons/flame.png" alt="" width="34" height="34" />
            <span>SERIE GIORNALIERA</span>
            <strong>
              {currentStreak} {currentStreak === 1 ? 'giorno' : 'giorni'}
            </strong>
          </div>
          <div className="lesson-metric-card">
            <img src="/assets/badges/check.png" alt="" width="34" height="34" />
            <span>RISPOSTE CORRETTE</span>
            <strong>
              {correct} / {total}
            </strong>
          </div>
          <div className="lesson-metric-card">
            <span>PRECISIONE</span>
            <strong>{accuracy}%</strong>
          </div>
        </div>

        <div className="lesson-reward-breakdown">
          <div className="lesson-reward-level-row">
            <div>
              <strong>
                Livello {currentLevel} · {rewards.levelTitle}
              </strong>
              <span>{toNumber(rewards.totalXp)} XP totali</span>
            </div>
            {rewards.leveledUp && <span className="lesson-levelup-badge">Salito di livello!</span>}
          </div>
          <div
            className="gamification-xp-bar"
            role="progressbar"
            aria-label="Progresso livello dopo la lezione"
            aria-valuemin={0}
            aria-valuemax={100}
            aria-valuenow={levelPercent}
          >
            <span style={{ width: `${Math.max(levelPercent, 6)}%` }} />
          </div>

          {rewards.xpBreakdown.length > 0 && (
            <ul className="lesson-xp-items">
              {rewards.xpBreakdown.map(item => (
                <li key={item.eventCode}>
                  <span>{item.label}</span>
                  <strong>+{toNumber(item.amount)} XP</strong>
                </li>
              ))}
            </ul>
          )}

          {rewards.dailyReviewCapReached && (
            <p className="lesson-cap-note">
              Hai già ricevuto i 3 premi giornalieri di ripasso: il ripasso resta libero per allenarti senza limiti.
            </p>
          )}
        </div>

        {rewards.newlyCompletedMissions.length > 0 && (
          <div className="lesson-unlocked-trophies" aria-label="Missioni completate">
            <span className="account-eyebrow">MISSIONI COMPLETATE</span>
            <ul>
              {rewards.newlyCompletedMissions.map(mission => (
                <li key={mission.code}>
                  <img src="/assets/icons/goal.png" alt="" width="34" height="34" />
                  <div>
                    <strong>
                      {mission.title} (+{toNumber(mission.rewardXp)} XP)
                    </strong>
                    <p>{mission.scope === 'weekly' ? 'Sfida settimanale completata' : 'Missione giornaliera completata'}</p>
                  </div>
                </li>
              ))}
            </ul>
          </div>
        )}

        {rewards.newlyUnlockedAchievements.length > 0 && (
          <div className="lesson-unlocked-trophies" aria-label="Nuovi trofei sbloccati">
            <span className="account-eyebrow">NUOVI TROFEI SBLOCCATI</span>
            <ul>
              {rewards.newlyUnlockedAchievements.map(trophy => (
                <li key={trophy.code}>
                  <img src={trophy.iconPath} alt="" width="40" height="40" />
                  <div>
                    <strong>{trophy.name}</strong>
                    <p>{trophy.description}</p>
                  </div>
                </li>
              ))}
            </ul>
          </div>
        )}

        {error && (
          <div className="account-error" role="alert">
            <p>{error}</p>
          </div>
        )}

        <div className="lesson-summary-actions">
          {completion.nextUnlockedLessonId &&
            completion.nextUnlockedLessonTitle &&
            (!rewards.hearts.consumptionEnabled || toNumber(rewards.hearts.currentHearts) > 0) && (
              <button
                type="button"
                className="account-primary"
                disabled={busy}
                onClick={() => void onStartNextLesson(completion.nextUnlockedLessonId!)}
              >
                Inizia «{completion.nextUnlockedLessonTitle}»
              </button>
            )}
          {rewards.hearts.consumptionEnabled && toNumber(rewards.hearts.currentHearts) === 0 && (
            <button
              type="button"
              className="account-primary"
              disabled={busy}
              onClick={() => void onStartPractice()}
            >
              Fai un ripasso per recuperare +1 ❤️
            </button>
          )}
          <button
            type="button"
            className={
              (completion.nextUnlockedLessonId &&
                (!rewards.hearts.consumptionEnabled || toNumber(rewards.hearts.currentHearts) > 0)) ||
              (rewards.hearts.consumptionEnabled && toNumber(rewards.hearts.currentHearts) === 0)
                ? 'account-secondary'
                : 'account-primary'
            }
            disabled={busy}
            onClick={onCloseToPath}
          >
            Torna al sentiero
          </button>
        </div>
      </section>
    )
  }

  if (!currentExercise) {
    return (
      <section className="lesson-runner" aria-live="polite">
        <p role="status">Concludiamo il riepilogo della lezione…</p>
        {error && (
          <div className="account-error" role="alert">
            <p>{error}</p>
            <button type="button" className="account-secondary" disabled={busy} onClick={() => void finalizeLesson(attempt.attemptId)}>
              Riprova a concludere
            </button>
          </div>
        )}
      </section>
    )
  }

  const readySubmission = buildSubmission(currentExercise) !== null
  const completedSteps = feedback ? toNumber(feedback.answeredCount) : answeredCount
  const progressPercent = Math.round((completedSteps / Math.max(1, totalExercises)) * 100)

  const formatLabel =
    'textWithGap' in currentExercise
      ? 'COMPLETA LA FRASE'
      : 'options' in currentExercise
        ? 'SCELTA MULTIPLA'
        : 'tokens' in currentExercise
          ? 'TRADUZIONE GUIDATA'
          : 'ABBINAMENTO'

  return (
    <section className="lesson-runner">
      <header className="lesson-runner-topbar">
        <button
          type="button"
          className="lesson-close-button"
          aria-label="Esci o metti in pausa la lezione"
          disabled={busy}
          onClick={() => setExitOpen(true)}
        >
          ✕
        </button>
        <div
          className="lesson-progress-track"
          role="progressbar"
          aria-label={`Avanzamento lezione: ${completedSteps} di ${totalExercises} esercizi`}
          aria-valuenow={progressPercent}
          aria-valuemin={0}
          aria-valuemax={100}
        >
          <span style={{ width: `${progressPercent}%` }} />
        </div>
        <span className="lesson-step-counter">
          {Math.min(stepIndex + 1, totalExercises)} / {totalExercises}
        </span>
        <span
          className={`lesson-hearts-badge ${attempt.isPractice ? 'practice' : toNumber(attempt.hearts.currentHearts) === 0 ? 'empty' : ''}`}
          aria-label={
            attempt.isPractice
              ? 'Sessione di ripasso: i cuori non si consumano'
              : `Cuori disponibili: ${toNumber(attempt.hearts.currentHearts)} su ${toNumber(attempt.hearts.maxHearts)}`
          }
        >
          <img src="/assets/icons/heart.png" alt="" width="18" height="18" />
          <strong>
            {attempt.isPractice
              ? 'Ripasso'
              : attempt.hearts.consumptionEnabled
                ? `${toNumber(attempt.hearts.currentHearts)}/${toNumber(attempt.hearts.maxHearts)}`
                : '∞'}
          </strong>
        </span>
      </header>

      <div className="lesson-card">
        <div className="lesson-kicker lesson-kicker-row">
          <span className="account-eyebrow">{attempt.lessonTitle.toUpperCase()} · {formatLabel}</span>
          {reportSentForExercise === currentExercise.id ? (
            <span className="lesson-report-sent" role="status">Segnalazione inviata</span>
          ) : (
            <button
              type="button"
              className="lesson-report-trigger"
              disabled={busy}
              onClick={() => setReportOpen(open => !open)}
            >
              Segnala esercizio
            </button>
          )}
        </div>

        {reportOpen && (
          <form className="lesson-report-box" onSubmit={e => void handleSubmitReport(e)}>
            <label>
              Tipo di segnalazione
              <select name="reasonCategory" defaultValue="variante">
                <option value="variante">Forma o variante catanese da verificare</option>
                <option value="ortografia">Refuso o grafia nella frase o nelle opzioni</option>
                <option value="traduzione">Traduzione o risposta valida non accettata</option>
                <option value="spiegazione">Spiegazione poco chiara</option>
                <option value="altro">Altro</option>
              </select>
            </label>
            <label>
              Dettagli per la redazione (min. 6 caratteri)
              <input
                name="message"
                required
                minLength={6}
                maxLength={1500}
                placeholder="Descrivi brevemente cosa controllare…"
              />
            </label>
            <div className="admin-action-row">
              <button type="submit" className="account-primary admin-inline-primary" disabled={busy}>
                Invia segnalazione
              </button>
              <button type="button" className="account-secondary" disabled={busy} onClick={() => setReportOpen(false)}>
                Chiudi
              </button>
            </div>
          </form>
        )}

        <div className="lesson-prompt-row">
          <img src="/assets/mascot/ficodi-study.png" alt="" width="64" height="74" />
          <div>
            <h1 ref={headingRef} tabIndex={-1}>
              {currentExercise.prompt}
            </h1>
            <p className="lesson-objective-hint">{attempt.lessonObjective}</p>
          </div>
        </div>

        {'options' in currentExercise && !('textWithGap' in currentExercise) && (
          <div className="lesson-options" role="group" aria-label="Opzioni di risposta">
            {currentExercise.options.map((option, index) => (
              <button
                key={option}
                type="button"
                disabled={busy || feedback !== null}
                aria-pressed={selectedOption === index}
                className={`lesson-option-btn ${selectedOption === index ? 'selected' : ''}`}
                onClick={() => setSelectedOption(index)}
              >
                <span className="lesson-option-index">{index + 1}</span>
                <strong>{option}</strong>
              </button>
            ))}
          </div>
        )}

        {'textWithGap' in currentExercise && (
          <div className="lesson-fillblank">
            {(() => {
              const parts = currentExercise.textWithGap.split('___')
              const chosenWord = selectedOption !== null ? currentExercise.options[selectedOption] : '___'
              return (
                <p className="lesson-gap-sentence" aria-live="polite">
                  <span>{parts[0]}</span>
                  <strong className={`lesson-gap-slot ${selectedOption !== null ? 'filled' : ''}`}>{chosenWord}</strong>
                  <span>{parts[1] ?? ''}</span>
                </p>
              )
            })()}
            <div className="lesson-options" role="group" aria-label="Parole per completare la frase">
              {currentExercise.options.map((option, index) => (
                <button
                  key={option}
                  type="button"
                  disabled={busy || feedback !== null}
                  aria-pressed={selectedOption === index}
                  className={`lesson-option-btn ${selectedOption === index ? 'selected' : ''}`}
                  onClick={() => setSelectedOption(index)}
                >
                  <span className="lesson-option-index">{index + 1}</span>
                  <strong>{option}</strong>
                </button>
              ))}
            </div>
          </div>
        )}

        {'tokens' in currentExercise && (
          <div className="lesson-translation">
            <div className="lesson-token-tray" aria-label="Frase composta" aria-live="polite">
              {selectedTokenIndices.length === 0 ? (
                <span className="lesson-tray-placeholder">Tocca le tessere qui sotto nell’ordine giusto…</span>
              ) : (
                selectedTokenIndices.map((tokenIdx, pos) => (
                  <button
                    key={`${tokenIdx}-${pos}`}
                    type="button"
                    className="lesson-token chosen"
                    disabled={busy || feedback !== null}
                    aria-label={`Rimuovi tessera ${currentExercise.tokens[tokenIdx]}`}
                    onClick={() =>
                      setSelectedTokenIndices(prev => prev.filter((_, itemPos) => itemPos !== pos))
                    }
                  >
                    {currentExercise.tokens[tokenIdx]}
                  </button>
                ))
              )}
            </div>
            <div className="lesson-token-bank" role="group" aria-label="Tessere disponibili">
              {currentExercise.tokens.map((token, index) => {
                const used = selectedTokenIndices.includes(index)
                return (
                  <button
                    key={`${token}-${index}`}
                    type="button"
                    className={`lesson-token ${used ? 'used' : ''}`}
                    disabled={busy || feedback !== null || used}
                    onClick={() => setSelectedTokenIndices(prev => [...prev, index])}
                  >
                    {token}
                  </button>
                )
              })}
            </div>
          </div>
        )}

        {'leftItems' in currentExercise && (
          <div className="lesson-matching">
            <p className="lesson-matching-hint">
              Seleziona una voce a sinistra e poi il suo significato a destra. Tocca una coppia già unita per cambiarla.
            </p>
            <div className="lesson-matching-columns">
              <div className="lesson-matching-col" role="group" aria-label="Espressioni da abbinare">
                {currentExercise.leftItems.map((left, idx) => {
                  const pairedRight = matchedPairs[left]
                  const isActive = activeLeft === left
                  return (
                    <button
                      key={left}
                      type="button"
                      disabled={busy || feedback !== null}
                      aria-pressed={isActive}
                      className={`lesson-match-item ${isActive ? 'active' : ''} ${pairedRight ? 'paired' : ''}`}
                      onClick={() => {
                        if (pairedRight) {
                          setMatchedPairs(prev => {
                            const copy = { ...prev }
                            delete copy[left]
                            return copy
                          })
                          setActiveLeft(left)
                        } else {
                          setActiveLeft(isActive ? null : left)
                        }
                      }}
                    >
                      <span className="lesson-option-index">{idx + 1}</span>
                      <div>
                        <strong>{left}</strong>
                        {pairedRight && <small>→ {pairedRight}</small>}
                      </div>
                    </button>
                  )
                })}
              </div>

              <div className="lesson-matching-col" role="group" aria-label="Significati disponibili">
                {currentExercise.rightItems.map(right => {
                  const ownerLeft = Object.entries(matchedPairs).find(([, val]) => val === right)?.[0]
                  return (
                    <button
                      key={right}
                      type="button"
                      disabled={busy || feedback !== null || (!activeLeft && !ownerLeft)}
                      className={`lesson-match-item right ${ownerLeft ? 'paired' : ''}`}
                      onClick={() => {
                        if (activeLeft) {
                          setMatchedPairs(prev => {
                            const next: Record<string, string> = {}
                            for (const [k, v] of Object.entries(prev)) {
                              if (v !== right) next[k] = v
                            }
                            next[activeLeft] = right
                            return next
                          })
                          setActiveLeft(null)
                        } else if (ownerLeft) {
                          setMatchedPairs(prev => {
                            const copy = { ...prev }
                            delete copy[ownerLeft]
                            return copy
                          })
                        }
                      }}
                    >
                      <div>
                        <strong>{right}</strong>
                        {ownerLeft && <small>← {ownerLeft}</small>}
                      </div>
                    </button>
                  )
                })}
              </div>
            </div>
          </div>
        )}

        {error && (
          <div className="account-error" role="alert">
            <p>{error}</p>
            <button type="button" className="account-secondary" disabled={busy} onClick={() => void resyncAttempt()}>
              Sincronizza tentativo
            </button>
          </div>
        )}

        <footer className={`lesson-feedback-bar ${feedback ? (feedback.isCorrect ? 'correct' : 'wrong') : ''}`}>
          <div className="lesson-feedback-copy" aria-live="polite">
            {feedback ? (
              <>
                <strong>
                  {feedback.isCorrect ? 'Proprio così! Ottima risposta.' : 'Quasi! Guardiamo insieme la forma giusta:'}
                </strong>
                {!feedback.isCorrect && (
                  <p className="lesson-expected-answer">
                    Soluzione: <b>{feedback.expectedDisplayAnswer}</b>
                    {feedback.heartConsumed ? ' · −1 cuore' : ''}
                  </p>
                )}
                <p>{feedback.explanation}</p>
                {feedback.heartConsumed && toNumber(feedback.hearts.currentHearts) === 0 && (
                  <p className="lesson-zero-hearts-note">
                    Hai esaurito i cuori, ma puoi completare con tranquillità questa lezione già iniziata!
                  </p>
                )}
              </>
            ) : (
              <span className="lesson-calm-hint">Rispondi con calma: il progresso viene salvato a ogni passo.</span>
            )}
          </div>
          <button
            type="button"
            className="account-primary lesson-submit-btn"
            disabled={busy || (!feedback && !readySubmission)}
            onClick={() => void handlePrimaryAction()}
          >
            {busy
              ? 'Un attimo…'
              : feedback
                ? feedback.isReadyToComplete
                  ? 'Vedi riepilogo'
                  : 'Continua'
                : 'Controlla'}
          </button>
        </footer>
      </div>

      {exitOpen && (
        <div className="lesson-modal-backdrop">
          <div className="lesson-modal" role="dialog" aria-modal="true" aria-labelledby="lesson-exit-title">
            <img src="/assets/mascot/ficodi-front.png" alt="" width="76" height="86" />
            <h2 id="lesson-exit-title">Vuoi uscire dalla lezione?</h2>
            <p>
              Puoi tornare al sentiero conservando le risposte già date per riprendere più tardi, oppure interrompere del
              tutto questo tentativo.
            </p>
            <div className="lesson-modal-actions">
              <button type="button" className="account-primary" autoFocus disabled={busy} onClick={() => setExitOpen(false)}>
                Resta nella lezione
              </button>
              <button
                type="button"
                className="account-secondary"
                disabled={busy}
                onClick={() => {
                  setExitOpen(false)
                  onCloseToPath()
                }}
              >
                Torna al sentiero (conserva progresso)
              </button>
              <button
                type="button"
                className="account-text-button lesson-abandon-btn"
                disabled={busy}
                onClick={() => void handleAbandonAndExit()}
              >
                Interrompi e azzera tentativo
              </button>
            </div>
          </div>
        </div>
      )}
    </section>
  )
}
